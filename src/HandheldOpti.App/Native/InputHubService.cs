using System.Windows.Interop;
using HandheldOpti.Core.Models;

namespace HandheldOpti.App.Native;

public sealed class InputHubService : IDisposable
{
    public const int HotkeyQamId = 0x5101;
    public const int HotkeyInsertId = 0x5102;
    public const int HotkeyConsoleId = 0x5103;
    public const int HotkeyOsdId = 0x5104;
    public const int HotkeyPaddleQamF13Id = 0x5111;
    public const int HotkeyPaddleOsdF14Id = 0x5112;
    public const int HotkeyPaddleInsF15Id = 0x5113;

    private const short StickThreshold = 18000;
    private const short MouseStickDeadzone = 7849;
    private const int InitialRepeatMs = 260;
    private const int RepeatIntervalMs = 95;

    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, DateTime> _directionRepeatAt = new();

    private AppSettings _settings;
    private nint _hotkeyWindowHwnd;
    private HwndSource? _hwndSource;
    private XInputButtons _prevButtons;
    private bool _qamComboArmed = true;
    private bool _insertComboArmed = true;
    private bool _osdComboArmed = true;
    private bool _mouseComboArmed = true;
    private volatile bool _suppressNavigationUntilRelease;
    private bool _recordingGamepadCombo;
    private XInputButtons _recordedMask;
    private volatile string? _activeUiTarget;
    private volatile bool _gamepadMouseModeEnabled;
    private bool _mouseLeftHeld;
    private bool _mouseRightHeld;
    private double _mouseCarryX;
    private double _mouseCarryY;
    private double _wheelCarry;

    public event Action? ToggleQamRequested;
    public event Action? ToggleConsoleRequested;
    public event Action? SendInsertToGameRequested;
    public event Action? CycleOsdRequested;
    public event Action? ToggleMouseModeRequested;
    public event Action<string>? UiGamepadAction;
    public event Action<int>? GamepadCountChanged;
    public event Action<List<string>>? GamepadComboRecorded;

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
            _activeUiTarget = value;
            if (!string.IsNullOrEmpty(value))
            {
                ReleaseHeldMouseButtons();
            }
        }
    }
    public bool IsUiActive => !string.IsNullOrEmpty(_activeUiTarget);

    public InputHubService(AppSettings settings)
    {
        _settings = settings;
        _gamepadMouseModeEnabled = settings.GamepadMouseModeEnabled;
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        GamepadMouseModeEnabled = settings.GamepadMouseModeEnabled;
        RegisterGlobalHotkeys();
    }

    public void AttachHotkeyWindow(nint hwnd)
    {
        _hotkeyWindowHwnd = hwnd;
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);
        RegisterGlobalHotkeys();
    }

    public void StartPolling()
    {
        _ = Task.Run(() => PollLoopAsync(_cts.Token), _cts.Token);
    }

    public void BeginRecordGamepadCombo()
    {
        _recordedMask = XInputButtons.None;
        _recordingGamepadCombo = true;
    }

    public void CancelRecordGamepadCombo()
    {
        _recordingGamepadCombo = false;
        _recordedMask = XInputButtons.None;
    }

    public void RegisterGlobalHotkeys()
    {
        if (_hotkeyWindowHwnd == 0) return;

        UnregisterGlobalHotkeys();

        TryRegisterHotkey(HotkeyQamId, _settings.QamHotkey);
        TryRegisterHotkey(HotkeyInsertId, _settings.InsertHotkey);
        TryRegisterHotkey(HotkeyConsoleId, _settings.ConsoleHotkey);
        TryRegisterHotkey(HotkeyOsdId, _settings.OsdHotkey);

        // Bare paddle bindings for GPD Win 5 L4/R4 (VK_F13=0x7C, VK_F14=0x7D, VK_F15=0x7E)
        Win32Interop.RegisterHotKey(_hotkeyWindowHwnd, HotkeyPaddleQamF13Id, Win32Interop.MOD_NOREPEAT, 0x7C);
        Win32Interop.RegisterHotKey(_hotkeyWindowHwnd, HotkeyPaddleOsdF14Id, Win32Interop.MOD_NOREPEAT, 0x7D);
        Win32Interop.RegisterHotKey(_hotkeyWindowHwnd, HotkeyPaddleInsF15Id, Win32Interop.MOD_NOREPEAT, 0x7E);
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
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == Win32Interop.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id is HotkeyQamId or HotkeyPaddleQamF13Id)
            {
                ToggleQamRequested?.Invoke();
                handled = true;
            }
            else if (id is HotkeyInsertId or HotkeyPaddleInsF15Id)
            {
                SendInsertToGameRequested?.Invoke();
                handled = true;
            }
            else if (id == HotkeyConsoleId)
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

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (XInputInterop.TryGetCombinedGamepadState(out var gamepad, out int count))
                {
                    if (count != ConnectedControllerCount)
                    {
                        ConnectedControllerCount = count;
                        GamepadCountChanged?.Invoke(count);
                    }

                    var buttons = (XInputButtons)gamepad.wButtons;

                    if (_recordingGamepadCombo)
                    {
                        if (buttons != XInputButtons.None)
                        {
                            _recordedMask |= buttons;
                        }
                        else if (_recordedMask != XInputButtons.None)
                        {
                            var captured = XInputInterop.ToButtonNames(_recordedMask);
                            _recordingGamepadCombo = false;
                            _recordedMask = XInputButtons.None;
                            GamepadComboRecorded?.Invoke(captured);
                        }

                        _prevButtons = buttons;
                        await Task.Delay(12, ct).ConfigureAwait(false);
                        continue;
                    }

                    // 1. Check global QAM toggle combo (works inside any game!)
                    var qamMask = XInputInterop.CombineMask(_settings.QamGamepadCombo.Buttons);
                    if (qamMask != XInputButtons.None)
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
                        else
                        {
                            _qamComboArmed = true;
                        }
                    }

                    // 2. Check global OptiScaler Insert combo (works inside any game!)
                    var insertMask = XInputInterop.CombineMask(_settings.InsertGamepadCombo.Buttons);
                    if (insertMask != XInputButtons.None)
                    {
                        if ((buttons & insertMask) == insertMask)
                        {
                            if (_insertComboArmed)
                            {
                                _insertComboArmed = false;
                                _suppressNavigationUntilRelease = true;
                                _directionRepeatAt.Clear();
                                SendInsertToGameRequested?.Invoke();
                            }
                        }
                        else
                        {
                            _insertComboArmed = true;
                        }
                    }

                    // 2.5 Check global OSD cycle combo & Gamepad Mouse Mode combo
                    var osdMask = XInputInterop.CombineMask(_settings.OsdGamepadCombo.Buttons);
                    if (osdMask != XInputButtons.None)
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
                        else
                        {
                            _osdComboArmed = true;
                        }
                    }

                    var mouseMask = XInputInterop.CombineMask(_settings.MouseModeGamepadCombo.Buttons);
                    if (mouseMask != XInputButtons.None)
                    {
                        if ((buttons & mouseMask) == mouseMask)
                        {
                            if (_mouseComboArmed)
                            {
                                _mouseComboArmed = false;
                                _suppressNavigationUntilRelease = true;
                                _directionRepeatAt.Clear();
                                ToggleMouseModeRequested?.Invoke();
                            }
                        }
                        else
                        {
                            _mouseComboArmed = true;
                        }
                    }

                    if (buttons == XInputButtons.None)
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

    public void Dispose()
    {
        ReleaseHeldMouseButtons();
        _cts.Cancel();
        UnregisterGlobalHotkeys();
        _hwndSource?.RemoveHook(WndProc);
        _cts.Dispose();
    }
}
