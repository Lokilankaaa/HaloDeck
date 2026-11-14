namespace HandheldOpti.Core.Models;

public sealed class OptiScalerConfig
{
    public string ProxyDllName { get; set; } = "auto";
    public string SrMode { get; set; } = "fsr4_int8";
    public string Dx12Upscaler { get; set; } = "ffx";
    public string Dx11Upscaler { get; set; } = "ffx_12";
    public string VulkanUpscaler { get; set; } = "ffx_12";
    public string FgType { get; set; } = "xefg";
    public string FgInputMode { get; set; } = "auto";
    public bool EnableOptiPatcher { get; set; } = true;
    public bool EnableFakenvapi { get; set; } = true;
    public bool EnableCustomDlls { get; set; } = true;
    public bool SpoofDxgi { get; set; } = true;
    public int DxgiVramGb { get; set; } = 10;
    public string SpoofedVendorId { get; set; } = "auto";
    public string SpoofedDeviceId { get; set; } = "auto";
    public string SpoofedGpuName { get; set; } = string.Empty;
    public bool StreamlineSpoofing { get; set; } = true;
    public string ShortcutKeyHex { get; set; } = "0x2D";
    public bool EnableFsr4Int8Hint { get; set; } = true;
    public bool Fsr4Watermark { get; set; } = false;
    public int FramerateLimit { get; set; } = 0;

    public OptiScalerConfig Clone() => new()
    {
        ProxyDllName = ProxyDllName,
        SrMode = SrMode,
        Dx12Upscaler = Dx12Upscaler,
        Dx11Upscaler = Dx11Upscaler,
        VulkanUpscaler = VulkanUpscaler,
        FgType = FgType,
        FgInputMode = FgInputMode,
        EnableOptiPatcher = EnableOptiPatcher,
        EnableFakenvapi = EnableFakenvapi,
        EnableCustomDlls = EnableCustomDlls,
        SpoofDxgi = SpoofDxgi,
        DxgiVramGb = DxgiVramGb,
        SpoofedVendorId = SpoofedVendorId,
        SpoofedDeviceId = SpoofedDeviceId,
        SpoofedGpuName = SpoofedGpuName,
        StreamlineSpoofing = StreamlineSpoofing,
        ShortcutKeyHex = ShortcutKeyHex,
        EnableFsr4Int8Hint = EnableFsr4Int8Hint,
        Fsr4Watermark = Fsr4Watermark,
        FramerateLimit = FramerateLimit
    };
}

public sealed class InjectionPreset
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string RecommendedFor { get; set; } = string.Empty;
    public OptiScalerConfig Config { get; set; } = new();
}
