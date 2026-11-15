namespace HaloDeck.Core.Services;

public sealed record DisplayRefreshModeInfo(
    int RefreshRateHz,
    int BaseRefreshRateHz,
    bool IsDynamicRefreshRate);

/// <summary>
/// Resolves Windows 11 CCD virtual/dynamic refresh rate (DRR/VRR) path flags and rational frequencies.
/// </summary>
public static class DisplayRefreshModeResolver
{
    public const uint PathActive = 0x00000001u;
    public const uint PathBoostRefreshRate = 0x00000010u;

    public static DisplayRefreshModeInfo ResolveFromCcd(
        uint pathFlags,
        uint targetRateNumerator,
        uint targetRateDenominator,
        uint targetModeVSyncNumerator,
        uint targetModeVSyncDenominator,
        int fallbackDevModeHz)
    {
        int targetHz = targetRateDenominator > 0
            ? (int)Math.Round((double)targetRateNumerator / targetRateDenominator)
            : 0;
        int physicalModeHz = targetModeVSyncDenominator > 0
            ? (int)Math.Round((double)targetModeVSyncNumerator / targetModeVSyncDenominator)
            : 0;

        bool boostFlag = (pathFlags & PathBoostRefreshRate) != 0;
        if (boostFlag)
        {
            int ceilingHz = physicalModeHz > 0
                ? physicalModeHz
                : (targetHz > 0 ? targetHz * 2 : Math.Max(60, fallbackDevModeHz));
            int baseHz = targetHz > 0 && targetHz < ceilingHz
                ? targetHz
                : Math.Max(30, ceilingHz / 2);
            return new DisplayRefreshModeInfo(ceilingHz, baseHz, true);
        }

        int fixedHz = physicalModeHz > 0
            ? physicalModeHz
            : (targetHz > 0 ? targetHz : fallbackDevModeHz);
        return new DisplayRefreshModeInfo(fixedHz, fixedHz, false);
    }

    public static int ResolveDynamicCeilingHz(
        IReadOnlyCollection<int>? supportedRates,
        bool currentlyDynamic,
        int currentRefreshHz)
    {
        if (supportedRates is { Count: > 0 })
        {
            int bestCeiling = 0;
            foreach (int hz in supportedRates)
            {
                if (hz >= 100 && hz % 2 == 0 && supportedRates.Contains(hz / 2) && hz > bestCeiling)
                {
                    bestCeiling = hz;
                }
            }
            if (bestCeiling > 0)
            {
                return bestCeiling;
            }
        }

        return currentlyDynamic && currentRefreshHz >= 100 ? currentRefreshHz : 0;
    }
}
