using System.Text.Json;
using HandheldOpti.Core.Models;

namespace HandheldOpti.Core.Config;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public string SettingsFilePath => _settingsPath;

    public SettingsStore(string? appDataRoot = null)
    {
        var root = appDataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HandheldOpti");
        Directory.CreateDirectory(root);
        _settingsPath = Path.Combine(root, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            var fresh = new AppSettings();
            Normalize(fresh, hasCustomTdpPresets: false);
            return fresh;
        }

        try
        {
            string raw = File.ReadAllText(_settingsPath);
            bool hasCustomTdp = raw.Contains("\"CustomTdpPresets\"", StringComparison.OrdinalIgnoreCase);
            var settings = JsonSerializer.Deserialize<AppSettings>(raw, JsonOptions) ?? new AppSettings();
            Normalize(settings, hasCustomTdp);
            return settings;
        }
        catch
        {
            var fallback = new AppSettings();
            Normalize(fallback, hasCustomTdpPresets: false);
            return fallback;
        }
    }

    public void Save(AppSettings settings)
    {
        Normalize(settings, hasCustomTdpPresets: true);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public static void Normalize(AppSettings settings, bool hasCustomTdpPresets = true)
    {
        settings.CurrentTdpWatts = Math.Clamp(settings.CurrentTdpWatts, 8, 120);
        settings.BatteryTdpWatts = Math.Clamp(settings.BatteryTdpWatts, 8, 120);
        settings.AcTdpWatts = Math.Clamp(settings.AcTdpWatts, 8, 120);
        settings.SlowPptWatts = Math.Clamp(settings.SlowPptWatts <= 0 ? Math.Min(130, settings.CurrentTdpWatts + 5) : settings.SlowPptWatts, 8, 130);
        settings.FastPptWatts = Math.Clamp(settings.FastPptWatts <= 0 ? Math.Min(140, Math.Max(settings.SlowPptWatts, settings.CurrentTdpWatts + 8)) : settings.FastPptWatts, 8, 140);
        settings.ManualFanPercent = Math.Clamp(settings.ManualFanPercent, FanCurveEvaluator.MinFanPercent, FanCurveEvaluator.MaxFanPercent);

        var presets = (settings.CustomTdpPresets ?? [])
            .Select(w => Math.Clamp(w, 8, 120))
            .Distinct()
            .OrderBy(w => w)
            .ToList();

        if (!hasCustomTdpPresets)
        {
            if (settings.TdpPresetSilent is >= 8 and <= 120 && settings.TdpPresetSilent != 18) presets.Add(settings.TdpPresetSilent);
            if (settings.TdpPresetBalanced is >= 8 and <= 120 && settings.TdpPresetBalanced != 40) presets.Add(settings.TdpPresetBalanced);
            if (settings.TdpPresetTurbo is >= 8 and <= 120 && settings.TdpPresetTurbo != 80) presets.Add(settings.TdpPresetTurbo);
            presets = presets.Distinct().OrderBy(w => w).ToList();
        }

        settings.CustomTdpPresets = presets.Count > 0 ? presets : [15, 25, 35, 45, 60, 80, 100];

        settings.FanCurves = FanCurveEvaluator.NormalizeCurves(settings.FanCurves);

        // Migrate legacy v0.3.0 natural-language fan modes -> technical modes
        string rawFanMode = (settings.FanMode ?? "auto").Trim().ToLowerInvariant();
        switch (rawFanMode)
        {
            case "auto":
                settings.FanMode = "auto";
                break;
            case "manual":
                settings.FanMode = "manual";
                break;
            case "curve":
                settings.FanMode = "curve";
                break;
            case "silent":
                settings.FanMode = "curve";
                settings.ActiveFanCurveId = "low_rpm";
                break;
            case "balanced":
                settings.FanMode = "curve";
                settings.ActiveFanCurveId = "linear_60";
                break;
            case "turbo":
                settings.FanMode = "curve";
                settings.ActiveFanCurveId = "max_cool";
                break;
            default:
                settings.FanMode = "auto";
                break;
        }

        settings.ActiveFanCurveId = FanCurveEvaluator.ResolveActiveCurve(settings.FanCurves, settings.ActiveFanCurveId).Id;

        string rawOsk = (settings.PreferredOskMode ?? "tabtip").Trim().ToLowerInvariant();
        settings.PreferredOskMode = rawOsk == "osk" ? "osk" : "tabtip";
    }
}
