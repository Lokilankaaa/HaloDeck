using HaloDeck.Core.Services;
using Xunit;

namespace HaloDeck.Tests;

public sealed class OskRawFallbackGuardTests
{
    [Fact]
    public void RawInputCtrlWinWithin150ms_DetectsCandidate_WhileSoloOrSlowGapDoesNot()
    {
        var guard = new OskRawFallbackGuard();

        // Real hardware trace: +1594ms vk=0x11 -> +1609ms vk=0x5B (15ms delta)
        Assert.False(guard.ProcessRawKeyDown(OskRawFallbackGuard.VkControl, 1594));
        Assert.True(guard.ProcessRawKeyDown(OskRawFallbackGuard.VkLWin, 1609));

        // Reverse order (Win -> Ctrl within 150ms) also matches
        Assert.False(guard.ProcessRawKeyDown(OskRawFallbackGuard.VkLWin, 3000));
        Assert.True(guard.ProcessRawKeyDown(OskRawFallbackGuard.VkLControl, 3050));

        // Solo Ctrl or non-modifier keys never trigger
        Assert.False(guard.ProcessRawKeyDown(OskRawFallbackGuard.VkControl, 5000));
        Assert.False(guard.ProcessRawKeyDown(0x4F, 5010));

        // Gap > 150ms must NOT false-positive
        Assert.False(guard.ProcessRawKeyDown(OskRawFallbackGuard.VkControl, 6000));
        Assert.False(guard.ProcessRawKeyDown(OskRawFallbackGuard.VkLWin, 6151));
    }

    [Fact]
    public void LeakedOsk_WhenHookAlreadyTriggered_OnlyDismissesLegacyOskWithoutDoubleTogglingTabTip()
    {
        var guard = new OskRawFallbackGuard();

        // Hook triggered at t=10_000, leaked osk.exe spotted 90ms later
        var res = guard.EvaluateLeakedOskAction(nowMs: 10_090, lastHookTriggerTickMs: 10_000);
        Assert.Equal(LeakedOskResolution.DismissLegacyOskOnly, res);
    }

    [Fact]
    public void LeakedOsk_WhenHookDidNotTrigger_DismissesAndTriggersTabTipOnceWithCooldown()
    {
        var guard = new OskRawFallbackGuard();

        // Hook never triggered (lastHookTriggerTickMs = -1000)
        var first = guard.EvaluateLeakedOskAction(nowMs: 20_000, lastHookTriggerTickMs: -1000);
        Assert.Equal(LeakedOskResolution.DismissLegacyOskAndTriggerTabTip, first);

        // Rapid second detection within 650ms cooldown only kills osk.exe, does not double-toggle TabTip
        var second = guard.EvaluateLeakedOskAction(nowMs: 20_300, lastHookTriggerTickMs: -1000);
        Assert.Equal(LeakedOskResolution.DismissLegacyOskOnly, second);

        // After cooldown elapses (>650ms), fallback can trigger again cleanly
        var third = guard.EvaluateLeakedOskAction(nowMs: 20_700, lastHookTriggerTickMs: -1000);
        Assert.Equal(LeakedOskResolution.DismissLegacyOskAndTriggerTabTip, third);
    }

    [Fact]
    public void HookWatchdog_RateLimitsReinstallAndIgnoresUiAccessKeyboardForeground()
    {
        var guard = new OskRawFallbackGuard();

        // Hook hit 100ms ago -> healthy, do not schedule
        Assert.False(guard.ShouldScheduleWatchdogCheck(nowMs: 5_100, lastHookHitTickMs: 5_000));

        // Hook silent for >400ms -> schedule check
        Assert.True(guard.ShouldScheduleWatchdogCheck(nowMs: 5_500, lastHookHitTickMs: 5_000));

        // If foreground is OSKMainClass/IPTip_Main_Window (uiAccess), do NOT claim reinstall
        Assert.False(guard.TryClaimWatchdogReinstall(nowMs: 5_580, lastHookHitTickMs: 5_000, foregroundIsUiAccessKeyboard: true));

        // If hook woke up during the 80ms grace window, do NOT reinstall
        Assert.False(guard.TryClaimWatchdogReinstall(nowMs: 5_580, lastHookHitTickMs: 5_540, foregroundIsUiAccessKeyboard: false));

        // Genuine dropout on normal foreground -> reinstall claimed
        Assert.True(guard.TryClaimWatchdogReinstall(nowMs: 5_580, lastHookHitTickMs: 5_000, foregroundIsUiAccessKeyboard: false));

        // Rate-limited for 2500ms after reinstall
        Assert.False(guard.ShouldScheduleWatchdogCheck(nowMs: 7_000, lastHookHitTickMs: 5_000));
        Assert.False(guard.TryClaimWatchdogReinstall(nowMs: 7_000, lastHookHitTickMs: 5_000, foregroundIsUiAccessKeyboard: false));

        // After 2500ms cooldown, allowed again
        Assert.True(guard.ShouldScheduleWatchdogCheck(nowMs: 8_100, lastHookHitTickMs: 5_000));
        Assert.True(guard.TryClaimWatchdogReinstall(nowMs: 8_100, lastHookHitTickMs: 5_000, foregroundIsUiAccessKeyboard: false));
    }

    [Fact]
    public void DiagnosticReport_IncludesKeyboardHookHealthLine()
    {
        var session = new KeyCaptureDiagnosticSession(1000);
        var envRunning = new KeyCaptureEnvironmentInfo(
            SelfElevated: true,
            RegisterHotKeyF13Ok: true,
            RegisterHotKeyF13Error: 0,
            GpdVendorHidConnected: true,
            GpdVendorHidDevice: "GPD",
            GpdVendorHidError: "",
            XInputBackend: "xinput1_4",
            ConnectedXInputCount: 1,
            GpdToolRunning: false,
            GpdToolElevated: false,
            GpdToolServiceRunning: false,
            FirmwareX: "1.12",
            FirmwareK: "1.08",
            InterceptPhysicalMenuButton: true,
            InterceptGuideMenuButton: true,
            KeyboardHookInstalled: true,
            KeyboardHookHandle: 0x1234ABCD,
            KeyboardHookHitCount: 42,
            KeyboardHookLastError: 0);

        var repRunning = session.BuildReport(envRunning, 5000);
        Assert.Contains("WH_KEYBOARD_LL 钩子: 运行中 (handle=0x1234ABCD, 累计命中=42次)", repRunning.CopyableReport);

        var envFailed = envRunning with { KeyboardHookInstalled = false, KeyboardHookHandle = 0, KeyboardHookLastError = 1428 };
        var repFailed = session.BuildReport(envFailed, 5000);
        Assert.Contains("WH_KEYBOARD_LL 钩子: 未安装/失败 (Win32=1428)", repFailed.CopyableReport);
    }
}
