namespace HaloDeck.Core.Config;

public static class HaloDeckPaths
{
    private static readonly object MigrationLock = new();
    private static bool _migrated;

    public static string ResolveAppDataRoot(string? customRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(customRoot))
        {
            Directory.CreateDirectory(customRoot);
            return customRoot;
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string newRoot = Path.Combine(localAppData, "HaloDeck");
        Directory.CreateDirectory(newRoot);

        TryMigrateLegacyAppData(localAppData, newRoot);
        return newRoot;
    }

    private static void TryMigrateLegacyAppData(string localAppData, string newRoot)
    {
        lock (MigrationLock)
        {
            if (_migrated)
            {
                return;
            }
            _migrated = true;

            try
            {
                string settingsFile = Path.Combine(newRoot, "settings.json");
                if (File.Exists(settingsFile))
                {
                    return;
                }

                string legacyRoot = Path.Combine(localAppData, "HandheldOpti");
                if (!Directory.Exists(legacyRoot))
                {
                    return;
                }

                foreach (var jsonName in new[] { "settings.json", "injected_index.json", "custom_games.json" })
                {
                    CopyFileIfMissing(
                        Path.Combine(legacyRoot, jsonName),
                        Path.Combine(newRoot, jsonName));
                }

                foreach (var subDir in new[] { "components", "custom_dlls" })
                {
                    string srcSub = Path.Combine(legacyRoot, subDir);
                    if (Directory.Exists(srcSub))
                    {
                        CopyDirectoryRecursive(srcSub, Path.Combine(newRoot, subDir));
                    }
                }
            }
            catch
            {
                // Best-effort migration; never block startup
            }
        }
    }

    private static void CopyFileIfMissing(string sourcePath, string destPath)
    {
        if (!File.Exists(sourcePath) || File.Exists(destPath))
        {
            return;
        }

        try
        {
            string? parent = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }
            File.Copy(sourcePath, destPath, overwrite: false);
        }
        catch
        {
            // Ignore locked or inaccessible individual files
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            CopyFileIfMissing(file, Path.Combine(destDir, Path.GetFileName(file)));
        }

        foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
        {
            string name = Path.GetFileName(subDir);
            if (string.Equals(name, "WebView2", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            CopyDirectoryRecursive(subDir, Path.Combine(destDir, name));
        }
    }
}
