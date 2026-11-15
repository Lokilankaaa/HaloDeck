using HandheldOpti.Core.Config;
using HandheldOpti.Core.Models;
using Xunit;

namespace HandheldOpti.Tests;

public sealed class HandheldV031Tests
{
    [Fact]
    public void FanCurveEvaluator_InterpolatesAndEnforcesThermalSafetyFloor()
    {
        var points = new List<FanCurvePoint>
        {
            new() { TempC = 40, FanPercent = 25 },
            new() { TempC = 60, FanPercent = 45 },
            new() { TempC = 80, FanPercent = 65 }
        };

        Assert.Equal(25, FanCurveEvaluator.Evaluate(30, points));
        Assert.Equal(35, FanCurveEvaluator.Evaluate(50, points));
        Assert.Equal(65, FanCurveEvaluator.Evaluate(80, points));

        // Safety floor overrides user curves that sit too low at high APU temps
        Assert.True(FanCurveEvaluator.Evaluate(82, points) >= FanCurveEvaluator.SafetyWarnMinPercent);
        Assert.Equal(100, FanCurveEvaluator.Evaluate(86, points));
        Assert.Equal(FanCurveEvaluator.SafetyWarnMinPercent, FanCurveEvaluator.ApplySafetyFloor(83, 25));
        Assert.Equal(100, FanCurveEvaluator.ApplySafetyFloor(88, 30));
    }

    [Fact]
    public void SettingsStore_NormalizesLegacyNaturalLanguageFanModesAndPresets()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "HaloDeck_V031_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string jsonPath = Path.Combine(tempDir, "settings.json");
            File.WriteAllText(jsonPath, """
                {
                  "TdpPresetSilent": 16,
                  "TdpPresetBalanced": 42,
                  "TdpPresetTurbo": 78,
                  "CurrentTdpWatts": 42,
                  "FanMode": "balanced"
                }
                """);

            var store = new SettingsStore(tempDir);
            var loaded = store.Load();

            Assert.Equal("curve", loaded.FanMode);
            Assert.Equal("linear_60", loaded.ActiveFanCurveId);
            Assert.Contains(16, loaded.CustomTdpPresets);
            Assert.Contains(42, loaded.CustomTdpPresets);
            Assert.Contains(78, loaded.CustomTdpPresets);
            Assert.True(loaded.FanCurves.Count >= 3);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SettingsStore_RoundtripsOpenPowerAndThreeStageLimits()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "HaloDeck_V031_Roundtrip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var store = new SettingsStore(tempDir);
            var settings = new AppSettings
            {
                CurrentTdpWatts = 95,
                UnlockThreeStagePower = true,
                SlowPptWatts = 110,
                FastPptWatts = 125,
                CustomTdpPresets = [12, 30, 55, 95, 115],
                FanMode = "curve",
                ActiveFanCurveId = "max_cool"
            };
            store.Save(settings);

            var reloaded = store.Load();
            Assert.Equal(95, reloaded.CurrentTdpWatts);
            Assert.True(reloaded.UnlockThreeStagePower);
            Assert.Equal(110, reloaded.SlowPptWatts);
            Assert.Equal(125, reloaded.FastPptWatts);
            Assert.Equal([12, 30, 55, 95, 115], reloaded.CustomTdpPresets);
            Assert.Equal("max_cool", reloaded.ActiveFanCurveId);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
