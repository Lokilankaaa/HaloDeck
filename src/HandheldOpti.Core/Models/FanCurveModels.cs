namespace HandheldOpti.Core.Models;

public sealed class FanCurvePoint
{
    public int TempC { get; set; }
    public int FanPercent { get; set; }
}

public sealed class FanCurveProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<FanCurvePoint> Points { get; set; } = [];
}

public static class FanCurveEvaluator
{
    public const int MinTempC = 25;
    public const int MaxTempC = 95;
    public const int MinFanPercent = 20;
    public const int MaxFanPercent = 100;

    // Non-bypassable thermal safety floor for Strix Halo handheld thermal envelope.
    public const int SafetyWarnTempC = 82;
    public const int SafetyWarnMinPercent = 80;
    public const int SafetyCriticalTempC = 86;
    public const int SafetyCriticalMinPercent = 100;

    public static List<FanCurveProfile> CreateDefaultCurves() =>
    [
        new FanCurveProfile
        {
            Id = "low_rpm",
            Name = "低噪温控 (30–80%)",
            Points =
            [
                new FanCurvePoint { TempC = 40, FanPercent = 30 },
                new FanCurvePoint { TempC = 55, FanPercent = 38 },
                new FanCurvePoint { TempC = 68, FanPercent = 52 },
                new FanCurvePoint { TempC = 76, FanPercent = 68 },
                new FanCurvePoint { TempC = 82, FanPercent = 85 }
            ]
        },
        new FanCurveProfile
        {
            Id = "linear_60",
            Name = "标准线性 (35–92%)",
            Points =
            [
                new FanCurvePoint { TempC = 40, FanPercent = 35 },
                new FanCurvePoint { TempC = 55, FanPercent = 48 },
                new FanCurvePoint { TempC = 65, FanPercent = 62 },
                new FanCurvePoint { TempC = 75, FanPercent = 78 },
                new FanCurvePoint { TempC = 82, FanPercent = 92 }
            ]
        },
        new FanCurveProfile
        {
            Id = "max_cool",
            Name = "低温优先 (45–100%)",
            Points =
            [
                new FanCurvePoint { TempC = 35, FanPercent = 45 },
                new FanCurvePoint { TempC = 50, FanPercent = 60 },
                new FanCurvePoint { TempC = 62, FanPercent = 78 },
                new FanCurvePoint { TempC = 72, FanPercent = 92 },
                new FanCurvePoint { TempC = 80, FanPercent = 100 }
            ]
        }
    ];

    public static List<FanCurvePoint> NormalizePoints(IEnumerable<FanCurvePoint>? raw)
    {
        var byTemp = new SortedDictionary<int, int>();
        if (raw is not null)
        {
            foreach (var p in raw)
            {
                if (p is null) continue;
                int t = Math.Clamp(p.TempC, MinTempC, MaxTempC);
                int pct = Math.Clamp(p.FanPercent, MinFanPercent, MaxFanPercent);
                byTemp[t] = byTemp.TryGetValue(t, out int existing) ? Math.Max(existing, pct) : pct;
            }
        }

        if (byTemp.Count == 0)
        {
            return CreateDefaultCurves()[1].Points
                .Select(p => new FanCurvePoint { TempC = p.TempC, FanPercent = p.FanPercent })
                .ToList();
        }

        return byTemp.Select(kv => new FanCurvePoint { TempC = kv.Key, FanPercent = kv.Value }).ToList();
    }

    public static List<FanCurveProfile> NormalizeCurves(IEnumerable<FanCurveProfile>? raw)
    {
        var result = new List<FanCurveProfile>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (raw is not null)
        {
            int idx = 1;
            foreach (var c in raw)
            {
                if (c is null) continue;
                string id = string.IsNullOrWhiteSpace(c.Id) ? $"curve_{idx}" : c.Id.Trim();
                while (!seenIds.Add(id))
                {
                    id = $"{id}_{idx++}";
                }

                string name = string.IsNullOrWhiteSpace(c.Name) ? $"自定义曲线 {result.Count + 1}" : c.Name.Trim();
                result.Add(new FanCurveProfile
                {
                    Id = id,
                    Name = name,
                    Points = NormalizePoints(c.Points)
                });
                idx++;
            }
        }

        return result.Count > 0 ? result : CreateDefaultCurves();
    }

    public static FanCurveProfile ResolveActiveCurve(IEnumerable<FanCurveProfile>? curves, string? activeId)
    {
        var list = curves as IList<FanCurveProfile> ?? curves?.ToList() ?? [];
        if (list.Count == 0)
        {
            return CreateDefaultCurves()[1];
        }

        if (!string.IsNullOrWhiteSpace(activeId))
        {
            var hit = list.FirstOrDefault(c => string.Equals(c.Id, activeId, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                return hit;
            }
        }

        return list[0];
    }

    public static int Evaluate(int tempC, FanCurveProfile? curve) =>
        Evaluate(tempC, curve?.Points);

    public static int Evaluate(int tempC, IEnumerable<FanCurvePoint>? rawPoints)
    {
        var pts = NormalizePoints(rawPoints);
        int clampedTemp = Math.Clamp(tempC, 0, 115);

        int pct;
        if (clampedTemp <= pts[0].TempC)
        {
            pct = pts[0].FanPercent;
        }
        else if (clampedTemp >= pts[^1].TempC)
        {
            pct = pts[^1].FanPercent;
        }
        else
        {
            pct = pts[^1].FanPercent;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var p0 = pts[i];
                var p1 = pts[i + 1];
                if (clampedTemp >= p0.TempC && clampedTemp <= p1.TempC)
                {
                    if (p1.TempC == p0.TempC)
                    {
                        pct = Math.Max(p0.FanPercent, p1.FanPercent);
                    }
                    else
                    {
                        double ratio = (clampedTemp - p0.TempC) / (double)(p1.TempC - p0.TempC);
                        pct = (int)Math.Round(p0.FanPercent + (p1.FanPercent - p0.FanPercent) * ratio);
                    }
                    break;
                }
            }
        }

        return ApplySafetyFloor(clampedTemp, pct);
    }

    public static int ApplySafetyFloor(int tempC, int fanPercent)
    {
        int clamped = Math.Clamp(fanPercent, MinFanPercent, MaxFanPercent);
        if (tempC >= SafetyCriticalTempC)
        {
            return Math.Max(clamped, SafetyCriticalMinPercent);
        }

        if (tempC >= SafetyWarnTempC)
        {
            return Math.Max(clamped, SafetyWarnMinPercent);
        }

        return clamped;
    }
}

public static class ZenThermalDecoder
{
    private const uint ThmTconCurTmpRangeSel = 1u << 19;

    /// <summary>
    /// Decodes AMD Zen SMU/SMN THM_TCON_CUR_TMP (0x00059800) register into whole degrees Celsius.
    /// Bits [31:21] carry temperature in 0.125C increments; bit 19 subtracts 49C when set.
    /// </summary>
    public static int DecodeTctlTempC(uint rawReg)
    {
        if (rawReg == 0 || rawReg == uint.MaxValue)
        {
            return 0;
        }

        double tempC = (rawReg >> 21) * 0.125;
        if ((rawReg & ThmTconCurTmpRangeSel) != 0)
        {
            tempC -= 49.0;
        }

        int rounded = (int)Math.Round(tempC);
        return rounded is >= 5 and <= 125 ? rounded : 0;
    }
}
