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
    public const uint WM_QUIT = 0x0012;
    public const uint WM_SYSCOMMAND = 0x0112;
    public const uint WM_APP = 0x8000;
    public const uint WM_APP_REINSTALL_KBD_HOOK = WM_APP + 0x41;
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
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int MA_NOACTIVATE = 3;
    public const uint GW_HWNDPREV = 3;
    public const int SW_RESTORE = 9;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOOWNERZORDER = 0x0200;
    public const uint SWP_NOSENDCHANGING = 0x0400;

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    public const int WM_POWERBROADCAST = 0x0218;
    public const uint PBT_APMSUSPEND = 0x0004;
    public const uint PBT_APMRESUMECRITICAL = 0x0006;
    public const uint PBT_APMRESUMESUSPEND = 0x0007;
    public const uint PBT_APMRESUMEAUTOMATIC = 0x0012;
    public const uint PBT_POWERSETTINGCHANGE = 0x8013;
    public const uint DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;
    public const uint DEVICE_NOTIFY_CALLBACK = 0x00000002;
    public static readonly Guid GUID_CONSOLE_DISPLAY_STATE = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    [StructLayout(LayoutKind.Sequential)]
    public struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public byte Data;
    }

    private const int DWMWA_CLOAKED = 14;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    public static readonly nint HWND_TOPMOST = new(-1);
    public static readonly nint HWND_NOTOPMOST = new(-2);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public delegate bool EnumWindowsProc(nint hWnd, nint lParam);
    public delegate void WinEventDelegate(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

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
    public static extern nint SendMessage(nint hWnd, int Msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern void SwitchToThisWindow(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool fAltTab);

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
    public static extern nint GetWindow(nint hWnd, uint uCmd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumChildWindows(nint hWndParent, EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    public static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(nint hWinEventHook);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint RegisterSuspendResumeNotification(nint hRecipient, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterSuspendResumeNotification(nint handle);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint RegisterPowerSettingNotification(nint hRecipient, ref Guid powerSettingGuid, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterPowerSettingNotification(nint handle);

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

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

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

    public static bool ForceForegroundOverlay(nint hwnd) => ForceForegroundWindow(hwnd, makeTopmost: true);

    public static bool ForceForegroundWindow(nint hwnd, bool makeTopmost)
    {
        if (hwnd == 0 || !IsWindow(hwnd)) return false;

        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        if ((ex & WS_EX_NOACTIVATE) != 0)
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, ex & ~WS_EX_NOACTIVATE);
        }

        if (makeTopmost)
        {
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        }

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

        // If summoned via XInput polling while an elevated/fullscreen game holds ForegroundLockTimeout,
        // emit inert VK 0xFF tap to unlock foreground permission cleanly without Alt/Menu side effects.
        if (GetForegroundWindow() != 0 && !IsWindowOrChildForeground(hwnd))
        {
            SendSyntheticChordBreak();
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            if (!IsWindowOrChildForeground(hwnd))
            {
                SwitchToThisWindow(hwnd, true);
            }
        }

        if (makeTopmost)
        {
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        }

        return IsWindowOrChildForeground(hwnd);
    }

    public static bool IsWindowOrChildForeground(nint hwnd)
    {
        if (hwnd == 0) return false;
        var fg = GetForegroundWindow();
        if (fg == 0) return false;
        return fg == hwnd || IsChild(hwnd, fg);
    }

    public static bool IsWindowCloaked(nint hwnd)
    {
        if (hwnd == 0) return false;
        try
        {
            return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool HasVisibleWindowAbove(nint targetHwnd, nint allowedAboveHwnd = 0)
    {
        if (targetHwnd == 0 || !IsWindow(targetHwnd)) return false;
        nint cur = GetWindow(targetHwnd, GW_HWNDPREV);
        int steps = 0;
        while (cur != 0 && steps++ < 64)
        {
            if (cur != allowedAboveHwnd &&
                IsWindowVisible(cur) &&
                !IsWindowCloaked(cur) &&
                GetWindowRect(cur, out var r) &&
                (r.Right - r.Left) > 1 &&
                (r.Bottom - r.Top) > 1)
            {
                return true;
            }
            cur = GetWindow(cur, GW_HWNDPREV);
        }
        return false;
    }

    /// <summary>
    /// Ultra-lightweight Z-order check (zero DWM calls) to detect if a visible overlapping window
    /// (such as a DX12 borderless/RE Engine game re-raising itself after splash/HDR switch) sits above the OSD.
    /// </summary>
    public static bool HasLightweightTopmostCover(nint targetHwnd, nint allowedAboveHwnd = 0)
    {
        if (targetHwnd == 0 || !IsWindow(targetHwnd) || !GetWindowRect(targetHwnd, out var targetRect))
        {
            return false;
        }

        nint cur = GetWindow(targetHwnd, GW_HWNDPREV);
        int steps = 0;
        while (cur != 0 && steps++ < 20)
        {
            if (cur != allowedAboveHwnd &&
                IsWindowVisible(cur) &&
                GetWindowRect(cur, out var r) &&
                (r.Right - r.Left) > 8 &&
                (r.Bottom - r.Top) > 8 &&
                r.Left < targetRect.Right &&
                r.Right > targetRect.Left &&
                r.Top < targetRect.Bottom &&
                r.Bottom > targetRect.Top)
            {
                return true;
            }
            cur = GetWindow(cur, GW_HWNDPREV);
        }
        return false;
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int pquns);

    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;

    public static bool IsD3DExclusiveFullscreenActive()
    {
        try
        {
            return SHQueryUserNotificationState(out int state) == 0 && state == QUNS_RUNNING_D3D_FULL_SCREEN;
        }
        catch
        {
            return false;
        }
    }

    public static void RaiseTopmostNoActivate(nint hwnd, bool forceReband = false, nint allowedAboveHwnd = 0)
    {
        if (hwnd == 0 || !IsWindow(hwnd)) return;

        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        if ((ex & WS_EX_TOPMOST) == 0)
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOPMOST);
        }

        const uint flags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOSENDCHANGING | SWP_SHOWWINDOW;
        if (forceReband)
        {
            SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOSENDCHANGING);
        }
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, flags);
    }

    public static void ApplyNoActivateOverlayStyle(nint hwnd)
    {
        if (hwnd == 0 || !IsWindow(hwnd)) return;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        int targetEx = ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
        if (ex != targetEx)
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, targetEx);
        }

        EnumChildWindows(hwnd, (child, _) =>
        {
            int childEx = GetWindowLong(child, GWL_EXSTYLE);
            if ((childEx & WS_EX_NOACTIVATE) == 0)
            {
                SetWindowLong(child, GWL_EXSTYLE, childEx | WS_EX_NOACTIVATE);
            }
            return true;
        }, 0);
    }

    public static void ApplyFocusableTopmostOverlayStyle(nint hwnd)
    {
        if (hwnd == 0 || !IsWindow(hwnd)) return;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        int targetEx = (ex | WS_EX_TOOLWINDOW | WS_EX_TOPMOST) & ~WS_EX_NOACTIVATE;
        if (ex != targetEx)
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, targetEx);
        }

        EnumChildWindows(hwnd, (child, _) =>
        {
            int childEx = GetWindowLong(child, GWL_EXSTYLE);
            if ((childEx & WS_EX_NOACTIVATE) != 0)
            {
                SetWindowLong(child, GWL_EXSTYLE, childEx & ~WS_EX_NOACTIVATE);
            }
            return true;
        }, 0);
    }

    public static void ParkWindowOffscreenNoActivate(nint hwnd)
    {
        if (hwnd == 0 || !IsWindow(hwnd)) return;
        SetWindowPos(
            hwnd,
            0,
            -32000,
            -32000,
            0,
            0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOSENDCHANGING);
    }

    public static void RestoreWindowFocusIfValid(nint hwnd)
    {
        if (hwnd == 0 || !IsWindow(hwnd)) return;
        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SW_RESTORE);
        }
        ForceForegroundWindow(hwnd, makeTopmost: false);
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClassNameW(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    public static string GetForegroundWindowClassName()
    {
        try
        {
            nint fg = GetForegroundWindow();
            if (fg == 0) return string.Empty;
            var sb = new StringBuilder(128);
            int len = GetClassNameW(fg, sb, sb.Capacity);
            return len > 0 ? sb.ToString(0, len) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessageW(nint hWnd, uint Msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PeekMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern nint DispatchMessageW(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostThreadMessageW(uint idThread, uint Msg, nint wParam, nint lParam);

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

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        nint tokenHandle,
        int tokenInformationClass,
        out uint tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

    private const uint TokenQuery = 0x0008;
    private const int TokenElevationClass = 20;
    private static readonly Lazy<bool> CachedSelfElevated = new(ComputeCurrentProcessElevated);

    public static bool IsCurrentProcessElevated() => CachedSelfElevated.Value;

    private static bool ComputeCurrentProcessElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsProcessElevated(uint pid)
    {
        if (pid <= 4) return false;
        if (pid == (uint)Environment.ProcessId) return IsCurrentProcessElevated();

        nint hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProc == 0)
        {
            // ERROR_ACCESS_DENIED (5) when querying a higher-integrity process from Medium IL
            return !IsCurrentProcessElevated() && Marshal.GetLastWin32Error() == 5;
        }

        try
        {
            if (!OpenProcessToken(hProc, TokenQuery, out nint hToken))
            {
                return !IsCurrentProcessElevated() && Marshal.GetLastWin32Error() == 5;
            }

            try
            {
                if (GetTokenInformation(hToken, TokenElevationClass, out uint elevated, sizeof(uint), out _))
                {
                    return elevated != 0;
                }
            }
            finally
            {
                CloseHandle(hToken);
            }
        }
        finally
        {
            CloseHandle(hProc);
        }

        return false;
    }

    public static bool IsWindowProcessElevated(nint hwnd)
    {
        if (hwnd == 0 || !IsWindow(hwnd)) return false;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return IsProcessElevated(pid);
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

    public const int WM_INPUT = 0x00FF;
    public const uint RIDEV_INPUTSINK = 0x00000100;
    public const uint RID_INPUT = 0x10000003;
    public const uint RIDI_DEVICEINFO = 0x2000000B;
    public const uint RIM_TYPEKEYBOARD = 1;
    public const uint RIM_TYPEHID = 2;
    public const ushort RI_KEY_BREAK = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public nint hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public nint hDevice;
        public nint wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWHID
    {
        public uint dwSizeHid;
        public uint dwCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RID_DEVICE_INFO_HID
    {
        public uint dwVendorId;
        public uint dwProductId;
        public uint dwVersionNumber;
        public ushort usUsagePage;
        public ushort usUsage;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct RID_DEVICE_INFO
    {
        [FieldOffset(0)] public uint cbSize;
        [FieldOffset(4)] public uint dwType;
        [FieldOffset(8)] public RID_DEVICE_INFO_HID hid;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterRawInputDevices(
        [In] RAWINPUTDEVICE[] pRawInputDevices,
        uint uiNumDevices,
        uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputData(
        nint hRawInput,
        uint uiCommand,
        nint pData,
        ref uint pcbSize,
        uint cbSizeHeader);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint GetRawInputDeviceInfoW(
        nint hDevice,
        uint uiCommand,
        nint pData,
        ref uint pcbSize);
}
