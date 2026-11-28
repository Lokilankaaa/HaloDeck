namespace HaloDeck.Core.Services;

/// <summary>
/// Pure focus &amp; controller-target arbitration rules ensuring HaloDeck UI and a game
/// never process controller input simultaneously.
/// </summary>
public static class FrontendFocusArbitrator
{
    public const double SummonFocusGraceMs = 450.0;
    public const long RecentGamepadCloseWindowMs = 350;
    public const int MaxCloseReleaseWaitMs = 2500;
    public const int PostReleaseDrainMs = 160;

    public static bool ShouldEngageCloseFocusShield(
        bool isAnyButtonOrTriggerHeld,
        long msSinceLastButtonOrTrigger,
        bool isEscapeKeyHeld)
    {
        if (isAnyButtonOrTriggerHeld || isEscapeKeyHeld)
        {
            return true;
        }

        return msSinceLastButtonOrTrigger >= 0 && msSinceLastButtonOrTrigger <= RecentGamepadCloseWindowMs;
    }

    public static string? ResolveActiveUiTarget(
        bool qamInteractiveVisible,
        bool qamActiveOrForeground,
        bool mainVisible,
        bool mainActiveOrForeground,
        bool desktopHasNoForeground,
        bool qamFocusBlockedByElevation = false,
        bool tabTipVisible = false)
    {
        if (tabTipVisible)
        {
            return null;
        }

        if (qamInteractiveVisible && (qamActiveOrForeground || desktopHasNoForeground || qamFocusBlockedByElevation))
        {
            return "qam";
        }

        if (mainVisible && (mainActiveOrForeground || desktopHasNoForeground))
        {
            return "console";
        }

        return null;
    }

    public static bool ShouldReassertQamForeground(
        bool qamInteractiveVisible,
        double msSinceShown,
        nint foregroundHwnd,
        bool qamIsForeground)
    {
        if (!qamInteractiveVisible || foregroundHwnd == 0 || qamIsForeground)
        {
            return false;
        }

        return msSinceShown >= 0 && msSinceShown < SummonFocusGraceMs;
    }

    public static bool ShouldAutoDismissQamOnExternalForeground(
        bool qamInteractiveVisible,
        double msSinceShown,
        nint foregroundHwnd,
        uint foregroundPid,
        uint selfPid,
        bool tabTipVisible,
        bool externalForegroundIsElevated = false,
        bool selfIsElevated = false)
    {
        if (!qamInteractiveVisible || foregroundHwnd == 0 || tabTipVisible)
        {
            return false;
        }

        if (foregroundPid == 0 || foregroundPid == selfPid)
        {
            return false;
        }

        // When a non-elevated HaloDeck opens QAM over an admin-elevated game (e.g. Endfield / NTE),
        // Windows UIPI prevents SetForegroundWindow from taking focus away from the game.
        // Never auto-dismiss QAM solely because UIPI kept foreground on the elevated game.
        if (externalForegroundIsElevated && !selfIsElevated)
        {
            return false;
        }

        return msSinceShown >= SummonFocusGraceMs;
    }
}
