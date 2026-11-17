namespace HaloDeck.Core.Services;

public sealed record PowerRuntimeResult(
    double SystemDrawWatts,
    double? ChargerInputWatts,
    double ChargeWatts,
    double BatteryDischargeWatts,
    double NextEmaSystemDrawWatts,
    double NextSlowApuWatts,
    int? EstimatedStandbyMinutes,
    bool IsBypassPower,
    string RuntimeMode); // "charging" | "ac_sustained" | "ac_discharging" | "battery" | "no_battery"

/// <summary>
/// Pure helper fusing instantaneous APU telemetry with Smart Battery WMI rates to eliminate BMS lag
/// and model AC charging, bypass/sustained AC, AC reverse-discharging (weak charger), and pure battery runtime.
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
        double previousEmaSystemDrawWatts,
        bool isBatteryDischarging = false,
        double previousSlowApuWatts = -1)
    {
        double batW = Math.Max(0, batteryPowerWatts ?? 0);
        bool reverseDischargingOnAc = isAcOnline && hasBattery && isBatteryDischarging && batW > 0.35;
        bool batteryDischargingOffAc = !isAcOnline && hasBattery && (isBatteryDischarging || !isCharging) && batW > 0.35;
        bool activelyCharging = isAcOnline && hasBattery && !reverseDischargingOnAc && isCharging && batW > 0.35;
        double chargeWatts = activelyCharging ? Math.Round(batW, 1) : 0.0;

        double apu = Math.Max(1.5, apuPowerWatts);
        double boardBaseWatts = 1.1;
        double screenWatts = 0.6 + (2.0 * Math.Clamp(brightnessPercent, 5, 100) / 100.0);
        double fanWatts = fanRpm <= 0
            ? 0.35
            : Math.Clamp(0.2 + 1.8 * Math.Pow(fanRpm / 4500.0, 2.2), 0.2, 2.8);
        // Strix Halo DC-DC VRM conversion loss (~85% buck efficiency) + 256-bit LPDDR5X-8000 / NVMe load scaling
        double vrmLossWatts = apu * 0.18;
        double memLoadFactor = Math.Clamp((apu - 9.0) / 14.0, 0.0, 1.0);
        double memAndUncoreWatts = 5.2 * memLoadFactor;
        double apuModelWatts = Math.Round(apu + boardBaseWatts + screenWatts + fanWatts + vrmLossWatts + memAndUncoreWatts, 1);

        double prevSlowApu = previousSlowApuWatts >= 0 ? previousSlowApuWatts : apuPowerWatts;
        double nextSlowApu = apuPowerWatts > 0
            ? Math.Round(prevSlowApu * 0.78 + apuPowerWatts * 0.22, 2)
            : prevSlowApu;

        double systemDrawWatts;
        if (!isAcOnline && hasBattery && batteryDischargingOffAc && batW > 1.0)
        {
            if (apuPowerWatts > 0)
            {
                // Compensate Smart Battery BMS rolling average lag by anchoring non-APU overhead to slow APU baseline
                // and immediately adding instantaneous APU package watts (allow up to 32W overhead for 45W-85W VRM+RAM loss).
                const double minOverhead = 2.5;
                double calibratedOverhead = Math.Clamp(batW - prevSlowApu, minOverhead, 32.0);
                systemDrawWatts = Math.Round(apuPowerWatts + calibratedOverhead, 1);
            }
            else
            {
                systemDrawWatts = Math.Round(batW, 1);
            }
        }
        else
        {
            systemDrawWatts = apuModelWatts;
        }

        if (!hasBattery)
        {
            return new PowerRuntimeResult(
                SystemDrawWatts: systemDrawWatts,
                ChargerInputWatts: isAcOnline ? systemDrawWatts : null,
                ChargeWatts: 0.0,
                BatteryDischargeWatts: 0.0,
                NextEmaSystemDrawWatts: 0.0,
                NextSlowApuWatts: nextSlowApu,
                EstimatedStandbyMinutes: null,
                IsBypassPower: isAcOnline,
                RuntimeMode: "no_battery");
        }

        if (isAcOnline && reverseDischargingOnAc)
        {
            double disW = Math.Round(batW, 1);
            double chargerInputW = Math.Round(Math.Max(0.0, systemDrawWatts - disW), 1);
            double nextEma = SmoothDischargeEma(disW, previousEmaSystemDrawWatts);
            int? standbyMinutes = ComputeRemainingMinutes(batteryRemainingWh, nextEma);

            return new PowerRuntimeResult(
                SystemDrawWatts: systemDrawWatts,
                ChargerInputWatts: chargerInputW,
                ChargeWatts: 0.0,
                BatteryDischargeWatts: disW,
                NextEmaSystemDrawWatts: nextEma,
                NextSlowApuWatts: nextSlowApu,
                EstimatedStandbyMinutes: standbyMinutes,
                IsBypassPower: false,
                RuntimeMode: "ac_discharging");
        }

        if (isAcOnline && activelyCharging)
        {
            double chargerInputW = Math.Round(systemDrawWatts + chargeWatts, 1);
            return new PowerRuntimeResult(
                SystemDrawWatts: systemDrawWatts,
                ChargerInputWatts: chargerInputW,
                ChargeWatts: chargeWatts,
                BatteryDischargeWatts: 0.0,
                NextEmaSystemDrawWatts: 0.0,
                NextSlowApuWatts: nextSlowApu,
                EstimatedStandbyMinutes: null,
                IsBypassPower: false,
                RuntimeMode: "charging");
        }

        if (isAcOnline)
        {
            bool isBypass = batteryPercent >= 94 || chargeWatts < 0.4;
            return new PowerRuntimeResult(
                SystemDrawWatts: systemDrawWatts,
                ChargerInputWatts: systemDrawWatts,
                ChargeWatts: 0.0,
                BatteryDischargeWatts: 0.0,
                NextEmaSystemDrawWatts: 0.0,
                NextSlowApuWatts: nextSlowApu,
                EstimatedStandbyMinutes: null,
                IsBypassPower: isBypass,
                RuntimeMode: "ac_sustained");
        }

        double batDisW = systemDrawWatts;
        double batNextEma = SmoothDischargeEma(batDisW, previousEmaSystemDrawWatts);
        int? batStandbyMinutes = ComputeRemainingMinutes(batteryRemainingWh, batNextEma);

        return new PowerRuntimeResult(
            SystemDrawWatts: systemDrawWatts,
            ChargerInputWatts: null,
            ChargeWatts: 0.0,
            BatteryDischargeWatts: batDisW,
            NextEmaSystemDrawWatts: batNextEma,
            NextSlowApuWatts: nextSlowApu,
            EstimatedStandbyMinutes: batStandbyMinutes,
            IsBypassPower: false,
            RuntimeMode: "battery");
    }

    private static double SmoothDischargeEma(double currentWatts, double previousEma)
    {
        if (previousEma <= 0.5)
        {
            return currentWatts;
        }
        double alpha = Math.Abs(currentWatts - previousEma) > 8.0 ? 0.65 : 0.42;
        return Math.Round(previousEma * (1.0 - alpha) + currentWatts * alpha, 2);
    }

    private static int? ComputeRemainingMinutes(double remainingWh, double emaDischargeWatts)
    {
        if (remainingWh <= 0.2)
        {
            return null;
        }
        double denomW = Math.Max(1.0, emaDischargeWatts);
        return Math.Clamp((int)Math.Round(remainingWh / denomW * 60.0), 1, 2400);
    }
}
