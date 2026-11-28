namespace HaloDeck.Core.Services;

public enum LeakedOskResolution
{
    Ignore = 0,
    DismissLegacyOskOnly = 1,
    DismissLegacyOskAndTriggerTabTip = 2
}

/// <summary>
/// Pure-logic, thread-safe coordinator for:
/// 1) Detecting hardware RawInput Ctrl+Win bursts (e.g. GPD Win 5 OSK key sending 0x11 -> 0x5B within 150ms),
/// 2) Resolving leaked legacy osk.exe processes without double-toggling TabTip when WH_KEYBOARD_LL already ran,
/// 3) Rate-limiting WH_KEYBOARD_LL dropout watchdog reinstalls while ignoring uiAccess keyboard foregrounds.
/// </summary>
public sealed class OskRawFallbackGuard
{
    public const ushort VkControl = 0x11;
    public const ushort VkLControl = 0xA2;
    public const ushort VkRControl = 0xA3;
    public const ushort VkLWin = 0x5B;
    public const ushort VkRWin = 0x5C;

    public const long DefaultChordWindowMs = 150;
    public const long DefaultHookRecentTriggerWindowMs = 600;
    public const long DefaultFallbackCooldownMs = 650;
    public const long DefaultHookStaleThresholdMs = 400;
    public const long DefaultWatchdogReinstallCooldownMs = 2500;

    private readonly object _sync = new();
    private readonly long _chordWindowMs;
    private readonly long _hookRecentTriggerWindowMs;
    private readonly long _fallbackCooldownMs;
    private readonly long _hookStaleThresholdMs;
    private readonly long _watchdogReinstallCooldownMs;

    private bool _hasCtrlDown;
    private long _lastCtrlDownMs;
    private bool _hasWinDown;
    private long _lastWinDownMs;

    private bool _hasFallbackTriggered;
    private long _lastFallbackTriggerMs;

    private bool _hasWatchdogReinstalled;
    private long _lastWatchdogReinstallMs;

    public OskRawFallbackGuard(
        long chordWindowMs = DefaultChordWindowMs,
        long hookRecentTriggerWindowMs = DefaultHookRecentTriggerWindowMs,
        long fallbackCooldownMs = DefaultFallbackCooldownMs,
        long hookStaleThresholdMs = DefaultHookStaleThresholdMs,
        long watchdogReinstallCooldownMs = DefaultWatchdogReinstallCooldownMs)
    {
        _chordWindowMs = chordWindowMs > 0 ? chordWindowMs : DefaultChordWindowMs;
        _hookRecentTriggerWindowMs = hookRecentTriggerWindowMs > 0 ? hookRecentTriggerWindowMs : DefaultHookRecentTriggerWindowMs;
        _fallbackCooldownMs = fallbackCooldownMs > 0 ? fallbackCooldownMs : DefaultFallbackCooldownMs;
        _hookStaleThresholdMs = hookStaleThresholdMs > 0 ? hookStaleThresholdMs : DefaultHookStaleThresholdMs;
        _watchdogReinstallCooldownMs = watchdogReinstallCooldownMs > 0 ? watchdogReinstallCooldownMs : DefaultWatchdogReinstallCooldownMs;
    }

    /// <summary>
    /// Feeds a RawInput keyboard key-down event (VKey) and returns true when Ctrl (0x11/0xA2/0xA3)
    /// and Win (0x5B/0x5C) arrive within <see cref="DefaultChordWindowMs"/> of each other.
    /// </summary>
    public bool ProcessRawKeyDown(ushort vKey, long nowMs)
    {
        lock (_sync)
        {
            if (vKey is VkControl or VkLControl or VkRControl)
            {
                _hasCtrlDown = true;
                _lastCtrlDownMs = nowMs;
                if (_hasWinDown)
                {
                    long delta = nowMs - _lastWinDownMs;
                    if (delta >= 0 && delta <= _chordWindowMs)
                    {
                        _hasCtrlDown = false;
                        _hasWinDown = false;
                        return true;
                    }
                }
                return false;
            }

            if (vKey is VkLWin or VkRWin)
            {
                _hasWinDown = true;
                _lastWinDownMs = nowMs;
                if (_hasCtrlDown)
                {
                    long delta = nowMs - _lastCtrlDownMs;
                    if (delta >= 0 && delta <= _chordWindowMs)
                    {
                        _hasCtrlDown = false;
                        _hasWinDown = false;
                        return true;
                    }
                }
                return false;
            }

            return false;
        }
    }

    /// <summary>
    /// Decides how to resolve a leaked legacy osk.exe detected after a RawInput Ctrl+Win chord.
    /// If WH_KEYBOARD_LL already triggered TabTip within the recent window, only kills osk.exe
    /// so TabTip is never double-toggled out of phase.
    /// </summary>
    public LeakedOskResolution EvaluateLeakedOskAction(long nowMs, long lastHookTriggerTickMs)
    {
        lock (_sync)
        {
            bool hookRecentlyTriggered =
                lastHookTriggerTickMs > 0 &&
                nowMs - lastHookTriggerTickMs >= 0 &&
                nowMs - lastHookTriggerTickMs < _hookRecentTriggerWindowMs;

            if (hookRecentlyTriggered)
            {
                return LeakedOskResolution.DismissLegacyOskOnly;
            }

            if (_hasFallbackTriggered)
            {
                long sinceFallback = nowMs - _lastFallbackTriggerMs;
                if (sinceFallback >= 0 && sinceFallback < _fallbackCooldownMs)
                {
                    return LeakedOskResolution.DismissLegacyOskOnly;
                }
            }

            _hasFallbackTriggered = true;
            _lastFallbackTriggerMs = nowMs;
            return LeakedOskResolution.DismissLegacyOskAndTriggerTabTip;
        }
    }

    /// <summary>
    /// Fast check on RawInput hardware key-down to decide whether to schedule an async 80ms
    /// hook-liveness recheck.
    /// </summary>
    public bool ShouldScheduleWatchdogCheck(long nowMs, long lastHookHitTickMs)
    {
        lock (_sync)
        {
            bool hookStale = lastHookHitTickMs <= 0 || (nowMs - lastHookHitTickMs) > _hookStaleThresholdMs;
            if (!hookStale)
            {
                return false;
            }

            if (_hasWatchdogReinstalled)
            {
                long sinceReinstall = nowMs - _lastWatchdogReinstallMs;
                if (sinceReinstall >= 0 && sinceReinstall < _watchdogReinstallCooldownMs)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Final recheck after the 80ms grace delay: claims a rate-limited hook reinstall iff the hook
    /// still hasn't received any callback and the foreground window is not a uiAccess OSK/TabTip surface.
    /// </summary>
    public bool TryClaimWatchdogReinstall(long nowMs, long lastHookHitTickMs, bool foregroundIsUiAccessKeyboard)
    {
        lock (_sync)
        {
            if (foregroundIsUiAccessKeyboard)
            {
                return false;
            }

            bool hookStillStale = lastHookHitTickMs <= 0 || (nowMs - lastHookHitTickMs) > _hookStaleThresholdMs;
            if (!hookStillStale)
            {
                return false;
            }

            if (_hasWatchdogReinstalled)
            {
                long sinceReinstall = nowMs - _lastWatchdogReinstallMs;
                if (sinceReinstall >= 0 && sinceReinstall < _watchdogReinstallCooldownMs)
                {
                    return false;
                }
            }

            _hasWatchdogReinstalled = true;
            _lastWatchdogReinstallMs = nowMs;
            return true;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _hasCtrlDown = false;
            _lastCtrlDownMs = 0;
            _hasWinDown = false;
            _lastWinDownMs = 0;
            _hasFallbackTriggered = false;
            _lastFallbackTriggerMs = 0;
            _hasWatchdogReinstalled = false;
            _lastWatchdogReinstallMs = 0;
        }
    }
}
