using HandheldOpti.Core.Config;
using Xunit;

namespace HandheldOpti.Tests;

public sealed class IniDocumentAndPresetTests
{
    [Fact]
    public void IniDocument_PreservesCommentsAndUncommentsTemplateKeysInPlace()
    {
        const string template = """
        ; OptiScaler default config
        [Upscalers]
        ; Set DX12 upscaler backend
        ;Dx12Upscaler=auto
        Dx11Upscaler=auto

        [Menu]
        ; Insert key default
        ShortcutKey=0x2D
        """;

        var doc = IniDocument.Parse(template);
        var preset = PresetCatalog.GetById("xess-antishimmer");
        PresetCatalog.ApplyToIni(doc, preset.Config);

        var output = doc.ToIniString();
        Assert.Contains("; OptiScaler default config", output);
        Assert.Contains("; Set DX12 upscaler backend", output);

        var reparsed = IniDocument.Parse(output);
        Assert.Equal("xess", reparsed.Get("Upscalers", "Dx12Upscaler"));
        Assert.Equal("xess_12", reparsed.Get("Upscalers", "Dx11Upscaler"));
        Assert.Equal("xess_12", reparsed.Get("Upscalers", "VulkanUpscaler"));
        Assert.Equal("true", reparsed.Get("FrameGen", "Enabled"));
        Assert.Equal("upscaler", reparsed.Get("FrameGen", "FGInput"));
        Assert.Equal("xefg", reparsed.Get("FrameGen", "FGOutput"));
        Assert.Equal("true", reparsed.Get("OptiFG", "HUDFix"));
        Assert.Equal("true", reparsed.Get("Plugins", "LoadAsiPlugins"));
        Assert.Equal("true", reparsed.Get("NvApi", "OverrideNvapiDll"));
        Assert.Equal("auto", reparsed.Get("Spoofing", "Dxgi"));
        Assert.Equal("0x2D", reparsed.Get("Menu", "ShortcutKey"));

        var spoofCfg = preset.Config.Clone();
        spoofCfg.SpoofDxgi = true;
        spoofCfg.SpoofedVendorId = "0x10de";
        spoofCfg.SpoofedDeviceId = "0x2684";
        spoofCfg.SpoofedGpuName = "NVIDIA GeForce RTX 4090";
        PresetCatalog.ApplyToIni(doc, spoofCfg);
        Assert.Equal("true", doc.Get("Spoofing", "Dxgi"));
        Assert.Equal("0x10de", doc.Get("Spoofing", "SpoofedVendorId"));
        Assert.Equal("0x2684", doc.Get("Spoofing", "SpoofedDeviceId"));
        Assert.Equal("NVIDIA GeForce RTX 4090", doc.Get("Spoofing", "SpoofedGPUName"));

        spoofCfg.SpoofDxgi = false;
        PresetCatalog.ApplyToIni(doc, spoofCfg);
        Assert.Equal("false", doc.Get("Spoofing", "Dxgi"));
        Assert.Equal("auto", doc.Get("Spoofing", "DxgiVRAM"));
    }

    [Fact]
    public void ApplyToIni_MapsNofgXefgAndLegacyFfxUpscalers()
    {
        var doc = IniDocument.Parse(string.Empty);
        var preset = PresetCatalog.GetById("low-latency-upscale");
        preset.Config.SrMode = "custom";
        preset.Config.Dx12Upscaler = "ffx";
        preset.Config.Dx11Upscaler = "ffx_12";
        PresetCatalog.ApplyToIni(doc, preset.Config);

        Assert.Equal("fsr31", doc.Get("Upscalers", "Dx12Upscaler"));
        Assert.Equal("fsr31_12", doc.Get("Upscalers", "Dx11Upscaler"));
        Assert.Equal("false", doc.Get("FrameGen", "Enabled"));
        Assert.Equal("nofg", doc.Get("FrameGen", "FGInput"));
        Assert.Equal("nofg", doc.Get("FrameGen", "FGOutput"));

        preset.Config.FgType = "xefg";
        preset.Config.EnableFakenvapi = false;
        PresetCatalog.ApplyToIni(doc, preset.Config);
        Assert.Equal("true", doc.Get("FrameGen", "Enabled"));
        Assert.Equal("upscaler", doc.Get("FrameGen", "FGInput"));
        Assert.Equal("xefg", doc.Get("FrameGen", "FGOutput"));
        Assert.Equal("false", doc.Get("NvApi", "OverrideNvapiDll"));
    }

    [Fact]
    public void ApplyToIni_V10Nightly_SetsFsr4Int8AndLiteAndXeFG_WithDlssGPreference()
    {
        const string v10Template = """
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
        """;

        var doc = IniDocument.Parse(v10Template);
        var quality = PresetCatalog.GetById("strix-halo-quality");
        quality.Config.Fsr4Watermark = true;
        PresetCatalog.ApplyToIni(doc, quality.Config, gameHasNativeDlssG: true);

        Assert.Equal("ffx", doc.Get("Upscalers", "Dx12Upscaler"));
        Assert.Equal("ffx_12", doc.Get("Upscalers", "Dx11Upscaler"));
        Assert.Equal("2", doc.Get("FSR", "Fsr4GpuSupport"));
        Assert.Equal("1", doc.Get("FSR", "Fsr4Model"));
        Assert.Equal("true", doc.Get("FSR", "FsrAgilitySDKUpgrade"));
        Assert.Equal("true", doc.Get("FSR", "Fsr4EnableWatermark"));
        Assert.Equal("true", doc.Get("FrameGen", "Enabled"));
        Assert.Equal("dlssg", doc.Get("FrameGen", "FGInput"));
        Assert.Equal("xefg", doc.Get("FrameGen", "FGOutput"));
        Assert.Equal("10", doc.Get("Spoofing", "DxgiVRAM"));
        Assert.Equal("true", doc.Get("Log", "LogToFile"));
        Assert.Equal("2", doc.Get("Log", "LogLevel"));
        Assert.Equal("true", doc.Get("V-Sync", "OverrideVsync"));
        Assert.Equal("false", doc.Get("V-Sync", "ForceVsync"));

        var lite = PresetCatalog.GetById("handheld-lite-xefg");
        PresetCatalog.ApplyToIni(doc, lite.Config, gameHasNativeDlssG: false);
        Assert.Equal("4", doc.Get("FSR", "Fsr4GpuSupport"));
        Assert.Equal("3", doc.Get("FSR", "Fsr4Model"));
        Assert.Equal("false", doc.Get("FSR", "Fsr4EnableWatermark"));
        Assert.Equal("upscaler", doc.Get("FrameGen", "FGInput"));
        Assert.Equal("xefg", doc.Get("FrameGen", "FGOutput"));
        Assert.Equal("10", doc.Get("Spoofing", "DxgiVRAM"));
    }
}
