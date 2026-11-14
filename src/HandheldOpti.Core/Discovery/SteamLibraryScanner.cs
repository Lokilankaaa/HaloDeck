using HandheldOpti.Core.Models;
using Microsoft.Win32;

namespace HandheldOpti.Core.Discovery;

public sealed class SteamLibraryScanner
{
    private static readonly HashSet<string> ExcludedAppIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "228980",  // Steamworks Common Redistributables
        "1070560", // Steam Linux Runtime
        "1391110",
        "1628350",
        "250820",  // SteamVR
        "1826330",
        "1493710", // Proton Experimental
        "993090",  // Lossless Scaling
        "241100",  // Steam Controller Configs
        "431960",  // Wallpaper Engine
        "223850",  // 3DMark
        "388080",  // Borderless Gaming
        "966610",  // FPS Monitor
        "367670",  // Controller Companion
        "629520"   // Soundpad
    };

    public IReadOnlyList<GameEntry> Scan(string? customSteamRoot = null)
    {
        var steamRoot = customSteamRoot ?? ResolveSteamInstallPath();
        if (string.IsNullOrWhiteSpace(steamRoot) || !Directory.Exists(steamRoot))
        {
            return Array.Empty<GameEntry>();
        }

        var libraryFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steamRoot };
        var libraryVdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libraryVdf))
        {
            try
            {
                var root = VdfParser.Parse(File.ReadAllText(libraryVdf));
                var libsNode = root.Get("libraryfolders") ?? root;
                foreach (var child in libsNode.Children.Values)
                {
                    var path = child.GetString("path");
                    if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                    {
                        libraryFolders.Add(path);
                    }
                }
            }
            catch
            {
                // Ignore malformed VDF and continue with default library
            }
        }

        var games = new List<GameEntry>();
        var seenAppIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var libRoot in libraryFolders)
        {
            var steamAppsDir = Path.Combine(libRoot, "steamapps");
            if (!Directory.Exists(steamAppsDir))
            {
                continue;
            }

            foreach (var acfPath in Directory.EnumerateFiles(steamAppsDir, "appmanifest_*.acf"))
            {
                try
                {
                    var root = VdfParser.Parse(File.ReadAllText(acfPath));
                    var appState = root.Get("AppState") ?? root;
                    var appId = appState.GetString("appid");
                    var name = appState.GetString("name");
                    var installDir = appState.GetString("installdir");

                    if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDir))
                    {
                        continue;
                    }

                    if (ExcludedAppIds.Contains(appId) || !seenAppIds.Add(appId))
                    {
                        continue;
                    }

                    var fullInstallDir = Path.Combine(steamAppsDir, "common", installDir);
                    if (!Directory.Exists(fullInstallDir))
                    {
                        continue;
                    }

                    var artworks = ResolveSteamArtworks(steamRoot, appId);
                    var fallbackCover = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900.jpg";

                    games.Add(new GameEntry
                    {
                        Id = $"steam:{appId}",
                        Title = name,
                        Platform = GamePlatform.Steam,
                        InstallDirectory = fullInstallDir,
                        TargetRenderDirectory = fullInstallDir,
                        LaunchUri = $"steam://rungameid/{appId}",
                        CoverImageUrl = artworks.Cover ?? fallbackCover,
                        HeroImageUrl = artworks.Hero,
                        HeroBlurImageUrl = artworks.HeroBlur,
                        LogoImageUrl = artworks.Logo
                    });
                }
                catch
                {
                    // Skip broken manifest files gracefully
                }
            }
        }

        return games;
    }

    private static string? ResolveSteamInstallPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var cu = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var path = cu?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                return path.Replace('/', Path.DirectorySeparatorChar);
            }

            using var lm = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            var lmPath = lm?.GetValue("InstallPath") as string;
            if (!string.IsNullOrWhiteSpace(lmPath) && Directory.Exists(lmPath))
            {
                return lmPath;
            }
        }
        catch
        {
            // Registry inaccessible
        }

        var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        return Directory.Exists(defaultPath) ? defaultPath : null;
    }

    public static string? ResolveSteamLibraryCacheDir()
    {
        var steamRoot = ResolveSteamInstallPath();
        if (string.IsNullOrWhiteSpace(steamRoot))
        {
            return null;
        }

        var cacheDir = Path.Combine(steamRoot, "appcache", "librarycache");
        return Directory.Exists(cacheDir) ? cacheDir : null;
    }

    private sealed record SteamArtworkSet(string? Cover, string? Hero, string? HeroBlur, string? Logo);

    private static SteamArtworkSet ResolveSteamArtworks(string steamRoot, string appId)
    {
        var cacheDir = Path.Combine(steamRoot, "appcache", "librarycache");
        if (!Directory.Exists(cacheDir))
        {
            return new SteamArtworkSet(null, null, null, null);
        }

        var files = new List<string>();
        var appDir = Path.Combine(cacheDir, appId);
        if (Directory.Exists(appDir))
        {
            try
            {
                files.AddRange(Directory.EnumerateFiles(appDir, "*.*", SearchOption.AllDirectories)
                    .Where(IsImageFile));
            }
            catch
            {
                // Ignore transient Steam cache read lock
            }
        }

        try
        {
            files.AddRange(Directory.EnumerateFiles(cacheDir, $"{appId}_*.*", SearchOption.TopDirectoryOnly)
                .Where(IsImageFile));
        }
        catch
        {
            // Ignore transient Steam cache read lock
        }

        if (files.Count == 0)
        {
            return new SteamArtworkSet(null, null, null, null);
        }

        string? cover = PickArtwork(files,
            "library_600x900_schinese",
            "library_600x900",
            "library_capsule_schinese",
            "library_capsule",
            "header_schinese",
            "header");

        string? hero = PickArtwork(files,
            f => IsArtworkMatch(f, "library_hero_schinese") && !f.Contains("blur", StringComparison.OrdinalIgnoreCase),
            f => IsArtworkMatch(f, "library_hero") && !f.Contains("blur", StringComparison.OrdinalIgnoreCase),
            f => IsArtworkMatch(f, "page_bg") && !f.Contains("blur", StringComparison.OrdinalIgnoreCase));

        string? heroBlur = PickArtwork(files,
            "library_hero_blur_schinese",
            "library_hero_blur") ?? hero;

        string? logo = PickArtwork(files,
            "logo_schinese",
            "logo");

        return new SteamArtworkSet(
            ToCacheUrl(cacheDir, cover),
            ToCacheUrl(cacheDir, hero),
            ToCacheUrl(cacheDir, heroBlur),
            ToCacheUrl(cacheDir, logo));
    }

    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsArtworkMatch(string filePath, string token) =>
        Path.GetFileNameWithoutExtension(filePath).Contains(token, StringComparison.OrdinalIgnoreCase);

    private static string? PickArtwork(List<string> files, params string[] orderedTokens)
    {
        foreach (var token in orderedTokens)
        {
            var match = files.FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).Contains(token, StringComparison.OrdinalIgnoreCase) &&
                !Path.GetFileNameWithoutExtension(f).Contains("_2x", StringComparison.OrdinalIgnoreCase))
                ?? files.FirstOrDefault(f =>
                    Path.GetFileNameWithoutExtension(f).Contains(token, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static string? PickArtwork(List<string> files, params Func<string, bool>[] predicates)
    {
        foreach (var predicate in predicates)
        {
            var match = files.FirstOrDefault(f => predicate(Path.GetFileNameWithoutExtension(f)));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static string? ToCacheUrl(string cacheDir, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var rel = Path.GetRelativePath(cacheDir, filePath).Replace('\\', '/');
        var encoded = string.Join("/", rel.Split('/').Select(Uri.EscapeDataString));
        return $"https://steamcache.handheldopti.local/{encoded}";
    }
}
