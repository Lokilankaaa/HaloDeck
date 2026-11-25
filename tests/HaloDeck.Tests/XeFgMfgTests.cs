using System.IO.Compression;
using System.Net;
using System.Text;
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

        [Plugins]
        LoadAsiPlugins=auto

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

    [Fact]
    public void ApplyToIni_V10_ForcesFrameGenEnabledTrueForMfgOrDmfgOnNativeDlssG_AndKeepsAutoForStandard2X()
    {
        var doc2x = IniDocument.Parse(V10Template);
        var cfg2x = PresetCatalog.GetById("strix-halo-quality").Config.Clone();
        cfg2x.XeFgMultiplier = 2;
        cfg2x.XeFgDynamicMfg = false;
        PresetCatalog.ApplyToIni(doc2x, cfg2x, gameHasNativeDlssG: true);

        Assert.Equal("auto", doc2x.Get("FrameGen", "Enabled"));
        Assert.Equal("dlssg", doc2x.Get("FrameGen", "FGInput"));
        Assert.Equal("xefg", doc2x.Get("FrameGen", "FGOutput"));
        Assert.Equal("1", doc2x.Get("XeFG", "InterpolationCount"));
        Assert.Equal("false", doc2x.Get("XeFG", "DynamicMFG"));

        // 3X or 4X on native DLSS-G MUST write Enabled=true so OptiScaler's Config::FGFollowsGame() doesn't clamp to game's 2X
        var doc4x = IniDocument.Parse(V10Template);
        var cfg4x = PresetCatalog.GetById("strix-halo-quality").Config.Clone();
        cfg4x.XeFgMultiplier = 4;
        cfg4x.XeFgDynamicMfg = false;
        PresetCatalog.ApplyToIni(doc4x, cfg4x, gameHasNativeDlssG: true);

        Assert.Equal("true", doc4x.Get("FrameGen", "Enabled"));
        Assert.Equal("dlssg", doc4x.Get("FrameGen", "FGInput"));
        Assert.Equal("xefg", doc4x.Get("FrameGen", "FGOutput"));
        Assert.Equal("3", doc4x.Get("XeFG", "InterpolationCount"));
        Assert.Equal("auto", doc4x.Get("Menu", "FGShortcutKey"));

        // 2X with DynamicMFG=true also requires Enabled=true to override game's fixed DLSS-G multiplier
        var docDmfg = IniDocument.Parse(V10Template);
        var cfgDmfg = PresetCatalog.GetById("strix-halo-quality").Config.Clone();
        cfgDmfg.XeFgMultiplier = 2;
        cfgDmfg.XeFgDynamicMfg = true;
        cfgDmfg.XeFgDynamicTargetFps = 90;
        PresetCatalog.ApplyToIni(docDmfg, cfgDmfg, gameHasNativeDlssG: true);

        Assert.Equal("true", docDmfg.Get("FrameGen", "Enabled"));
        Assert.Equal("1", docDmfg.Get("XeFG", "InterpolationCount"));
        Assert.Equal("true", docDmfg.Get("XeFG", "DynamicMFG"));
        Assert.Equal("90", docDmfg.Get("XeFG", "FramerateTargetDMFG"));
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

        PresetCatalog.ApplyToIni(doc, cfg, gameHasNativeDlssG: false);

        Assert.Equal("true", doc.Get("FrameGen", "Enabled"));
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

        quality.XeFgDynamicTargetFps = 90;
        Assert.Equal("strix-halo-quality", PresetCatalog.MatchPresetId(quality));

        var cfg3x = PresetCatalog.GetById("strix-halo-quality").Config.Clone();
        cfg3x.XeFgMultiplier = 3;
        Assert.Equal("custom", PresetCatalog.MatchPresetId(cfg3x));

        var cfg4x = PresetCatalog.GetById("strix-halo-quality").Config.Clone();
        cfg4x.XeFgMultiplier = 4;
        Assert.Equal("custom", PresetCatalog.MatchPresetId(cfg4x));

        var cfgDmfg = PresetCatalog.GetById("strix-halo-quality").Config.Clone();
        cfgDmfg.XeFgDynamicMfg = true;
        Assert.Equal("custom", PresetCatalog.MatchPresetId(cfgDmfg));

        var noFg = PresetCatalog.GetById("low-latency-upscale").Config.Clone();
        noFg.XeFgMultiplier = 4;
        noFg.XeFgDynamicMfg = true;
        Assert.Equal("low-latency-upscale", PresetCatalog.MatchPresetId(noFg));
    }

    [Fact]
    public void ComponentManager_IncludesMfgUnlockerWithAttribution_AndParsesAtomAndExpandedAssets()
    {
        var appData = Path.Combine(_tempRoot, "AppDataCatalog");
        var cm = new ComponentManager(appData);
        var mfg = cm.GetComponent(ComponentSlot.MfgUnlocker);

        Assert.Equal("dashdogy/MFG-Unlocker", mfg.RepoSlug);
        Assert.Contains("dashdogy", mfg.DisplayName);
        Assert.Contains("MIT", mfg.Description);
        Assert.Contains("github.com/dashdogy/MFG-Unlocker", mfg.Description);

        const string atomXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry>
                <link rel="alternate" type="text/html" href="https://github.com/dashdogy/MFG-Unlocker/releases/tag/v1.7.2"/>
                <title>Universal MFG Unlocker V1.7.2</title>
              </entry>
            </feed>
            """;
        Assert.Equal("v1.7.2", ComponentManager.ParseAtomLatestTag(atomXml));

        const string expandedHtml = """
            <ul>
              <li><a href="/dashdogy/MFG-Unlocker/releases/download/v1.7.2/SHA256SUMS.txt">SHA256SUMS.txt</a></li>
              <li><a href="/dashdogy/MFG-Unlocker/releases/download/v1.7.2/MFGUnlocker-v1.7.2.zip">MFGUnlocker-v1.7.2.zip</a></li>
            </ul>
            """;
        var assets = ComponentManager.ParseExpandedAssetsHtml(expandedHtml, "dashdogy/MFG-Unlocker");
        var asset = Assert.Single(assets);
        Assert.Equal("MFGUnlocker-v1.7.2.zip", asset.Name);
        Assert.Equal("https://github.com/dashdogy/MFG-Unlocker/releases/download/v1.7.2/MFGUnlocker-v1.7.2.zip", asset.DownloadUrl);
    }

    [Fact]
    public async Task ComponentManager_FallsBackToZeroRateLimitAtomAndMirrorDownloadWhenApiReturns403()
    {
        var appData = Path.Combine(_tempRoot, "AppData403");
        var staging = Path.Combine(_tempRoot, "Staging403");
        Directory.CreateDirectory(staging);

        var payloadZipBytes = CreateZipBytes(new Dictionary<string, string>
        {
            ["MFGUnlocker.dll"] = "MFG_UNLOCKER_V172_BINARY"
        });

        var requestedUrls = new List<string>();
        var handler = new StubHttpMessageHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            requestedUrls.Add(url);

            if (url.StartsWith("https://api.github.com/", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent("API rate limit exceeded")
                };
            }

            if (url == "https://ghfast.top/https://github.com/dashdogy/MFG-Unlocker/releases.atom")
            {
                const string atom = """
                    <?xml version="1.0" encoding="UTF-8"?>
                    <feed xmlns="http://www.w3.org/2005/Atom">
                      <entry>
                        <link rel="alternate" type="text/html" href="https://github.com/dashdogy/MFG-Unlocker/releases/tag/v1.7.2"/>
                      </entry>
                    </feed>
                    """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(atom, Encoding.UTF8, "application/atom+xml")
                };
            }

            if (url == "https://ghfast.top/https://github.com/dashdogy/MFG-Unlocker/releases/expanded_assets/v1.7.2")
            {
                const string html = """
                    <a href="/dashdogy/MFG-Unlocker/releases/download/v1.7.2/MFGUnlocker-v1.7.2.zip">MFGUnlocker-v1.7.2.zip</a>
                    """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(html, Encoding.UTF8, "text/html")
                };
            }

            if (url == "https://ghfast.top/https://github.com/dashdogy/MFG-Unlocker/releases/download/v1.7.2/MFGUnlocker-v1.7.2.zip")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(payloadZipBytes)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var cm = new ComponentManager(appData, handler);
        var info = await cm.InstallOrUpdateAsync(
            ComponentSlot.MfgUnlocker,
            progress: null,
            mirrorPrefix: "https://ghfast.top/");

        Assert.True(info.IsInstalled);
        Assert.Equal("v1.7.2", info.LatestVersion);
        Assert.Equal("v1.7.2", info.InstalledVersion);
        var binPath = cm.ResolveMfgUnlockerBinaryPath();
        Assert.NotNull(binPath);
        Assert.Equal("MFGUnlocker.dll", Path.GetFileName(binPath));
        Assert.Contains("https://ghfast.top/https://github.com/dashdogy/MFG-Unlocker/releases/download/v1.7.2/MFGUnlocker-v1.7.2.zip", requestedUrls);
    }

    [Fact]
    public async Task InjectionEngine_V10Nightly_DeploysPluginsToOptiScalerSubfolder_AndManagesMfgUnlockerLifecycle()
    {
        var appData = Path.Combine(_tempRoot, "AppDataV10");
        var staging = Path.Combine(_tempRoot, "StagingV10");
        var gameDir = Path.Combine(_tempRoot, "GameV10");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(gameDir);

        var nightlyZip = CreateZipPackage(
            Path.Combine(staging, "OptiScaler_v10.zip"),
            new Dictionary<string, string>
            {
                ["OptiScaler.dll"] = "OPTISCALER_V10_BINARY",
                ["OptiScaler.ini"] = V10Template,
                ["OptiScaler/libxess_fg.dll"] = "XESS3_FG_DLL_1_3_1_78",
                ["OptiScaler/libxell.dll"] = "XELL_DLL_PAYLOAD"
            });

        var patcherZip = CreateZipPackage(
            Path.Combine(staging, "OptiPatcher.zip"),
            new Dictionary<string, string> { ["OptiPatcher.asi"] = "OPTIPATCHER_ASI" });

        var mfgZip = CreateZipPackage(
            Path.Combine(staging, "MFGUnlocker-v1.7.2.zip"),
            new Dictionary<string, string> { ["MFGUnlocker.dll"] = "MFG_UNLOCKER_ASI_PAYLOAD" });

        var cm = new ComponentManager(appData);
        cm.ImportLocalPackage(ComponentSlot.OptiScalerNightly, nightlyZip, "v10.0.0-pre1");
        cm.ImportLocalPackage(ComponentSlot.OptiPatcher, patcherZip, "v0.3.1");

        var game = new GameEntry
        {
            Id = "custom:xefg-mfg-v10",
            Title = "XeFG MFG V10 Game",
            InstallDirectory = gameDir,
            TargetRenderDirectory = gameDir,
            Architecture = PeArchitecture.X64,
            NativeUpscalers = new UpscalerCapabilities { HasDlss = true, HasDlssG = true },
            IsWritable = true
        };

        var engine = new InjectionEngine(cm, appData);

        // 1. Missing MfgUnlocker when 4X requested -> emits clear warning
        var cfg4x = PresetCatalog.GetById("strix-halo-quality").Config.Clone();
        cfg4x.EnableOptiPatcher = false; // Also verify LoadAsiPlugins=true once MfgUnlocker is installed
        cfg4x.EnableFakenvapi = false;
        cfg4x.XeFgMultiplier = 4;
        cfg4x.XeFgDynamicMfg = false;

        var missingRes = await engine.InjectAsync(game, cfg4x, nameof(ComponentSlot.OptiScalerNightly), "custom");
        Assert.True(missingRes.Success, missingRes.Message);
        Assert.Contains(missingRes.Warnings, w => w.Contains("MFG Unlocker"));
        Assert.False(File.Exists(Path.Combine(gameDir, "OptiScaler", "plugins", "MFGUnlocker.asi")));

        // 2. Import MFG Unlocker + re-inject 4X with OptiPatcher=true -> both ASI plugins go to OptiScaler\plugins\, NOT root plugins\
        cm.ImportLocalPackage(ComponentSlot.MfgUnlocker, mfgZip, "v1.7.2");
        cfg4x.EnableOptiPatcher = true;
        var res4x = await engine.InjectAsync(game, cfg4x, nameof(ComponentSlot.OptiScalerNightly), "custom");

        Assert.True(res4x.Success, res4x.Message);
        Assert.True(res4x.Manifest!.HasMfgUnlocker);
        Assert.True(File.Exists(Path.Combine(gameDir, "OptiScaler", "plugins", "MFGUnlocker.asi")));
        Assert.True(File.Exists(Path.Combine(gameDir, "OptiScaler", "plugins", "OptiPatcher.asi")));
        Assert.False(File.Exists(Path.Combine(gameDir, "plugins", "MFGUnlocker.asi")));
        Assert.False(File.Exists(Path.Combine(gameDir, "plugins", "OptiPatcher.asi")));

        var ini4x = IniDocument.Load(Path.Combine(gameDir, "OptiScaler.ini"));
        Assert.Equal("true", ini4x.Get("FrameGen", "Enabled"));
        Assert.Equal("3", ini4x.Get("XeFG", "InterpolationCount"));
        Assert.Equal("true", ini4x.Get("Plugins", "LoadAsiPlugins"));

        // 3. Even if EnableOptiPatcher=false, 4X MfgUnlocker keeps LoadAsiPlugins=true
        cfg4x.EnableOptiPatcher = false;
        var res4xNoPatcher = await engine.InjectAsync(game, cfg4x, nameof(ComponentSlot.OptiScalerNightly), "custom");
        Assert.True(res4xNoPatcher.Success);
        Assert.False(File.Exists(Path.Combine(gameDir, "OptiScaler", "plugins", "OptiPatcher.asi")));
        Assert.True(File.Exists(Path.Combine(gameDir, "OptiScaler", "plugins", "MFGUnlocker.asi")));
        var ini4xNoPatcher = IniDocument.Load(Path.Combine(gameDir, "OptiScaler.ini"));
        Assert.Equal("true", ini4xNoPatcher.Get("Plugins", "LoadAsiPlugins"));

        // 4. Switch back to standard 2X (non-DMFG) -> removes managed MFGUnlocker.asi & returns Enabled=auto on native DLSS-G
        var cfg2x = PresetCatalog.GetById("strix-halo-quality").Config.Clone();
        cfg2x.EnableFakenvapi = false;
        cfg2x.XeFgMultiplier = 2;
        cfg2x.XeFgDynamicMfg = false;
        var res2x = await engine.InjectAsync(game, cfg2x, nameof(ComponentSlot.OptiScalerNightly), "strix-halo-quality");
        Assert.True(res2x.Success);
        Assert.False(res2x.Manifest!.HasMfgUnlocker);
        Assert.False(File.Exists(Path.Combine(gameDir, "OptiScaler", "plugins", "MFGUnlocker.asi")));
        Assert.True(File.Exists(Path.Combine(gameDir, "OptiScaler", "plugins", "OptiPatcher.asi")));
        var ini2x = IniDocument.Load(Path.Combine(gameDir, "OptiScaler.ini"));
        Assert.Equal("auto", ini2x.Get("FrameGen", "Enabled"));
        Assert.Equal("1", ini2x.Get("XeFG", "InterpolationCount"));

        // 5. Re-enable 4X then RestoreCleanAsync -> removes OptiScaler\plugins & OptiScaler folders completely
        var res4xAgain = await engine.InjectAsync(game, cfg4x, nameof(ComponentSlot.OptiScalerNightly), "custom");
        Assert.True(res4xAgain.Success);
        Assert.True(File.Exists(Path.Combine(gameDir, "OptiScaler", "plugins", "MFGUnlocker.asi")));

        var restoreRes = await engine.RestoreCleanAsync(game);
        Assert.True(restoreRes.Success, restoreRes.Message);
        Assert.False(File.Exists(Path.Combine(gameDir, "OptiScaler", "plugins", "MFGUnlocker.asi")));
        Assert.False(Directory.Exists(Path.Combine(gameDir, "OptiScaler", "plugins")));
        Assert.False(Directory.Exists(Path.Combine(gameDir, "OptiScaler")));
        Assert.False(File.Exists(Path.Combine(gameDir, "OptiScaler.ini")));
    }

    [Fact]
    public async Task InjectionEngine_DoesNotDeleteUserPlacedMfgUnlockerWhenNotManaged()
    {
        var appData = Path.Combine(_tempRoot, "AppDataUser");
        var staging = Path.Combine(_tempRoot, "StagingUser");
        var gameDir = Path.Combine(_tempRoot, "GameUser");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(Path.Combine(gameDir, "OptiScaler", "plugins"));

        var userMfgPath = Path.Combine(gameDir, "OptiScaler", "plugins", "MFGUnlocker.asi");
        await File.WriteAllTextAsync(userMfgPath, "USER_CUSTOM_MFG_UNLOCKER");

        var nightlyZip = CreateZipPackage(
            Path.Combine(staging, "OptiScaler_v10.zip"),
            new Dictionary<string, string>
            {
                ["OptiScaler.dll"] = "OPTISCALER_V10_BINARY",
                ["OptiScaler.ini"] = V10Template,
                ["OptiScaler/libxess_fg.dll"] = "XESS3_FG"
            });

        var cm = new ComponentManager(appData);
        cm.ImportLocalPackage(ComponentSlot.OptiScalerNightly, nightlyZip, "v10.0.0-pre1");

        var game = new GameEntry
        {
            Id = "custom:user-mfg",
            Title = "User Placed MFG Game",
            InstallDirectory = gameDir,
            TargetRenderDirectory = gameDir,
            Architecture = PeArchitecture.X64,
            IsWritable = true
        };

        var cfg2x = PresetCatalog.GetById("strix-halo-quality").Config.Clone();
        cfg2x.EnableOptiPatcher = false;
        cfg2x.EnableFakenvapi = false;
        cfg2x.XeFgMultiplier = 2;

        var engine = new InjectionEngine(cm, appData);
        var res = await engine.InjectAsync(game, cfg2x, nameof(ComponentSlot.OptiScalerNightly), "strix-halo-quality");
        Assert.True(res.Success);
        Assert.True(File.Exists(userMfgPath));
        Assert.Equal("USER_CUSTOM_MFG_UNLOCKER", await File.ReadAllTextAsync(userMfgPath));
    }

    [Fact]
    public void ApplyToIni_DefaultDxgiVramIsZero_AndOverwritesStaleSpoofedVramWithAuto()
    {
        var cfg = new OptiScalerConfig();
        Assert.Equal(0, cfg.DxgiVramGb);

        var iniWithStaleVram = V10Template + "\n[Spoofing]\nDxgi=auto\nDxgiVRAM=10\n";
        var doc = IniDocument.Parse(iniWithStaleVram);

        PresetCatalog.ApplyToIni(doc, cfg);

        Assert.Equal("auto", doc.Get("Spoofing", "DxgiVRAM"));
        Assert.Equal(0, PresetCatalog.ReadConfigFromIni(doc, cfg).DxgiVramGb);
    }

    [Fact]
    public async Task InjectionEngine_AutoResetsLegacyOverReportedDxgiVramToAuto_On8GbDedicatedVramHost()
    {
        var appData = Path.Combine(_tempRoot, "AppDataVramGuard");
        var staging = Path.Combine(_tempRoot, "StagingVramGuard");
        var gameDir = Path.Combine(_tempRoot, "GameVramGuard");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(gameDir);

        var nightlyZip = CreateZipPackage(
            Path.Combine(staging, "OptiScaler_v10.zip"),
            new Dictionary<string, string>
            {
                ["OptiScaler.dll"] = "OPTISCALER_V10_BINARY",
                ["OptiScaler.ini"] = V10Template + "\n[Spoofing]\nDxgi=auto\nDxgiVRAM=auto\n"
            });

        var cm = new ComponentManager(appData);
        cm.ImportLocalPackage(ComponentSlot.OptiScalerNightly, nightlyZip, "v10.0.0-pre1");

        var game = new GameEntry
        {
            Id = "custom:vram-guard",
            Title = "VRAM Guard Game",
            InstallDirectory = gameDir,
            TargetRenderDirectory = gameDir,
            Architecture = PeArchitecture.X64,
            IsWritable = true
        };

        var legacyCfg = PresetCatalog.GetById("strix-halo-quality").Config.Clone();
        legacyCfg.EnableOptiPatcher = false;
        legacyCfg.EnableFakenvapi = false;
        legacyCfg.DxgiVramGb = 10; // Simulates legacy saved manifest/UI value > 8014 MB

        var engine8Gb = new InjectionEngine(cm, appData, dedicatedVramMbProvider: () => 8014);
        var res = await engine8Gb.InjectAsync(game, legacyCfg, nameof(ComponentSlot.OptiScalerNightly), "strix-halo-quality");
        Assert.True(res.Success);
        Assert.NotNull(res.Manifest);
        Assert.Equal(0, res.Manifest!.AppliedConfig.DxgiVramGb);
        Assert.Contains(res.Warnings, w => w.Contains("DxgiVRAM=10 GB 自动重置为 auto"));

        var ini = IniDocument.Load(Path.Combine(gameDir, "OptiScaler.ini"));
        Assert.Equal("auto", ini.Get("Spoofing", "DxgiVRAM"));
    }

    [Fact]
    public async Task InjectionEngine_PreservesExplicitDxgiVramWithinPhysicalBudget_AndSkipsAutoUmaUnder2Gb()
    {
        var appData = Path.Combine(_tempRoot, "AppDataVramExplicit");
        var staging = Path.Combine(_tempRoot, "StagingVramExplicit");
        var gameDir = Path.Combine(_tempRoot, "GameVramExplicit");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(gameDir);

        var nightlyZip = CreateZipPackage(
            Path.Combine(staging, "OptiScaler_v10.zip"),
            new Dictionary<string, string>
            {
                ["OptiScaler.dll"] = "OPTISCALER_V10_BINARY",
                ["OptiScaler.ini"] = V10Template + "\n[Spoofing]\nDxgi=auto\nDxgiVRAM=auto\n"
            });

        var cm = new ComponentManager(appData);
        cm.ImportLocalPackage(ComponentSlot.OptiScalerNightly, nightlyZip, "v10.0.0-pre1");

        var game = new GameEntry
        {
            Id = "custom:vram-explicit",
            Title = "VRAM Explicit Game",
            InstallDirectory = gameDir,
            TargetRenderDirectory = gameDir,
            Architecture = PeArchitecture.X64,
            IsWritable = true
        };

        var explicitCfg = PresetCatalog.GetById("strix-halo-quality").Config.Clone();
        explicitCfg.EnableOptiPatcher = false;
        explicitCfg.EnableFakenvapi = false;
        explicitCfg.DxgiVramGb = 8;

        // 16 GB physical VRAM host: explicit 8 GB is within budget -> preserved without warning
        var engine16Gb = new InjectionEngine(cm, appData, dedicatedVramMbProvider: () => 16384);
        var res16 = await engine16Gb.InjectAsync(game, explicitCfg, nameof(ComponentSlot.OptiScalerNightly), "strix-halo-quality");
        Assert.True(res16.Success);
        Assert.Equal(8, res16.Manifest!.AppliedConfig.DxgiVramGb);
        Assert.DoesNotContain(res16.Warnings, w => w.Contains("DxgiVRAM"));
        Assert.Equal("8", IniDocument.Load(Path.Combine(gameDir, "OptiScaler.ini")).Get("Spoofing", "DxgiVRAM"));

        // 512 MB BIOS Auto-UMA host (< 2048 MB): do not falsely reset explicit setting; default 0 writes auto
        var engineUma512 = new InjectionEngine(cm, appData, dedicatedVramMbProvider: () => 512);
        var res512Explicit = await engineUma512.InjectAsync(game, explicitCfg, nameof(ComponentSlot.OptiScalerNightly), "strix-halo-quality");
        Assert.True(res512Explicit.Success);
        Assert.Equal(8, res512Explicit.Manifest!.AppliedConfig.DxgiVramGb);
        Assert.DoesNotContain(res512Explicit.Warnings, w => w.Contains("DxgiVRAM"));
        Assert.Equal("8", IniDocument.Load(Path.Combine(gameDir, "OptiScaler.ini")).Get("Spoofing", "DxgiVRAM"));

        explicitCfg.DxgiVramGb = 0;
        var res512Auto = await engineUma512.InjectAsync(game, explicitCfg, nameof(ComponentSlot.OptiScalerNightly), "strix-halo-quality");
        Assert.True(res512Auto.Success);
        Assert.Equal("auto", IniDocument.Load(Path.Combine(gameDir, "OptiScaler.ini")).Get("Spoofing", "DxgiVRAM"));
    }

    private static string CreateZipPackage(string zipPath, Dictionary<string, string> files)
    {
        File.WriteAllBytes(zipPath, CreateZipBytes(files));
        return zipPath;
    }

    private static byte[] CreateZipBytes(Dictionary<string, string> files)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (relPath, content) in files)
            {
                var entry = archive.CreateEntry(relPath);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }
        return ms.ToArray();
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
