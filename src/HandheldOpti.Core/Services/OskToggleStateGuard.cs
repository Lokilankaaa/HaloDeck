using System.Threading;

namespace HandheldOpti.Core.Services;

/// <summary>
/// Thread-safe state guard for Windows 11 native TabTip / Gamepad keyboard visibility.
/// Prevents toggle-bounce (immediate re-open) when physical Ctrl+Win+O keydown or UI pointer-down
/// auto-dismisses TextInputHost shortly before the toggle handler runs.
/// </summary>
public sealed class OskToggleStateGuard
{
    private int _cachedVisible;
    private int _closingInProgress;
    private long _lastObservedVisibleTicks;
    private long _lastIntentionalCloseTicks;
    private long _suppressVisibleRefreshUntilTicks;

    public bool CachedVisible => Volatile.Read(ref _cachedVisible) != 0;

    public void RecordSample(bool isVisible, long nowTicks)
    {
        if (!isVisible)
        {
            Volatile.Write(ref _cachedVisible, 0);
            return;
        }

        if (Volatile.Read(ref _closingInProgress) != 0 ||
            nowTicks < Interlocked.Read(ref _suppressVisibleRefreshUntilTicks))
        {
            return;
        }

        Volatile.Write(ref _cachedVisible, 1);
        Interlocked.Exchange(ref _lastObservedVisibleTicks, nowTicks);
    }

    public bool ShouldCloseFromSnapshot(long nowTicks, int autoDismissGraceMs = 550)
    {
        if (Volatile.Read(ref _closingInProgress) != 0 || CachedVisible)
        {
            return true;
        }

        long lastVis = Interlocked.Read(ref _lastObservedVisibleTicks);
        long lastClose = Interlocked.Read(ref _lastIntentionalCloseTicks);
        if (lastVis > 0 && lastVis > lastClose)
        {
            long elapsed = nowTicks - lastVis;
            if (elapsed >= 0 && elapsed < autoDismissGraceMs)
            {
                return true;
            }
        }

        return false;
    }

    public bool ShouldCloseWithLiveQuery(bool liveVisible, long nowTicks, int autoDismissGraceMs = 750)
    {
        RecordSample(liveVisible, nowTicks);
        if (liveVisible)
        {
            return true;
        }

        if (Volatile.Read(ref _closingInProgress) != 0)
        {
            return true;
        }

        long lastVis = Interlocked.Read(ref _lastObservedVisibleTicks);
        long lastClose = Interlocked.Read(ref _lastIntentionalCloseTicks);
        if (lastVis > 0 && lastVis > lastClose)
        {
            long elapsed = nowTicks - lastVis;
            if (elapsed >= 0 && elapsed < autoDismissGraceMs)
            {
                return true;
            }
        }

        return false;
    }

    public void BeginIntentionalClose(long nowTicks)
    {
        Volatile.Write(ref _closingInProgress, 1);
        Interlocked.Exchange(ref _lastIntentionalCloseTicks, nowTicks);
        Interlocked.Exchange(ref _lastObservedVisibleTicks, 0);
        Volatile.Write(ref _cachedVisible, 0);
    }

    public void CompleteIntentionalClose(long nowTicks, int postCloseSuppressMs = 350)
    {
        Interlocked.Exchange(ref _lastIntentionalCloseTicks, nowTicks);
        Interlocked.Exchange(ref _suppressVisibleRefreshUntilTicks, nowTicks + Math.Max(0, postCloseSuppressMs));
        Interlocked.Exchange(ref _lastObservedVisibleTicks, 0);
        Volatile.Write(ref _cachedVisible, 0);
        Volatile.Write(ref _closingInProgress, 0);
    }

    public void BeginIntentionalOpen()
    {
        Interlocked.Exchange(ref _suppressVisibleRefreshUntilTicks, 0);
        Volatile.Write(ref _closingInProgress, 0);
    }
}
