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
        Assert.Equal("fsr31", reparsed.Get("Upscalers", "Dx11Upscaler"));
        Assert.Equal("fsr31", reparsed.Get("Upscalers", "VulkanUpscaler"));
        Assert.Equal("true", reparsed.Get("FrameGen", "Enabled"));
        Assert.Equal("upscaler", reparsed.Get("FrameGen", "FGInput"));
        Assert.Equal("fsrfg", reparsed.Get("FrameGen", "FGOutput"));
        Assert.Equal("true", reparsed.Get("OptiFG", "HUDFix"));
        Assert.Equal("true", reparsed.Get("Plugins", "LoadAsiPlugins"));
        Assert.Equal("true", reparsed.Get("NvApi", "OverrideNvapiDll"));
        Assert.Equal("false", reparsed.Get("Spoofing", "Dxgi"));
        Assert.Equal("0x2D", reparsed.Get("Menu", "ShortcutKey"));
    }

    [Fact]
    public void ApplyToIni_MapsNofgXefgAndLegacyFfxUpscalers()
    {
        var doc = IniDocument.Parse(string.Empty);
        var preset = PresetCatalog.GetById("low-latency-upscale");
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
}
