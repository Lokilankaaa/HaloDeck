using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace HandheldOpti.Core.Components;

public static class ArchiveExtractor
{
    private static readonly HashSet<string> CoreMarkerFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "OptiScaler.dll",
        "nvngx.dll",
        "OptiPatcher.asi",
        "fakenvapi.dll",
        "nvapi64.dll"
    };

    /// <summary>
    /// Extracts .7z (via sequential IReader for solid 7z support) and .zip archives
    /// cleanly with Zip-Slip path traversal protection.
    /// </summary>
    public static string ExtractToCleanDirectory(string archivePath, string destinationDirectory)
    {
        if (Directory.Exists(destinationDirectory))
        {
            Directory.Delete(destinationDirectory, recursive: true);
        }
        Directory.CreateDirectory(destinationDirectory);

        var destFullRoot = Path.GetFullPath(destinationDirectory);
        if (!destFullRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            destFullRoot += Path.DirectorySeparatorChar;
        }

        using (var archive = ArchiveFactory.OpenArchive(archivePath))
        {
            if (archive.IsSolid || archive.Type == ArchiveType.SevenZip)
            {
                using var reader = archive.ExtractAllEntries();
                while (reader.MoveToNextEntry())
                {
                    var entry = reader.Entry;
                    if (entry.IsDirectory || string.IsNullOrWhiteSpace(entry.Key))
                    {
                        continue;
                    }

                    var targetPath = ResolveSafeTargetPath(destinationDirectory, destFullRoot, entry.Key);
                    reader.WriteEntryToFile(targetPath, new ExtractionOptions
                    {
                        ExtractFullPath = false,
                        Overwrite = true
                    });
                }
            }
            else
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory || string.IsNullOrWhiteSpace(entry.Key))
                    {
                        continue;
                    }

                    var targetPath = ResolveSafeTargetPath(destinationDirectory, destFullRoot, entry.Key);
                    entry.WriteToFile(targetPath, new ExtractionOptions
                    {
                        ExtractFullPath = false,
                        Overwrite = true
                    });
                }
            }
        }

        return ResolvePayloadRoot(destinationDirectory);
    }

    private static string ResolveSafeTargetPath(string destinationDirectory, string destFullRoot, string entryKey)
    {
        var relative = entryKey.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        var targetPath = Path.GetFullPath(Path.Combine(destinationDirectory, relative));

        if (!targetPath.StartsWith(destFullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsafe archive path detected: {entryKey}");
        }

        var parentDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        return targetPath;
    }

    /// <summary>
    /// Unwraps single nested folder layers (e.g. "OptiScaler_v0.7.9/") until hitting the
    /// directory containing OptiScaler/OptiPatcher/Fakenvapi payload binaries.
    /// </summary>
    public static string ResolvePayloadRoot(string directory)
    {
        var current = directory;
        for (int depth = 0; depth < 3; depth++)
        {
            var files = Directory.GetFiles(current);
            if (files.Any(f => CoreMarkerFiles.Contains(Path.GetFileName(f))))
            {
                return current;
            }

            var subDirs = Directory.GetDirectories(current);
            if (subDirs.Length == 1 && files.Length == 0)
            {
                current = subDirs[0];
                continue;
            }

            string? matchingSub = subDirs.FirstOrDefault(sd =>
                Directory.GetFiles(sd).Any(f => CoreMarkerFiles.Contains(Path.GetFileName(f))));
            if (matchingSub is not null)
            {
                return matchingSub;
            }

            break;
        }

        return current;
    }
}
