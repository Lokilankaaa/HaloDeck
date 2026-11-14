using System.Windows.Interop;
using HandheldOpti.Core.Models;

namespace HandheldOpti.App.Native;

public sealed class InputHubService : IDisposable
{
    public const int HotkeyQamId = 0x5101;
    public const int HotkeyInsertId = 0x5102;
    public const int HotkeyConsoleId = 0x5103;

    private const short StickThreshold = 18000;
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
    private volatile bool _suppressNavigationUntilRelease;
    private bool _recordingGamepadCombo;
    private XInputButtons _recordedMask;
    private volatile string? _activeUiTarget;

    public event Action? ToggleQamRequested;
    public event Action? ToggleConsoleRequested;
    public event Action? SendInsertToGameRequested;
    public event Action<string>? UiGamepadAction;
    public event Action<int>? GamepadCountChanged;
    public event Action<List<string>>? GamepadComboRecorded;

    public int ConnectedControllerCount { get; private set; }
    public string? ActiveUiTarget
    {
        get => _activeUiTarget;
        set => _activeUiTarget = value;
    }
    public bool IsUiActive => !string.IsNullOrEmpty(_activeUiTarget);

    public InputHubService(AppSettings settings)
    {
        _settings = settings;
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
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
    }

    private void TryRegisterHotkey(int id, HotkeyBinding binding)
    {
        if (binding.VirtualKey == 0) return;
        Win32Interop.RegisterHotKey(_hotkeyWindowHwnd, id, binding.Modifiers | Win32Interop.MOD_NOREPEAT, binding.VirtualKey);
    }

    private void UnregisterGlobalHotkeys()
    {
        if (_hotkeyWindowHwnd == 0) return;
        Win32Interop.UnregisterHotKey(_hotkeyWindowHwnd, HotkeyQamId);
        Win32Interop.UnregisterHotKey(_hotkeyWindowHwnd, HotkeyInsertId);
        Win32Interop.UnregisterHotKey(_hotkeyWindowHwnd, HotkeyConsoleId);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == Win32Interop.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HotkeyQamId)
            {
                ToggleQamRequested?.Invoke();
                handled = true;
            }
            else if (id == HotkeyInsertId)
            {
                SendInsertToGameRequested?.Invoke();
                handled = true;
            }
            else if (id == HotkeyConsoleId)
            {
                ToggleConsoleRequested?.Invoke();
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

                    if (buttons == XInputButtons.None)
                    {
                        _suppressNavigationUntilRelease = false;
                    }

                    // 3. When HandheldOpti UI (MainWindow or QAM) is active, translate buttons + sticks to UI navigation
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

                    _prevButtons = buttons;
                }
                else if (ConnectedControllerCount != 0)
                {
                    ConnectedControllerCount = 0;
                    _prevButtons = XInputButtons.None;
                    _suppressNavigationUntilRelease = false;
                    _directionRepeatAt.Clear();
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

    public void Dispose()
    {
        _cts.Cancel();
        UnregisterGlobalHotkeys();
        _hwndSource?.RemoveHook(WndProc);
        _cts.Dispose();
    }
}
