using HaloDeck.Core.Config;
using HaloDeck.Core.Models;
using HaloDeck.Core.Services;
using Xunit;

namespace HaloDeck.Tests;

public sealed class HaloDeckV055ElevationTests
{
    [Fact]
    public void ShouldAutoDismissQam_DoesNotDismissWhenBlockedByElevatedGameUipi()
    {
        // Standard non-elevated external window after grace period -> auto-dismisses normally
        Assert.True(FrontendFocusArbitrator.ShouldAutoDismissQamOnExternalForeground(
            qamInteractiveVisible: true,
            msSinceShown: 900,
            foregroundHwnd: 0x1234,
            foregroundPid: 4242,
            selfPid: 1000,
            tabTipVisible: false,
            externalForegroundIsElevated: false,
            selfIsElevated: false));

        // Elevated game while HaloDeck is Medium IL -> UIPI blocks focus steal; NEVER auto-dismiss QAM
        Assert.False(FrontendFocusArbitrator.ShouldAutoDismissQamOnExternalForeground(
            qamInteractiveVisible: true,
            msSinceShown: 5000,
            foregroundHwnd: 0x1234,
            foregroundPid: 4242,
            selfPid: 1000,
            tabTipVisible: false,
            externalForegroundIsElevated: true,
            selfIsElevated: false));

        // Once HaloDeck itself runs elevated, normal focus-loss auto-dismiss resumes
        Assert.True(FrontendFocusArbitrator.ShouldAutoDismissQamOnExternalForeground(
            qamInteractiveVisible: true,
            msSinceShown: 900,
            foregroundHwnd: 0x1234,
            foregroundPid: 4242,
            selfPid: 1000,
            tabTipVisible: false,
            externalForegroundIsElevated: true,
            selfIsElevated: true));
    }

    [Fact]
    public void ResolveActiveUiTarget_KeepsGamepadControlOnQamWhenBlockedByElevation()
    {
        // Normal unfocused QAM -> null
        Assert.Null(FrontendFocusArbitrator.ResolveActiveUiTarget(
            qamInteractiveVisible: true,
            qamActiveOrForeground: false,
            mainVisible: false,
            mainActiveOrForeground: false,
            desktopHasNoForeground: false,
            qamFocusBlockedByElevation: false));

        // QAM open over elevated game where UIPI blocked foreground -> still routes gamepad nav to QAM
        Assert.Equal("qam", FrontendFocusArbitrator.ResolveActiveUiTarget(
            qamInteractiveVisible: true,
            qamActiveOrForeground: false,
            mainVisible: false,
            mainActiveOrForeground: false,
            desktopHasNoForeground: false,
            qamFocusBlockedByElevation: true));
    }

    [Fact]
    public void AppSettings_RunAsAdministrator_DefaultsFalseAndPersistsRoundTrip()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "HaloDeckTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var store = new SettingsStore(tempDir);
            var initial = store.Load();
            Assert.False(initial.RunAsAdministrator);

            initial.RunAsAdministrator = true;
            store.Save(initial);

            var reloaded = store.Load();
            Assert.True(reloaded.RunAsAdministrator);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
