namespace HandheldOpti.Core.Models;

public sealed class OptiScalerConfig
{
    public string ProxyDllName { get; set; } = "auto";
    public string Dx12Upscaler { get; set; } = "auto";
    public string Dx11Upscaler { get; set; } = "fsr31";
    public string VulkanUpscaler { get; set; } = "fsr31";
    public string FgType { get; set; } = "optifg";
    public bool EnableOptiPatcher { get; set; } = true;
    public bool EnableFakenvapi { get; set; } = true;
    public bool EnableCustomDlls { get; set; } = true;
    public bool SpoofDxgi { get; set; } = true;
    public string SpoofedVendorId { get; set; } = "auto";
    public string SpoofedDeviceId { get; set; } = "auto";
    public string SpoofedGpuName { get; set; } = string.Empty;
    public bool StreamlineSpoofing { get; set; } = true;
    public string ShortcutKeyHex { get; set; } = "0x2D";
    public bool EnableFsr4Int8Hint { get; set; } = false;

    public OptiScalerConfig Clone() => new()
    {
        ProxyDllName = ProxyDllName,
        Dx12Upscaler = Dx12Upscaler,
        Dx11Upscaler = Dx11Upscaler,
        VulkanUpscaler = VulkanUpscaler,
        FgType = FgType,
        EnableOptiPatcher = EnableOptiPatcher,
        EnableFakenvapi = EnableFakenvapi,
        EnableCustomDlls = EnableCustomDlls,
        SpoofDxgi = SpoofDxgi,
        SpoofedVendorId = SpoofedVendorId,
        SpoofedDeviceId = SpoofedDeviceId,
        SpoofedGpuName = SpoofedGpuName,
        StreamlineSpoofing = StreamlineSpoofing,
        ShortcutKeyHex = ShortcutKeyHex,
        EnableFsr4Int8Hint = EnableFsr4Int8Hint
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
