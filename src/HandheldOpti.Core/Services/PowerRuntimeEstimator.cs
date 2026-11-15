namespace HandheldOpti.Core.Services;

public sealed record PowerRuntimeResult(
    double SystemDrawWatts,
    double? ChargerInputWatts,
    double ChargeWatts,
    double NextEmaSystemDrawWatts,
    int? EstimatedStandbyMinutes,
    bool IsBypassPower);

/// <summary>
/// Pure helper separating whole-system operating consumption (excluding battery charge rate)
/// from total charger wall/PD input, and estimating EMA-smoothed remaining runtime in minutes.
/// </summary>
public static class PowerRuntimeEstimator
{
    public static PowerRuntimeResult Compute(
        bool isAcOnline,
        bool hasBattery,
        bool isCharging,
        int batteryPercent,
        double? batteryPowerWatts,
        double batteryRemainingWh,
        int apuPowerWatts,
        int brightnessPercent,
        int fanRpm,
        double previousEmaSystemDrawWatts)
    {
        double batW = Math.Max(0, batteryPowerWatts ?? 0);
        double chargeWatts = (isAcOnline && hasBattery && isCharging) ? batW : 0.0;

        double systemDrawWatts;
        if (!isAcOnline && hasBattery && !isCharging && batW > 1.0)
        {
            // Real physical Smart Battery discharge rate includes screen, board, VRM, fans, SSD, and APU.
            systemDrawWatts = Math.Round(batW, 1);
        }
        else
        {
            // Whole-system operating draw on AC excludes battery charging current.
            double screenAndBoardLoss = 3.2
                + (2.2 * Math.Clamp(brightnessPercent, 5, 100) / 100.0)
                + (fanRpm > 3200 ? 0.8 : 0.3);
            systemDrawWatts = Math.Round(Math.Max(1.5, apuPowerWatts) + screenAndBoardLoss, 1);
        }

        double? chargerInputWatts = null;
        bool isBypass = isAcOnline && !hasBattery;
        if (isAcOnline)
        {
            if (hasBattery)
            {
                isBypass = batteryPercent >= 94 && chargeWatts < 0.5;
            }
            chargerInputWatts = Math.Round(systemDrawWatts + chargeWatts, 1);
        }

        double nextEma;
        if (previousEmaSystemDrawWatts <= 0.5)
        {
            nextEma = systemDrawWatts;
        }
        else
        {
            double alpha = Math.Abs(systemDrawWatts - previousEmaSystemDrawWatts) > 12.0 ? 0.55 : 0.35;
            nextEma = Math.Round(previousEmaSystemDrawWatts * (1.0 - alpha) + systemDrawWatts * alpha, 2);
        }

        int? standbyMinutes = null;
        if (hasBattery && batteryRemainingWh > 0.2)
        {
            double denomW = Math.Max(2.0, nextEma);
            standbyMinutes = Math.Clamp((int)Math.Round(batteryRemainingWh / denomW * 60.0), 1, 2400);
        }

        return new PowerRuntimeResult(
            SystemDrawWatts: systemDrawWatts,
            ChargerInputWatts: chargerInputWatts,
            ChargeWatts: Math.Round(chargeWatts, 1),
            NextEmaSystemDrawWatts: nextEma,
            EstimatedStandbyMinutes: standbyMinutes,
            IsBypassPower: isBypass);
    }
}
