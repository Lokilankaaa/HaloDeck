using HaloDeck.Core.Services;
using Xunit;

namespace HaloDeck.Tests;

public sealed class PhysicalOskChordTrackerTests
{
    [Fact]
    public void SinglePacketHidBurst_SameMillisecond_LCtrl_LWin_O_InterceptsEvenWhenAsyncKeyStateLags()
    {
        var tracker = new PhysicalOskChordTracker();
        long t = 10_000;

        Assert.Equal(PhysicalOskChordAction.PassThrough,
            tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkLControl, true, false, 0, false, true, false, false, t));
        Assert.Equal(PhysicalOskChordAction.PassThrough,
            tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkLWin, true, false, 0, false, true, false, false, t));

        Assert.True(tracker.IsCtrlWinActive(t));

        // Simulate post-hook GetAsyncKeyState still reporting false on single-packet HID burst
        var action = tracker.ProcessKeyEvent(
            PhysicalOskChordTracker.VkO, true, false, 0, false, true, asyncCtrlDown: false, asyncWinDown: false, t);
        Assert.Equal(PhysicalOskChordAction.SwallowKeyDownAndTrigger, action);

        // Auto-repeat while held is swallowed without re-triggering
        var repeatAction = tracker.ProcessKeyEvent(
            PhysicalOskChordTracker.VkO, true, false, 0, false, true, asyncCtrlDown: false, asyncWinDown: false, t + 120);
        Assert.Equal(PhysicalOskChordAction.SwallowKeyDownRepeat, repeatAction);
    }

    [Fact]
    public void RightModifiers_And_LWinBeforeLCtrl_BothIntercept()
    {
        var tracker = new PhysicalOskChordTracker();

        // RCtrl + RWin + O
        tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkRControl, true, false, 0, false, true, false, false, 1000);
        tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkRWin, true, false, 0, false, true, false, false, 1001);
        Assert.Equal(PhysicalOskChordAction.SwallowKeyDownAndTrigger,
            tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkO, true, false, 0, false, true, false, false, 1002));
        Assert.Equal(PhysicalOskChordAction.SwallowKeyUp,
            tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkO, false, true, 0, false, true, false, false, 1020));
        tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkRWin, false, true, 0, false, true, false, false, 1025);
        tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkRControl, false, true, 0, false, true, false, false, 1026);

        // LWin pressed BEFORE LCtrl, then O
        tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkLWin, true, false, 0, false, true, false, false, 2000);
        tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkLControl, true, false, 0, false, true, false, false, 2001);
        Assert.Equal(PhysicalOskChordAction.SwallowKeyDownAndTrigger,
            tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkO, true, false, 0, false, true, false, false, 2002));
    }

    [Fact]
    public void SoloO_CtrlO_WinO_NeverFalsePositive()
    {
        var tracker = new PhysicalOskChordTracker();

        // Solo O
        Assert.Equal(PhysicalOskChordAction.PassThrough,
            tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkO, true, false, 0, false, true, false, false, 1000));
        Assert.Equal(PhysicalOskChordAction.PassThrough,
            tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkO, false, true, 0, false, true, false, false, 1010));

        // Ctrl + O only
        tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkLControl, true, false, 0, false, true, false, false, 2000);
        Assert.Equal(PhysicalOskChordAction.PassThrough,
            tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkO, true, false, 0, false, true, asyncCtrlDown: true, asyncWinDown: false, 2005));
        tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkLControl, false, true, 0, false, true, false, false, 2020);

        // Win + O only
        tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkLWin, true, false, 0, false, true, false, false, 3000);
        Assert.Equal(PhysicalOskChordAction.PassThrough,
            tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkO, true, false, 0, false, true, asyncCtrlDown: false, asyncWinDown: true, 3005));
        tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkLWin, false, true, 0, false, true, false, false, 3020);
    }

    [Fact]
    public void DroppedKeyUp_ExpiresAfter4000ms_SoNormalODoesNotGetStuck()
    {
        var tracker = new PhysicalOskChordTracker();

        // Modifiers pressed, keyup never arrives
        tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkLControl, true, false, 0, false, true, false, false, 1000);
        tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkLWin, true, false, 0, false, true, false, false, 1000);
        Assert.True(tracker.IsCtrlWinActive(4999));

        // After > 4000ms without keyup, stale state expires automatically
        Assert.False(tracker.IsCtrlWinActive(5001));
        Assert.Equal(PhysicalOskChordAction.PassThrough,
            tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkO, true, false, 0, false, true, false, false, 5001));
    }

    [Fact]
    public void KeyUpPairedSwallow_ResetsStateCleanly()
    {
        var tracker = new PhysicalOskChordTracker();

        tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkLControl, true, false, 0, false, true, false, false, 1000);
        tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkLWin, true, false, 0, false, true, false, false, 1000);
        Assert.Equal(PhysicalOskChordAction.SwallowKeyDownAndTrigger,
            tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkO, true, false, 0, false, true, false, false, 1000));
        Assert.True(tracker.IsSwallowedODown);

        // First O keyup is swallowed and resets _swallowedODown
        Assert.Equal(PhysicalOskChordAction.SwallowKeyUp,
            tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkO, false, true, 0, false, true, false, false, 1030));
        Assert.False(tracker.IsSwallowedODown);

        // Release modifiers; subsequent normal O down/up passes through cleanly
        tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkLWin, false, true, 0, false, true, false, false, 1031);
        tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkLControl, false, true, 0, false, true, false, false, 1032);
        Assert.Equal(PhysicalOskChordAction.PassThrough,
            tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkO, true, false, 0, false, true, false, false, 1100));
        Assert.Equal(PhysicalOskChordAction.PassThrough,
            tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkO, false, true, 0, false, true, false, false, 1120));
    }

    [Fact]
    public void InjectedChordStillIntercepts_WhileSyntheticChordBreakExtraInfoIsIgnored()
    {
        var tracker = new PhysicalOskChordTracker();

        // Our own SyntheticChordBreakExtraInfo (including standalone Ctrl tap) must never pollute state or trigger
        Assert.Equal(PhysicalOskChordAction.PassThrough,
            tracker.ProcessKeyEvent(
                PhysicalOskChordTracker.VkControl, true, false,
                PhysicalOskChordTracker.SyntheticChordBreakExtraInfo, isInjected: true, interceptEnabled: true,
                asyncCtrlDown: false, asyncWinDown: false, nowMs: 1000));
        Assert.Equal(PhysicalOskChordAction.PassThrough,
            tracker.ProcessKeyEvent(
                PhysicalOskChordTracker.VkLWin, true, false,
                PhysicalOskChordTracker.SyntheticChordBreakExtraInfo, isInjected: true, interceptEnabled: true,
                asyncCtrlDown: false, asyncWinDown: false, nowMs: 1001));
        Assert.False(tracker.IsCtrlWinActive(1002));
        Assert.Equal(PhysicalOskChordAction.PassThrough,
            tracker.ProcessKeyEvent(
                PhysicalOskChordTracker.VkO, true, false,
                PhysicalOskChordTracker.SyntheticChordBreakExtraInfo, isInjected: true, interceptEnabled: true,
                asyncCtrlDown: true, asyncWinDown: true, nowMs: 1002));

        // Driver/mapper-injected Ctrl+Win+O (isInjected=true, normal extraInfo=0) MUST still be intercepted
        tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkLControl, true, false, 0, isInjected: true, true, false, false, 2000);
        tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkLWin, true, false, 0, isInjected: true, true, false, false, 2000);
        Assert.Equal(PhysicalOskChordAction.SwallowKeyDownAndTrigger,
            tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkO, true, false, 0, isInjected: true, true, false, false, 2000));
        Assert.Equal(PhysicalOskChordAction.SwallowKeyUp,
            tracker.ProcessKeyEvent(PhysicalOskChordTracker.VkO, false, true, 0, isInjected: true, true, false, false, 2015));
    }

    [Fact]
    public void AsyncKeyStateFallback_WorksWhenModifiersWerePressedBeforeHookInstalled()
    {
        var tracker = new PhysicalOskChordTracker();

        Assert.Equal(PhysicalOskChordAction.SwallowKeyDownAndTrigger,
            tracker.ProcessKeyEvent(
                PhysicalOskChordTracker.VkO, true, false, 0, false, interceptEnabled: true,
                asyncCtrlDown: true, asyncWinDown: true, nowMs: 5000));
    }
}
