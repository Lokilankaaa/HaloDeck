using HaloDeck.Core.Services;
using Xunit;

namespace HaloDeck.Tests;

public sealed class ThrottledProgressTests
{
    [Fact]
    public void ThousandSequentialReports_ThrottledToAtMost105AndIncludesEndpoints()
    {
        long fakeClockMs = 10_000;
        var emitted = new List<double>();
        var progress = new ThrottledProgress(
            pct => emitted.Add(pct),
            minDeltaPercent: 1.0,
            minIntervalMs: 120,
            clockMs: () => fakeClockMs);

        for (int i = 0; i <= 1000; i++)
        {
            double pct = i / 10.0; // 0.0 -> 100.0 in 0.1% steps
            progress.Report(pct);
        }

        Assert.NotEmpty(emitted);
        Assert.Equal(0.0, emitted[0]);
        Assert.Equal(100.0, emitted[^1]);
        Assert.True(emitted.Count <= 105, $"Expected <= 105 throttled callbacks, got {emitted.Count}");
        Assert.True(emitted.Count >= 95, $"Expected reasonable progress granularity, got {emitted.Count}");
    }

    [Fact]
    public void SlowDownload_EmitsByTimeIntervalEvenWhenPercentDeltaIsSmall()
    {
        long fakeClockMs = 1_000;
        var emitted = new List<double>();
        var progress = new ThrottledProgress(
            pct => emitted.Add(pct),
            minDeltaPercent: 1.0,
            minIntervalMs: 120,
            clockMs: () => fakeClockMs);

        progress.Report(0.0);
        fakeClockMs += 50;
        progress.Report(0.2); // skipped (< 1% and < 120ms)
        fakeClockMs += 80;    // total +130ms >= 120ms
        progress.Report(0.4); // emitted by time interval
        fakeClockMs += 10;
        progress.Report(100.0); // always emits terminal 100%

        Assert.Equal(new[] { 0.0, 0.4, 100.0 }, emitted);
    }

    [Fact]
    public void DuplicateBoundaryAndInvalidValues_AreSuppressedAndClamped()
    {
        long fakeClockMs = 5_000;
        var emitted = new List<double>();
        var progress = new ThrottledProgress(
            pct => emitted.Add(pct),
            minDeltaPercent: 1.0,
            minIntervalMs: 120,
            clockMs: () => fakeClockMs);

        progress.Report(double.NaN);
        progress.Report(double.PositiveInfinity);
        progress.Report(-5.0);  // clamped to 0.0
        progress.Report(0.0);   // duplicate 0.0 within 120ms suppressed
        progress.Report(105.0); // clamped to 100.0
        progress.Report(100.0); // duplicate 100.0 within 120ms suppressed

        Assert.Equal(new[] { 0.0, 100.0 }, emitted);
    }
}
