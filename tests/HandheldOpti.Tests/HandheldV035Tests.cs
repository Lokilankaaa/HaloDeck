using System.Buffers.Binary;
using HandheldOpti.Core.Models;
using HandheldOpti.Core.Services;
using Xunit;

namespace HandheldOpti.Tests;

public sealed class HandheldV035Tests
{
    [Fact]
    public void PowerRuntimeEstimator_SeparatesSystemDrawFromAcChargingRate()
    {
        // On AC with 35W APU and 18W flowing into the battery:
        // whole-system draw must NOT include battery charging power, while ChargerInputWatts captures both.
        var res = PowerRuntimeEstimator.Compute(
            isAcOnline: true,
            hasBattery: true,
            isCharging: true,
            batteryPercent: 60,
            batteryPowerWatts: 18.0,
            batteryRemainingWh: 48.0,
            apuPowerWatts: 35,
            brightnessPercent: 70,
            fanRpm: 3000,
            previousEmaSystemDrawWatts: 0);

        Assert.InRange(res.SystemDrawWatts, 40.0, 44.0);
        Assert.Equal(18.0, res.ChargeWatts);
        Assert.NotNull(res.ChargerInputWatts);
        Assert.InRange(res.ChargerInputWatts!.Value, 58.0, 62.0);
        Assert.Null(res.EstimatedStandbyMinutes);
        Assert.Equal("charging", res.RuntimeMode);
        Assert.False(res.IsBypassPower);
    }

    [Fact]
    public void PowerRuntimeEstimator_ComputesExactBatteryDischargeRuntime()
    {
        // On battery discharging at real Smart Battery 24W with 48Wh remaining => 120 minutes runtime.
        var res = PowerRuntimeEstimator.Compute(
            isAcOnline: false,
            hasBattery: true,
            isCharging: false,
            batteryPercent: 60,
            batteryPowerWatts: 24.0,
            batteryRemainingWh: 48.0,
            apuPowerWatts: 18,
            brightnessPercent: 50,
            fanRpm: 2200,
            previousEmaSystemDrawWatts: 24.0);

        Assert.Equal(24.0, res.SystemDrawWatts);
        Assert.Null(res.ChargerInputWatts);
        Assert.Equal(0.0, res.ChargeWatts);
        Assert.Equal(120, res.EstimatedStandbyMinutes);
        Assert.False(res.IsBypassPower);
    }

    [Fact]
    public void GpdV2PacketCodec_EncodesAndDecodesGamepadAndKeyboardCombos()
    {
        var buf = new byte[GpdV2PacketCodec.ConfigBufferSize];
        // Initialize a valid header magic as V2 hardware returns
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0, 2), 0x55AA);

        var req = new GpdControllerUpdateRequest
        {
            L4ComboCodes = [0x800B, 0x800C],         // Gamepad LB + RB
            R4ComboCodes = [0x0012, 0x00E1, 0x00E0], // Intentionally unordered O + LShift + LCtrl
            RumbleLevel = 2,
            LeftDeadzone = -2,
            RightDeadzone = 1
        };

        GpdV2PacketCodec.ApplyUpdateInPlace(buf, req);

        Assert.True(GpdV2PacketCodec.VerifyChecksum(buf));

        // Verify R4 normalized modifiers-first order and wrapper hold timings (130ms / 15ms delay)
        int r4SlotBase = GpdV2PacketCodec.HeaderSize + 180 * 2;
        ushort slot0Code = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(r4SlotBase, 2));
        ushort slot0Delay = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(r4SlotBase + 2, 2));
        ushort slot0Hold = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(r4SlotBase + 4, 2));
        ushort slot2Code = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(r4SlotBase + 12, 2));
        ushort slot2Delay = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(r4SlotBase + 14, 2));
        ushort slot2Hold = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(r4SlotBase + 16, 2));

        Assert.Equal((ushort)0x00E1, slot0Code);
        Assert.Equal((ushort)0, slot0Delay);
        Assert.Equal((ushort)130, slot0Hold);
        Assert.Equal((ushort)0x0012, slot2Code);
        Assert.Equal((ushort)15, slot2Delay);
        Assert.Equal((ushort)95, slot2Hold);

        var decoded = GpdV2PacketCodec.Decode(buf, "1.6", "2.0");
        Assert.Equal(2, decoded.L4Mode);
        Assert.Equal(2, decoded.L4Slots);
        Assert.Equal(new ushort[] { 0x800B, 0x800C }, decoded.L4ComboCodes);
        Assert.Equal("多键组合宏 (手柄LB + 手柄RB)", decoded.L4KeyLabel);

        Assert.Equal(2, decoded.R4Mode);
        Assert.Equal(3, decoded.R4Slots);
        Assert.Equal(new ushort[] { 0x00E1, 0x00E0, 0x0012 }, decoded.R4ComboCodes);
        Assert.Equal("多键组合宏 (Shift + Ctrl + O)", decoded.R4KeyLabel);
        Assert.Equal(2, decoded.RumbleLevel);
        Assert.Equal(-2, decoded.LeftDeadzone);
        Assert.Equal(1, decoded.RightDeadzone);
    }
}
