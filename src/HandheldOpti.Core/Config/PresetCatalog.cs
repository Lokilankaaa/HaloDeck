using HandheldOpti.Core.Models;

namespace HandheldOpti.Core.Config;

public static class PresetCatalog
{
    public static IReadOnlyList<InjectionPreset> GetPresets() => new List<InjectionPreset>
    {
        new()
        {
            Id = "strix-halo-quality",
            Name = "Strix Halo 均衡画质帧生",
            Subtitle = "GPD Win 5 推荐预设 · RDNA 3.5",
            Description = "优先通过 OptiPatcher 免显卡伪装解锁游戏原生 DLSS + OptiFG 帧生成，并启用 Fakenvapi 将 Reflex 映射为 AMD Anti-Lag 2。",
            RecommendedFor = "黑神话：悟空 / 赛博朋克 2077 / 多数 UE5 与 DX12 单机大作",
            Config = new OptiScalerConfig
            {
                ProxyDllName = "auto",
                Dx12Upscaler = "auto",
                Dx11Upscaler = "fsr31",
                VulkanUpscaler = "fsr31",
                FgType = "optifg",
                EnableOptiPatcher = true,
                EnableFakenvapi = true,
                EnableCustomDlls = true,
                SpoofDxgi = false,
                StreamlineSpoofing = true,
                ShortcutKeyHex = "0x2D"
            }
        },
        new()
        {
            Id = "xess-antishimmer",
            Name = "XeSS 稳定抗闪烁",
            Subtitle = "细线条 / 植被 / 粒子抗抖动",
            Description = "DX12 下强制使用 Intel XeSS 2.x/DP4a 管线配合 OptiFG，显著减少铁丝网与远景植被的闪烁问题。",
            RecommendedFor = "UE5 远景多闪烁游戏 / 追求画面稳定性",
            Config = new OptiScalerConfig
            {
                ProxyDllName = "auto",
                Dx12Upscaler = "xess",
                Dx11Upscaler = "fsr31",
                VulkanUpscaler = "fsr31",
                FgType = "optifg",
                EnableOptiPatcher = true,
                EnableFakenvapi = true,
                EnableCustomDlls = true,
                SpoofDxgi = false,
                StreamlineSpoofing = true,
                ShortcutKeyHex = "0x2D"
            }
        },
        new()
        {
            Id = "low-latency-upscale",
            Name = "低延迟纯超分 (无帧生成)",
            Subtitle = "60+ FPS 跟手操控 · 掌机省电",
            Description = "关闭插帧仅启用 FFX FSR 3.1/4 超分与 Anti-Lag 2 延迟优化，降低输入延迟与 8060S 额外插帧功耗。",
            RecommendedFor = "动作类游戏 / 灵魂类 / 高帧率低延迟场景",
            Config = new OptiScalerConfig
            {
                ProxyDllName = "auto",
                Dx12Upscaler = "fsr31",
                Dx11Upscaler = "fsr31",
                VulkanUpscaler = "fsr31",
                FgType = "nofg",
                EnableOptiPatcher = true,
                EnableFakenvapi = true,
                EnableCustomDlls = true,
                SpoofDxgi = false,
                StreamlineSpoofing = true,
                ShortcutKeyHex = "0x2D"
            }
        },
        new()
        {
            Id = "dlss-spoof-compat",
            Name = "强力显卡伪装兼容 (RTX 4090)",
            Subtitle = "针对未适配 OptiPatcher 的老游戏",
            Description = "当游戏检测显卡型号并隐藏 DLSS 选项时，启用 DXGI 显卡伪装为 RTX 4090 (0x10DE / 0x2684) 解锁选项。",
            RecommendedFor = "老版本 UE4 / 严格检测 GPU Vendor 的非支持列表游戏",
            Config = new OptiScalerConfig
            {
                ProxyDllName = "auto",
                Dx12Upscaler = "fsr31",
                Dx11Upscaler = "fsr31",
                VulkanUpscaler = "fsr31",
                FgType = "optifg",
                EnableOptiPatcher = false,
                EnableFakenvapi = true,
                EnableCustomDlls = true,
                SpoofDxgi = true,
                SpoofedVendorId = "0x10de",
                SpoofedDeviceId = "0x2684",
                SpoofedGpuName = "NVIDIA GeForce RTX 4090",
                StreamlineSpoofing = true,
                ShortcutKeyHex = "0x2D"
            }
        }
    };

    public static InjectionPreset GetById(string? id)
    {
        var all = GetPresets();
        return all.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? all[0];
    }

    private static string NormalizeUpscaler(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "auto";
        }

        var trimmed = value.Trim();
        return trimmed.ToLowerInvariant() switch
        {
            "ffx" => "fsr31",
            "ffx_12" => "fsr31_12",
            _ => trimmed
        };
    }

    public static void ApplyToIni(IniDocument ini, OptiScalerConfig config)
    {
        ini.Set("Upscalers", "Dx11Upscaler", NormalizeUpscaler(config.Dx11Upscaler));
        ini.Set("Upscalers", "Dx12Upscaler", NormalizeUpscaler(config.Dx12Upscaler));
        ini.Set("Upscalers", "VulkanUpscaler", NormalizeUpscaler(config.VulkanUpscaler));

        // OptiScaler v0.9.4+ replaced [FrameGen] FgType with Enabled + FGInput + FGOutput.
        var fg = (config.FgType ?? "auto").Trim().ToLowerInvariant();
        switch (fg)
        {
            case "nofg":
                ini.Set("FrameGen", "Enabled", "false");
                ini.Set("FrameGen", "FGInput", "nofg");
                ini.Set("FrameGen", "FGOutput", "nofg");
                break;
            case "nukems":
                ini.Set("FrameGen", "Enabled", "true");
                ini.Set("FrameGen", "FGInput", "nukems");
                ini.Set("FrameGen", "FGOutput", "nukems");
                break;
            case "xefg":
                ini.Set("FrameGen", "Enabled", "true");
                ini.Set("FrameGen", "FGInput", "upscaler");
                ini.Set("FrameGen", "FGOutput", "xefg");
                break;
            case "optifg":
            case "fsrfg":
                ini.Set("FrameGen", "Enabled", "true");
                ini.Set("FrameGen", "FGInput", "upscaler");
                ini.Set("FrameGen", "FGOutput", "fsrfg");
                ini.Set("OptiFG", "HUDFix", "true");
                break;
            default:
                ini.Set("FrameGen", "Enabled", "true");
                ini.Set("FrameGen", "FGInput", "auto");
                ini.Set("FrameGen", "FGOutput", "auto");
                break;
        }

        ini.Set("Menu", "ShortcutKey", string.IsNullOrWhiteSpace(config.ShortcutKeyHex) ? "0x2D" : config.ShortcutKeyHex);

        ini.Set("Plugins", "LoadAsiPlugins", config.EnableOptiPatcher ? "true" : "false");

        ini.Set("NvApi", "OverrideNvapiDll", config.EnableFakenvapi ? "true" : "false");

        ini.Set("Spoofing", "Dxgi", config.SpoofDxgi ? "true" : "false");
        ini.Set("Spoofing", "StreamlineSpoofing", config.StreamlineSpoofing ? "true" : "false");

        if (config.SpoofDxgi)
        {
            ini.Set("Spoofing", "SpoofedVendorId", string.IsNullOrWhiteSpace(config.SpoofedVendorId) ? "0x10de" : config.SpoofedVendorId);
            ini.Set("Spoofing", "SpoofedDeviceId", string.IsNullOrWhiteSpace(config.SpoofedDeviceId) ? "0x2684" : config.SpoofedDeviceId);
            if (!string.IsNullOrWhiteSpace(config.SpoofedGpuName))
            {
                ini.Set("Spoofing", "SpoofedGPUName", config.SpoofedGpuName);
            }
        }
    }
}
