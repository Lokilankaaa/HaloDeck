using HandheldOpti.Core.Models;

namespace HandheldOpti.Core.Analysis;

public static class AntiCheatDetector
{
    private static readonly (string Pattern, string DisplayName, AntiCheatRiskLevel Risk)[] Rules =
    {
        ("EasyAntiCheat", "Easy Anti-Cheat (EAC)", AntiCheatRiskLevel.HighRisk),
        ("EAC", "Easy Anti-Cheat (EAC)", AntiCheatRiskLevel.HighRisk),
        ("BattlEye", "BattlEye", AntiCheatRiskLevel.HighRisk),
        ("BEService", "BattlEye Service", AntiCheatRiskLevel.HighRisk),
        ("vgk.sys", "Riot Vanguard", AntiCheatRiskLevel.HighRisk),
        ("vgc.exe", "Riot Vanguard", AntiCheatRiskLevel.HighRisk),
        ("AntiCheatExpert", "Tencent ACE", AntiCheatRiskLevel.HighRisk),
        ("SGuard64", "Tencent ACE (SGuard64)", AntiCheatRiskLevel.HighRisk),
        ("EAAntiCheat", "EA AntiCheat", AntiCheatRiskLevel.HighRisk),
        ("randgrid.sys", "Call of Duty Ricochet", AntiCheatRiskLevel.HighRisk),
        ("nProtect", "nProtect GameGuard", AntiCheatRiskLevel.Warning),
        ("GameGuard", "GameGuard", AntiCheatRiskLevel.Warning),
        ("Wellbia", "XIGNCODE3", AntiCheatRiskLevel.Warning),
        ("xigncode", "XIGNCODE3", AntiCheatRiskLevel.Warning),
        ("DenuvoAntiCheat", "Denuvo Anti-Cheat", AntiCheatRiskLevel.HighRisk)
    };

    public static AntiCheatInfo Detect(string installDirectory, string? targetRenderDirectory = null)
    {
        var info = new AntiCheatInfo();
        if (!Directory.Exists(installDirectory))
        {
            return info;
        }

        var detectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var maxRisk = AntiCheatRiskLevel.None;

        var dirsToInspect = new List<string> { installDirectory };
        if (!string.IsNullOrWhiteSpace(targetRenderDirectory) &&
            Directory.Exists(targetRenderDirectory) &&
            !string.Equals(targetRenderDirectory, installDirectory, StringComparison.OrdinalIgnoreCase))
        {
            dirsToInspect.Add(targetRenderDirectory);
        }

        foreach (var root in dirsToInspect)
        {
            ScanDirectory(root, 0, 3, detectedNames, ref maxRisk);
        }

        info.DetectedSystems = detectedNames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        info.HasAntiCheat = info.DetectedSystems.Count > 0;
        info.RiskLevel = maxRisk;
        return info;
    }

    private static void ScanDirectory(string dir, int depth, int maxDepth, HashSet<string> detected, ref AntiCheatRiskLevel maxRisk)
    {
        if (depth > maxDepth)
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var name = Path.GetFileName(file);
                MatchRules(name, detected, ref maxRisk);
            }

            foreach (var subDir in Directory.EnumerateDirectories(dir))
            {
                var dirName = Path.GetFileName(subDir);
                MatchRules(dirName, detected, ref maxRisk);
                ScanDirectory(subDir, depth + 1, maxDepth, detected, ref maxRisk);
            }
        }
        catch
        {
            // Ignore unauthorized subdirectories
        }
    }

    private static void MatchRules(string candidate, HashSet<string> detected, ref AntiCheatRiskLevel maxRisk)
    {
        foreach (var (pattern, displayName, risk) in Rules)
        {
            if (candidate.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                // Avoid matching random strings with short "EAC" unless exact/prefix/suffix
                if (pattern == "EAC" && !candidate.Equals("EAC", StringComparison.OrdinalIgnoreCase) &&
                    !candidate.StartsWith("EAC", StringComparison.OrdinalIgnoreCase) &&
                    !candidate.Contains("_EAC", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                detected.Add(displayName);
                if (risk > maxRisk)
                {
                    maxRisk = risk;
                }
            }
        }
    }
}
