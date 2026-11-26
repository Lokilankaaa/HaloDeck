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
    // Opt-in elevation to break Windows UIPI against admin-elevated games (e.g. Endfield / NTE / anti-cheat launchers).
    public bool RunAsAdministrator { get; set; } = false;
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

    // Native Click-Through Overlay HUD (+ optional RTSSSharedMemoryV2 bridge)
    public int OverlayLevel { get; set; } = 0; // 0=off, 1..4
    public string OverlayPosition { get; set; } = "top-center"; // "top-left" | "top-center" | "top-right"
    public double OverlayOpacity { get; set; } = 0.86;
    public string OverlayRenderer { get; set; } = "native"; // "native" | "auto_rtss" | "rtss"

    // Desktop Gamepad Mouse Mode
    public bool GamepadMouseModeEnabled { get; set; } = false;

    // GPD Win 5 recommended bindings:
    // Main Console (Frontend): Select + LB or Ctrl+Shift+O (or bind L4/R4 -> F16)
    // QAM Sidebar: Select + RB or Ctrl+Alt+O (or bind L4 -> F13)
    // Overlay HUD: Select + Y or Ctrl+Alt+M (or bind R4 -> F14)
    // OptiScaler Menu (Insert): Select + Start or Ctrl+Alt+I (or bind R4 -> F15)
    public GamepadComboBinding ConsoleGamepadCombo { get; set; } = new() { Buttons = new() { "BACK", "LB" } };
    public GamepadComboBinding QamGamepadCombo { get; set; } = new() { Buttons = new() { "BACK", "RB" } };
    public GamepadComboBinding InsertGamepadCombo { get; set; } = new() { Buttons = new() { "BACK", "START" } };
    public GamepadComboBinding OsdGamepadCombo { get; set; } = new() { Buttons = new() { "BACK", "Y" } };
    public GamepadComboBinding MouseModeGamepadCombo { get; set; } = new();

    public HotkeyBinding QamHotkey { get; set; } = new() { Modifiers = 0x0001 | 0x0002, VirtualKey = 0x4F, DisplayText = "Ctrl + Alt + O" };
    public HotkeyBinding InsertHotkey { get; set; } = new() { Modifiers = 0x0001 | 0x0002, VirtualKey = 0x49, DisplayText = "Ctrl + Alt + I" };
    public HotkeyBinding ConsoleHotkey { get; set; } = new() { Modifiers = 0x0002 | 0x0004, VirtualKey = 0x4F, DisplayText = "Ctrl + Shift + O" };
    public HotkeyBinding OsdHotkey { get; set; } = new() { Modifiers = 0x0001 | 0x0002, VirtualKey = 0x4D, DisplayText = "Ctrl + Alt + M" };

    public bool InterceptPhysicalOskButton { get; set; } = true;
    // Short-press of GPD Win 5 physical GPD Tool / gamepad-mouse toggle key emits VK_F13 (0x7C).
    // Kept enabled by default and completely decoupled from L4/R4 back-button F14/F15 takeover.
    public bool InterceptPhysicalMenuButton { get; set; } = true;
    // Solo short-press of XInput hidden Guide/Menu button (0x0400 via XInputGetStateEx #100), matching GPDTool 0x0400007C.
    public bool InterceptGuideMenuButton { get; set; } = true;
    // Quietly check GitHub Releases for newer HaloDeck versions on startup (non-intrusive badge only).
    public bool AutoCheckUpdatesOnStartup { get; set; } = true;
    public string PreferredOskMode { get; set; } = "tabtip"; // "tabtip" | "osk"
    // Disabled by default so native L4/R4 back buttons (which emit F14/F15 in GPD Win 5 firmware) are never hijacked in games.
    public bool EnableBackButtonHotkeys { get; set; } = false;
    // Schema >= 1 marks settings migrated past the legacy v0.5.3 default (EnableBackButtonHotkeys = true).
    public int BackButtonHotkeysSchemaVersion { get; set; } = 1;
    // Disabled by default: remote XInputEnable(FALSE) can break controller recovery on UE/Denuvo games (e.g. Stellar Blade).
    // Players of background-polling engines (e.g. Nioh 3) can opt in from Tab 4 -> Controls.
    public bool MuteGameGamepadInOverlay { get; set; } = false;

    public Dictionary<string, GamePerformanceProfile> PerGameProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ExcludedGameIds { get; set; } = new();
    public List<string> DisabledCustomDlls { get; set; } = new();
    // User-subscribed third-party community plugin store JSON URLs (Decky-style zero-server catalogs).
    public List<string> PluginStoreUrls { get; set; } = new();
    // Optional HTTPS mirror prefix for China-friendly GitHub Release plugin zip downloads (e.g. "https://gh-proxy.com/").
    public string PluginDownloadMirrorPrefix { get; set; } = string.Empty;
    // Developer mode exposes hot-reload UI, restart backend, open plugin directory, and direct folder import.
    public bool PluginDeveloperMode { get; set; } = false;
}
