using HaloDeck.Core.Models;
using HaloDeck.Core.Services;
using Xunit;

namespace HaloDeck.Tests;

public sealed class TdpResumeReconcilerTests
{
    [Fact]
    public void TdpLockPolicy_ComputeStageLimits_HandlesStandardFlatAndThreeStage()
    {
        var standard = TdpLockPolicy.ComputeStageLimits(
            stapmWatts: 35,
            flatPowerLock: false,
            unlockThreeStagePower: false,
            slowPptWatts: 50,
            fastPptWatts: 60);
        Assert.Equal(new TdpPowerTarget(35, 35, 37), standard);

        var flat = TdpLockPolicy.ComputeStageLimits(
            stapmWatts: 35,
            flatPowerLock: true,
            unlockThreeStagePower: false,
            slowPptWatts: 50,
            fastPptWatts: 60);
        Assert.Equal(new TdpPowerTarget(35, 35, 35), flat);

        var threeStage = TdpLockPolicy.ComputeStageLimits(
            stapmWatts: 35,
            flatPowerLock: true,
            unlockThreeStagePower: true,
            slowPptWatts: 45,
            fastPptWatts: 60);
        Assert.Equal(new TdpPowerTarget(35, 45, 60), threeStage);
    }

    [Fact]
    public void TdpLockPolicy_ResolveEffectiveTarget_PrioritizesPerGameThenPowerLinkageThenGlobal()
    {
        var settings = new AppSettings
        {
            CurrentTdpWatts = 40,
            FlatPowerLock = true,
            PowerLinkageEnabled = true,
            BatteryTdpWatts = 22,
            AcTdpWatts = 65
        };

        // Power linkage AC vs Battery
        Assert.Equal(
            new TdpPowerTarget(65, 65, 65),
            TdpLockPolicy.ResolveEffectiveTarget(settings, runningGame: null, isAcOnline: true));
        Assert.Equal(
            new TdpPowerTarget(22, 22, 22),
            TdpLockPolicy.ResolveEffectiveTarget(settings, runningGame: null, isAcOnline: false));

        // Disabled linkage falls back to CurrentTdpWatts
        settings.PowerLinkageEnabled = false;
        Assert.Equal(
            new TdpPowerTarget(40, 40, 40),
            TdpLockPolicy.ResolveEffectiveTarget(settings, runningGame: null, isAcOnline: true));

        // Active per-game profile overrides global & linkage
        settings.PowerLinkageEnabled = true;
        settings.PerGameProfiles["game-1"] = new GamePerformanceProfile
        {
            GameId = "game-1",
            GameName = "Test Game",
            Enabled = true,
            TdpWatts = 28
        };
        var runningGame = new GameEntry
        {
            Id = "game-1",
            Title = "Test Game",
            IsCurrentlyRunning = true
        };
        Assert.Equal(
            new TdpPowerTarget(28, 28, 28),
            TdpLockPolicy.ResolveEffectiveTarget(settings, runningGame, isAcOnline: true));

        // Non-running inspected game does NOT override
        runningGame.IsCurrentlyRunning = false;
        Assert.Equal(
            new TdpPowerTarget(65, 65, 65),
            TdpLockPolicy.ResolveEffectiveTarget(settings, runningGame, isAcOnline: true));
    }

    [Fact]
    public void TdpLockPolicy_SleepTimeGapAndDisplayWake_DetectExpectedTransitions()
    {
        var t0 = new DateTime(2026, 11, 26, 10, 0, 0, DateTimeKind.Utc);
        Assert.False(TdpLockPolicy.IsSleepTimeGap(DateTime.MinValue, t0));
        Assert.False(TdpLockPolicy.IsSleepTimeGap(t0, t0.AddSeconds(2)));
        Assert.True(TdpLockPolicy.IsSleepTimeGap(t0, t0.AddSeconds(12)));

        Assert.False(TdpLockPolicy.IsDisplayWakeTransition(-1, 1));
        Assert.False(TdpLockPolicy.IsDisplayWakeTransition(1, 0));
        Assert.False(TdpLockPolicy.IsDisplayWakeTransition(1, 1));
        Assert.True(TdpLockPolicy.IsDisplayWakeTransition(0, 1));
    }

    [Fact]
    public void Reconciler_InitialSyncAndDaemonRestartForceSync_ApplyOnceThenHold()
    {
        var reconciler = new TdpLockReconciler();
        var target = new TdpPowerTarget(25, 25, 27);
        var t0 = new DateTime(2026, 11, 26, 10, 0, 0, DateTimeKind.Utc);

        reconciler.RequestInitialSync();
        var d1 = reconciler.Evaluate(t0, target, null, null, null);
        Assert.True(d1.ShouldApply);
        Assert.Equal(TdpReapplyReason.InitialSync, d1.Reason);

        reconciler.RecordApplied(t0, target, d1.Reason);
        var d2 = reconciler.Evaluate(t0.AddSeconds(5), target, 25.0, 25.0, 27.0);
        Assert.False(d2.ShouldApply);

        // If daemon restarted (TdpLockActive == false), forceSyncRequired triggers immediate re-sync
        var d3 = reconciler.Evaluate(t0.AddSeconds(6), target, 40.0, 45.0, 50.0, forceSyncRequired: true);
        Assert.True(d3.ShouldApply);
        Assert.Equal(TdpReapplyReason.InitialSync, d3.Reason);
    }

    [Fact]
    public void Reconciler_PowerResumeBurst_ExecutesThreeShotsAndDeduplicatesOverlappingWakeSignals()
    {
        var reconciler = new TdpLockReconciler();
        var target = new TdpPowerTarget(28, 28, 30);
        var wakeTime = new DateTime(2026, 11, 26, 10, 5, 0, DateTimeKind.Utc);

        reconciler.RecordApplied(wakeTime.AddMinutes(-5), target);

        // Multiple overlapping OS wake events (WM_POWERBROADCAST + SystemEvents + DisplayWake)
        reconciler.NotifyPowerResume(wakeTime, "WM_POWERBROADCAST");
        reconciler.NotifyPowerResume(wakeTime.AddMilliseconds(200), "SystemEvents.Resume");

        // Before first burst slot (+1.5s from second call = +1.7s)
        var beforeSlot1 = reconciler.Evaluate(wakeTime.AddMilliseconds(900), target, 120.0, 120.0, 120.0);
        Assert.False(beforeSlot1.ShouldApply);

        // Slot 1 (+1.5s)
        var slot1 = reconciler.Evaluate(wakeTime.AddMilliseconds(1800), target, 120.0, 120.0, 120.0);
        Assert.True(slot1.ShouldApply);
        Assert.Equal(TdpReapplyReason.PowerResumeBurst, slot1.Reason);
        reconciler.RecordApplied(wakeTime.AddMilliseconds(1800), target, slot1.Reason);

        // Between slot 1 and slot 2
        var between1And2 = reconciler.Evaluate(wakeTime.AddMilliseconds(3000), target, 28.0, 28.0, 30.0);
        Assert.False(between1And2.ShouldApply);

        // Slot 2 (+4.0s) fires even if PM briefly looked ok (beats delayed BIOS override)
        var slot2 = reconciler.Evaluate(wakeTime.AddMilliseconds(4300), target, 28.0, 28.0, 30.0);
        Assert.True(slot2.ShouldApply);
        Assert.Equal(TdpReapplyReason.PowerResumeBurst, slot2.Reason);
        reconciler.RecordApplied(wakeTime.AddMilliseconds(4300), target, slot2.Reason);

        // Slot 3 (+8.0s)
        var slot3 = reconciler.Evaluate(wakeTime.AddMilliseconds(8300), target, 28.0, 28.0, 30.0);
        Assert.True(slot3.ShouldApply);
        Assert.Equal(TdpReapplyReason.PowerResumeBurst, slot3.Reason);
        reconciler.RecordApplied(wakeTime.AddMilliseconds(8300), target, slot3.Reason);

        // Burst finished -> steady state with matching PM table does not fire
        var steady = reconciler.Evaluate(wakeTime.AddSeconds(15), target, 28.0, 28.0, 30.0);
        Assert.False(steady.ShouldApply);
    }

    [Fact]
    public void Reconciler_HardwareDrift_ReappliesAfterGraceAndRespectsCooldown()
    {
        var reconciler = new TdpLockReconciler();
        var target = new TdpPowerTarget(30, 30, 32);
        var t0 = new DateTime(2026, 11, 26, 10, 10, 0, DateTimeKind.Utc);

        reconciler.RecordApplied(t0, target);

        // Inside 3s post-apply grace period: ignore stale PM table
        var inGrace = reconciler.Evaluate(t0.AddSeconds(2), target, 120.0, 120.0, 120.0);
        Assert.False(inGrace.ShouldApply);

        // Null PM table: do not blindly spam SMU writes
        var nullPm = reconciler.Evaluate(t0.AddSeconds(5), target, null, null, null);
        Assert.False(nullPm.ShouldApply);

        // Beyond grace period and PM table shows full-power 120W reset -> HardwareDrift triggers
        var drift1 = reconciler.Evaluate(t0.AddSeconds(5), target, 120.0, 120.0, 120.0);
        Assert.True(drift1.ShouldApply);
        Assert.Equal(TdpReapplyReason.HardwareDrift, drift1.Reason);
        reconciler.RecordApplied(t0.AddSeconds(5), target, drift1.Reason, 120.0, 120.0, 120.0);

        // Hardware recovers to 30W -> clears post-drift clamp check
        var recovered = reconciler.Evaluate(t0.AddSeconds(10), target, 30.0, 30.0, 32.0);
        Assert.False(recovered.ShouldApply);

        // Later another drift occurs inside 12s cooldown -> blocked until cooldown expires
        var duringCooldown = reconciler.Evaluate(t0.AddSeconds(14), target, 85.0, 85.0, 85.0);
        Assert.False(duringCooldown.ShouldApply);
        Assert.Equal("drift_cooldown", duringCooldown.Diagnostic);

        var afterCooldown = reconciler.Evaluate(t0.AddSeconds(18), target, 85.0, 85.0, 85.0);
        Assert.True(afterCooldown.ShouldApply);
        Assert.Equal(TdpReapplyReason.HardwareDrift, afterCooldown.Reason);
    }

    [Fact]
    public void Reconciler_BiosClampSuppression_PreventsInfiniteWriteLoopAndClearsOnTargetOrWake()
    {
        var reconciler = new TdpLockReconciler();
        var target120 = new TdpPowerTarget(120, 120, 122);
        var t0 = new DateTime(2026, 11, 26, 10, 20, 0, DateTimeKind.Utc);

        reconciler.RecordApplied(t0, target120);

        // BIOS/EC clamped board to 85W; first drift check tries once
        var attempt = reconciler.Evaluate(t0.AddSeconds(5), target120, 85.0, 85.0, 85.0);
        Assert.True(attempt.ShouldApply);
        Assert.Equal(TdpReapplyReason.HardwareDrift, attempt.Reason);
        reconciler.RecordApplied(t0.AddSeconds(5), target120, attempt.Reason, 85.0, 85.0, 85.0);

        // Hardware still reads 85W after grace period -> suppressed instead of spamming SMU
        var suppressed = reconciler.Evaluate(t0.AddSeconds(20), target120, 85.0, 85.0, 85.0);
        Assert.False(suppressed.ShouldApply);
        Assert.Equal("bios_clamped_suppressed", suppressed.Diagnostic);

        // Still suppressed 1 minute later at same clamped value
        var stillSuppressed = reconciler.Evaluate(t0.AddSeconds(80), target120, 85.2, 85.0, 85.0);
        Assert.False(stillSuppressed.ShouldApply);

        // Power resume clears suppression and runs fresh burst
        reconciler.NotifyPowerResume(t0.AddSeconds(90), "DisplayWake");
        var postWake = reconciler.Evaluate(t0.AddSeconds(92), target120, 85.0, 85.0, 85.0);
        Assert.True(postWake.ShouldApply);
        Assert.Equal(TdpReapplyReason.PowerResumeBurst, postWake.Reason);
    }
}
