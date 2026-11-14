using System.Text.Json;
using System.Xml.Linq;
using HandheldOpti.Core.Models;
using Microsoft.Win32;

namespace HandheldOpti.Core.Discovery;

public sealed class EpicGogXboxScanner
{
    public IReadOnlyList<GameEntry> ScanAll()
    {
        var results = new List<GameEntry>();
        results.AddRange(ScanEpic());
        results.AddRange(ScanGog());
        results.AddRange(ScanXbox());
        return results;
    }

    public IReadOnlyList<GameEntry> ScanEpic(string? customManifestDir = null)
    {
        var manifestsDir = customManifestDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");

        if (!Directory.Exists(manifestsDir))
        {
            return Array.Empty<GameEntry>();
        }

        var games = new List<GameEntry>();
        foreach (var file in Directory.EnumerateFiles(manifestsDir, "*.item"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;

                var installLoc = root.TryGetProperty("InstallLocation", out var locProp) ? locProp.GetString() : null;
                var displayName = root.TryGetProperty("DisplayName", out var nameProp) ? nameProp.GetString() : null;
                var appName = root.TryGetProperty("AppName", out var appProp) ? appProp.GetString() : null;
                var catalogNs = root.TryGetProperty("CatalogNamespace", out var nsProp) ? nsProp.GetString() : null;
                var catalogItem = root.TryGetProperty("CatalogItemId", out var itemProp) ? itemProp.GetString() : null;

                if (string.IsNullOrWhiteSpace(installLoc) || string.IsNullOrWhiteSpace(displayName) || !Directory.Exists(installLoc))
                {
                    continue;
                }

                // Skip launcher/SDK tools or incomplete installs
                if (root.TryGetProperty("bIsIncompleteInstall", out var incProp) && incProp.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                string? launchUri = !string.IsNullOrWhiteSpace(catalogNs) && !string.IsNullOrWhiteSpace(catalogItem) && !string.IsNullOrWhiteSpace(appName)
                    ? $"com.epicgames.launcher://apps/{Uri.EscapeDataString(catalogNs)}%3A{Uri.EscapeDataString(catalogItem)}%3A{Uri.EscapeDataString(appName)}?action=launch&silent=true"
                    : null;

                games.Add(new GameEntry
                {
                    Id = $"epic:{appName ?? displayName}",
                    Title = displayName,
                    Platform = GamePlatform.Epic,
                    InstallDirectory = installLoc,
                    TargetRenderDirectory = installLoc,
                    LaunchUri = launchUri
                });
            }
            catch
            {
                // Ignore malformed .item manifest
            }
        }

        return games;
    }

    public IReadOnlyList<GameEntry> ScanGog()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<GameEntry>();
        }

        var games = new List<GameEntry>();
        try
        {
            using var gogRoot = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
            if (gogRoot is null)
            {
                return games;
            }

            foreach (var subKeyName in gogRoot.GetSubKeyNames())
            {
                using var gameKey = gogRoot.OpenSubKey(subKeyName);
                var path = gameKey?.GetValue("path") as string;
                var name = gameKey?.GetValue("gameName") as string;
                var exe = gameKey?.GetValue("exe") as string;

                if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(name) || !Directory.Exists(path))
                {
                    continue;
                }

                games.Add(new GameEntry
                {
                    Id = $"gog:{subKeyName}",
                    Title = name,
                    Platform = GamePlatform.Gog,
                    InstallDirectory = path,
                    TargetRenderDirectory = path,
                    MainExecutablePath = !string.IsNullOrWhiteSpace(exe) && File.Exists(exe) ? exe : null
                });
            }
        }
        catch
        {
            // Ignore registry access failures
        }

        return games;
    }

    public IReadOnlyList<GameEntry> ScanXbox(IEnumerable<string>? customXboxRoots = null)
    {
        var candidateRoots = new List<string>();
        if (customXboxRoots is not null)
        {
            candidateRoots.AddRange(customXboxRoots);
        }
        else
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable))
                    {
                        continue;
                    }

                    var xboxDir = Path.Combine(drive.RootDirectory.FullName, "XboxGames");
                    if (Directory.Exists(xboxDir))
                    {
                        candidateRoots.Add(xboxDir);
                    }
                }
                catch
                {
                    // Skip unreadable drives
                }
            }
        }

        var games = new List<GameEntry>();
        foreach (var xboxRoot in candidateRoots)
        {
            if (!Directory.Exists(xboxRoot))
            {
                continue;
            }

            foreach (var gameDir in Directory.EnumerateDirectories(xboxRoot))
            {
                var contentDir = Path.Combine(gameDir, "Content");
                var searchRoot = Directory.Exists(contentDir) ? contentDir : gameDir;
                var configPath = Path.Combine(searchRoot, "MicrosoftGame.config");
                if (!File.Exists(configPath))
                {
                    continue;
                }

                try
                {
                    var doc = XDocument.Load(configPath);
                    var title = doc.Descendants("ShellVisuals").FirstOrDefault()?.Attribute("DefaultDisplayName")?.Value;
                    var identityName = doc.Descendants("Identity").FirstOrDefault()?.Attribute("Name")?.Value;
                    var exeName = doc.Descendants("Executable").FirstOrDefault()?.Attribute("Name")?.Value;

                    title ??= Path.GetFileName(gameDir);
                    var exePath = !string.IsNullOrWhiteSpace(exeName) ? Path.Combine(searchRoot, exeName) : null;

                    games.Add(new GameEntry
                    {
                        Id = $"xbox:{identityName ?? title}",
                        Title = title,
                        Platform = GamePlatform.Xbox,
                        InstallDirectory = searchRoot,
                        TargetRenderDirectory = searchRoot,
                        MainExecutablePath = exePath is not null && File.Exists(exePath) ? exePath : null
                    });
                }
                catch
                {
                    // Skip invalid MicrosoftGame.config
                }
            }
        }

        return games;
    }
}
