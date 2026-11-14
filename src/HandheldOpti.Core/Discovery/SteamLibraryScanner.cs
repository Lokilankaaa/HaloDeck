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

                    var localCover = ResolveLocalSteamCover(steamRoot, appId);
                    var coverUrl = localCover is not null
                        ? new Uri(localCover).AbsoluteUri
                        : $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900.jpg";

                    games.Add(new GameEntry
                    {
                        Id = $"steam:{appId}",
                        Title = name,
                        Platform = GamePlatform.Steam,
                        InstallDirectory = fullInstallDir,
                        TargetRenderDirectory = fullInstallDir,
                        LaunchUri = $"steam://rungameid/{appId}",
                        CoverImageUrl = coverUrl
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

    private static string? ResolveLocalSteamCover(string steamRoot, string appId)
    {
        var cacheDir = Path.Combine(steamRoot, "appcache", "librarycache");
        if (!Directory.Exists(cacheDir))
        {
            return null;
        }

        string[] candidates =
        {
            Path.Combine(cacheDir, $"{appId}_library_600x900.jpg"),
            Path.Combine(cacheDir, $"{appId}_library_capsule.jpg"),
            Path.Combine(cacheDir, $"{appId}_header.jpg"),
            Path.Combine(cacheDir, appId, "library_600x900.jpg"),
            Path.Combine(cacheDir, appId, "header.jpg")
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
