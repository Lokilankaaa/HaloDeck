using HaloDeck.Core.Models;

namespace HaloDeck.Core.Config;

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
                Fsr4Watermark = false,
                VsyncMode = "auto"
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

    public static int NormalizeXeFgMultiplier(int multiplier) => multiplier switch
    {
        <= 2 => 2,
        3 => 3,
        _ => 4
    };

    public static int NormalizeXeFgDynamicTargetFps(int targetFps) => targetFps >= 10 ? targetFps : 0;

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
                    ini.Set("FSR", "Fsr4Model", "auto");
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
                    ini.Set("FSR", "Fsr4Model", "auto");
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
        bool needsXeFgMfgOverride = isV10 && fg == "xefg" &&
                                    (NormalizeXeFgMultiplier(config.XeFgMultiplier) > 2 || config.XeFgDynamicMfg);
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
                // On native DLSS-G games, Enabled=auto triggers Config::FGFollowsGame() which overrides
                // [XeFG] InterpolationCount with the game's 2X multiplier. Force Enabled=true when MFG/DMFG is active.
                ini.Set("FrameGen", "Enabled", effectiveFgInput == "dlssg" && !needsXeFgMfgOverride ? "auto" : "true");
                ini.Set("FrameGen", "FGInput", effectiveFgInput);
                ini.Set("FrameGen", "FGOutput", "xefg");
                break;
            case "optifg":
            case "fsrfg":
                ini.Set("FrameGen", "Enabled", effectiveFgInput == "dlssg" ? "auto" : "true");
                ini.Set("FrameGen", "FGInput", effectiveFgInput);
                ini.Set("FrameGen", "FGOutput", "fsrfg");
                break;
            default:
                ini.Set("FrameGen", "Enabled", "true");
                ini.Set("FrameGen", "FGInput", "auto");
                ini.Set("FrameGen", "FGOutput", "auto");
                break;
        }

        bool needHudFix = (fg is "xefg" or "optifg" or "fsrfg") && effectiveFgInput == "upscaler";
        ini.Set("OptiFG", "HUDFix", needHudFix ? "true" : "auto");

        if (isV10)
        {
            if (fg == "xefg")
            {
                var multiplier = NormalizeXeFgMultiplier(config.XeFgMultiplier);
                var interpCount = (multiplier - 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                ini.Set("XeFG", "InterpolationCount", interpCount);
                ini.Set("XeFG", "DynamicMFG", config.XeFgDynamicMfg ? "true" : "false");
                var dmfgTarget = config.XeFgDynamicMfg ? NormalizeXeFgDynamicTargetFps(config.XeFgDynamicTargetFps) : 0;
                ini.Set("XeFG", "FramerateTargetDMFG",
                    dmfgTarget >= 10
                        ? dmfgTarget.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : "auto");
            }
            else
            {
                ini.Set("XeFG", "InterpolationCount", "auto");
                ini.Set("XeFG", "DynamicMFG", "auto");
                ini.Set("XeFG", "FramerateTargetDMFG", "auto");
            }

            var vsync = (config.VsyncMode ?? "on").Trim().ToLowerInvariant();
            switch (vsync)
            {
                case "on":
                    ini.Set("V-Sync", "OverrideVsync", "true");
                    ini.Set("V-Sync", "ForceVsync", "true");
                    ini.Set("V-Sync", "SyncInterval", "1");
                    break;
                case "off":
                    ini.Set("V-Sync", "OverrideVsync", "true");
                    ini.Set("V-Sync", "ForceVsync", "false");
                    ini.Set("V-Sync", "SyncInterval", "0");
                    break;
                default:
                    ini.Set("V-Sync", "OverrideVsync", "auto");
                    ini.Set("V-Sync", "ForceVsync", "auto");
                    ini.Set("V-Sync", "SyncInterval", "auto");
                    break;
            }
        }

        ini.Set("Framerate", "FramerateLimit",
            config.FramerateLimit > 0
                ? config.FramerateLimit.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "auto");

        ini.Set("Menu", "ShortcutKey", string.IsNullOrWhiteSpace(config.ShortcutKeyHex) ? "0x2D" : config.ShortcutKeyHex);

        ini.Set("Plugins", "LoadAsiPlugins", config.EnableOptiPatcher ? "true" : "false");

        ini.Set("NvApi", "OverrideNvapiDll", config.EnableFakenvapi ? "true" : "false");

        ini.Set("Log", "LogToFile", "true");
        ini.Set("Log", "LogLevel", "2");

        ini.Set("Spoofing", "StreamlineSpoofing", config.StreamlineSpoofing ? "true" : "false");

        if (!config.SpoofDxgi)
        {
            ini.Set("Spoofing", "Dxgi", "false");
            ini.Set("Spoofing", "DxgiVRAM", "auto");
        }
        else
        {
            var vendor = config.SpoofedVendorId?.Trim();
            bool hasExplicitVendor = !string.IsNullOrWhiteSpace(vendor) &&
                                     !string.Equals(vendor, "auto", StringComparison.OrdinalIgnoreCase);

            ini.Set("Spoofing", "Dxgi", hasExplicitVendor ? "true" : "auto");

            ini.Set("Spoofing", "DxgiVRAM",
                config.DxgiVramGb > 0
                    ? config.DxgiVramGb.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "auto");

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

    public static OptiScalerConfig ReadConfigFromIni(IniDocument ini, OptiScalerConfig? seed = null)
    {
        var cfg = seed?.Clone() ?? new OptiScalerConfig();

        var dx12 = ini.Get("Upscalers", "Dx12Upscaler")?.Trim();
        var dx11 = ini.Get("Upscalers", "Dx11Upscaler")?.Trim();
        var vk = ini.Get("Upscalers", "VulkanUpscaler")?.Trim();
        if (!string.IsNullOrWhiteSpace(dx12)) cfg.Dx12Upscaler = dx12;
        if (!string.IsNullOrWhiteSpace(dx11)) cfg.Dx11Upscaler = dx11;
        if (!string.IsNullOrWhiteSpace(vk)) cfg.VulkanUpscaler = vk;

        var dx12Lower = (dx12 ?? cfg.Dx12Upscaler).Trim().ToLowerInvariant();
        var fsr4Support = ini.Get("FSR", "Fsr4GpuSupport")?.Trim().ToLowerInvariant();
        var fsr4Model = ini.Get("FSR", "Fsr4Model")?.Trim().ToLowerInvariant();
        var fsr4ForceInt8 = ini.Get("FSR", "Fsr4ForceEnableInt8")?.Trim().ToLowerInvariant();

        if (dx12Lower is "xess" or "xess_12")
        {
            cfg.SrMode = "xess";
            cfg.EnableFsr4Int8Hint = false;
        }
        else if (dx12Lower is "ffx" or "ffx_12" or "fsr31" or "fsr31_12" or "auto" or "")
        {
            if (fsr4Support is not null)
            {
                if (fsr4Support is "0" or "false")
                {
                    cfg.SrMode = "fsr31";
                    cfg.EnableFsr4Int8Hint = false;
                }
                else if (fsr4Model == "3" || fsr4Support == "4")
                {
                    cfg.SrMode = "fsr4_int8_lite";
                    cfg.EnableFsr4Int8Hint = true;
                }
                else if (fsr4Support is "1" or "2" or "3" or "true" ||
                         fsr4Model is "0" or "1" or "2" or "4" or "5")
                {
                    cfg.SrMode = "fsr4_int8";
                    cfg.EnableFsr4Int8Hint = true;
                }
            }
            else if (fsr4ForceInt8 is not null)
            {
                if (fsr4ForceInt8 == "true")
                {
                    cfg.SrMode = string.Equals(cfg.SrMode, "fsr4_int8_lite", StringComparison.OrdinalIgnoreCase)
                        ? "fsr4_int8_lite"
                        : "fsr4_int8";
                    cfg.EnableFsr4Int8Hint = true;
                }
                else if (fsr4ForceInt8 == "false")
                {
                    cfg.SrMode = "fsr31";
                    cfg.EnableFsr4Int8Hint = false;
                }
            }
        }
        else
        {
            cfg.SrMode = dx12Lower;
        }

        if (TryParseIniBool(ini.Get("FSR", "Fsr4EnableWatermark"), out var watermark))
        {
            cfg.Fsr4Watermark = watermark;
        }

        var fgEnabled = ini.Get("FrameGen", "Enabled")?.Trim().ToLowerInvariant();
        var fgInput = ini.Get("FrameGen", "FGInput")?.Trim().ToLowerInvariant();
        var fgOutput = ini.Get("FrameGen", "FGOutput")?.Trim().ToLowerInvariant();

        if (fgEnabled == "false" || fgOutput == "nofg" || fgInput == "nofg")
        {
            cfg.FgType = "nofg";
        }
        else if (fgOutput == "nukems" || fgInput == "nukems")
        {
            cfg.FgType = "nukems";
        }
        else if (fgOutput == "xefg")
        {
            cfg.FgType = "xefg";
        }
        else if (fgOutput is "fsrfg" or "optifg")
        {
            cfg.FgType = "optifg";
        }

        if (fgInput is "dlssg" or "upscaler" &&
            (seed is null || !string.Equals(seed.FgInputMode, "auto", StringComparison.OrdinalIgnoreCase)))
        {
            cfg.FgInputMode = fgInput;
        }

        var xeInterp = ini.Get("XeFG", "InterpolationCount")?.Trim();
        if (string.Equals(xeInterp, "auto", StringComparison.OrdinalIgnoreCase))
        {
            cfg.XeFgMultiplier = 2;
        }
        else if (int.TryParse(xeInterp, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var interpCount) && interpCount >= 1)
        {
            cfg.XeFgMultiplier = NormalizeXeFgMultiplier(interpCount + 1);
        }

        var xeDynamic = ini.Get("XeFG", "DynamicMFG")?.Trim();
        if (string.Equals(xeDynamic, "auto", StringComparison.OrdinalIgnoreCase))
        {
            cfg.XeFgDynamicMfg = false;
        }
        else if (TryParseIniBool(xeDynamic, out var dynamicMfg))
        {
            cfg.XeFgDynamicMfg = dynamicMfg;
        }

        var xeDmfgTarget = ini.Get("XeFG", "FramerateTargetDMFG")?.Trim();
        if (string.Equals(xeDmfgTarget, "auto", StringComparison.OrdinalIgnoreCase))
        {
            cfg.XeFgDynamicTargetFps = 0;
        }
        else if (double.TryParse(xeDmfgTarget, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dmfgTargetFps))
        {
            cfg.XeFgDynamicTargetFps = dmfgTargetFps >= 10.0 ? (int)Math.Round(dmfgTargetFps) : 0;
        }

        var overrideVsync = ini.Get("V-Sync", "OverrideVsync")?.Trim().ToLowerInvariant();
        var forceVsync = ini.Get("V-Sync", "ForceVsync")?.Trim().ToLowerInvariant();
        if (overrideVsync == "true" && forceVsync == "true")
        {
            cfg.VsyncMode = "on";
        }
        else if (overrideVsync == "true" && forceVsync == "false")
        {
            cfg.VsyncMode = "off";
        }
        else if (overrideVsync is not null)
        {
            cfg.VsyncMode = "auto";
        }

        var fpsStr = ini.Get("Framerate", "FramerateLimit")?.Trim();
        if (int.TryParse(fpsStr, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var fps) && fps >= 0)
        {
            cfg.FramerateLimit = fps;
        }
        else if (string.Equals(fpsStr, "auto", StringComparison.OrdinalIgnoreCase))
        {
            cfg.FramerateLimit = 0;
        }

        var shortcutKey = ini.Get("Menu", "ShortcutKey")?.Trim();
        if (!string.IsNullOrWhiteSpace(shortcutKey) && !string.Equals(shortcutKey, "auto", StringComparison.OrdinalIgnoreCase))
        {
            cfg.ShortcutKeyHex = shortcutKey;
        }

        if (TryParseIniBool(ini.Get("Plugins", "LoadAsiPlugins"), out var asiPlugins))
        {
            cfg.EnableOptiPatcher = asiPlugins;
        }

        if (TryParseIniBool(ini.Get("NvApi", "OverrideNvapiDll"), out var nvapi))
        {
            cfg.EnableFakenvapi = nvapi;
        }

        if (TryParseIniBool(ini.Get("Spoofing", "StreamlineSpoofing"), out var streamline))
        {
            cfg.StreamlineSpoofing = streamline;
        }

        var dxgi = ini.Get("Spoofing", "Dxgi")?.Trim().ToLowerInvariant();
        if (dxgi == "false")
        {
            cfg.SpoofDxgi = false;
        }
        else if (dxgi is "true" or "auto")
        {
            cfg.SpoofDxgi = true;
        }

        var vramStr = ini.Get("Spoofing", "DxgiVRAM")?.Trim();
        if (string.Equals(vramStr, "auto", StringComparison.OrdinalIgnoreCase))
        {
            cfg.DxgiVramGb = 0;
        }
        else if (int.TryParse(vramStr, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var vram) && vram > 0)
        {
            cfg.DxgiVramGb = vram;
        }

        var vendor = ini.Get("Spoofing", "SpoofedVendorId")?.Trim();
        if (!string.IsNullOrWhiteSpace(vendor) && !string.Equals(vendor, "auto", StringComparison.OrdinalIgnoreCase))
        {
            cfg.SpoofedVendorId = vendor;
        }

        var device = ini.Get("Spoofing", "SpoofedDeviceId")?.Trim();
        if (!string.IsNullOrWhiteSpace(device) && !string.Equals(device, "auto", StringComparison.OrdinalIgnoreCase))
        {
            cfg.SpoofedDeviceId = device;
        }

        var gpuName = ini.Get("Spoofing", "SpoofedGPUName")?.Trim();
        if (!string.IsNullOrWhiteSpace(gpuName) && !string.Equals(gpuName, "auto", StringComparison.OrdinalIgnoreCase))
        {
            cfg.SpoofedGpuName = gpuName;
        }

        return cfg;
    }

    public static string MatchPresetId(OptiScalerConfig config)
    {
        var effectiveSr = ResolveEffectiveSrMode(config);
        var fg = (config.FgType ?? "xefg").Trim().ToLowerInvariant();
        if (fg == "fsrfg") fg = "optifg";
        var vsync = (config.VsyncMode ?? "on").Trim().ToLowerInvariant();
        var vendor = (config.SpoofedVendorId ?? "auto").Trim().ToLowerInvariant();

        var effectiveXeFgMult = fg == "xefg" ? NormalizeXeFgMultiplier(config.XeFgMultiplier) : 2;
        var effectiveXeFgDmfg = fg == "xefg" && config.XeFgDynamicMfg;
        var effectiveXeFgTarget = effectiveXeFgDmfg ? NormalizeXeFgDynamicTargetFps(config.XeFgDynamicTargetFps) : 0;

        foreach (var preset in GetPresets())
        {
            var pc = preset.Config;
            var presetFg = pc.FgType.Trim().ToLowerInvariant();
            var presetXeFgMult = presetFg == "xefg" ? NormalizeXeFgMultiplier(pc.XeFgMultiplier) : 2;
            var presetXeFgDmfg = presetFg == "xefg" && pc.XeFgDynamicMfg;
            var presetXeFgTarget = presetXeFgDmfg ? NormalizeXeFgDynamicTargetFps(pc.XeFgDynamicTargetFps) : 0;

            if (effectiveSr == ResolveEffectiveSrMode(pc) &&
                fg == presetFg &&
                effectiveXeFgMult == presetXeFgMult &&
                effectiveXeFgDmfg == presetXeFgDmfg &&
                effectiveXeFgTarget == presetXeFgTarget &&
                vsync == pc.VsyncMode.Trim().ToLowerInvariant() &&
                config.FramerateLimit == pc.FramerateLimit &&
                config.EnableOptiPatcher == pc.EnableOptiPatcher &&
                config.EnableFakenvapi == pc.EnableFakenvapi &&
                config.SpoofDxgi == pc.SpoofDxgi &&
                config.DxgiVramGb == pc.DxgiVramGb &&
                vendor == pc.SpoofedVendorId.Trim().ToLowerInvariant() &&
                string.IsNullOrWhiteSpace(config.SpoofedGpuName) &&
                config.StreamlineSpoofing == pc.StreamlineSpoofing &&
                config.Fsr4Watermark == pc.Fsr4Watermark)
            {
                return preset.Id;
            }
        }

        return "custom";
    }

    private static bool TryParseIniBool(string? raw, out bool value)
    {
        var normalized = raw?.Trim().ToLowerInvariant();
        if (normalized is "true" or "1")
        {
            value = true;
            return true;
        }
        if (normalized is "false" or "0")
        {
            value = false;
            return true;
        }
        value = false;
        return false;
    }
}
