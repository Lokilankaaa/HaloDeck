namespace HaloDeck.Core.Models;

public sealed class HotkeyBinding
{
    public uint Modifiers { get; set; } // Win32 MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4, MOD_WIN=8
    public uint VirtualKey { get; set; }
    public string DisplayText { get; set; } = string.Empty;
}

public sealed class GamepadComboBinding
{
    public List<string> Buttons { get; set; } = new();
    public string DisplayText => Buttons.Count == 0 ? "未绑定" : string.Join(" + ", Buttons);
}

public sealed class GamePerformanceProfile
{
    public string GameId { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public int TdpWatts { get; set; } = 25;
    public int RefreshRateHz { get; set; } // 0 = keep current, -1 = dynamic DRR, >0 = fixed Hz (e.g. 60 / 120)
    public int CpuMaxFreqMhz { get; set; } // 0 = keep current, >0 = per-game sub-base/boost cap (e.g. 2800 / 3000)
}

public sealed class AppSettings
{
    public string ActiveOptiScalerSlot { get; set; } = nameof(ComponentSlot.OptiScalerNightly);
    public string DefaultPresetId { get; set; } = "strix-halo-quality";
    public string UiTheme { get; set; } = "aurora"; // "aurora" (极光深色) | "frost" (凝霜浅色)
    public string UiLayout { get; set; } = "bento"; // "bento" (掌机大磁贴风) | "cockpit" (桌面仪表盘风)
    public bool BackgroundAutoInjectEnabled { get; set; } = false;
    public bool StartMinimizedToTray { get; set; } = false;
    public bool LaunchConsoleFullscreen { get; set; } = false;
    public bool LaunchAtWindowsStartup { get; set; } = false;
    public int InsertHoldDurationMs { get; set; } = 65;

    // TDP & Power Linkage (v0.3.1 Open Power & Direct Fan Curves)
    public int TdpPresetSilent { get; set; } = 18;
    public int TdpPresetBalanced { get; set; } = 40;
    public int TdpPresetTurbo { get; set; } = 80;
    public List<int> CustomTdpPresets { get; set; } = [10, 15, 20, 25, 30, 45, 60];
    public int CurrentTdpWatts { get; set; } = 40;
    public bool FlatPowerLock { get; set; } = false; // True => SPL = sPPT = fPPT = CurrentTdpWatts
    public bool UnlockThreeStagePower { get; set; } = false;
    public int SlowPptWatts { get; set; } = 45;
    public int FastPptWatts { get; set; } = 50;
    public bool PowerLinkageEnabled { get; set; } = false;
    public int BatteryTdpWatts { get; set; } = 25;
    public int AcTdpWatts { get; set; } = 55;

    // CPU Frequency Cap & Dual-Fan Control
    public int CpuMaxFreqMhz { get; set; } = 0; // 0 = unlimited
    public string FanMode { get; set; } = "auto"; // "auto" | "manual" | "curve"
    public int ManualFanPercent { get; set; } = 55;
    public string ActiveFanCurveId { get; set; } = "linear_60";
    public List<FanCurveProfile> FanCurves { get; set; } = FanCurveEvaluator.CreateDefaultCurves();

    // Steady & Eco Mode (Zero-Hook Frame Stability & Peripheral Power Sanitizer)
    public string SteadyEcoMode { get; set; } = "off"; // "off" | "auto" | "always"
    public bool SteadyEcoCpuEnabled { get; set; } = true;
    public bool SteadyEcoBackgroundEnabled { get; set; } = true;
    public bool SteadyEcoPeripheralEnabled { get; set; } = true;
    public int SteadyEcoCpuEpp { get; set; } = 70;

    // Native Click-Through Overlay HUD
    public int OverlayLevel { get; set; } = 0; // 0=off, 1..4
    public string OverlayPosition { get; set; } = "top-center"; // "top-left" | "top-center" | "top-right"
    public double OverlayOpacity { get; set; } = 0.86;

    // Desktop Gamepad Mouse Mode
    public bool GamepadMouseModeEnabled { get; set; } = false;

    // GPD Win 5 recommended bindings:
    // QAM Sidebar: Select + RB or Ctrl+Alt+O (or bind L4 -> F13)
    // Overlay HUD: Select + Y or Ctrl+Alt+M (or bind R4 -> F14)
    // Mouse Mode: Select + LB
    // OptiScaler Menu (Insert): Select + Start or Ctrl+Alt+I
    // Main Console: Ctrl+Shift+O
    public GamepadComboBinding QamGamepadCombo { get; set; } = new() { Buttons = new() { "BACK", "RB" } };
    public GamepadComboBinding InsertGamepadCombo { get; set; } = new() { Buttons = new() { "BACK", "START" } };
    public GamepadComboBinding OsdGamepadCombo { get; set; } = new() { Buttons = new() { "BACK", "Y" } };
    public GamepadComboBinding MouseModeGamepadCombo { get; set; } = new() { Buttons = new() { "BACK", "LB" } };

    public HotkeyBinding QamHotkey { get; set; } = new() { Modifiers = 0x0001 | 0x0002, VirtualKey = 0x4F, DisplayText = "Ctrl + Alt + O" };
    public HotkeyBinding InsertHotkey { get; set; } = new() { Modifiers = 0x0001 | 0x0002, VirtualKey = 0x49, DisplayText = "Ctrl + Alt + I" };
    public HotkeyBinding ConsoleHotkey { get; set; } = new() { Modifiers = 0x0002 | 0x0004, VirtualKey = 0x4F, DisplayText = "Ctrl + Shift + O" };
    public HotkeyBinding OsdHotkey { get; set; } = new() { Modifiers = 0x0001 | 0x0002, VirtualKey = 0x4D, DisplayText = "Ctrl + Alt + M" };

    public bool InterceptPhysicalOskButton { get; set; } = true;
    public string PreferredOskMode { get; set; } = "tabtip"; // "tabtip" | "osk"
    public bool EnableBackButtonHotkeys { get; set; } = true;

    public Dictionary<string, GamePerformanceProfile> PerGameProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ExcludedGameIds { get; set; } = new();
    public List<string> DisabledCustomDlls { get; set; } = new();
}
