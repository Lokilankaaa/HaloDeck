using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using HaloDeck.Core.Process;

namespace HaloDeck.App.Native;

internal static class Win32Interop
{
    public const int WM_HOTKEY = 0x0312;
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_SYSCOMMAND = 0x0112;
    public const nint SC_CLOSE = 0xF060;
    public const uint LLKHF_INJECTED = 0x10;

    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_LCONTROL = 0xA2;
    public const ushort VK_RCONTROL = 0xA3;
    public const ushort VK_LWIN = 0x5B;
    public const ushort VK_RWIN = 0x5C;
    public const ushort VK_O = 0x4F;
    public const nuint SyntheticChordBreakExtraInfo = 0x48444B42; // "HDKB"

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    public const uint INPUT_MOUSE = 0;
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_SCANCODE = 0x0008;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsChild(nint hWndParent, nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(nint hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    public static void EnableRoundedCorners(nint hwnd)
    {
        if (hwnd == 0) return;
        try
        {
            int pref = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch
        {
            // Ignore on older Windows builds
        }
    }

    public static bool ForceForegroundOverlay(nint hwnd)
    {
        if (hwnd == 0 || !IsWindow(hwnd)) return false;

        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_SHOWWINDOW = 0x0040;
        nint hwndTopMost = new(-1);

        SetWindowPos(hwnd, hwndTopMost, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

        var fg = GetForegroundWindow();
        uint currentTid = GetCurrentThreadId();
        uint fgTid = fg != 0 ? GetWindowThreadProcessId(fg, out _) : 0;
        bool attached = false;

        try
        {
            if (fgTid != 0 && fgTid != currentTid)
            {
                attached = AttachThreadInput(currentTid, fgTid, true);
            }

            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(currentTid, fgTid, false);
            }
        }

        SetWindowPos(hwnd, hwndTopMost, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        return IsWindowOrChildForeground(hwnd);
    }

    public static bool IsWindowOrChildForeground(nint hwnd)
    {
        if (hwnd == 0) return false;
        var fg = GetForegroundWindow();
        if (fg == 0) return false;
        return fg == hwnd || IsChild(hwnd, fg);
    }

    public static ForegroundProcessSnapshot? GetForegroundProcessSnapshot()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == 0) return null;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return null;

            var exePath = QueryProcessPath(pid);
            if (string.IsNullOrWhiteSpace(exePath))
            {
                using var p = Process.GetProcessById((int)pid);
                exePath = p.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    return null;
                }
            }

            var procName = Path.GetFileNameWithoutExtension(exePath);
            return new ForegroundProcessSnapshot((int)pid, hwnd, procName, exePath);
        }
        catch
        {
            return null;
        }
    }

    private static string? QueryProcessPath(uint pid)
    {
        nint hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProc == 0) return null;
        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            return QueryFullProcessImageNameW(hProc, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally
        {
            CloseHandle(hProc);
        }
    }

    /// <summary>
    /// Synthesizes a key press with hardware scan code + extended key flag and holds it
    /// for <paramref name="holdDurationMs"/> ms so DirectX/ImGui games reliably catch it on poll.
    /// </summary>
    public static async Task SendKeyHoldAsync(ushort virtualKey, int holdDurationMs = 65)
    {
        uint scanCode = MapVirtualKeyW(virtualKey, 0);
        uint flags = IsExtendedKey(virtualKey) ? KEYEVENTF_EXTENDEDKEY : 0;

        var down = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = (ushort)scanCode,
                    dwFlags = flags
                }
            }
        };

        var up = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = (ushort)scanCode,
                    dwFlags = flags | KEYEVENTF_KEYUP
                }
            }
        };

        SendInput(1, new[] { down }, Marshal.SizeOf<INPUT>());
        await Task.Delay(Math.Clamp(holdDurationMs, 30, 200)).ConfigureAwait(false);
        SendInput(1, new[] { up }, Marshal.SizeOf<INPUT>());
    }

    [DllImport("user32.dll")]
    public static extern nint GetDesktopWindow();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetWindowsHookExW(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint FindWindowW(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessageW(nint hWnd, uint Msg, nint wParam, nint lParam);

    public static void SendSyntheticChordBreak()
    {
        var inputs = new INPUT[]
        {
            new() { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = 0xFF, dwExtraInfo = SyntheticChordBreakExtraInfo } } },
            new() { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = 0xFF, dwFlags = KEYEVENTF_KEYUP, dwExtraInfo = SyntheticChordBreakExtraInfo } } }
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public static void SendStandaloneCtrlTap()
    {
        var inputs = new INPUT[]
        {
            new() { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CONTROL, dwExtraInfo = SyntheticChordBreakExtraInfo } } },
            new() { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CONTROL, dwFlags = KEYEVENTF_KEYUP, dwExtraInfo = SyntheticChordBreakExtraInfo } } }
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public static void SendWinChord(ushort vk)
    {
        const ushort vkLWin = 0x5B;
        var inputs = new INPUT[]
        {
            new() { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vkLWin } } },
            new() { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk } } },
            new() { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP } } },
            new() { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vkLWin, dwFlags = KEYEVENTF_KEYUP } } }
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public static void SendWinCtrlChord(ushort vk)
    {
        const ushort vkLWin = 0x5B;
        const ushort vkLControl = 0xA2;
        var inputs = new INPUT[]
        {
            new() { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vkLWin } } },
            new() { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vkLControl } } },
            new() { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk } } },
            new() { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP } } },
            new() { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vkLControl, dwFlags = KEYEVENTF_KEYUP } } },
            new() { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vkLWin, dwFlags = KEYEVENTF_KEYUP } } }
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public static void SendMouseMove(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = MOUSEEVENTF_MOVE }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public static void SendMouseButton(bool isLeft, bool isDown)
    {
        uint flag = isLeft
            ? (isDown ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP)
            : (isDown ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP);
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion { mi = new MOUSEINPUT { dwFlags = flag } }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public static void SendMouseWheel(int wheelDelta)
    {
        if (wheelDelta == 0) return;
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT { mouseData = unchecked((uint)wheelDelta), dwFlags = MOUSEEVENTF_WHEEL }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static bool IsExtendedKey(ushort vk) => vk is
        0x2D or // VK_INSERT
        0x2E or // VK_DELETE
        0x24 or // VK_HOME
        0x23 or // VK_END
        0x21 or // VK_PRIOR (PageUp)
        0x22 or // VK_NEXT (PageDown)
        0x25 or 0x26 or 0x27 or 0x28; // Arrow keys

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public static object GetSystemMemoryHealth()
    {
        var stat = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref stat))
        {
            return new
            {
                totalRamGb = 0.0,
                availRamGb = 0.0,
                commitLimitGb = 0.0,
                commitAvailGb = 0.0,
                lowCommitHeadroom = false
            };
        }

        const double gb = 1024.0 * 1024.0 * 1024.0;
        double totalRamGb = Math.Round(stat.ullTotalPhys / gb, 2);
        double availRamGb = Math.Round(stat.ullAvailPhys / gb, 2);
        double commitLimitGb = Math.Round(stat.ullTotalPageFile / gb, 2);
        double commitAvailGb = Math.Round(stat.ullAvailPageFile / gb, 2);
        bool lowCommitHeadroom = (totalRamGb <= 20.0 && commitLimitGb < 38.0) || commitAvailGb < 8.0;

        return new
        {
            totalRamGb,
            availRamGb,
            commitLimitGb,
            commitAvailGb,
            lowCommitHeadroom
        };
    }
}
