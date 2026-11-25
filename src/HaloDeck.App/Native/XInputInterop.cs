using System.Runtime.InteropServices;

namespace HaloDeck.App.Native;

[Flags]
public enum XInputButtons : ushort
{
    None = 0,
    DPadUp = 0x0001,
    DPadDown = 0x0002,
    DPadLeft = 0x0004,
    DPadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LeftThumb = 0x0040,
    RightThumb = 0x0080,
    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,
    Guide = 0x0400,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000
}

[StructLayout(LayoutKind.Sequential)]
internal struct XInputGamepad
{
    public ushort wButtons;
    public byte bLeftTrigger;
    public byte bRightTrigger;
    public short sThumbLX;
    public short sThumbLY;
    public short sThumbRX;
    public short sThumbRY;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XInputState
{
    public uint dwPacketNumber;
    public XInputGamepad Gamepad;
}

internal static class XInputInterop
{
    private const uint ERROR_SUCCESS = 0;

    // Undocumented ordinal #100 (XInputGetStateEx) exposes hidden bit 0x0400 (Guide / GPD Menu button).
    [DllImport("xinput1_4.dll", EntryPoint = "#100")]
    private static extern uint XInputGetStateEx_1_4(uint dwUserIndex, out XInputState pState);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState_1_4(uint dwUserIndex, out XInputState pState);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState_9_1_0(uint dwUserIndex, out XInputState pState);

    private static bool _useFallbackStandard14;
    private static bool _useFallback910;
    private static long _lastButtonOrTriggerTick64;
    private static readonly long[] DisconnectedRetryTick64 = new long[4];
    private const long DisconnectedBackoffMs = 3000;

    public static bool TryGetCombinedGamepadState(out XInputGamepad combined, out int connectedCount)
    {
        combined = default;
        connectedCount = 0;
        bool any = false;
        long nowTick = Environment.TickCount64;

        for (uint i = 0; i < 4; i++)
        {
            long retryAt = Interlocked.Read(ref DisconnectedRetryTick64[i]);
            if (retryAt > nowTick)
            {
                continue;
            }

            if (TryGetSingleState(i, out var state))
            {
                if (retryAt != 0)
                {
                    Interlocked.Exchange(ref DisconnectedRetryTick64[i], 0);
                }
                any = true;
                connectedCount++;
                combined.wButtons |= state.Gamepad.wButtons;
                combined.bLeftTrigger = Math.Max(combined.bLeftTrigger, state.Gamepad.bLeftTrigger);
                combined.bRightTrigger = Math.Max(combined.bRightTrigger, state.Gamepad.bRightTrigger);

                if (Math.Abs(state.Gamepad.sThumbLX) > Math.Abs(combined.sThumbLX))
                    combined.sThumbLX = state.Gamepad.sThumbLX;
                if (Math.Abs(state.Gamepad.sThumbLY) > Math.Abs(combined.sThumbLY))
                    combined.sThumbLY = state.Gamepad.sThumbLY;
                if (Math.Abs(state.Gamepad.sThumbRX) > Math.Abs(combined.sThumbRX))
                    combined.sThumbRX = state.Gamepad.sThumbRX;
                if (Math.Abs(state.Gamepad.sThumbRY) > Math.Abs(combined.sThumbRY))
                    combined.sThumbRY = state.Gamepad.sThumbRY;
            }
            else
            {
                // Avoid calling XInputGetState on empty slots at 55Hz (prevents periodic Windows HID enumeration stalls)
                Interlocked.Exchange(ref DisconnectedRetryTick64[i], nowTick + DisconnectedBackoffMs);
            }
        }

        if (any && (combined.wButtons != 0 || combined.bLeftTrigger > 30 || combined.bRightTrigger > 30))
        {
            Interlocked.Exchange(ref _lastButtonOrTriggerTick64, Environment.TickCount64);
        }

        return any;
    }

    public static long MillisecondsSinceLastButtonOrTrigger()
    {
        long last = Interlocked.Read(ref _lastButtonOrTriggerTick64);
        if (last <= 0) return long.MaxValue;
        return Math.Max(0, Environment.TickCount64 - last);
    }

    public static bool HasAnyActiveButtonOrTrigger(byte triggerDeadzone = 30)
    {
        if (!TryGetCombinedGamepadState(out var gp, out _))
        {
            return false;
        }
        return gp.wButtons != 0 || gp.bLeftTrigger > triggerDeadzone || gp.bRightTrigger > triggerDeadzone;
    }

    public static bool HasAnyActiveInput(short stickDeadzone = 18000, byte triggerDeadzone = 30)
    {
        if (!TryGetCombinedGamepadState(out var gp, out _))
        {
            return false;
        }
        return gp.wButtons != 0 ||
               gp.bLeftTrigger > triggerDeadzone ||
               gp.bRightTrigger > triggerDeadzone ||
               Math.Abs((int)gp.sThumbLX) > stickDeadzone ||
               Math.Abs((int)gp.sThumbLY) > stickDeadzone ||
               Math.Abs((int)gp.sThumbRX) > stickDeadzone ||
               Math.Abs((int)gp.sThumbRY) > stickDeadzone;
    }

    private static bool TryGetSingleState(uint userIndex, out XInputState state)
    {
        state = default;
        try
        {
            if (_useFallback910)
            {
                return XInputGetState_9_1_0(userIndex, out state) == ERROR_SUCCESS;
            }
            if (!_useFallbackStandard14)
            {
                try
                {
                    return XInputGetStateEx_1_4(userIndex, out state) == ERROR_SUCCESS;
                }
                catch (EntryPointNotFoundException)
                {
                    _useFallbackStandard14 = true;
                }
            }
            return XInputGetState_1_4(userIndex, out state) == ERROR_SUCCESS;
        }
        catch (DllNotFoundException)
        {
            if (!_useFallback910)
            {
                _useFallback910 = true;
                try
                {
                    return XInputGetState_9_1_0(userIndex, out state) == ERROR_SUCCESS;
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static XInputButtons ParseButtonName(string name) => name.Trim().ToUpperInvariant() switch
    {
        "UP" or "DPAD_UP" => XInputButtons.DPadUp,
        "DOWN" or "DPAD_DOWN" => XInputButtons.DPadDown,
        "LEFT" or "DPAD_LEFT" => XInputButtons.DPadLeft,
        "RIGHT" or "DPAD_RIGHT" => XInputButtons.DPadRight,
        "START" or "MENU" => XInputButtons.Start,
        "BACK" or "SELECT" or "VIEW" => XInputButtons.Back,
        "LS" or "L3" or "LEFT_THUMB" => XInputButtons.LeftThumb,
        "RS" or "R3" or "RIGHT_THUMB" => XInputButtons.RightThumb,
        "LB" or "L1" or "LEFT_SHOULDER" => XInputButtons.LeftShoulder,
        "RB" or "R1" or "RIGHT_SHOULDER" => XInputButtons.RightShoulder,
        "A" => XInputButtons.A,
        "B" => XInputButtons.B,
        "X" => XInputButtons.X,
        "Y" => XInputButtons.Y,
        _ => XInputButtons.None
    };

    public static XInputButtons CombineMask(IEnumerable<string> buttonNames)
    {
        XInputButtons mask = XInputButtons.None;
        foreach (var b in buttonNames)
        {
            mask |= ParseButtonName(b);
        }
        return mask;
    }

    public static List<string> ToButtonNames(XInputButtons mask)
    {
        var list = new List<string>();
        if (mask.HasFlag(XInputButtons.Back)) list.Add("BACK");
        if (mask.HasFlag(XInputButtons.Start)) list.Add("START");
        if (mask.HasFlag(XInputButtons.LeftShoulder)) list.Add("LB");
        if (mask.HasFlag(XInputButtons.RightShoulder)) list.Add("RB");
        if (mask.HasFlag(XInputButtons.LeftThumb)) list.Add("LS");
        if (mask.HasFlag(XInputButtons.RightThumb)) list.Add("RS");
        if (mask.HasFlag(XInputButtons.DPadUp)) list.Add("UP");
        if (mask.HasFlag(XInputButtons.DPadDown)) list.Add("DOWN");
        if (mask.HasFlag(XInputButtons.DPadLeft)) list.Add("LEFT");
        if (mask.HasFlag(XInputButtons.DPadRight)) list.Add("RIGHT");
        if (mask.HasFlag(XInputButtons.A)) list.Add("A");
        if (mask.HasFlag(XInputButtons.B)) list.Add("B");
        if (mask.HasFlag(XInputButtons.X)) list.Add("X");
        if (mask.HasFlag(XInputButtons.Y)) list.Add("Y");
        return list;
    }
}
