using HaloDeck.Core.Services;
using Xunit;

namespace HaloDeck.Tests;

public sealed class FrontendFocusArbitratorOskTests
{
    [Fact]
    public void ResolveActiveUiTarget_ReturnsNullWhenTabTipIsVisible_ToYieldGamepadToOnScreenKeyboard()
    {
        // QAM visible and focused while TabTip is open -> pause HaloDeck gamepad UI nav
        Assert.Null(FrontendFocusArbitrator.ResolveActiveUiTarget(
            qamInteractiveVisible: true,
            qamActiveOrForeground: true,
            mainVisible: false,
            mainActiveOrForeground: false,
            desktopHasNoForeground: false,
            qamFocusBlockedByElevation: false,
            tabTipVisible: true));

        // QAM open over elevated game (UIPI) while TabTip is open -> still yield gamepad to OSK
        Assert.Null(FrontendFocusArbitrator.ResolveActiveUiTarget(
            qamInteractiveVisible: true,
            qamActiveOrForeground: false,
            mainVisible: false,
            mainActiveOrForeground: false,
            desktopHasNoForeground: false,
            qamFocusBlockedByElevation: true,
            tabTipVisible: true));

        // Main console visible and focused while TabTip is open -> yield gamepad to OSK
        Assert.Null(FrontendFocusArbitrator.ResolveActiveUiTarget(
            qamInteractiveVisible: false,
            qamActiveOrForeground: false,
            mainVisible: true,
            mainActiveOrForeground: true,
            desktopHasNoForeground: false,
            qamFocusBlockedByElevation: false,
            tabTipVisible: true));

        // Even with desktopHasNoForeground == true, TabTip takes precedence for gamepad typing
        Assert.Null(FrontendFocusArbitrator.ResolveActiveUiTarget(
            qamInteractiveVisible: true,
            qamActiveOrForeground: true,
            mainVisible: true,
            mainActiveOrForeground: true,
            desktopHasNoForeground: true,
            qamFocusBlockedByElevation: true,
            tabTipVisible: true));
    }

    [Fact]
    public void ResolveActiveUiTarget_RoutesNormallyToQamAndConsole_WhenTabTipIsHidden()
    {
        Assert.Equal("qam", FrontendFocusArbitrator.ResolveActiveUiTarget(
            qamInteractiveVisible: true,
            qamActiveOrForeground: true,
            mainVisible: false,
            mainActiveOrForeground: false,
            desktopHasNoForeground: false,
            qamFocusBlockedByElevation: false,
            tabTipVisible: false));

        Assert.Equal("qam", FrontendFocusArbitrator.ResolveActiveUiTarget(
            qamInteractiveVisible: true,
            qamActiveOrForeground: false,
            mainVisible: false,
            mainActiveOrForeground: false,
            desktopHasNoForeground: false,
            qamFocusBlockedByElevation: true,
            tabTipVisible: false));

        Assert.Equal("console", FrontendFocusArbitrator.ResolveActiveUiTarget(
            qamInteractiveVisible: false,
            qamActiveOrForeground: false,
            mainVisible: true,
            mainActiveOrForeground: true,
            desktopHasNoForeground: false,
            qamFocusBlockedByElevation: false,
            tabTipVisible: false));

        // Backward-compatible default (tabTipVisible omitted => false)
        Assert.Equal("qam", FrontendFocusArbitrator.ResolveActiveUiTarget(
            qamInteractiveVisible: true,
            qamActiveOrForeground: true,
            mainVisible: false,
            mainActiveOrForeground: false,
            desktopHasNoForeground: false));
    }

    [Fact]
    public void ShouldAutoDismissQamOnExternalForeground_NeverDismissesWhileTabTipIsVisible()
    {
        // Even well past SummonFocusGraceMs with an external foreground PID (e.g., TextInputHost / TabTip)
        Assert.False(FrontendFocusArbitrator.ShouldAutoDismissQamOnExternalForeground(
            qamInteractiveVisible: true,
            msSinceShown: 5000,
            foregroundHwnd: 0x5678,
            foregroundPid: 8888,
            selfPid: 1000,
            tabTipVisible: true,
            externalForegroundIsElevated: false,
            selfIsElevated: true));

        Assert.False(FrontendFocusArbitrator.ShouldAutoDismissQamOnExternalForeground(
            qamInteractiveVisible: true,
            msSinceShown: 5000,
            foregroundHwnd: 0x5678,
            foregroundPid: 8888,
            selfPid: 1000,
            tabTipVisible: true,
            externalForegroundIsElevated: true,
            selfIsElevated: true));

        // Once TabTip closes and an external window owns foreground past grace window, normal auto-dismiss resumes
        Assert.True(FrontendFocusArbitrator.ShouldAutoDismissQamOnExternalForeground(
            qamInteractiveVisible: true,
            msSinceShown: 5000,
            foregroundHwnd: 0x5678,
            foregroundPid: 8888,
            selfPid: 1000,
            tabTipVisible: false,
            externalForegroundIsElevated: false,
            selfIsElevated: true));
    }
}
