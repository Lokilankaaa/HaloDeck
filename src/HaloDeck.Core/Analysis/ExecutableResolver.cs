using HaloDeck.Core.Models;

namespace HaloDeck.Core.Analysis;

public sealed record ResolvedGameLayout(
    GameEngine Engine,
    string TargetRenderDirectory,
    string? MainExecutablePath,
    PeArchitecture Architecture,
    UpscalerCapabilities Upscalers,
    bool IsWritable);

public static class ExecutableResolver
{
    private static readonly string[] ExcludedExeKeywords =
    {
        "unins",
        "crashreport",
        "crashhandler",
        "crashpad",
        "checkenv",
        "createdump",
        "prereq",
        "ueprereq",
        "vcredist",
        "dxwebsetup",
        "easyanticheat",
        "battleye",
        "beservice",
        "start_protected_game",
        "launcher",
        "updater",
        "setup",
        "redist",
        "dotnetfx",
        "8za",
        "7za",
        "7z",
        "epicwebhelper",
        "webhelper",
        "cefprocess",
        "installermessage",
        "benchmark",
        "cleanup"
    };

    public static ResolvedGameLayout Resolve(string installDirectory, string? knownExePath = null)
    {
        if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory))
        {
            return new ResolvedGameLayout(
                GameEngine.Unknown,
                installDirectory,
                knownExePath,
                PeArchitecture.Unknown,
                new UpscalerCapabilities(),
                false);
        }

        // If caller already gave an explicit executable inside a real directory, inspect its directory first
        string? explicitDir = !string.IsNullOrWhiteSpace(knownExePath) && File.Exists(knownExePath)
            ? Path.GetDirectoryName(knownExePath)
            : null;

        var engine = DetectEngine(installDirectory);
        string targetDir = installDirectory;
        string? mainExe = !string.IsNullOrWhiteSpace(knownExePath) && File.Exists(knownExePath) ? knownExePath : null;

        if (engine == GameEngine.Unreal)
        {
            var ueShipping = FindUnrealShippingExe(installDirectory);
            if (ueShipping is not null)
            {
                mainExe = ueShipping;
                targetDir = Path.GetDirectoryName(ueShipping)!;
            }
        }
        else if (engine == GameEngine.RedEngine)
        {
            var binX64 = Path.Combine(installDirectory, "bin", "x64");
            if (Directory.Exists(binX64))
            {
                var redExe = PickBestExeInDirectory(binX64);
                if (redExe is not null)
                {
                    mainExe = redExe;
                    targetDir = binX64;
                }
            }
        }
        else if (engine == GameEngine.Unity)
        {
            var unityExe = FindUnityExe(installDirectory);
            if (unityExe is not null)
            {
                mainExe = unityExe;
                targetDir = Path.GetDirectoryName(unityExe)!;
            }
        }

        if (mainExe is null || string.Equals(targetDir, installDirectory, StringComparison.OrdinalIgnoreCase))
        {
            var scored = FindBestExecutableByScore(installDirectory, 4);
            if (scored is not null)
            {
                mainExe = scored;
                targetDir = Path.GetDirectoryName(scored)!;
            }
            else if (explicitDir is not null)
            {
                targetDir = explicitDir;
            }
        }

        var arch = mainExe is not null && File.Exists(mainExe)
            ? PeHeaderReader.ReadArchitecture(mainExe)
            : PeArchitecture.Unknown;

        var upscalers = DetectUpscalers(installDirectory, targetDir);
        bool writable = CheckDirectoryWritable(targetDir);

        return new ResolvedGameLayout(engine, targetDir, mainExe, arch, upscalers, writable);
    }

    public static GameEngine DetectEngine(string installDirectory)
    {
        try
        {
            if (File.Exists(Path.Combine(installDirectory, "UnityPlayer.dll")) ||
                Directory.EnumerateFiles(installDirectory, "UnityPlayer.dll", new EnumerationOptions { MaxRecursionDepth = 2, RecurseSubdirectories = true, IgnoreInaccessible = true }).Any())
            {
                return GameEngine.Unity;
            }

            if (Directory.Exists(Path.Combine(installDirectory, "Engine", "Binaries")) ||
                Directory.EnumerateFiles(installDirectory, "*-Shipping.exe", new EnumerationOptions { MaxRecursionDepth = 4, RecurseSubdirectories = true, IgnoreInaccessible = true }).Any())
            {
                return GameEngine.Unreal;
            }

            if (Directory.Exists(Path.Combine(installDirectory, "bin", "x64")) &&
                (Directory.Exists(Path.Combine(installDirectory, "r6")) || Directory.Exists(Path.Combine(installDirectory, "archive"))))
            {
                return GameEngine.RedEngine;
            }
        }
        catch
        {
            // Ignore access errors
        }

        return GameEngine.Native;
    }

    private static string? FindUnrealShippingExe(string root)
    {
        try
        {
            var candidates = Directory.EnumerateFiles(root, "*-Shipping.exe", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = 5,
                IgnoreInaccessible = true
            })
            .Where(p =>
            {
                var norm = p.Replace('/', '\\');
                return norm.Contains(@"\Binaries\Win64\", StringComparison.OrdinalIgnoreCase) &&
                       !norm.Contains(@"\Engine\Binaries\", StringComparison.OrdinalIgnoreCase) &&
                       !Path.GetFileName(p).Contains("CrashReport", StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(p => new FileInfo(p).Length)
            .ToList();

            if (candidates.Count > 0)
            {
                return candidates[0];
            }

            // Fallback: large Win64 game binary (>= 15 MB) not inside Engine\Binaries
            var win64Candidates = Directory.EnumerateFiles(root, "*.exe", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = 5,
                IgnoreInaccessible = true
            })
            .Where(p =>
            {
                var norm = p.Replace('/', '\\');
                return norm.Contains(@"\Binaries\Win64\", StringComparison.OrdinalIgnoreCase) &&
                       !norm.Contains(@"\Engine\Binaries\", StringComparison.OrdinalIgnoreCase) &&
                       !IsExcludedExe(Path.GetFileName(p)) &&
                       new FileInfo(p).Length >= 15L * 1024 * 1024;
            })
            .OrderByDescending(p => new FileInfo(p).Length)
            .ToList();

            var topWin64 = win64Candidates.FirstOrDefault();
            var topScored = FindBestExecutableByScore(root, 4);
            if (topWin64 is null)
            {
                return topScored;
            }

            if (topScored is not null &&
                new FileInfo(topScored).Length > new FileInfo(topWin64).Length * 2)
            {
                return topScored;
            }

            return topWin64;
        }
        catch
        {
            return null;
        }
    }

    private static string? FindUnityExe(string root)
    {
        try
        {
            var unityDll = Directory.EnumerateFiles(root, "UnityPlayer.dll", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = 2,
                IgnoreInaccessible = true
            }).FirstOrDefault();

            if (unityDll is null)
            {
                return null;
            }

            var dir = Path.GetDirectoryName(unityDll)!;
            return PickBestExeInDirectory(dir);
        }
        catch
        {
            return null;
        }
    }

    private static string? PickBestExeInDirectory(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(p => !IsExcludedExe(Path.GetFileName(p)))
                .OrderByDescending(p => new FileInfo(p).Length)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string? FindBestExecutableByScore(string root, int maxDepth)
    {
        try
        {
            var exes = Directory.EnumerateFiles(root, "*.exe", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = maxDepth,
                IgnoreInaccessible = true
            })
            .Where(p => !IsExcludedExe(Path.GetFileName(p)))
            .ToList();

            if (exes.Count == 0)
            {
                return null;
            }

            string rootName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            return exes
                .Where(exe => !exe.Replace('/', '\\').Contains(@"\Engine\Binaries\", StringComparison.OrdinalIgnoreCase))
                .Select(exe =>
                {
                    var dir = Path.GetDirectoryName(exe)!;
                    double sizeMb = new FileInfo(exe).Length / (1024.0 * 1024.0);
                    int score = 0;

                    if (Path.GetFileName(exe).Contains("-Shipping", StringComparison.OrdinalIgnoreCase))
                        score += 120;

                    if (dir.Replace('/', '\\').Contains(@"\Binaries\Win64", StringComparison.OrdinalIgnoreCase) && sizeMb >= 15.0)
                        score += 35;

                    if (HasAnyUpscalerDllInDir(dir))
                        score += 90;

                    if (File.Exists(Path.Combine(dir, "UnityPlayer.dll")))
                        score += 80;

                    var exeBase = Path.GetFileNameWithoutExtension(exe);
                    if (!string.IsNullOrWhiteSpace(rootName) &&
                        (exeBase.Contains(rootName, StringComparison.OrdinalIgnoreCase) ||
                         rootName.Contains(exeBase, StringComparison.OrdinalIgnoreCase)))
                        score += 40;

                    if (sizeMb >= 100.0)
                        score += 95;
                    else if (sizeMb >= 30.0)
                        score += 55;
                    else if (sizeMb >= 10.0)
                        score += 25;
                    else if (sizeMb < 2.5)
                        score -= 40;

                    if (PeHeaderReader.ReadArchitecture(exe) == PeArchitecture.X64)
                        score += 20;

                    return (exe, score);
                })
                .OrderByDescending(x => x.score)
                .Select(x => x.exe)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static bool HasAnyUpscalerDllInDir(string dir)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.dll", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                if (IsUpscalerDllName(name))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Ignore
        }
        return false;
    }

    public static UpscalerCapabilities DetectUpscalers(string installDirectory, string targetRenderDirectory)
    {
        var caps = new UpscalerCapabilities();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var dirs = new List<string> { installDirectory };
        if (!string.Equals(installDirectory, targetRenderDirectory, StringComparison.OrdinalIgnoreCase))
        {
            dirs.Add(targetRenderDirectory);
        }

        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;

            try
            {
                foreach (var dll in Directory.EnumerateFiles(dir, "*.dll", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    MaxRecursionDepth = 9,
                    IgnoreInaccessible = true
                }))
                {
                    var name = Path.GetFileName(dll);
                    if (!IsUpscalerDllName(name))
                    {
                        continue;
                    }

                    seen.Add(name);
                    var lower = name.ToLowerInvariant();
                    if (lower.Contains("nvngx_dlssg") || lower.Contains("sl.dlss_g"))
                    {
                        caps.HasDlssG = true;
                        caps.HasDlss = true;
                    }
                    else if (lower.Contains("nvngx_dlss") || lower.Contains("sl.dlss"))
                    {
                        caps.HasDlss = true;
                    }
                    if (lower.Contains("libxess") || lower.Contains("sl.xess"))
                    {
                        caps.HasXeSs = true;
                    }
                    if (lower.Contains("fidelityfx") || lower.Contains("ffx_fsr"))
                    {
                        caps.HasFsr = true;
                    }
                    if (lower.Contains("sl.interposer") || lower.Contains("sl.common") || lower.Contains("sl.dlss"))
                    {
                        caps.HasStreamline = true;
                    }
                }
            }
            catch
            {
                // Ignore inaccessible dirs
            }
        }

        caps.DetectedDlls = seen.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        return caps;
    }

    private static bool IsUpscalerDllName(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        return lower.StartsWith("nvngx_dlss", StringComparison.Ordinal) ||
               lower.StartsWith("libxess", StringComparison.Ordinal) ||
               lower.StartsWith("amd_fidelityfx", StringComparison.Ordinal) ||
               lower.StartsWith("ffx_fsr", StringComparison.Ordinal) ||
               lower.StartsWith("sl.interposer", StringComparison.Ordinal) ||
               lower.StartsWith("sl.common", StringComparison.Ordinal) ||
               lower.StartsWith("sl.xess", StringComparison.Ordinal) ||
               lower.StartsWith("sl.dlss", StringComparison.Ordinal);
    }

    private static bool IsExcludedExe(string fileName)
    {
        return ExcludedExeKeywords.Any(kw => fileName.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }

    public static bool CheckDirectoryWritable(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return false;
        }

        var probe = Path.Combine(dir, $".halodeck_probe_{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
