namespace HaloDeck.Core.Services;

/// <summary>
/// Context-free <see cref="IProgress{T}"/> wrapper that throttles high-frequency download/extract
/// progress updates without capturing WPF <see cref="SynchronizationContext"/>.
/// Guarantees initial (0%) and terminal (100%) delivery while coalescing intermediate ticks by
/// percentage delta or elapsed time.
/// </summary>
public sealed class ThrottledProgress : IProgress<double>
{
    private readonly Action<double> _onReport;
    private readonly double _minDeltaPercent;
    private readonly long _minIntervalMs;
    private readonly Func<long> _clockMs;
    private readonly object _gate = new();

    private bool _hasReported;
    private double _lastReportedPercent;
    private long _lastReportedMs;

    public ThrottledProgress(
        Action<double> onReport,
        double minDeltaPercent = 1.0,
        long minIntervalMs = 120,
        Func<long>? clockMs = null)
    {
        _onReport = onReport ?? throw new ArgumentNullException(nameof(onReport));
        _minDeltaPercent = minDeltaPercent > 0 ? minDeltaPercent : 1.0;
        _minIntervalMs = minIntervalMs >= 0 ? minIntervalMs : 120;
        _clockMs = clockMs ?? (() => Environment.TickCount64);
    }

    public void Report(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return;
        }

        double clamped = Math.Clamp(value, 0.0, 100.0);
        bool shouldEmit = false;

        lock (_gate)
        {
            long nowMs = _clockMs();
            bool isBoundary = clamped <= 0.0 || clamped >= 100.0;

            if (!_hasReported)
            {
                shouldEmit = true;
            }
            else if (isBoundary && Math.Abs(clamped - _lastReportedPercent) > 0.0001)
            {
                shouldEmit = true;
            }
            else if (Math.Abs(clamped - _lastReportedPercent) >= _minDeltaPercent ||
                     (nowMs - _lastReportedMs) >= _minIntervalMs)
            {
                shouldEmit = true;
            }

            if (shouldEmit)
            {
                _hasReported = true;
                _lastReportedPercent = clamped;
                _lastReportedMs = nowMs;
            }
        }

        if (shouldEmit)
        {
            _onReport(clamped);
        }
    }
}
