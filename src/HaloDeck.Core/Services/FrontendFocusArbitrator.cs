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
        bool desktopHasNoForeground)
    {
        if (qamInteractiveVisible && (qamActiveOrForeground || desktopHasNoForeground))
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
        bool tabTipVisible)
    {
        if (!qamInteractiveVisible || foregroundHwnd == 0 || tabTipVisible)
        {
            return false;
        }

        if (foregroundPid == 0 || foregroundPid == selfPid)
        {
            return false;
        }

        return msSinceShown >= SummonFocusGraceMs;
    }
}
