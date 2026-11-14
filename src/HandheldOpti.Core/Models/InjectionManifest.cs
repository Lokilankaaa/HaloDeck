namespace HandheldOpti.Core.Models;

public sealed class BackedUpOriginalFile
{
    public string RelativePath { get; set; } = string.Empty;
    public string BackupFileName { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class InjectionManifest
{
    public string GameId { get; set; } = string.Empty;
    public string GameTitle { get; set; } = string.Empty;
    public string TargetRenderDirectory { get; set; } = string.Empty;
    public string ProxyDllName { get; set; } = string.Empty;
    public string PresetId { get; set; } = string.Empty;
    public string? OptiScalerVersion { get; set; }
    public bool HasOptiPatcher { get; set; }
    public bool HasFakenvapi { get; set; }
    public DateTimeOffset InjectedAtUtc { get; set; }
    public List<string> CreatedFiles { get; set; } = new();
    public List<BackedUpOriginalFile> BackedUpFiles { get; set; } = new();
    public OptiScalerConfig AppliedConfig { get; set; } = new();
}

public sealed class InjectionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? SelectedProxyDll { get; set; }
    public bool ConflictAvoided { get; set; }
    public string? ConflictNote { get; set; }
    public InjectionManifest? Manifest { get; set; }
    public List<string> Warnings { get; set; } = new();
}
