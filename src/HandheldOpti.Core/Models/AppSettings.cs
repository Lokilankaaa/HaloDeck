namespace HandheldOpti.Core.Models;

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

public sealed class AppSettings
{
    public string ActiveOptiScalerSlot { get; set; } = nameof(ComponentSlot.OptiScalerNightly);
    public string DefaultPresetId { get; set; } = "strix-halo-quality";
    public bool BackgroundAutoInjectEnabled { get; set; } = false;
    public bool StartMinimizedToTray { get; set; } = false;
    public bool LaunchConsoleFullscreen { get; set; } = false;
    public bool LaunchAtWindowsStartup { get; set; } = false;
    public int InsertHoldDurationMs { get; set; } = 65;

    // GPD Win 5 recommended bindings:
    // QAM Sidebar: Select + RB or Ctrl+Alt+O (bind to L4 paddle in WinControls)
    // OptiScaler Menu (Insert): Select + Start or Ctrl+Alt+I (bind to R4 paddle in WinControls)
    // Main Console: Ctrl+Shift+O
    public GamepadComboBinding QamGamepadCombo { get; set; } = new() { Buttons = new() { "BACK", "RB" } };
    public GamepadComboBinding InsertGamepadCombo { get; set; } = new() { Buttons = new() { "BACK", "START" } };

    public HotkeyBinding QamHotkey { get; set; } = new() { Modifiers = 0x0001 | 0x0002, VirtualKey = 0x4F, DisplayText = "Ctrl + Alt + O" };
    public HotkeyBinding InsertHotkey { get; set; } = new() { Modifiers = 0x0001 | 0x0002, VirtualKey = 0x49, DisplayText = "Ctrl + Alt + I" };
    public HotkeyBinding ConsoleHotkey { get; set; } = new() { Modifiers = 0x0002 | 0x0004, VirtualKey = 0x4F, DisplayText = "Ctrl + Shift + O" };

    public List<string> ExcludedGameIds { get; set; } = new();
    public List<string> DisabledCustomDlls { get; set; } = new();
}
