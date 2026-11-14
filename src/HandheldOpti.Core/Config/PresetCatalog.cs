using HandheldOpti.Core.Models;

namespace HandheldOpti.Core.Config;

public static class PresetCatalog
{
    public static IReadOnlyList<InjectionPreset> GetPresets() => new List<InjectionPreset>
    {
        new()
        {
            Id = "strix-halo-quality",
            Name = "FSR 4.1.2 INT8 + XeFG",
            Subtitle = "2026 主流推荐 · RDNA 3.5",
            Description = "默认启用 FSR 4.1.2 INT8 超分搭配 Intel XeSS 帧生成 (XeFG)，并启用 Anti-Lag 2 与智能 DLSS-G 接管，兼顾画质与流畅度。",
            RecommendedFor = "绝大多数 DX12 单机大作（如黑神话、真三起源、仁王 3 等）",
            Config = new OptiScalerConfig
            {
                ProxyDllName = "auto",
                SrMode = "fsr4_int8",
                Dx12Upscaler = "ffx",
                Dx11Upscaler = "ffx_12",
                VulkanUpscaler = "ffx_12",
                FgType = "xefg",
                FgInputMode = "auto",
                EnableOptiPatcher = true,
                EnableFakenvapi = true,
                EnableCustomDlls = true,
                SpoofDxgi = true,
                StreamlineSpoofing = true,
                ShortcutKeyHex = "0x2D",
                EnableFsr4Int8Hint = true,
                Fsr4Watermark = false
            }
        },
        new()
        {
            Id = "handheld-lite-xefg",
            Name = "FSR 4.1.2 Lite + XeFG",
            Subtitle = "掌机高帧省电",
            Description = "切换至开销更低的 FSR 4.1.2 INT8 Lite 轻量模型配合 XeFG 帧生成，在掌机功耗墙下换取更高基础帧率与更低渲染耗时。",
            RecommendedFor = "高负载虚幻 5 新作 / 需要稳定高帧率或省电续航",
            Config = new OptiScalerConfig
            {
                ProxyDllName = "auto",
                SrMode = "fsr4_int8_lite",
                Dx12Upscaler = "ffx",
                Dx11Upscaler = "ffx_12",
                VulkanUpscaler = "ffx_12",
                FgType = "xefg",
                FgInputMode = "auto",
                EnableOptiPatcher = true,
                EnableFakenvapi = true,
                EnableCustomDlls = true,
                SpoofDxgi = true,
                StreamlineSpoofing = true,
                ShortcutKeyHex = "0x2D",
                EnableFsr4Int8Hint = true,
                Fsr4Watermark = false
            }
        },
        new()
        {
            Id = "low-latency-upscale",
            Name = "FSR 4.1.2 纯超分 (无插帧)",
            Subtitle = "动作 / 魂类低延迟",
            Description = "仅启用 FSR 4.1.2 INT8 超分与 Anti-Lag 2 管线，关闭帧生成以消除额外输入延迟与 UI 伪影。",
            RecommendedFor = "动作格斗 / 魂类 / 竞技及对跟手度要求极高的场景",
            Config = new OptiScalerConfig
            {
                ProxyDllName = "auto",
                SrMode = "fsr4_int8",
                Dx12Upscaler = "ffx",
                Dx11Upscaler = "ffx_12",
                VulkanUpscaler = "ffx_12",
                FgType = "nofg",
                FgInputMode = "upscaler",
                EnableOptiPatcher = true,
                EnableFakenvapi = true,
                EnableCustomDlls = true,
                SpoofDxgi = true,
                StreamlineSpoofing = true,
                ShortcutKeyHex = "0x2D",
                EnableFsr4Int8Hint = true,
                Fsr4Watermark = false
            }
        },
        new()
        {
            Id = "xess-antishimmer",
            Name = "XeSS 2.0 + XeFG 稳态方案",
            Subtitle = "植被 / 细线 / 粒子抗闪烁",
            Description = "使用 Intel XeSS 2.0 超分配合 XeFG 帧生成，有效缓解部分游戏远景栅栏、草地与细小粒子闪烁。",
            RecommendedFor = "UE5 远景多闪烁游戏 / 偏好稳态抗锯齿场景",
            Config = new OptiScalerConfig
            {
                ProxyDllName = "auto",
                SrMode = "xess",
                Dx12Upscaler = "xess",
                Dx11Upscaler = "xess_12",
                VulkanUpscaler = "xess_12",
                FgType = "xefg",
                FgInputMode = "auto",
                EnableOptiPatcher = true,
                EnableFakenvapi = true,
                EnableCustomDlls = true,
                SpoofDxgi = true,
                StreamlineSpoofing = true,
                ShortcutKeyHex = "0x2D",
                EnableFsr4Int8Hint = false,
                Fsr4Watermark = false
            }
        }
    };

    public static InjectionPreset GetById(string? id)
    {
        var all = GetPresets();
        return all.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? all[0];
    }

    private static string NormalizeUpscaler(string? value, bool isV10)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "auto";
        }

        var lower = value.Trim().ToLowerInvariant();
        if (isV10)
        {
            return lower switch
            {
                "fsr31" => "ffx",
                "fsr31_12" => "ffx_12",
                _ => value.Trim()
            };
        }

        return lower switch
        {
            "ffx" => "fsr31",
            "ffx_12" => "fsr31_12",
            _ => value.Trim()
        };
    }

    private static string ResolveEffectiveSrMode(OptiScalerConfig config)
    {
        var sr = (config.SrMode ?? string.Empty).Trim().ToLowerInvariant();
        if (sr is "fsr4_int8" or "fsr4_int8_lite" or "xess" or "fsr31")
        {
            return sr;
        }

        var dx12 = (config.Dx12Upscaler ?? string.Empty).Trim().ToLowerInvariant();
        if (dx12 is "xess" or "xess_12")
        {
            return "xess";
        }

        if (config.EnableFsr4Int8Hint)
        {
            return "fsr4_int8";
        }

        return dx12 is "ffx" or "fsr31" ? "fsr31" : "custom";
    }

    public static void ApplyToIni(IniDocument ini, OptiScalerConfig config, bool gameHasNativeDlssG = false)
    {
        // v10.0.0+ (Nightly) uses [FSR] Fsr4GpuSupport + Fsr4Model and Dx12Upscaler=ffx;
        // v0.9.4 Stable uses [FSR] Fsr4ForceEnableInt8 and Dx12Upscaler=fsr31.
        bool isV10 = ini.Get("FSR", "Fsr4GpuSupport") is not null;
        string srMode = ResolveEffectiveSrMode(config);

        switch (srMode)
        {
            case "fsr4_int8":
                ini.Set("Upscalers", "Dx12Upscaler", isV10 ? "ffx" : "fsr31");
                ini.Set("Upscalers", "Dx11Upscaler", isV10 ? "ffx_12" : "fsr31_12");
                ini.Set("Upscalers", "VulkanUpscaler", isV10 ? "ffx_12" : "fsr31_12");
                if (isV10)
                {
                    ini.Set("FSR", "UpscalerIndex", "0");
                    ini.Set("FSR", "Fsr4GpuSupport", "2");
                    ini.Set("FSR", "Fsr4Model", "1");
                    ini.Set("FSR", "FsrAgilitySDKUpgrade", "true");
                }
                else
                {
                    ini.Set("FSR", "Fsr4ForceEnableInt8", "true");
                }
                break;

            case "fsr4_int8_lite":
                ini.Set("Upscalers", "Dx12Upscaler", isV10 ? "ffx" : "fsr31");
                ini.Set("Upscalers", "Dx11Upscaler", isV10 ? "ffx_12" : "fsr31_12");
                ini.Set("Upscalers", "VulkanUpscaler", isV10 ? "ffx_12" : "fsr31_12");
                if (isV10)
                {
                    ini.Set("FSR", "UpscalerIndex", "0");
                    ini.Set("FSR", "Fsr4GpuSupport", "4");
                    ini.Set("FSR", "Fsr4Model", "3");
                    ini.Set("FSR", "FsrAgilitySDKUpgrade", "true");
                }
                else
                {
                    ini.Set("FSR", "Fsr4ForceEnableInt8", "true");
                }
                break;

            case "xess":
                ini.Set("Upscalers", "Dx12Upscaler", "xess");
                ini.Set("Upscalers", "Dx11Upscaler", "xess_12");
                ini.Set("Upscalers", "VulkanUpscaler", "xess_12");
                if (isV10)
                {
                    ini.Set("FSR", "Fsr4GpuSupport", "0");
                }
                else
                {
                    ini.Set("FSR", "Fsr4ForceEnableInt8", "false");
                }
                break;

            case "fsr31":
                ini.Set("Upscalers", "Dx12Upscaler", isV10 ? "ffx" : "fsr31");
                ini.Set("Upscalers", "Dx11Upscaler", isV10 ? "ffx_12" : "fsr31_12");
                ini.Set("Upscalers", "VulkanUpscaler", isV10 ? "ffx_12" : "fsr31_12");
                if (isV10)
                {
                    ini.Set("FSR", "UpscalerIndex", "0");
                    ini.Set("FSR", "Fsr4GpuSupport", "0");
                }
                else
                {
                    ini.Set("FSR", "Fsr4ForceEnableInt8", "false");
                }
                break;

            default:
                ini.Set("Upscalers", "Dx11Upscaler", NormalizeUpscaler(config.Dx11Upscaler, isV10));
                ini.Set("Upscalers", "Dx12Upscaler", NormalizeUpscaler(config.Dx12Upscaler, isV10));
                ini.Set("Upscalers", "VulkanUpscaler", NormalizeUpscaler(config.VulkanUpscaler, isV10));
                break;
        }

        if (isV10)
        {
            ini.Set("FSR", "Fsr4EnableWatermark", config.Fsr4Watermark ? "true" : "false");
        }

        // Resolve FGInput: prefer native dlssg when game ships nvngx_dlssg.dll (zero HUD ghosting),
        // or honour explicit user override ("dlssg" / "upscaler").
        var fgInputMode = (config.FgInputMode ?? "auto").Trim().ToLowerInvariant();
        string effectiveFgInput = fgInputMode switch
        {
            "dlssg" => "dlssg",
            "upscaler" => "upscaler",
            _ => gameHasNativeDlssG ? "dlssg" : "upscaler"
        };

        var fg = (config.FgType ?? "xefg").Trim().ToLowerInvariant();
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
                ini.Set("FrameGen", "FGInput", effectiveFgInput);
                ini.Set("FrameGen", "FGOutput", "xefg");
                if (effectiveFgInput == "upscaler")
                {
                    ini.Set("OptiFG", "HUDFix", "true");
                }
                break;
            case "optifg":
            case "fsrfg":
                ini.Set("FrameGen", "Enabled", "true");
                ini.Set("FrameGen", "FGInput", effectiveFgInput);
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

        ini.Set("Spoofing", "StreamlineSpoofing", config.StreamlineSpoofing ? "true" : "false");

        if (!config.SpoofDxgi)
        {
            ini.Set("Spoofing", "Dxgi", "false");
        }
        else
        {
            var vendor = config.SpoofedVendorId?.Trim();
            bool hasExplicitVendor = !string.IsNullOrWhiteSpace(vendor) &&
                                     !string.Equals(vendor, "auto", StringComparison.OrdinalIgnoreCase);

            ini.Set("Spoofing", "Dxgi", hasExplicitVendor ? "true" : "auto");

            if (hasExplicitVendor)
            {
                ini.Set("Spoofing", "SpoofedVendorId", vendor!);
                var device = config.SpoofedDeviceId?.Trim();
                ini.Set("Spoofing", "SpoofedDeviceId",
                    string.IsNullOrWhiteSpace(device) || string.Equals(device, "auto", StringComparison.OrdinalIgnoreCase)
                        ? "0x2684"
                        : device!);
                if (!string.IsNullOrWhiteSpace(config.SpoofedGpuName))
                {
                    ini.Set("Spoofing", "SpoofedGPUName", config.SpoofedGpuName.Trim());
                }
            }
        }
    }
}
