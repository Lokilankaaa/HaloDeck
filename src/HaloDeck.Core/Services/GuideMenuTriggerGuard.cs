namespace HaloDeck.Core.Services;

/// <summary>
/// Pure state machine coordinating GPD Win 5 physical quick-menu summons across dual channels:
/// 1) XInputGetStateEx hidden Guide/Menu button (0x0400) solo short-press (< 450 ms, no chord buttons),
/// 2) Keyboard VK_F13 (0x7C) from either RegisterHotKey or WH_KEYBOARD_LL fallback.
/// Enforces a shared debounce window so firmware emitting both Guide + F13 never double-toggles QAM.
/// </summary>
public sealed class GuideMenuTriggerGuard
{
    private readonly object _sync = new();
    private readonly long _maxShortPressMs;
    private readonly long _debounceMs;

    private bool _guideHeld;
    private long _guideDownTickMs;
    private bool _chordTainted;
    private long _lastTriggerTickMs = -10_000;

    public GuideMenuTriggerGuard(long maxShortPressMs = 450, long debounceMs = 350)
    {
        _maxShortPressMs = Math.Clamp(maxShortPressMs, 150, 1200);
        _debounceMs = Math.Clamp(debounceMs, 100, 1500);
    }

    public bool UpdateGuideState(bool guideDown, bool anyOtherButtonOrTriggerActive, long nowTickMs)
    {
        lock (_sync)
        {
            if (guideDown)
            {
                if (!_guideHeld)
                {
                    _guideHeld = true;
                    _guideDownTickMs = nowTickMs;
                    _chordTainted = anyOtherButtonOrTriggerActive;
                }
                else if (anyOtherButtonOrTriggerActive)
                {
                    _chordTainted = true;
                }
                return false;
            }

            if (!_guideHeld)
            {
                return false;
            }

            _guideHeld = false;
            long heldMs = Math.Max(0, nowTickMs - _guideDownTickMs);
            bool tainted = _chordTainted;
            _chordTainted = false;

            if (tainted || heldMs > _maxShortPressMs)
            {
                return false;
            }

            return TryClaimLocked(nowTickMs);
        }
    }

    public bool TryClaimQamMenuToggle(long nowTickMs)
    {
        lock (_sync)
        {
            return TryClaimLocked(nowTickMs);
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _guideHeld = false;
            _chordTainted = false;
        }
    }

    private bool TryClaimLocked(long nowTickMs)
    {
        if (nowTickMs - _lastTriggerTickMs < _debounceMs)
        {
            return false;
        }
        _lastTriggerTickMs = nowTickMs;
        return true;
    }
}
