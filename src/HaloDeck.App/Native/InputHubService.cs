using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using HaloDeck.Core.Models;
using HaloDeck.Core.Services;

namespace HaloDeck.App.Native;

public sealed class InputHubService : IDisposable
{
    public const int HotkeyQamId = 0x5101;
    public const int HotkeyInsertId = 0x5102;
    public const int HotkeyConsoleId = 0x5103;
    public const int HotkeyOsdId = 0x5104;
    public const int HotkeyPaddleQamF13Id = 0x5111;
    public const int HotkeyPaddleOsdF14Id = 0x5112;
    public const int HotkeyPaddleInsF15Id = 0x5113;
    public const int HotkeyPaddleConsoleF16Id = 0x5114;

    private const short StickThreshold = 18000;
    private const short MouseStickDeadzone = 7849;
    private const int InitialRepeatMs = 260;
    private const int RepeatIntervalMs = 95;

    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, DateTime> _directionRepeatAt = new();

    private AppSettings _settings;
    private HashSet<ushort>? _assignedBackButtonFnHidCodes;
    private nint _hotkeyWindowHwnd;
    private HwndSource? _hwndSource;
    private XInputButtons _prevButtons;
    private bool _consoleComboArmed = true;
    private bool _qamComboArmed = true;
    private bool _insertComboArmed = true;
    private bool _osdComboArmed = true;
    private bool _mouseComboArmed = true;
    private volatile bool _suppressNavigationUntilRelease;
    private bool _recordingGamepadCombo;
    private bool _recordGamepadWaitRelease;
    private XInputButtons _recordedMask;
    private volatile bool _recordingBackButtonGamepad;
    private volatile bool _recordBackWaitRelease;
    private readonly HashSet<ushort> _recordedBackButtonCodes = new();
    private volatile string? _activeUiTarget;
    private volatile bool _gamepadMouseModeEnabled;
    private bool _mouseLeftHeld;
    private bool _mouseRightHeld;
    private double _mouseCarryX;
    private double _mouseCarryY;
    private double _wheelCarry;
    private Dispatcher? _uiDispatcher;
    private nint _keyboardHook;
    private Win32Interop.LowLevelKeyboardProc? _keyboardHookProc;
    private bool _ctrlWinOSwallowedDown;
    private DateTime _lastOskTriggerAtUtc = DateTime.MinValue;
    private int _oskReleaseDispatchPending;
    private readonly GuideMenuTriggerGuard _guideMenuGuard = new();
    private readonly GpdVendorHidMenuListener _vendorHidListener = new();
    private readonly GpdVendorHidMenuParser _rawHidParser = new();
    private bool _rawInputRegistered;
    private bool _prevAsyncF13Down;
    private volatile KeyCaptureDiagnosticSession? _activeDiagSession;

    public bool LastRegisterF13Success { get; private set; }
    public int LastRegisterF13Error { get; private set; }

    public event Action? ToggleQamRequested;
    public event Action? ToggleConsoleRequested;
    public event Action? SendInsertToGameRequested;
    public event Action? CycleOsdRequested;
    public event Action? ToggleMouseModeRequested;
    public event Action<bool>? ToggleOnScreenKeyboardRequested;
    public event Action<string>? UiGamepadAction;
    public event Action<int>? GamepadCountChanged;
    public event Action<List<string>>? GamepadComboRecorded;
    public event Action<List<ushort>>? BackButtonGamepadRecorded;

    public int ConnectedControllerCount { get; private set; }
    public bool GamepadMouseModeEnabled
    {
        get => _gamepadMouseModeEnabled;
        set
        {
            if (_gamepadMouseModeEnabled == value) return;
            _gamepadMouseModeEnabled = value;
            if (!value) ReleaseHeldMouseButtons();
        }
    }
    public string? ActiveUiTarget
    {
        get => _activeUiTarget;
        set
        {
            bool wasActive = !string.IsNullOrEmpty(_activeUiTarget);
            bool isNowActive = !string.IsNullOrEmpty(value);
            _activeUiTarget = value;
            if (isNowActive)
            {
                ReleaseHeldMouseButtons();
                if (!wasActive)
                {
                    _directionRepeatAt.Clear();
                    if (XInputInterop.HasAnyActiveInput(StickThreshold))
                    {
                        _suppressNavigationUntilRelease = true;
                    }
                }
            }
            else if (wasActive)
            {
                _directionRepeatAt.Clear();
                _suppressNavigationUntilRelease = true;
            }
        }
    }
    public bool IsUiActive => !string.IsNullOrEmpty(_activeUiTarget);

    public void SuppressUntilGamepadRelease()
    {
        _suppressNavigationUntilRelease = true;
        _directionRepeatAt.Clear();
    }

    public InputHubService(AppSettings settings)
    {
        _settings = settings;
        _gamepadMouseModeEnabled = settings.GamepadMouseModeEnabled;
        _vendorHidListener.RawReportReceived += OnVendorHidRawReport;
        _vendorHidListener.MenuButtonPressed += OnVendorHidMenuButtonPressed;
    }

    private void OnVendorHidRawReport(byte[] packet)
    {
        if (_activeDiagSession is { } diag)
        {
            int len = Math.Min(16, packet.Length);
            string hex = BitConverter.ToString(packet, 0, len);
            byte magic = packet.Length > 1 ? packet[1] : (byte)0;
            byte keyByte = packet.Length >= 9 ? packet[8] : (byte)0;
            diag.Record(
                Environment.TickCount64,
                "GpdVendorHid",
                $"len={packet.Length} magic=0x{magic:X2} key[8]=0x{keyByte:X2}",
                hex,
                wouldTriggerQam: false);
        }
    }

    private void OnVendorHidMenuButtonPressed()
    {
        if (!_settings.InterceptPhysicalMenuButton)
        {
            return;
        }

        long now = Environment.TickCount64;
        if (_activeDiagSession is { } diag)
        {
            diag.Record(
                now,
                "GpdVendorHid",
                "GPD 0xFF00 物理菜单键上升沿 (01 A5/B4 ... [8]=0x68)",
                "01-A5/B4-...-68",
                wouldTriggerQam: true);
            return;
        }

        if (_guideMenuGuard.TryClaimQamMenuToggle(now))
        {
            _ = Task.Run(() => ToggleQamRequested?.Invoke());
        }
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        GamepadMouseModeEnabled = settings.GamepadMouseModeEnabled;
        if (_uiDispatcher is not null && !_uiDispatcher.CheckAccess())
        {
            _uiDispatcher.BeginInvoke(RegisterGlobalHotkeys);
        }
        else
        {
            RegisterGlobalHotkeys();
        }
    }

    private volatile bool _suppressFirmwareGhostF14;
    private volatile bool _suppressFirmwareGhostF15;

    public void UpdateAssignedBackButtonFnCodes(IEnumerable<ushort>? hidCodes)
    {
        _assignedBackButtonFnHidCodes = hidCodes is null ? null : new HashSet<ushort>(hidCodes);
        if (_uiDispatcher is not null && !_uiDispatcher.CheckAccess())
        {
            _uiDispatcher.BeginInvoke(RegisterGlobalHotkeys);
        }
        else
        {
            RegisterGlobalHotkeys();
        }
    }

    public void UpdateFirmwareGhostKeySuppression(bool suppressF14, bool suppressF15)
    {
        _suppressFirmwareGhostF14 = suppressF14;
        _suppressFirmwareGhostF15 = suppressF15;
    }

    public void AttachHotkeyWindow(nint hwnd)
    {
        _uiDispatcher = Dispatcher.CurrentDispatcher;
        _hotkeyWindowHwnd = hwnd;
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);
        EnsureKeyboardHookInstalled();
        RegisterGlobalHotkeys();
        RegisterRawInputChannels(hwnd);
    }

    private void RegisterRawInputChannels(nint hwnd)
    {
        if (hwnd == 0 || _rawInputRegistered)
        {
            return;
        }

        var devices = new Win32Interop.RAWINPUTDEVICE[]
        {
            new() { usUsagePage = 0x0001, usUsage = 0x0006, dwFlags = Win32Interop.RIDEV_INPUTSINK, hwndTarget = hwnd },
            new() { usUsagePage = 0x000C, usUsage = 0x0001, dwFlags = Win32Interop.RIDEV_INPUTSINK, hwndTarget = hwnd },
            new() { usUsagePage = 0xFF00, usUsage = 0x0001, dwFlags = Win32Interop.RIDEV_INPUTSINK, hwndTarget = hwnd }
        };

        try
        {
            _rawInputRegistered = Win32Interop.RegisterRawInputDevices(
                devices,
                (uint)devices.Length,
                (uint)Marshal.SizeOf<Win32Interop.RAWINPUTDEVICE>());
        }
        catch
        {
            _rawInputRegistered = false;
        }
    }

    public void StartPolling()
    {
        _vendorHidListener.Start();
        _ = Task.Run(() => PollLoopAsync(_cts.Token), _cts.Token);
    }

    public void BeginRecordGamepadCombo()
    {
        _recordedMask = XInputButtons.None;
        _recordGamepadWaitRelease = true;
        _recordingGamepadCombo = true;
    }

    public void CancelRecordGamepadCombo()
    {
        _recordingGamepadCombo = false;
        _recordGamepadWaitRelease = false;
        _recordedMask = XInputButtons.None;
    }

    public void BeginRecordBackButtonGamepad()
    {
        _recordedBackButtonCodes.Clear();
        _recordBackWaitRelease = true;
        _recordingBackButtonGamepad = true;
    }

    public void CancelRecordBackButtonGamepad()
    {
        _recordingBackButtonGamepad = false;
        _recordBackWaitRelease = false;
        _recordedBackButtonCodes.Clear();
    }

    public void RegisterGlobalHotkeys()
    {
        if (_hotkeyWindowHwnd == 0) return;

        UnregisterGlobalHotkeys();

        TryRegisterHotkey(HotkeyQamId, _settings.QamHotkey);
        TryRegisterHotkey(HotkeyInsertId, _settings.InsertHotkey);
        TryRegisterHotkey(HotkeyConsoleId, _settings.ConsoleHotkey);
        TryRegisterHotkey(HotkeyOsdId, _settings.OsdHotkey);

        // GPD Win 5 physical front quick-menu key (short press of GPD Tool / gamepad-mouse toggle key) emits VK_F13 (0x7C).
        // Decoupled from L4/R4 back buttons so users can summon QAM with the dedicated physical key while keeping L4/R4 untouched.
        bool filterByHw = _assignedBackButtonFnHidCodes is not null;
        bool backF13 = _settings.EnableBackButtonHotkeys && (!filterByHw || _assignedBackButtonFnHidCodes!.Contains(0x0068));
        if (_settings.InterceptPhysicalMenuButton || backF13)
        {
            LastRegisterF13Success = Win32Interop.RegisterHotKey(_hotkeyWindowHwnd, HotkeyPaddleQamF13Id, Win32Interop.MOD_NOREPEAT, 0x7C);
            LastRegisterF13Error = LastRegisterF13Success ? 0 : Marshal.GetLastWin32Error();
        }
        else
        {
            LastRegisterF13Success = false;
            LastRegisterF13Error = 0;
        }

        // Bare paddle bindings for GPD Win 5 L4/R4 (HID 0x69..0x6B -> VK_F14..VK_F16).
        // Off by default so GPD Win 5 firmware's native F14/F15 virtual keyboard output is never hijacked.
        // When enabled AND the GPD controller mapping is known, only register the specific F14..F16 keys
        // actually assigned to L4/R4 so an unmapped/game-bound paddle never misfires firmware F14/F15.
        if (_settings.EnableBackButtonHotkeys)
        {
            bool allowF14 = !filterByHw || _assignedBackButtonFnHidCodes!.Contains(0x0069);
            bool allowF15 = !filterByHw || _assignedBackButtonFnHidCodes!.Contains(0x006A);
            bool allowF16 = !filterByHw || _assignedBackButtonFnHidCodes!.Contains(0x006B);

            if (allowF14) Win32Interop.RegisterHotKey(_hotkeyWindowHwnd, HotkeyPaddleOsdF14Id, Win32Interop.MOD_NOREPEAT, 0x7D);
            if (allowF15) Win32Interop.RegisterHotKey(_hotkeyWindowHwnd, HotkeyPaddleInsF15Id, Win32Interop.MOD_NOREPEAT, 0x7E);
            if (allowF16) Win32Interop.RegisterHotKey(_hotkeyWindowHwnd, HotkeyPaddleConsoleF16Id, Win32Interop.MOD_NOREPEAT, 0x7F);
        }
    }

    private void EnsureKeyboardHookInstalled()
    {
        if (_keyboardHook != 0) return;
        _keyboardHookProc = KeyboardHookCallback;
        nint mod = Win32Interop.GetModuleHandleW(null);
        _keyboardHook = Win32Interop.SetWindowsHookExW(Win32Interop.WH_KEYBOARD_LL, _keyboardHookProc, mod, 0);
    }

    private void RemoveKeyboardHook()
    {
        if (_keyboardHook != 0)
        {
            Win32Interop.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = 0;
        }
        _keyboardHookProc = null;
    }

    private nint KeyboardHookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && lParam != 0)
        {
            var kb = Marshal.PtrToStructure<Win32Interop.KBDLLHOOKSTRUCT>(lParam);
            if (kb.dwExtraInfo == Win32Interop.SyntheticChordBreakExtraInfo)
            {
                return Win32Interop.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
            }

            bool isInjected = (kb.flags & Win32Interop.LLKHF_INJECTED) != 0;
            int msgHook = wParam.ToInt32();
            bool isKeyDownHook = msgHook is Win32Interop.WM_KEYDOWN or Win32Interop.WM_SYSKEYDOWN;

            if (isKeyDownHook && _activeDiagSession is { } diag)
            {
                bool isF13Diag = kb.vkCode == 0x7C && _settings.InterceptPhysicalMenuButton;
                diag.Record(
                    Environment.TickCount64,
                    "KeyboardHookLL",
                    $"vk=0x{kb.vkCode:X2} scan=0x{kb.scanCode:X2} flags=0x{kb.flags:X2} injected={isInjected}",
                    $"{kb.vkCode:X2}-{kb.scanCode:X2}-{kb.flags:X2}",
                    wouldTriggerQam: isF13Diag);
            }

            // Capture GPD Win 5 physical menu key VK_F13 (0x7C) BEFORE LLKHF_INJECTED skip
            // so driver/helper-injected F13 or non-RegisterHotKey firmware both reliably summon QAM.
            if (kb.vkCode == 0x7C && _settings.InterceptPhysicalMenuButton)
            {
                if (isKeyDownHook && _activeDiagSession is null && _guideMenuGuard.TryClaimQamMenuToggle(Environment.TickCount64))
                {
                    _ = Task.Run(() => ToggleQamRequested?.Invoke());
                }
                return Win32Interop.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
            }

            if (isInjected)
            {
                return Win32Interop.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
            }

            // Suppress GPD Win 5 firmware's unconditional raw F14 (0x7D) / F15 (0x7E) duplicate keystrokes
            // when L4/R4 are mapped to other keys (or disabled) so games/apps only receive the programmed key.
            if (kb.vkCode == 0x7D && _suppressFirmwareGhostF14)
            {
                return 1;
            }
            if (kb.vkCode == 0x7E && _suppressFirmwareGhostF15)
            {
                return 1;
            }

            if (_settings.InterceptPhysicalOskButton && kb.vkCode == Win32Interop.VK_O)
            {
                int msg = wParam.ToInt32();
                bool isDown = msg is Win32Interop.WM_KEYDOWN or Win32Interop.WM_SYSKEYDOWN;
                bool isUp = msg is Win32Interop.WM_KEYUP or Win32Interop.WM_SYSKEYUP;

                if (isDown)
                {
                    bool ctrlDown = (Win32Interop.GetAsyncKeyState(Win32Interop.VK_CONTROL) & 0x8000) != 0 ||
                                    (Win32Interop.GetAsyncKeyState(Win32Interop.VK_LCONTROL) & 0x8000) != 0 ||
                                    (Win32Interop.GetAsyncKeyState(Win32Interop.VK_RCONTROL) & 0x8000) != 0;
                    bool winDown = (Win32Interop.GetAsyncKeyState(Win32Interop.VK_LWIN) & 0x8000) != 0 ||
                                   (Win32Interop.GetAsyncKeyState(Win32Interop.VK_RWIN) & 0x8000) != 0;

                    if (ctrlDown && winDown)
                    {
                        if (!_ctrlWinOSwallowedDown)
                        {
                            _ctrlWinOSwallowedDown = true;
                            // Capture pre-chord TabTip state BEFORE Ctrl/Win or synthetic chord break auto-dismisses Gamepad TabTip.
                            bool shouldClose = HandheldToolboxService.ShouldCloseFromHookSnapshot();
                            // Break LWin solo-tap Start Menu trigger before swallowing VK_O.
                            Win32Interop.SendSyntheticChordBreak();

                            if ((DateTime.UtcNow - _lastOskTriggerAtUtc).TotalMilliseconds >= 420 &&
                                Interlocked.CompareExchange(ref _oskReleaseDispatchPending, 1, 0) == 0)
                            {
                                _lastOskTriggerAtUtc = DateTime.UtcNow;
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        // Match GPD Tool 1.66: wait for physical Ctrl+Win+O keys to release + 50ms settle
                                        for (int elapsed = 0; elapsed < 600; elapsed += 15)
                                        {
                                            bool anyHeld =
                                                (Win32Interop.GetAsyncKeyState(Win32Interop.VK_O) & 0x8000) != 0 ||
                                                (Win32Interop.GetAsyncKeyState(Win32Interop.VK_CONTROL) & 0x8000) != 0 ||
                                                (Win32Interop.GetAsyncKeyState(Win32Interop.VK_LCONTROL) & 0x8000) != 0 ||
                                                (Win32Interop.GetAsyncKeyState(Win32Interop.VK_RCONTROL) & 0x8000) != 0 ||
                                                (Win32Interop.GetAsyncKeyState(Win32Interop.VK_LWIN) & 0x8000) != 0 ||
                                                (Win32Interop.GetAsyncKeyState(Win32Interop.VK_RWIN) & 0x8000) != 0;
                                            if (!anyHeld) break;
                                            await Task.Delay(15).ConfigureAwait(false);
                                        }
                                        await Task.Delay(50).ConfigureAwait(false);
                                        ToggleOnScreenKeyboardRequested?.Invoke(shouldClose);
                                    }
                                    finally
                                    {
                                        Interlocked.Exchange(ref _oskReleaseDispatchPending, 0);
                                    }
                                });
                            }
                        }
                        return 1;
                    }
                }
                else if (isUp && _ctrlWinOSwallowedDown)
                {
                    _ctrlWinOSwallowedDown = false;
                    return 1;
                }
            }
        }
        return Win32Interop.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private void TryRegisterHotkey(int id, HotkeyBinding binding)
    {
        if (binding is null || binding.VirtualKey == 0) return;
        Win32Interop.RegisterHotKey(_hotkeyWindowHwnd, id, binding.Modifiers | Win32Interop.MOD_NOREPEAT, binding.VirtualKey);
    }

    private void UnregisterGlobalHotkeys()
    {
        if (_hotkeyWindowHwnd == 0) return;
        Win32Interop.UnregisterHotKey(_hotkeyWindowHwnd, HotkeyQamId);
        Win32Interop.UnregisterHotKey(_hotkeyWindowHwnd, HotkeyInsertId);
        Win32Interop.UnregisterHotKey(_hotkeyWindowHwnd, HotkeyConsoleId);
        Win32Interop.UnregisterHotKey(_hotkeyWindowHwnd, HotkeyOsdId);
        Win32Interop.UnregisterHotKey(_hotkeyWindowHwnd, HotkeyPaddleQamF13Id);
        Win32Interop.UnregisterHotKey(_hotkeyWindowHwnd, HotkeyPaddleOsdF14Id);
        Win32Interop.UnregisterHotKey(_hotkeyWindowHwnd, HotkeyPaddleInsF15Id);
        Win32Interop.UnregisterHotKey(_hotkeyWindowHwnd, HotkeyPaddleConsoleF16Id);
    }

    private readonly Dictionary<int, long> _lastHotkeyTickMs = new();

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == Win32Interop.WM_INPUT)
        {
            HandleRawInput(lParam);
            return 0;
        }

        if (msg == Win32Interop.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            long now = Environment.TickCount64;
            if (_activeDiagSession is { } diagHot)
            {
                bool isMenuHk = id is HotkeyQamId or HotkeyPaddleQamF13Id;
                diagHot.Record(now, "RegisterHotKey", $"WM_HOTKEY id=0x{id:X}", $"{id:X4}", wouldTriggerQam: isMenuHk);
                if (isMenuHk)
                {
                    handled = true;
                    return 0;
                }
            }

            if (_lastHotkeyTickMs.TryGetValue(id, out long prev) && now - prev < 280)
            {
                handled = true;
                return 0;
            }
            _lastHotkeyTickMs[id] = now;

            if (id is HotkeyQamId or HotkeyPaddleQamF13Id)
            {
                if (id != HotkeyPaddleQamF13Id || _guideMenuGuard.TryClaimQamMenuToggle(now))
                {
                    ToggleQamRequested?.Invoke();
                }
                handled = true;
            }
            else if (id is HotkeyInsertId or HotkeyPaddleInsF15Id)
            {
                SendInsertToGameRequested?.Invoke();
                handled = true;
            }
            else if (id is HotkeyConsoleId or HotkeyPaddleConsoleF16Id)
            {
                ToggleConsoleRequested?.Invoke();
                handled = true;
            }
            else if (id is HotkeyOsdId or HotkeyPaddleOsdF14Id)
            {
                CycleOsdRequested?.Invoke();
                handled = true;
            }
        }
        return 0;
    }

    private void HandleRawInput(nint hRawInput)
    {
        if (hRawInput == 0)
        {
            return;
        }

        uint headerSize = (uint)Marshal.SizeOf<Win32Interop.RAWINPUTHEADER>();
        uint dwSize = 0;
        if (Win32Interop.GetRawInputData(hRawInput, Win32Interop.RID_INPUT, 0, ref dwSize, headerSize) != 0 ||
            dwSize < headerSize ||
            dwSize > 4096)
        {
            return;
        }

        nint buf = Marshal.AllocHGlobal((int)dwSize);
        try
        {
            uint read = Win32Interop.GetRawInputData(hRawInput, Win32Interop.RID_INPUT, buf, ref dwSize, headerSize);
            if (read == uint.MaxValue || read < headerSize)
            {
                return;
            }

            var header = Marshal.PtrToStructure<Win32Interop.RAWINPUTHEADER>(buf);
            if (header.dwType == Win32Interop.RIM_TYPEKEYBOARD)
            {
                if (read < headerSize + (uint)Marshal.SizeOf<Win32Interop.RAWKEYBOARD>())
                {
                    return;
                }

                var rk = Marshal.PtrToStructure<Win32Interop.RAWKEYBOARD>(buf + (int)headerSize);
                if ((rk.Flags & Win32Interop.RI_KEY_BREAK) != 0)
                {
                    return;
                }

                bool matches = RawKeyboardMenuMatcher.IsF13KeyDown(rk.VKey, rk.MakeCode, rk.Flags);
                long now = Environment.TickCount64;
                if (_activeDiagSession is { } diag)
                {
                    diag.Record(
                        now,
                        "RawInputKbd",
                        $"vk=0x{rk.VKey:X2} make=0x{rk.MakeCode:X2} flags=0x{rk.Flags:X2}",
                        $"{rk.VKey:X2}-{rk.MakeCode:X2}-{rk.Flags:X2}",
                        wouldTriggerQam: matches && _settings.InterceptPhysicalMenuButton);
                    return;
                }

                if (_settings.InterceptPhysicalMenuButton && matches && _guideMenuGuard.TryClaimQamMenuToggle(now))
                {
                    _ = Task.Run(() => ToggleQamRequested?.Invoke());
                }
            }
            else if (header.dwType == Win32Interop.RIM_TYPEHID)
            {
                uint rawHidSize = (uint)Marshal.SizeOf<Win32Interop.RAWHID>();
                if (read < headerSize + rawHidSize)
                {
                    return;
                }

                var rawHid = Marshal.PtrToStructure<Win32Interop.RAWHID>(buf + (int)headerSize);
                int dataOffset = (int)(headerSize + rawHidSize);
                int avail = (int)read - dataOffset;
                long totalRaw = (long)rawHid.dwSizeHid * rawHid.dwCount;
                if (avail <= 0 || totalRaw <= 0)
                {
                    return;
                }

                int copyLen = (int)Math.Min(avail, Math.Min(totalRaw, 256));
                var rawBytes = new byte[copyLen];
                Marshal.Copy(buf + dataOffset, rawBytes, 0, copyLen);

                uint vid = 0, pid = 0;
                ushort up = 0, u = 0;
                if (header.hDevice != 0)
                {
                    uint devInfoSize = (uint)Marshal.SizeOf<Win32Interop.RID_DEVICE_INFO>();
                    nint devInfoBuf = Marshal.AllocHGlobal((int)devInfoSize);
                    try
                    {
                        Marshal.WriteInt32(devInfoBuf, (int)devInfoSize);
                        if (Win32Interop.GetRawInputDeviceInfoW(header.hDevice, Win32Interop.RIDI_DEVICEINFO, devInfoBuf, ref devInfoSize) != uint.MaxValue)
                        {
                            var info = Marshal.PtrToStructure<Win32Interop.RID_DEVICE_INFO>(devInfoBuf);
                            if (info.dwType == Win32Interop.RIM_TYPEHID)
                            {
                                vid = info.hid.dwVendorId;
                                pid = info.hid.dwProductId;
                                up = info.hid.usUsagePage;
                                u = info.hid.usUsage;
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(devInfoBuf);
                    }
                }

                bool isGpdVendorMenu = up == 0xFF00 &&
                                       vid == 0x2F24 &&
                                       !GpdControllerService.IsConfigTransferActive &&
                                       _rawHidParser.ProcessReport(rawBytes);

                long now = Environment.TickCount64;
                if (_activeDiagSession is { } diag)
                {
                    string hex = BitConverter.ToString(rawBytes, 0, Math.Min(16, rawBytes.Length));
                    diag.Record(
                        now,
                        "RawInputHid",
                        $"VID={vid:X4} PID={pid:X4} UP=0x{up:X4} U=0x{u:X4} len={rawBytes.Length}",
                        hex,
                        wouldTriggerQam: isGpdVendorMenu && _settings.InterceptPhysicalMenuButton);
                    return;
                }

                if (_settings.InterceptPhysicalMenuButton && isGpdVendorMenu && _guideMenuGuard.TryClaimQamMenuToggle(now))
                {
                    _ = Task.Run(() => ToggleQamRequested?.Invoke());
                }
            }
        }
        catch
        {
            // Ignore malformed or transient raw input packets
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                bool f13AsyncDown = (Win32Interop.GetAsyncKeyState(0x7C) & 0x8000) != 0;
                if (f13AsyncDown && !_prevAsyncF13Down && _settings.InterceptPhysicalMenuButton)
                {
                    long nowTick = Environment.TickCount64;
                    if (_activeDiagSession is { } diagAsync)
                    {
                        diagAsync.Record(nowTick, "AsyncKeyState", "GetAsyncKeyState(VK_F13 0x7C) 上升沿", "7C", wouldTriggerQam: true);
                    }
                    else if (_guideMenuGuard.TryClaimQamMenuToggle(nowTick))
                    {
                        ToggleQamRequested?.Invoke();
                    }
                }
                _prevAsyncF13Down = f13AsyncDown;

                if (XInputInterop.TryGetCombinedGamepadState(out var gamepad, out int count))
                {
                    if (count != ConnectedControllerCount)
                    {
                        ConnectedControllerCount = count;
                        GamepadCountChanged?.Invoke(count);
                    }

                    var buttons = (XInputButtons)gamepad.wButtons;

                    if (_recordingBackButtonGamepad)
                    {
                        var curCodes = ExtractV2GamepadCodes(gamepad, buttons);
                        if (_recordBackWaitRelease)
                        {
                            if (curCodes.Count == 0)
                            {
                                _recordBackWaitRelease = false;
                            }
                            _prevButtons = buttons;
                            await Task.Delay(12, ct).ConfigureAwait(false);
                            continue;
                        }
                        if (curCodes.Count > 0)
                        {
                            foreach (ushort code in curCodes)
                            {
                                if (_recordedBackButtonCodes.Count < 4)
                                {
                                    _recordedBackButtonCodes.Add(code);
                                }
                            }
                        }
                        else if (_recordedBackButtonCodes.Count > 0)
                        {
                            var captured = _recordedBackButtonCodes.ToList();
                            _recordingBackButtonGamepad = false;
                            _recordedBackButtonCodes.Clear();
                            _suppressNavigationUntilRelease = true;
                            _directionRepeatAt.Clear();
                            BackButtonGamepadRecorded?.Invoke(captured);
                        }

                        _prevButtons = buttons;
                        await Task.Delay(12, ct).ConfigureAwait(false);
                        continue;
                    }

                    if (_recordingGamepadCombo)
                    {
                        if (_recordGamepadWaitRelease)
                        {
                            if (buttons == XInputButtons.None)
                            {
                                _recordGamepadWaitRelease = false;
                            }
                            _prevButtons = buttons;
                            await Task.Delay(12, ct).ConfigureAwait(false);
                            continue;
                        }
                        if (buttons != XInputButtons.None)
                        {
                            _recordedMask |= buttons;
                        }
                        else if (_recordedMask != XInputButtons.None)
                        {
                            var captured = XInputInterop.ToButtonNames(_recordedMask);
                            _recordingGamepadCombo = false;
                            _recordedMask = XInputButtons.None;
                            _suppressNavigationUntilRelease = true;
                            _directionRepeatAt.Clear();
                            GamepadComboRecorded?.Invoke(captured);
                        }

                        _prevButtons = buttons;
                        await Task.Delay(12, ct).ConfigureAwait(false);
                        continue;
                    }

                    if (_activeDiagSession is { } diagX && buttons != _prevButtons)
                    {
                        diagX.Record(
                            Environment.TickCount64,
                            "XInputEx",
                            $"wButtons=0x{(ushort)buttons:X4} Guide={(buttons.HasFlag(XInputButtons.Guide) ? 1 : 0)} LT={gamepad.bLeftTrigger} RT={gamepad.bRightTrigger}",
                            $"{(ushort)buttons:X4}",
                            wouldTriggerQam: buttons.HasFlag(XInputButtons.Guide) && _settings.InterceptGuideMenuButton);
                    }

                    // 0. Check solo short-press of XInput hidden Guide/Menu button (0x0400 via XInputGetStateEx #100)
                    // Matches GPDTool's dual-channel 0x0400007C (Guide + F13) while preserving Steam Guide+Button chords.
                    if (_settings.InterceptGuideMenuButton && _activeDiagSession is null)
                    {
                        bool guideDown = buttons.HasFlag(XInputButtons.Guide);
                        bool otherActive = (buttons & XInputInterop.KnownNonGuideButtonsMask) != XInputButtons.None ||
                                           gamepad.bLeftTrigger > 30 || gamepad.bRightTrigger > 30;
                        if (_guideMenuGuard.UpdateGuideState(guideDown, otherActive, Environment.TickCount64))
                        {
                            _suppressNavigationUntilRelease = true;
                            _directionRepeatAt.Clear();
                            ToggleQamRequested?.Invoke();
                        }
                    }
                    else if (!_settings.InterceptGuideMenuButton)
                    {
                        _guideMenuGuard.Reset();
                    }

                    // 0.5 Check global Main Console (Frontend) toggle combo (require >=2 buttons so single gameplay buttons like Start are never hijacked)
                    var consoleMask = XInputInterop.CombineMask(_settings.ConsoleGamepadCombo?.Buttons ?? []);
                    if (IsValidGlobalComboMask(consoleMask))
                    {
                        if ((buttons & consoleMask) == consoleMask)
                        {
                            if (_consoleComboArmed)
                            {
                                _consoleComboArmed = false;
                                _suppressNavigationUntilRelease = true;
                                _directionRepeatAt.Clear();
                                ToggleConsoleRequested?.Invoke();
                            }
                        }
                        else if ((buttons & consoleMask) == XInputButtons.None)
                        {
                            _consoleComboArmed = true;
                        }
                    }

                    // 1. Check global QAM toggle combo (works inside any game!)
                    var qamMask = XInputInterop.CombineMask(_settings.QamGamepadCombo.Buttons);
                    if (IsValidGlobalComboMask(qamMask))
                    {
                        if ((buttons & qamMask) == qamMask)
                        {
                            if (_qamComboArmed)
                            {
                                _qamComboArmed = false;
                                _suppressNavigationUntilRelease = true;
                                _directionRepeatAt.Clear();
                                ToggleQamRequested?.Invoke();
                            }
                        }
                        else if ((buttons & qamMask) == XInputButtons.None)
                        {
                            _qamComboArmed = true;
                        }
                    }

                    // 2. Check global OptiScaler Insert combo (works inside any game, blocked when HaloDeck UI owns focus)
                    var insertMask = XInputInterop.CombineMask(_settings.InsertGamepadCombo.Buttons);
                    if (IsValidGlobalComboMask(insertMask))
                    {
                        if ((buttons & insertMask) == insertMask)
                        {
                            if (!IsUiActive && _insertComboArmed)
                            {
                                _insertComboArmed = false;
                                _suppressNavigationUntilRelease = true;
                                _directionRepeatAt.Clear();
                                SendInsertToGameRequested?.Invoke();
                            }
                        }
                        else if ((buttons & insertMask) == XInputButtons.None)
                        {
                            _insertComboArmed = true;
                        }
                    }

                    // 2.5 Check global OSD cycle combo & Gamepad Mouse Mode combo
                    var osdMask = XInputInterop.CombineMask(_settings.OsdGamepadCombo.Buttons);
                    if (IsValidGlobalComboMask(osdMask))
                    {
                        if ((buttons & osdMask) == osdMask)
                        {
                            if (_osdComboArmed)
                            {
                                _osdComboArmed = false;
                                _suppressNavigationUntilRelease = true;
                                _directionRepeatAt.Clear();
                                CycleOsdRequested?.Invoke();
                            }
                        }
                        else if ((buttons & osdMask) == XInputButtons.None)
                        {
                            _osdComboArmed = true;
                        }
                    }

                    var mouseMask = XInputInterop.CombineMask(_settings.MouseModeGamepadCombo.Buttons);
                    if (IsValidGlobalComboMask(mouseMask))
                    {
                        if ((buttons & mouseMask) == mouseMask)
                        {
                            if (!IsUiActive && _mouseComboArmed)
                            {
                                _mouseComboArmed = false;
                                _suppressNavigationUntilRelease = true;
                                _directionRepeatAt.Clear();
                                ToggleMouseModeRequested?.Invoke();
                            }
                        }
                        else if ((buttons & mouseMask) == XInputButtons.None)
                        {
                            _mouseComboArmed = true;
                        }
                    }

                    if (buttons == XInputButtons.None &&
                        gamepad.bLeftTrigger <= 30 &&
                        gamepad.bRightTrigger <= 30 &&
                        Math.Abs((int)gamepad.sThumbLX) <= StickThreshold &&
                        Math.Abs((int)gamepad.sThumbLY) <= StickThreshold)
                    {
                        _suppressNavigationUntilRelease = false;
                    }

                    // 3. When HaloDeck UI is active, translate buttons + sticks to UI navigation;
                    //    otherwise if Desktop Mouse Mode is enabled, drive pointer/wheel/clicks.
                    if (IsUiActive && !_suppressNavigationUntilRelease)
                    {
                        var pressed = buttons & ~_prevButtons;
                        DispatchButtonAction(pressed, XInputButtons.A, "A");
                        DispatchButtonAction(pressed, XInputButtons.B, "B");
                        DispatchButtonAction(pressed, XInputButtons.X, "X");
                        DispatchButtonAction(pressed, XInputButtons.Y, "Y");
                        DispatchButtonAction(pressed, XInputButtons.LeftShoulder, "LB");
                        DispatchButtonAction(pressed, XInputButtons.RightShoulder, "RB");
                        DispatchButtonAction(pressed, XInputButtons.Start, "START");

                        var now = DateTime.UtcNow;
                        HandleDirectionalRepeat("UP",
                            buttons.HasFlag(XInputButtons.DPadUp) || gamepad.sThumbLY > StickThreshold,
                            _prevButtons.HasFlag(XInputButtons.DPadUp),
                            now);
                        HandleDirectionalRepeat("DOWN",
                            buttons.HasFlag(XInputButtons.DPadDown) || gamepad.sThumbLY < -StickThreshold,
                            _prevButtons.HasFlag(XInputButtons.DPadDown),
                            now);
                        HandleDirectionalRepeat("LEFT",
                            buttons.HasFlag(XInputButtons.DPadLeft) || gamepad.sThumbLX < -StickThreshold,
                            _prevButtons.HasFlag(XInputButtons.DPadLeft),
                            now);
                        HandleDirectionalRepeat("RIGHT",
                            buttons.HasFlag(XInputButtons.DPadRight) || gamepad.sThumbLX > StickThreshold,
                            _prevButtons.HasFlag(XInputButtons.DPadRight),
                            now);
                    }
                    else if (!IsUiActive && _gamepadMouseModeEnabled && !_suppressNavigationUntilRelease)
                    {
                        ProcessDesktopMouseTick(gamepad, buttons);
                    }

                    _prevButtons = buttons;
                }
                else if (ConnectedControllerCount != 0)
                {
                    ConnectedControllerCount = 0;
                    _prevButtons = XInputButtons.None;
                    _suppressNavigationUntilRelease = false;
                    _directionRepeatAt.Clear();
                    ReleaseHeldMouseButtons();
                    GamepadCountChanged?.Invoke(0);
                }

                await Task.Delay(10, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
        }
    }

    internal static bool IsValidGlobalComboMask(XInputButtons mask)
        => mask != XInputButtons.None && System.Numerics.BitOperations.PopCount((uint)mask) >= 2;

    private static List<ushort> ExtractV2GamepadCodes(XInputGamepad gp, XInputButtons buttons)
    {
        var codes = new List<ushort>();
        if (buttons.HasFlag(XInputButtons.DPadUp)) codes.Add(0x8000);
        if (buttons.HasFlag(XInputButtons.DPadDown)) codes.Add(0x8001);
        if (buttons.HasFlag(XInputButtons.DPadLeft)) codes.Add(0x8002);
        if (buttons.HasFlag(XInputButtons.DPadRight)) codes.Add(0x8003);
        if (buttons.HasFlag(XInputButtons.Start)) codes.Add(0x8004);
        if (buttons.HasFlag(XInputButtons.Back)) codes.Add(0x8005);
        if (buttons.HasFlag(XInputButtons.A)) codes.Add(0x8007);
        if (buttons.HasFlag(XInputButtons.B)) codes.Add(0x8008);
        if (buttons.HasFlag(XInputButtons.X)) codes.Add(0x8009);
        if (buttons.HasFlag(XInputButtons.Y)) codes.Add(0x800A);
        if (buttons.HasFlag(XInputButtons.LeftShoulder)) codes.Add(0x800B);
        if (buttons.HasFlag(XInputButtons.RightShoulder)) codes.Add(0x800C);
        if (gp.bLeftTrigger > 65) codes.Add(0x800D);
        if (gp.bRightTrigger > 65) codes.Add(0x800E);
        if (buttons.HasFlag(XInputButtons.LeftThumb)) codes.Add(0x800F);
        if (buttons.HasFlag(XInputButtons.RightThumb)) codes.Add(0x8010);
        return codes;
    }

    private void DispatchButtonAction(XInputButtons newlyPressed, XInputButtons flag, string actionName)
    {
        if (newlyPressed.HasFlag(flag))
        {
            UiGamepadAction?.Invoke(actionName);
        }
    }

    private void HandleDirectionalRepeat(string direction, bool isHeld, bool wasDpadHeld, DateTime now)
    {
        if (!isHeld)
        {
            _directionRepeatAt.Remove(direction);
            return;
        }

        if (!_directionRepeatAt.TryGetValue(direction, out var nextAt))
        {
            _directionRepeatAt[direction] = now.AddMilliseconds(InitialRepeatMs);
            UiGamepadAction?.Invoke(direction);
        }
        else if (now >= nextAt || (!wasDpadHeld && nextAt > now.AddMilliseconds(InitialRepeatMs)))
        {
            _directionRepeatAt[direction] = now.AddMilliseconds(RepeatIntervalMs);
            UiGamepadAction?.Invoke(direction);
        }
    }

    private void ProcessDesktopMouseTick(XInputGamepad gp, XInputButtons buttons)
    {
        double speedScale = gp.bLeftTrigger > 120 ? 0.35 : 1.0;

        static double NormalizeAxis(short raw)
        {
            int abs = Math.Abs((int)raw);
            if (abs <= MouseStickDeadzone) return 0;
            double norm = (abs - MouseStickDeadzone) / (32767.0 - MouseStickDeadzone);
            return Math.Sign(raw) * Math.Pow(Math.Clamp(norm, 0, 1), 1.65);
        }

        double nx = NormalizeAxis(gp.sThumbLX);
        double ny = NormalizeAxis(gp.sThumbLY);
        if (nx != 0 || ny != 0)
        {
            const double maxPxPerTick = 16.0;
            _mouseCarryX += nx * maxPxPerTick * speedScale;
            _mouseCarryY += -ny * maxPxPerTick * speedScale;

            int dx = (int)Math.Truncate(_mouseCarryX);
            int dy = (int)Math.Truncate(_mouseCarryY);
            _mouseCarryX -= dx;
            _mouseCarryY -= dy;
            if (dx != 0 || dy != 0)
            {
                Win32Interop.SendMouseMove(dx, dy);
            }
        }
        else
        {
            _mouseCarryX = 0;
            _mouseCarryY = 0;
        }

        double ry = NormalizeAxis(gp.sThumbRY);
        if (ry != 0)
        {
            _wheelCarry += ry * 32.0;
            int wDelta = (int)Math.Truncate(_wheelCarry);
            if (Math.Abs(wDelta) >= 15)
            {
                _wheelCarry -= wDelta;
                Win32Interop.SendMouseWheel(wDelta);
            }
        }
        else
        {
            _wheelCarry = 0;
        }

        bool wantLeft = buttons.HasFlag(XInputButtons.A);
        if (wantLeft != _mouseLeftHeld)
        {
            _mouseLeftHeld = wantLeft;
            Win32Interop.SendMouseButton(isLeft: true, isDown: wantLeft);
        }

        bool wantRight = buttons.HasFlag(XInputButtons.B);
        if (wantRight != _mouseRightHeld)
        {
            _mouseRightHeld = wantRight;
            Win32Interop.SendMouseButton(isLeft: false, isDown: wantRight);
        }
    }

    private void ReleaseHeldMouseButtons()
    {
        if (_mouseLeftHeld)
        {
            _mouseLeftHeld = false;
            Win32Interop.SendMouseButton(isLeft: true, isDown: false);
        }
        if (_mouseRightHeld)
        {
            _mouseRightHeld = false;
            Win32Interop.SendMouseButton(isLeft: false, isDown: false);
        }
        _mouseCarryX = 0;
        _mouseCarryY = 0;
        _wheelCarry = 0;
    }

    public async Task<KeyCaptureSessionReport> RunMenuKeyDiagnosticAsync(
        int durationMs = 5000,
        GpdControllerConfig? gpdConfig = null,
        CancellationToken ct = default)
    {
        int clampedMs = Math.Clamp(durationMs, 2000, 10000);
        var session = new KeyCaptureDiagnosticSession(Environment.TickCount64);
        _activeDiagSession = session;
        try
        {
            await Task.Delay(clampedMs, ct).ConfigureAwait(false);
        }
        finally
        {
            _activeDiagSession = null;
            _guideMenuGuard.Reset();
        }

        bool gpdToolRunning = false;
        bool gpdToolElevated = false;
        try
        {
            var procs = Process.GetProcessesByName("GPDTool");
            foreach (var proc in procs)
            {
                using (proc)
                {
                    gpdToolRunning = true;
                    try
                    {
                        if (Win32Interop.IsProcessElevated((uint)proc.Id))
                        {
                            gpdToolElevated = true;
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        var (_, gpdSvcRunning) = PawnIoHardwareDaemon.ProbeWindowsServiceState("GPDToolService");

        var env = new KeyCaptureEnvironmentInfo(
            SelfElevated: Win32Interop.IsCurrentProcessElevated(),
            RegisterHotKeyF13Ok: LastRegisterF13Success,
            RegisterHotKeyF13Error: LastRegisterF13Error,
            GpdVendorHidConnected: _vendorHidListener.IsConnected,
            GpdVendorHidDevice: _vendorHidListener.ConnectedDeviceLabel,
            GpdVendorHidError: _vendorHidListener.LastOpenError,
            XInputBackend: XInputInterop.ActiveBackendName,
            ConnectedXInputCount: ConnectedControllerCount,
            GpdToolRunning: gpdToolRunning,
            GpdToolElevated: gpdToolElevated,
            GpdToolServiceRunning: gpdSvcRunning,
            FirmwareX: gpdConfig?.FirmwareX ?? string.Empty,
            FirmwareK: gpdConfig?.FirmwareK ?? string.Empty,
            InterceptPhysicalMenuButton: _settings.InterceptPhysicalMenuButton,
            InterceptGuideMenuButton: _settings.InterceptGuideMenuButton);

        return session.BuildReport(env, clampedMs);
    }

    public void Dispose()
    {
        _activeDiagSession = null;
        ReleaseHeldMouseButtons();
        _cts.Cancel();
        _vendorHidListener.RawReportReceived -= OnVendorHidRawReport;
        _vendorHidListener.MenuButtonPressed -= OnVendorHidMenuButtonPressed;
        _vendorHidListener.Dispose();
        RemoveKeyboardHook();
        UnregisterGlobalHotkeys();
        _hwndSource?.RemoveHook(WndProc);
        _cts.Dispose();
    }
}
