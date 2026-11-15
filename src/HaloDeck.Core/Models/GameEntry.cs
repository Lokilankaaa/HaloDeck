namespace HaloDeck.Core.Models;

public enum GamePlatform
{
    Steam,
    Epic,
    Gog,
    Xbox,
    Custom,
    RunningProcess
}

public enum GameEngine
{
    Unknown,
    Unreal,
    Unity,
    RedEngine,
    Godot,
    Native
}

public enum PeArchitecture
{
    Unknown,
    X64,
    X86
}

public enum AntiCheatRiskLevel
{
    None,
    Warning,
    HighRisk
}

public sealed class UpscalerCapabilities
{
    public bool HasDlss { get; set; }
    public bool HasDlssG { get; set; }
    public bool HasXeSs { get; set; }
    public bool HasFsr { get; set; }
    public bool HasStreamline { get; set; }
    public List<string> DetectedDlls { get; set; } = new();
    public bool HasAnyUpscaler => HasDlss || HasDlssG || HasXeSs || HasFsr || HasStreamline || DetectedDlls.Count > 0;
}

public sealed class AntiCheatInfo
{
    public bool HasAntiCheat { get; set; }
    public AntiCheatRiskLevel RiskLevel { get; set; } = AntiCheatRiskLevel.None;
    public List<string> DetectedSystems { get; set; } = new();
    public string Summary => DetectedSystems.Count == 0
        ? "未检测到内核/常见反作弊"
        : $"检测到反作弊: {string.Join(", ", DetectedSystems)}";
}

public sealed class InjectionStatus
{
    public bool IsInjected { get; set; }
    public string? ProxyDllName { get; set; }
    public string? PresetId { get; set; }
    public string? OptiScalerVersion { get; set; }
    public bool HasOptiPatcher { get; set; }
    public bool HasFakenvapi { get; set; }
    public DateTimeOffset? InjectedAtUtc { get; set; }
    public string? LastLogSnippet { get; set; }
    public OptiScalerConfig? AppliedConfig { get; set; }
}

public sealed class GameEntry
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public GamePlatform Platform { get; set; }
    public string InstallDirectory { get; set; } = string.Empty;
    public string TargetRenderDirectory { get; set; } = string.Empty;
    public string? MainExecutablePath { get; set; }
    public string? LaunchUri { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? HeroImageUrl { get; set; }
    public string? HeroBlurImageUrl { get; set; }
    public string? LogoImageUrl { get; set; }
    public GameEngine Engine { get; set; } = GameEngine.Unknown;
    public PeArchitecture Architecture { get; set; } = PeArchitecture.Unknown;
    public UpscalerCapabilities NativeUpscalers { get; set; } = new();
    public AntiCheatInfo AntiCheat { get; set; } = new();
    public InjectionStatus Injection { get; set; } = new();
    public bool IsWritable { get; set; } = true;
    public bool AutoInjectExcluded { get; set; }
    public bool IsCurrentlyRunning { get; set; }
    public int? RunningPid { get; set; }
}
