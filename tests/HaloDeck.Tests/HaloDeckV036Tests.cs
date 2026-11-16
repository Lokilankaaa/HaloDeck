using HaloDeck.Core.Services;
using Xunit;

namespace HaloDeck.Tests;

public sealed class HaloDeckV036Tests
{
    [Fact]
    public void DisplayRefreshModeResolver_DecodesWindows11DynamicRefreshRateAs120HzCeiling()
    {
        // Live Windows 11 DRR on GPD Win 5 reports flags = 0x11 (Active | BoostRefreshRate),
        // virtual base targetRate = 120/2 (60Hz), and physical TargetMode vSync = 120/1 (120Hz).
        var drr = DisplayRefreshModeResolver.ResolveFromCcd(
            pathFlags: 0x11u,
            targetRateNumerator: 120u,
            targetRateDenominator: 2u,
            targetModeVSyncNumerator: 120u,
            targetModeVSyncDenominator: 1u,
            fallbackDevModeHz: 60);

        Assert.True(drr.IsDynamicRefreshRate);
        Assert.Equal(120, drr.RefreshRateHz);
        Assert.Equal(60, drr.BaseRefreshRateHz);

        var fixed120 = DisplayRefreshModeResolver.ResolveFromCcd(
            pathFlags: 0x01u,
            targetRateNumerator: 120u,
            targetRateDenominator: 1u,
            targetModeVSyncNumerator: 120u,
            targetModeVSyncDenominator: 1u,
            fallbackDevModeHz: 120);

        Assert.False(fixed120.IsDynamicRefreshRate);
        Assert.Equal(120, fixed120.RefreshRateHz);
        Assert.Equal(120, fixed120.BaseRefreshRateHz);

        int ceiling = DisplayRefreshModeResolver.ResolveDynamicCeilingHz(
            new[] { 48, 60, 75, 100, 120 },
            currentlyDynamic: false,
            currentRefreshHz: 60);
        Assert.Equal(120, ceiling);
    }

    [Fact]
    public void PowerRuntimeEstimator_AcChargingAndSustainedShowNoDrainCountdown()
    {
        var charging = PowerRuntimeEstimator.Compute(
            isAcOnline: true,
            hasBattery: true,
            isCharging: true,
            batteryPercent: 58,
            batteryPowerWatts: 22.0,
            batteryRemainingWh: 46.0,
            apuPowerWatts: 30,
            brightnessPercent: 60,
            fanRpm: 2600,
            previousEmaSystemDrawWatts: 25.0,
            isBatteryDischarging: false);

        Assert.Equal("charging", charging.RuntimeMode);
        Assert.Null(charging.EstimatedStandbyMinutes);
        Assert.Equal(22.0, charging.ChargeWatts);
        Assert.Equal(0.0, charging.BatteryDischargeWatts);

        var sustained = PowerRuntimeEstimator.Compute(
            isAcOnline: true,
            hasBattery: true,
            isCharging: false,
            batteryPercent: 98,
            batteryPowerWatts: 0.0,
            batteryRemainingWh: 78.4,
            apuPowerWatts: 35,
            brightnessPercent: 70,
            fanRpm: 3000,
            previousEmaSystemDrawWatts: 0.0,
            isBatteryDischarging: false);

        Assert.Equal("ac_sustained", sustained.RuntimeMode);
        Assert.Null(sustained.EstimatedStandbyMinutes);
        Assert.True(sustained.IsBypassPower);
        Assert.Equal(0.0, sustained.BatteryDischargeWatts);
    }

    [Fact]
    public void PowerRuntimeEstimator_AcWeakChargerDischargingComputesRuntimeFromBatteryDischargeWatts()
    {
        // Plugged into weak PD charger where system needs ~60W total while battery discharges at 16.0W net.
        // Remaining 48.0 Wh / 16.0 W * 60 = 180 minutes.
        var weakAc = PowerRuntimeEstimator.Compute(
            isAcOnline: true,
            hasBattery: true,
            isCharging: false,
            batteryPercent: 60,
            batteryPowerWatts: 16.0,
            batteryRemainingWh: 48.0,
            apuPowerWatts: 55,
            brightnessPercent: 80,
            fanRpm: 3800,
            previousEmaSystemDrawWatts: 0.0,
            isBatteryDischarging: true);

        Assert.Equal("ac_discharging", weakAc.RuntimeMode);
        Assert.False(weakAc.IsBypassPower);
        Assert.Equal(16.0, weakAc.BatteryDischargeWatts);
        Assert.Equal(0.0, weakAc.ChargeWatts);
        Assert.Equal(180, weakAc.EstimatedStandbyMinutes);
        Assert.Equal(Math.Round(weakAc.SystemDrawWatts - 16.0, 1), weakAc.ChargerInputWatts);
    }

    [Fact]
    public void PowerRuntimeEstimator_BatteryInstantaneouslyTracksApuPowerJumpWithoutBmsLag()
    {
        // Steady state on battery: 15W APU + 5.5W board/display = 20.5W Smart Battery discharge.
        var steady = PowerRuntimeEstimator.Compute(
            isAcOnline: false,
            hasBattery: true,
            isCharging: false,
            batteryPercent: 75,
            batteryPowerWatts: 20.5,
            batteryRemainingWh: 60.0,
            apuPowerWatts: 15,
            brightnessPercent: 50,
            fanRpm: 2400,
            previousEmaSystemDrawWatts: 20.5,
            isBatteryDischarging: true,
            previousSlowApuWatts: 15.0);

        Assert.Equal("battery", steady.RuntimeMode);
        Assert.Equal(20.5, steady.SystemDrawWatts);

        // Next tick: APU jumps immediately to 40W (+25W) while BMS BatteryStatus still reports lagging 20.5W.
        // Complementary sensor fusion must reflect 40 + 5.5 = 45.5W on the very first tick!
        var jumped = PowerRuntimeEstimator.Compute(
            isAcOnline: false,
            hasBattery: true,
            isCharging: false,
            batteryPercent: 75,
            batteryPowerWatts: 20.5,
            batteryRemainingWh: 60.0,
            apuPowerWatts: 40,
            brightnessPercent: 50,
            fanRpm: 2400,
            previousEmaSystemDrawWatts: steady.NextEmaSystemDrawWatts,
            isBatteryDischarging: true,
            previousSlowApuWatts: steady.NextSlowApuWatts);

        Assert.Equal(45.5, jumped.SystemDrawWatts);
        Assert.NotNull(jumped.EstimatedStandbyMinutes);
    }

    [Fact]
    public void FrontendFocusArbitrator_NeverRoutesGamepadToQamWhenExternalGameOwnsForeground()
    {
        // QAM visible while an external game window actually holds desktop foreground -> never route controller to QAM!
        Assert.Null(FrontendFocusArbitrator.ResolveActiveUiTarget(
            qamInteractiveVisible: true,
            qamActiveOrForeground: false,
            mainVisible: false,
            mainActiveOrForeground: false,
            desktopHasNoForeground: false));

        // QAM visible and holding foreground -> strictly route to QAM
        Assert.Equal("qam", FrontendFocusArbitrator.ResolveActiveUiTarget(
            qamInteractiveVisible: true,
            qamActiveOrForeground: true,
            mainVisible: false,
            mainActiveOrForeground: false,
            desktopHasNoForeground: false));

        // Closing shield active (qamInteractiveVisible == false) -> never accept further QAM input while waiting release
        Assert.Null(FrontendFocusArbitrator.ResolveActiveUiTarget(
            qamInteractiveVisible: false,
            qamActiveOrForeground: true,
            mainVisible: false,
            mainActiveOrForeground: false,
            desktopHasNoForeground: false));

        // Console active & visible -> route to console
        Assert.Equal("console", FrontendFocusArbitrator.ResolveActiveUiTarget(
            qamInteractiveVisible: false,
            qamActiveOrForeground: false,
            mainVisible: true,
            mainActiveOrForeground: true,
            desktopHasNoForeground: false));
    }

    [Fact]
    public void FrontendFocusArbitrator_ReassertsDuringSummonGraceAndAutoDismissesAfterGrace()
    {
        // Within initial 450ms summon grace window, re-assert QAM foreground if an external window took focus, do not dismiss.
        Assert.True(FrontendFocusArbitrator.ShouldReassertQamForeground(
            qamInteractiveVisible: true,
            msSinceShown: 120,
            foregroundHwnd: 0x1234,
            qamIsForeground: false));
        Assert.False(FrontendFocusArbitrator.ShouldAutoDismissQamOnExternalForeground(
            qamInteractiveVisible: true,
            msSinceShown: 120,
            foregroundHwnd: 0x1234,
            foregroundPid: 9999,
            selfPid: 1000,
            tabTipVisible: false));

        // After 450ms grace window, external game regaining foreground auto-dismisses QAM.
        Assert.False(FrontendFocusArbitrator.ShouldReassertQamForeground(
            qamInteractiveVisible: true,
            msSinceShown: 600,
            foregroundHwnd: 0x1234,
            qamIsForeground: false));
        Assert.True(FrontendFocusArbitrator.ShouldAutoDismissQamOnExternalForeground(
            qamInteractiveVisible: true,
            msSinceShown: 600,
            foregroundHwnd: 0x1234,
            foregroundPid: 9999,
            selfPid: 1000,
            tabTipVisible: false));

        // Never auto-dismiss for HaloDeck's own PID or while TabTip is open.
        Assert.False(FrontendFocusArbitrator.ShouldAutoDismissQamOnExternalForeground(
            qamInteractiveVisible: true,
            msSinceShown: 600,
            foregroundHwnd: 0x1234,
            foregroundPid: 1000,
            selfPid: 1000,
            tabTipVisible: false));
        Assert.False(FrontendFocusArbitrator.ShouldAutoDismissQamOnExternalForeground(
            qamInteractiveVisible: true,
            msSinceShown: 600,
            foregroundHwnd: 0x1234,
            foregroundPid: 9999,
            selfPid: 1000,
            tabTipVisible: true));
    }

    [Fact]
    public void FrontendFocusArbitrator_EngagesCloseShieldForBothHeldButtonsAndRecentFastTapIpcArrival()
    {
        Assert.True(FrontendFocusArbitrator.ShouldEngageCloseFocusShield(
            isAnyButtonOrTriggerHeld: true,
            msSinceLastButtonOrTrigger: 9999,
            isEscapeKeyHeld: false));

        // Fast B tap released just before WebView2 RPC arrived (45ms ago): still engage close shield.
        Assert.True(FrontendFocusArbitrator.ShouldEngageCloseFocusShield(
            isAnyButtonOrTriggerHeld: false,
            msSinceLastButtonOrTrigger: 45,
            isEscapeKeyHeld: false));

        // Keyboard Escape closing frontend: also engage shield until released.
        Assert.True(FrontendFocusArbitrator.ShouldEngageCloseFocusShield(
            isAnyButtonOrTriggerHeld: false,
            msSinceLastButtonOrTrigger: 2000,
            isEscapeKeyHeld: true));

        // Idle mouse/touch click close with no recent gamepad input: no delay needed.
        Assert.False(FrontendFocusArbitrator.ShouldEngageCloseFocusShield(
            isAnyButtonOrTriggerHeld: false,
            msSinceLastButtonOrTrigger: 1200,
            isEscapeKeyHeld: false));
    }
}
