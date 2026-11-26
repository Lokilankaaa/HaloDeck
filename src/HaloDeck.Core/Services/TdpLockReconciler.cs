using HaloDeck.Core.Models;

namespace HaloDeck.Core.Services;

public readonly record struct TdpPowerTarget(int StapmWatts, int SlowWatts, int FastWatts);

public enum TdpReapplyReason
{
    None = 0,
    InitialSync = 1,
    PowerResumeBurst = 2,
    HardwareDrift = 3
}

public readonly record struct TdpReconcileDecision(
    bool ShouldApply,
    TdpReapplyReason Reason,
    string Diagnostic);

/// <summary>
/// Pure-C# TDP target computation &amp; wake/drift detection helpers shared between
/// the Session-0 hardware daemon and the user-mode bridge.
/// </summary>
public static class TdpLockPolicy
{
    public const int MinStapmWatts = 8;
    public const int MaxStapmWatts = 120;
    public const int MaxSlowPptWatts = 130;
    public const int MaxFastPptWatts = 140;

    public static readonly TimeSpan DefaultSleepGapThreshold = TimeSpan.FromSeconds(10);

    public static TdpPowerTarget ComputeStageLimits(
        int stapmWatts,
        bool flatPowerLock,
        bool unlockThreeStagePower,
        int slowPptWatts,
        int fastPptWatts)
    {
        int stapm = Math.Clamp(stapmWatts, MinStapmWatts, MaxStapmWatts);
        int slow = unlockThreeStagePower
            ? Math.Clamp(slowPptWatts, MinStapmWatts, MaxSlowPptWatts)
            : stapm;
        int fast = unlockThreeStagePower
            ? Math.Clamp(fastPptWatts, MinStapmWatts, MaxFastPptWatts)
            : (flatPowerLock ? stapm : Math.Min(MaxFastPptWatts, stapm + 2));

        return new TdpPowerTarget(stapm, slow, fast);
    }

    public static TdpPowerTarget ResolveEffectiveTarget(
        AppSettings settings,
        GameEntry? runningGame,
        bool isAcOnline)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (runningGame is { IsCurrentlyRunning: true } &&
            settings.PerGameProfiles.TryGetValue(runningGame.Id, out var prof) &&
            prof.Enabled &&
            prof.TdpWatts is >= MinStapmWatts and <= MaxStapmWatts)
        {
            return ComputeStageLimits(
                prof.TdpWatts,
                settings.FlatPowerLock,
                unlockThreeStagePower: false,
                prof.TdpWatts,
                prof.TdpWatts);
        }

        if (settings.PowerLinkageEnabled)
        {
            int linkedWatts = isAcOnline ? settings.AcTdpWatts : settings.BatteryTdpWatts;
            return ComputeStageLimits(
                linkedWatts,
                settings.FlatPowerLock,
                settings.UnlockThreeStagePower,
                settings.SlowPptWatts,
                settings.FastPptWatts);
        }

        return ComputeStageLimits(
            settings.CurrentTdpWatts,
            settings.FlatPowerLock,
            settings.UnlockThreeStagePower,
            settings.SlowPptWatts,
            settings.FastPptWatts);
    }

    public static bool IsSleepTimeGap(
        DateTime previousUtc,
        DateTime currentUtc,
        TimeSpan? threshold = null)
    {
        if (previousUtc == DateTime.MinValue)
        {
            return false;
        }

        return (currentUtc - previousUtc) >= (threshold ?? DefaultSleepGapThreshold);
    }

    public static bool IsDisplayWakeTransition(int previousDisplayState, int currentDisplayState)
        => previousDisplayState == 0 && currentDisplayState == 1;
}

/// <summary>
/// Thread-safe state machine coordinating initial TDP lock sync, multi-shot post-resume
/// re-apply bursts (to beat delayed BIOS/EC/S0ix resets), and SMU PM-table drift recovery
/// with adaptive BIOS-clamp suppression.
/// </summary>
public sealed class TdpLockReconciler
{
    public static readonly TimeSpan[] BurstOffsets =
    [
        TimeSpan.FromMilliseconds(1500),
        TimeSpan.FromMilliseconds(4000),
        TimeSpan.FromMilliseconds(8000)
    ];

    public static readonly TimeSpan PostApplyGracePeriod = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan DriftReapplyCooldown = TimeSpan.FromSeconds(12);
    public const double StapmDriftToleranceWatts = 2.0;
    public const double PptDriftToleranceWatts = 3.0;
    public const double ClampMatchToleranceWatts = 1.5;

    private readonly object _sync = new();
    private readonly Queue<DateTime> _pendingResumeBurstUtc = new();

    private bool _pendingInitialSync;
    private string _lastResumeSource = string.Empty;
    private TdpPowerTarget? _lastAppliedTarget;
    private DateTime _lastAppliedUtc = DateTime.MinValue;
    private DateTime _lastDriftReapplyUtc = DateTime.MinValue;

    private bool _awaitingPostDriftVerification;
    private double? _lastDriftObservedStapm;
    private double? _lastDriftObservedSlow;
    private double? _lastDriftObservedFast;

    private TdpPowerTarget? _suppressedClampTarget;
    private double? _suppressedClampStapm;
    private double? _suppressedClampSlow;
    private double? _suppressedClampFast;

    public void RequestInitialSync()
    {
        lock (_sync)
        {
            _pendingInitialSync = true;
            _awaitingPostDriftVerification = false;
            ClearClampSuppressionLocked();
        }
    }

    public void NotifyPowerResume(DateTime nowUtc, string source)
    {
        lock (_sync)
        {
            _pendingResumeBurstUtc.Clear();
            foreach (var offset in BurstOffsets)
            {
                _pendingResumeBurstUtc.Enqueue(nowUtc + offset);
            }

            _lastResumeSource = source ?? string.Empty;
            _awaitingPostDriftVerification = false;
            ClearClampSuppressionLocked();
        }
    }

    public void RecordApplied(
        DateTime nowUtc,
        TdpPowerTarget target,
        TdpReapplyReason reason = TdpReapplyReason.None,
        double? observedStapm = null,
        double? observedSlow = null,
        double? observedFast = null)
    {
        lock (_sync)
        {
            if (_lastAppliedTarget != target)
            {
                _awaitingPostDriftVerification = false;
                ClearClampSuppressionLocked();
            }

            _lastAppliedTarget = target;
            _lastAppliedUtc = nowUtc;
            _pendingInitialSync = false;

            if (reason == TdpReapplyReason.HardwareDrift)
            {
                _lastDriftReapplyUtc = nowUtc;
                _lastDriftObservedStapm = observedStapm;
                _lastDriftObservedSlow = observedSlow;
                _lastDriftObservedFast = observedFast;
                _awaitingPostDriftVerification = true;
            }
        }
    }

    public TdpReconcileDecision Evaluate(
        DateTime nowUtc,
        TdpPowerTarget target,
        double? hwStapmWatts,
        double? hwSlowWatts,
        double? hwFastWatts,
        bool forceSyncRequired = false)
    {
        lock (_sync)
        {
            if (_pendingInitialSync || forceSyncRequired)
            {
                return new TdpReconcileDecision(true, TdpReapplyReason.InitialSync, "initial_or_service_sync");
            }

            if (_pendingResumeBurstUtc.Count > 0)
            {
                if (_pendingResumeBurstUtc.Peek() <= nowUtc)
                {
                    while (_pendingResumeBurstUtc.Count > 0 && _pendingResumeBurstUtc.Peek() <= nowUtc)
                    {
                        _pendingResumeBurstUtc.Dequeue();
                    }

                    return new TdpReconcileDecision(
                        true,
                        TdpReapplyReason.PowerResumeBurst,
                        $"resume_burst:{_lastResumeSource}");
                }

                return new TdpReconcileDecision(false, TdpReapplyReason.None, string.Empty);
            }

            if (hwStapmWatts is null && hwSlowWatts is null && hwFastWatts is null)
            {
                return new TdpReconcileDecision(false, TdpReapplyReason.None, string.Empty);
            }

            if (_lastAppliedUtc != DateTime.MinValue &&
                (nowUtc - _lastAppliedUtc) < PostApplyGracePeriod)
            {
                return new TdpReconcileDecision(false, TdpReapplyReason.None, string.Empty);
            }

            bool stapmDrifted = hwStapmWatts.HasValue &&
                                Math.Abs(hwStapmWatts.Value - target.StapmWatts) >= StapmDriftToleranceWatts;
            bool slowDrifted = hwSlowWatts.HasValue &&
                               Math.Abs(hwSlowWatts.Value - target.SlowWatts) >= PptDriftToleranceWatts;
            bool fastDrifted = hwFastWatts.HasValue &&
                               Math.Abs(hwFastWatts.Value - target.FastWatts) >= PptDriftToleranceWatts;

            if (!stapmDrifted && !slowDrifted && !fastDrifted)
            {
                _awaitingPostDriftVerification = false;
                ClearClampSuppressionLocked();
                return new TdpReconcileDecision(false, TdpReapplyReason.None, string.Empty);
            }

            if (_awaitingPostDriftVerification)
            {
                _awaitingPostDriftVerification = false;
                if (AllDriftedChannelsMatch(
                        stapmDrifted, slowDrifted, fastDrifted,
                        hwStapmWatts, hwSlowWatts, hwFastWatts,
                        _lastDriftObservedStapm, _lastDriftObservedSlow, _lastDriftObservedFast))
                {
                    _suppressedClampTarget = target;
                    _suppressedClampStapm = hwStapmWatts;
                    _suppressedClampSlow = hwSlowWatts;
                    _suppressedClampFast = hwFastWatts;
                    return new TdpReconcileDecision(false, TdpReapplyReason.None, "bios_clamped_suppressed");
                }
            }

            if (_suppressedClampTarget == target)
            {
                if (AllDriftedChannelsMatch(
                        stapmDrifted, slowDrifted, fastDrifted,
                        hwStapmWatts, hwSlowWatts, hwFastWatts,
                        _suppressedClampStapm, _suppressedClampSlow, _suppressedClampFast))
                {
                    return new TdpReconcileDecision(false, TdpReapplyReason.None, "bios_clamped_suppressed");
                }

                ClearClampSuppressionLocked();
            }

            if (_lastDriftReapplyUtc != DateTime.MinValue &&
                (nowUtc - _lastDriftReapplyUtc) < DriftReapplyCooldown)
            {
                return new TdpReconcileDecision(false, TdpReapplyReason.None, "drift_cooldown");
            }

            return new TdpReconcileDecision(
                true,
                TdpReapplyReason.HardwareDrift,
                $"drift:spl={hwStapmWatts?.ToString("0.0") ?? "na"}/{target.StapmWatts},slow={hwSlowWatts?.ToString("0.0") ?? "na"}/{target.SlowWatts},fast={hwFastWatts?.ToString("0.0") ?? "na"}/{target.FastWatts}");
        }
    }

    private void ClearClampSuppressionLocked()
    {
        _suppressedClampTarget = null;
        _suppressedClampStapm = null;
        _suppressedClampSlow = null;
        _suppressedClampFast = null;
    }

    private static bool ChannelMatches(double? a, double? b)
        => a.HasValue && b.HasValue && Math.Abs(a.Value - b.Value) <= ClampMatchToleranceWatts;

    private static bool AllDriftedChannelsMatch(
        bool stapmDrifted,
        bool slowDrifted,
        bool fastDrifted,
        double? obsStapm,
        double? obsSlow,
        double? obsFast,
        double? refStapm,
        double? refSlow,
        double? refFast)
    {
        return (!stapmDrifted || ChannelMatches(obsStapm, refStapm)) &&
               (!slowDrifted || ChannelMatches(obsSlow, refSlow)) &&
               (!fastDrifted || ChannelMatches(obsFast, refFast));
    }
}
