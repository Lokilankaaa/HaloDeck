using System.IO.Compression;
using System.Text.Json;
using HaloDeck.Core.Components;
using HaloDeck.Core.Config;
using HaloDeck.Core.Injection;
using HaloDeck.Core.Models;
using Xunit;

namespace HaloDeck.Tests;

public sealed class XeFgMfgTests : IDisposable
{
    private const string V10Template = """
        [Upscalers]
        Dx12Upscaler=auto
        Dx11Upscaler=auto
        VulkanUpscaler=auto

        [FSR]
        UpscalerIndex=0
        Fsr4GpuSupport=auto
        Fsr4Model=auto
        Fsr4EnableWatermark=false
        FsrAgilitySDKUpgrade=auto

        [FrameGen]
        Enabled=auto
        FGInput=auto
        FGOutput=auto

        [XeFG]
        InterpolationCount=auto
        DynamicMFG=auto
        FramerateTargetDMFG=auto

        [Menu]
        ShortcutKey=0x2D
        FGShortcutKey=auto
        """;

    private const string StableV094Template = """
        [Upscalers]
        Dx12Upscaler=auto
        Dx11Upscaler=auto
        VulkanUpscaler=auto

        [FSR]
        Fsr4ForceEnableInt8=auto

        [FrameGen]
        Enabled=auto
        FGInput=auto
        FGOutput=auto
        """;

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"hd_xefg_mfg_{Guid.NewGuid():N}");

    [Fact]
    public void OptiScalerConfig_DefaultsAndCloneAndJsonCamelCase_WorkAsExpected()
    {
        var cfg = new OptiScalerConfig();
        Assert.Equal(2, cfg.XeFgMultiplier);
        Assert.False(cfg.XeFgDynamicMfg);
        Assert.Equal(0, cfg.XeFgDynamicTargetFps);

        cfg.XeFgMultiplier = 4;
        cfg.XeFgDynamicMfg = true;
        cfg.XeFgDynamicTargetFps = 120;

        var clone = cfg.Clone();
        Assert.Equal(4, clone.XeFgMultiplier);
        Assert.True(clone.XeFgDynamicMfg);
        Assert.Equal(120, clone.XeFgDynamicTargetFps);

        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(clone, opts);
        Assert.Contains("\"xeFgMultiplier\":4", json);
        Assert.Contains("\"xeFgDynamicMfg\":true", json);
        Assert.Contains("\"xeFgDynamicTargetFps\":120", json);

        var deserialized = JsonSerializer.Deserialize<OptiScalerConfig>(json, opts);
        Assert.NotNull(deserialized);
        Assert.Equal(4, deserialized!.XeFgMultiplier);
        Assert.True(deserialized.XeFgDynamicMfg);
        Assert.Equal(120, deserialized.XeFgDynamicTargetFps);
    }

    [Theory]
    [InlineData(1, "1")]
    [InlineData(2, "1")]
    [InlineData(3, "2")]
    [InlineData(4, "3")]
    [InlineData(6, "3")]
    public void ApplyToIni_V10_WritesClampedXeFgInterpolationCountAndPreservesFgShortcut(int multiplier, string expectedCount)
    {
        var doc = IniDocument.Parse(V10Template);
        var cfg = PresetCatalog.GetById("strix-halo-quality").Config.Clone();
        cfg.XeFgMultiplier = multiplier;
        cfg.XeFgDynamicMfg = false;
        cfg.XeFgDynamicTargetFps = 90; // Ignored when DynamicMFG is false

        PresetCatalog.ApplyToIni(doc, cfg, gameHasNativeDlssG: true);

        Assert.Equal("xefg", doc.Get("FrameGen", "FGOutput"));
        Assert.Equal(expectedCount, doc.Get("XeFG", "InterpolationCount"));
        Assert.Equal("false", doc.Get("XeFG", "DynamicMFG"));
        Assert.Equal("auto", doc.Get("XeFG", "FramerateTargetDMFG"));
        Assert.Equal("auto", doc.Get("Menu", "FGShortcutKey"));
    }

    [Theory]
    [InlineData(0, "auto")]
    [InlineData(5, "auto")]
    [InlineData(9, "auto")]
    [InlineData(10, "10")]
    [InlineData(60, "60")]
    [InlineData(90, "90")]
    [InlineData(120, "120")]
    public void ApplyToIni_V10_WritesDynamicMfgAndTargetFpsBoundaries(int targetFps, string expectedIniValue)
    {
        var doc = IniDocument.Parse(V10Template);
        var cfg = PresetCatalog.GetById("strix-halo-quality").Config.Clone();
        cfg.XeFgMultiplier = 4;
        cfg.XeFgDynamicMfg = true;
        cfg.XeFgDynamicTargetFps = targetFps;

        PresetCatalog.ApplyToIni(doc, cfg);

        Assert.Equal("3", doc.Get("XeFG", "InterpolationCount"));
        Assert.Equal("true", doc.Get("XeFG", "DynamicMFG"));
        Assert.Equal(expectedIniValue, doc.Get("XeFG", "FramerateTargetDMFG"));
    }

    [Theory]
    [InlineData("optifg")]
    [InlineData("nofg")]
    [InlineData("nukems")]
    public void ApplyToIni_V10_ResetsXeFgMfgKeysToAutoWhenSwitchingAwayFromXeFg(string nextFgType)
    {
        var doc = IniDocument.Parse(V10Template);
        var cfg = PresetCatalog.GetById("strix-halo-quality").Config.Clone();
        cfg.XeFgMultiplier = 4;
        cfg.XeFgDynamicMfg = true;
        cfg.XeFgDynamicTargetFps = 90;
        PresetCatalog.ApplyToIni(doc, cfg);

        Assert.Equal("3", doc.Get("XeFG", "InterpolationCount"));
        Assert.Equal("true", doc.Get("XeFG", "DynamicMFG"));
        Assert.Equal("90", doc.Get("XeFG", "FramerateTargetDMFG"));

        cfg.FgType = nextFgType;
        PresetCatalog.ApplyToIni(doc, cfg);

        Assert.Equal("auto", doc.Get("XeFG", "InterpolationCount"));
        Assert.Equal("auto", doc.Get("XeFG", "DynamicMFG"));
        Assert.Equal("auto", doc.Get("XeFG", "FramerateTargetDMFG"));
    }

    [Fact]
    public void ApplyToIni_StableV094_DoesNotWriteV10XeFgKeys()
    {
        var doc = IniDocument.Parse(StableV094Template);
        var cfg = PresetCatalog.GetById("strix-halo-quality").Config.Clone();
        cfg.XeFgMultiplier = 4;
        cfg.XeFgDynamicMfg = true;
        cfg.XeFgDynamicTargetFps = 120;

        PresetCatalog.ApplyToIni(doc, cfg);

        Assert.Null(doc.Get("XeFG", "InterpolationCount"));
        Assert.Null(doc.Get("XeFG", "DynamicMFG"));
        Assert.Null(doc.Get("XeFG", "FramerateTargetDMFG"));
    }

    [Fact]
    public void ReadConfigFromIni_RoundTripsXeFgMultiplierDynamicMfgAndFloatTargets()
    {
        var doc = IniDocument.Parse(V10Template);
        var cfg = PresetCatalog.GetById("strix-halo-quality").Config.Clone();
        cfg.XeFgMultiplier = 3;
        cfg.XeFgDynamicMfg = true;
        cfg.XeFgDynamicTargetFps = 90;
        PresetCatalog.ApplyToIni(doc, cfg, gameHasNativeDlssG: true);

        var parsed = PresetCatalog.ReadConfigFromIni(doc);
        Assert.Equal("xefg", parsed.FgType);
        Assert.Equal(3, parsed.XeFgMultiplier);
        Assert.True(parsed.XeFgDynamicMfg);
        Assert.Equal(90, parsed.XeFgDynamicTargetFps);
        Assert.Equal("custom", PresetCatalog.MatchPresetId(parsed));

        // Float value from OptiScaler menu save (e.g. 116.0 / 0.0)
        doc.Set("XeFG", "InterpolationCount", "3");
        doc.Set("XeFG", "FramerateTargetDMFG", "116.0");
        var parsedFloat = PresetCatalog.ReadConfigFromIni(doc);
        Assert.Equal(4, parsedFloat.XeFgMultiplier);
        Assert.Equal(116, parsedFloat.XeFgDynamicTargetFps);

        doc.Set("XeFG", "FramerateTargetDMFG", "0.0");
        var parsedZeroFloat = PresetCatalog.ReadConfigFromIni(doc);
        Assert.Equal(0, parsedZeroFloat.XeFgDynamicTargetFps);

        // Auto resets back to 2X / false / 0 even when given a 4X seed
        doc.Set("XeFG", "InterpolationCount", "auto");
        doc.Set("XeFG", "DynamicMFG", "auto");
        doc.Set("XeFG", "FramerateTargetDMFG", "auto");
        var parsedAuto = PresetCatalog.ReadConfigFromIni(doc, seed: cfg);
        Assert.Equal(2, parsedAuto.XeFgMultiplier);
        Assert.False(parsedAuto.XeFgDynamicMfg);
        Assert.Equal(0, parsedAuto.XeFgDynamicTargetFps);
    }

    [Fact]
    public void MatchPresetId_DistinguishesDefault2XFromMfgAndDmfgOverrides()
    {
        var quality = PresetCatalog.GetById("strix-halo-quality").Config.Clone();
        Assert.Equal("strix-halo-quality", PresetCatalog.MatchPresetId(quality));

        // Target FPS alone when DMFG is off does not invalidate default 2X preset match
        quality.XeFgDynamicTargetFps = 90;
        Assert.Equal("strix-halo-quality", PresetCatalog.MatchPresetId(quality));

        // 3X or 4X must not misidentify as default 2X preset
        var cfg3x = PresetCatalog.GetById("strix-halo-quality").Config.Clone();
        cfg3x.XeFgMultiplier = 3;
        Assert.Equal("custom", PresetCatalog.MatchPresetId(cfg3x));

        var cfg4x = PresetCatalog.GetById("strix-halo-quality").Config.Clone();
        cfg4x.XeFgMultiplier = 4;
        Assert.Equal("custom", PresetCatalog.MatchPresetId(cfg4x));

        // 2X + DMFG enabled must also report custom
        var cfgDmfg = PresetCatalog.GetById("strix-halo-quality").Config.Clone();
        cfgDmfg.XeFgDynamicMfg = true;
        Assert.Equal("custom", PresetCatalog.MatchPresetId(cfgDmfg));

        // Non-XeFG preset remains matched regardless of leftover XeFgMultiplier value
        var noFg = PresetCatalog.GetById("low-latency-upscale").Config.Clone();
        noFg.XeFgMultiplier = 4;
        noFg.XeFgDynamicMfg = true;
        Assert.Equal("low-latency-upscale", PresetCatalog.MatchPresetId(noFg));
    }

    [Fact]
    public async Task InjectionEngine_V10Nightly_DeploysXeFgRuntimeDllsAndPersistsMfgIniAndRestoresCleanly()
    {
        var appData = Path.Combine(_tempRoot, "AppData");
        var staging = Path.Combine(_tempRoot, "Staging");
        var gameDir = Path.Combine(_tempRoot, "Game");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(gameDir);

        var nightlyZip = Path.Combine(staging, "OptiScaler_v10.zip");
        using (var fs = new FileStream(nightlyZip, FileMode.Create))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            void Add(string name, string content)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }

            Add("OptiScaler.dll", "OPTISCALER_V10_BINARY");
            Add("OptiScaler.ini", V10Template);
            Add("OptiScaler/libxess_fg.dll", "XESS3_FG_DLL_1_3_1_78");
            Add("OptiScaler/libxell.dll", "XELL_DLL_PAYLOAD");
        }

        var cm = new ComponentManager(appData);
        cm.ImportLocalPackage(ComponentSlot.OptiScalerNightly, nightlyZip, "v10.0.0-pre1");

        var game = new GameEntry
        {
            Id = "custom:xefg-mfg-test",
            Title = "XeFG MFG Test Game",
            InstallDirectory = gameDir,
            TargetRenderDirectory = gameDir,
            Architecture = PeArchitecture.X64,
            IsWritable = true
        };

        var cfg = PresetCatalog.GetById("strix-halo-quality").Config.Clone();
        cfg.EnableOptiPatcher = false;
        cfg.EnableFakenvapi = false;
        cfg.XeFgMultiplier = 4;
        cfg.XeFgDynamicMfg = true;
        cfg.XeFgDynamicTargetFps = 120;

        var engine = new InjectionEngine(cm, appData);
        var injectResult = await engine.InjectAsync(game, cfg, nameof(ComponentSlot.OptiScalerNightly), "custom");

        Assert.True(injectResult.Success, injectResult.Message);
        Assert.True(File.Exists(Path.Combine(gameDir, "OptiScaler", "libxess_fg.dll")));
        Assert.True(File.Exists(Path.Combine(gameDir, "OptiScaler", "libxell.dll")));

        var writtenIni = IniDocument.Load(Path.Combine(gameDir, "OptiScaler.ini"));
        Assert.Equal("3", writtenIni.Get("XeFG", "InterpolationCount"));
        Assert.Equal("true", writtenIni.Get("XeFG", "DynamicMFG"));
        Assert.Equal("120", writtenIni.Get("XeFG", "FramerateTargetDMFG"));
        Assert.Equal("auto", writtenIni.Get("Menu", "FGShortcutKey"));

        var restoreResult = await engine.RestoreCleanAsync(game);
        Assert.True(restoreResult.Success, restoreResult.Message);
        Assert.False(File.Exists(Path.Combine(gameDir, "OptiScaler", "libxess_fg.dll")));
        Assert.False(File.Exists(Path.Combine(gameDir, "OptiScaler", "libxell.dll")));
        Assert.False(File.Exists(Path.Combine(gameDir, "OptiScaler.ini")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
