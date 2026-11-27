namespace HaloDeck.Core.Services;

public enum PhysicalOskChordAction
{
    PassThrough = 0,
    SwallowKeyDownAndTrigger = 1,
    SwallowKeyDownRepeat = 2,
    SwallowKeyUp = 3
}

/// <summary>
/// Pure-logic, lock-free state tracker for intercepting physical Ctrl+Win+O on-screen keyboard chords
/// (such as GPD Win 5's dedicated OSK key) inside WH_KEYBOARD_LL.
/// Handles single-packet HID [LCtrl, LWin, O] bursts where GetAsyncKeyState has not yet updated,
/// driver/mapper-injected chords, and stale modifier recovery if a keyup packet is dropped.
/// </summary>
public sealed class PhysicalOskChordTracker
{
    public const uint VkControl = 0x11;
    public const uint VkLControl = 0xA2;
    public const uint VkRControl = 0xA3;
    public const uint VkLWin = 0x5B;
    public const uint VkRWin = 0x5C;
    public const uint VkO = 0x4F;
    public const ulong SyntheticChordBreakExtraInfo = 0x48444B42UL; // "HDKB"
    public const long DefaultStaleModifierTimeoutMs = 4000;

    private readonly long _staleModifierTimeoutMs;

    private bool _ctrlDown;
    private long _ctrlDownAtMs;
    private bool _lCtrlDown;
    private long _lCtrlDownAtMs;
    private bool _rCtrlDown;
    private long _rCtrlDownAtMs;

    private bool _lWinDown;
    private long _lWinDownAtMs;
    private bool _rWinDown;
    private long _rWinDownAtMs;

    private bool _swallowedODown;
    private long _swallowedODownAtMs;

    public PhysicalOskChordTracker(long staleModifierTimeoutMs = DefaultStaleModifierTimeoutMs)
    {
        _staleModifierTimeoutMs = staleModifierTimeoutMs > 0
            ? staleModifierTimeoutMs
            : DefaultStaleModifierTimeoutMs;
    }

    public bool IsSwallowedODown => _swallowedODown;

    public bool IsCtrlWinActive(long nowMs)
    {
        PruneStale(nowMs);
        bool ctrl = _ctrlDown || _lCtrlDown || _rCtrlDown;
        bool win = _lWinDown || _rWinDown;
        return ctrl && win;
    }

    public void Reset()
    {
        _ctrlDown = false;
        _ctrlDownAtMs = 0;
        _lCtrlDown = false;
        _lCtrlDownAtMs = 0;
        _rCtrlDown = false;
        _rCtrlDownAtMs = 0;
        _lWinDown = false;
        _lWinDownAtMs = 0;
        _rWinDown = false;
        _rWinDownAtMs = 0;
        _swallowedODown = false;
        _swallowedODownAtMs = 0;
    }

    public PhysicalOskChordAction ProcessKeyEvent(
        uint vkCode,
        bool isKeyDown,
        bool isKeyUp,
        ulong extraInfo,
        bool isInjected,
        bool interceptEnabled,
        bool asyncCtrlDown,
        bool asyncWinDown,
        long nowMs)
    {
        // Always ignore our own synthetic chord-break / standalone Ctrl tap events so they never pollute state.
        if (extraInfo == SyntheticChordBreakExtraInfo)
        {
            return PhysicalOskChordAction.PassThrough;
        }

        // Track Ctrl / Win modifier states BEFORE any isInjected filtering so both single-packet HID bursts
        // and driver/mapper-injected Ctrl+Win+O chords are reliably observed.
        _ = isInjected;
        switch (vkCode)
        {
            case VkLControl:
                if (isKeyDown)
                {
                    _lCtrlDown = true;
                    _lCtrlDownAtMs = nowMs;
                }
                else if (isKeyUp)
                {
                    _lCtrlDown = false;
                    _ctrlDown = false;
                }
                return PhysicalOskChordAction.PassThrough;

            case VkRControl:
                if (isKeyDown)
                {
                    _rCtrlDown = true;
                    _rCtrlDownAtMs = nowMs;
                }
                else if (isKeyUp)
                {
                    _rCtrlDown = false;
                    _ctrlDown = false;
                }
                return PhysicalOskChordAction.PassThrough;

            case VkControl:
                if (isKeyDown)
                {
                    _ctrlDown = true;
                    _ctrlDownAtMs = nowMs;
                }
                else if (isKeyUp)
                {
                    _ctrlDown = false;
                    _lCtrlDown = false;
                    _rCtrlDown = false;
                }
                return PhysicalOskChordAction.PassThrough;

            case VkLWin:
                if (isKeyDown)
                {
                    _lWinDown = true;
                    _lWinDownAtMs = nowMs;
                }
                else if (isKeyUp)
                {
                    _lWinDown = false;
                }
                return PhysicalOskChordAction.PassThrough;

            case VkRWin:
                if (isKeyDown)
                {
                    _rWinDown = true;
                    _rWinDownAtMs = nowMs;
                }
                else if (isKeyUp)
                {
                    _rWinDown = false;
                }
                return PhysicalOskChordAction.PassThrough;

            case VkO:
                PruneStale(nowMs);
                if (isKeyUp)
                {
                    if (_swallowedODown)
                    {
                        _swallowedODown = false;
                        _swallowedODownAtMs = 0;
                        return PhysicalOskChordAction.SwallowKeyUp;
                    }
                    return PhysicalOskChordAction.PassThrough;
                }

                if (!interceptEnabled || !isKeyDown)
                {
                    return PhysicalOskChordAction.PassThrough;
                }

                bool ctrlWinActive = IsCtrlWinActive(nowMs) || (asyncCtrlDown && asyncWinDown);
                if (!ctrlWinActive)
                {
                    return PhysicalOskChordAction.PassThrough;
                }

                if (_swallowedODown)
                {
                    _swallowedODownAtMs = nowMs;
                    return PhysicalOskChordAction.SwallowKeyDownRepeat;
                }

                _swallowedODown = true;
                _swallowedODownAtMs = nowMs;
                return PhysicalOskChordAction.SwallowKeyDownAndTrigger;

            default:
                return PhysicalOskChordAction.PassThrough;
        }
    }

    private void PruneStale(long nowMs)
    {
        if (_ctrlDown && IsExpired(_ctrlDownAtMs, nowMs)) _ctrlDown = false;
        if (_lCtrlDown && IsExpired(_lCtrlDownAtMs, nowMs)) _lCtrlDown = false;
        if (_rCtrlDown && IsExpired(_rCtrlDownAtMs, nowMs)) _rCtrlDown = false;
        if (_lWinDown && IsExpired(_lWinDownAtMs, nowMs)) _lWinDown = false;
        if (_rWinDown && IsExpired(_rWinDownAtMs, nowMs)) _rWinDown = false;
        if (_swallowedODown && IsExpired(_swallowedODownAtMs, nowMs)) _swallowedODown = false;
    }

    private bool IsExpired(long pressedAtMs, long nowMs)
    {
        long elapsed = nowMs - pressedAtMs;
        return elapsed < 0 || elapsed > _staleModifierTimeoutMs;
    }
}
