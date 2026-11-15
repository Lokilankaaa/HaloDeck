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

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState_1_4(uint dwUserIndex, out XInputState pState);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState_9_1_0(uint dwUserIndex, out XInputState pState);

    private static bool _useFallback910;

    public static bool TryGetCombinedGamepadState(out XInputGamepad combined, out int connectedCount)
    {
        combined = default;
        connectedCount = 0;
        bool any = false;

        for (uint i = 0; i < 4; i++)
        {
            if (TryGetSingleState(i, out var state))
            {
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
        }

        return any;
    }

    private static bool TryGetSingleState(uint userIndex, out XInputState state)
    {
        state = default;
        try
        {
            uint res = _useFallback910
                ? XInputGetState_9_1_0(userIndex, out state)
                : XInputGetState_1_4(userIndex, out state);
            return res == ERROR_SUCCESS;
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
