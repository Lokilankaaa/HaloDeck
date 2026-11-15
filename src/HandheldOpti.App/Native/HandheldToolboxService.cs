using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace HandheldOpti.App.Native;

public sealed class FrozenGameRecord
{
    public int Pid { get; set; }
    public string GameName { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public DateTime FrozenAtUtc { get; set; }
}

/// <summary>
/// Non-admin handheld quick toolbox + "Game Fridge" (NtSuspendProcess / NtResumeProcess)
/// for single-player games, with guaranteed resume-all cleanup on app exit.
/// </summary>
public sealed class HandheldToolboxService : IDisposable
{
    private const uint ProcessSuspendResume = 0x0800;
    public const string GpdKeyboardExePath = @"C:\Program Files\GPD\GPDKeyboard.exe";
    public static bool IsGpdXboxKeyboardAvailable => File.Exists(GetTabTipExePath());

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(nint processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(nint processHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint hObject);

    private readonly object _lock = new();
    private readonly Dictionary<int, FrozenGameRecord> _frozenProcesses = new();

    public IReadOnlyList<FrozenGameRecord> GetFrozenGames()
    {
        lock (_lock)
        {
            PruneExitedLocked();
            return _frozenProcesses.Values.ToList();
        }
    }

    public string ExecuteAction(
        string action,
        int targetPid = 0,
        string? targetGameName = null,
        bool hasAntiCheat = false,
        string preferredOskMode = "tabtip",
        bool fromUiClick = false,
        bool? forceCloseOsk = null)
    {
        switch ((action ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "show_desktop":
                Win32Interop.SendWinChord(0x44); // Win + D
                return "已切换显示桌面 (Win + D)";

            case "task_view":
                Win32Interop.SendWinChord(0x09); // Win + Tab
                return "已打开多任务视图 (Win + Tab)";

            case "osk":
                return ToggleOnScreenKeyboard(preferredOskMode, fromUiClick, forceCloseOsk);

            case "screenshot":
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "ms-screenclip:", UseShellExecute = true });
                return "已启动系统区域截图工具";

            case "taskmgr":
                Process.Start(new ProcessStartInfo { FileName = "taskmgr.exe", UseShellExecute = true });
                return "已打开 Windows 任务管理器";

            case "kill_game":
                return KillDetectedGameProcess(targetPid, targetGameName);

            case "toggle_fridge":
                return ToggleGameFridge(targetPid, targetGameName ?? "当前游戏", hasAntiCheat);

            case "resume_all_fridge":
            {
                int resumed = ResumeAllFrozenProcesses();
                return resumed > 0 ? $"已从游戏冰箱解冻恢复 {resumed} 个进程" : "当前没有处于冻结状态的游戏进程";
            }

            default:
                throw new ArgumentException($"未知快捷工具操作: {action}", nameof(action));
        }
    }

    public string ToggleGameFridge(int pid, string gameName, bool hasAntiCheat)
    {
        lock (_lock)
        {
            PruneExitedLocked();

            if (pid > 0 && _frozenProcesses.TryGetValue(pid, out var existing))
            {
                ResumeSingleProcessLocked(pid, existing.ProcessName);
                _frozenProcesses.Remove(pid);
                return $"已解冻恢复「{existing.GameName}」继续运行";
            }

            // If user clicked fridge with pid=0 while a frozen game exists, resume the most recent frozen game
            if (pid <= 0 && _frozenProcesses.Count > 0)
            {
                var first = _frozenProcesses.Values.First();
                ResumeSingleProcessLocked(first.Pid, first.ProcessName);
                _frozenProcesses.Remove(first.Pid);
                return $"已解冻恢复「{first.GameName}」继续运行";
            }

            if (pid <= 0 || pid == Environment.ProcessId)
            {
                throw new InvalidOperationException("当前未检测到可冻结的运行中游戏进程。");
            }

            if (hasAntiCheat)
            {
                throw new InvalidOperationException("检测到反作弊保护，已禁用「游戏冰箱」挂起进程以防掉线或封禁。");
            }

            using var proc = Process.GetProcessById(pid);
            string procName = proc.ProcessName;
            if (IsProtectedSystemProcess(procName))
            {
                throw new InvalidOperationException($"安全保护：拒绝冻结系统进程 {procName}。");
            }

            nint hProc = OpenProcess(ProcessSuspendResume, false, pid);
            if (hProc == 0)
            {
                throw new InvalidOperationException($"无法获取进程挂起句柄 (Win32={Marshal.GetLastWin32Error()})。");
            }

            try
            {
                int st = NtSuspendProcess(hProc);
                if (st < 0)
                {
                    throw new InvalidOperationException($"挂起游戏进程失败 (NTSTATUS=0x{st:X8})。");
                }
            }
            finally
            {
                CloseHandle(hProc);
            }

            _frozenProcesses[pid] = new FrozenGameRecord
            {
                Pid = pid,
                GameName = gameName,
                ProcessName = procName,
                FrozenAtUtc = DateTime.UtcNow
            };
            return $"❄️ 已冻结挂起「{gameName}」（功耗降为 0，再次点击即可秒恢复）";
        }
    }

    public int ResumeAllFrozenProcesses()
    {
        int count = 0;
        lock (_lock)
        {
            foreach (var item in _frozenProcesses.Values.ToList())
            {
                if (ResumeSingleProcessLocked(item.Pid, item.ProcessName))
                {
                    count++;
                }
            }
            _frozenProcesses.Clear();
        }
        return count;
    }

    private static bool ResumeSingleProcessLocked(int pid, string expectedProcessName)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            if (!string.Equals(proc.ProcessName, expectedProcessName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            nint hProc = OpenProcess(ProcessSuspendResume, false, pid);
            if (hProc == 0) return false;
            try
            {
                return NtResumeProcess(hProc) >= 0;
            }
            finally
            {
                CloseHandle(hProc);
            }
        }
        catch
        {
            return false;
        }
    }

    private void PruneExitedLocked()
    {
        foreach (var kv in _frozenProcesses.ToList())
        {
            try
            {
                using var p = Process.GetProcessById(kv.Key);
                if (p.HasExited || !string.Equals(p.ProcessName, kv.Value.ProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    _frozenProcesses.Remove(kv.Key);
                }
            }
            catch
            {
                _frozenProcesses.Remove(kv.Key);
            }
        }
    }

    private string KillDetectedGameProcess(int pid, string? gameName)
    {
        if (pid <= 0 || pid == Environment.ProcessId)
        {
            throw new InvalidOperationException("当前没有正在运行的受控游戏进程可结束。");
        }

        using var proc = Process.GetProcessById(pid);
        if (IsProtectedSystemProcess(proc.ProcessName))
        {
            throw new InvalidOperationException($"安全保护：拒绝强制结束系统进程 {proc.ProcessName}。");
        }

        lock (_lock)
        {
            _frozenProcesses.Remove(pid);
        }

        proc.Kill(entireProcessTree: false);
        return $"已强制结束游戏进程「{gameName ?? proc.ProcessName}」";
    }

    private static bool IsProtectedSystemProcess(string name) =>
        string.Equals(name, "explorer", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "dwm", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "csrss", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "winlogon", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "svchost", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "HaloDeck", StringComparison.OrdinalIgnoreCase);

    private static readonly Guid ClsidUiHostNoLaunch = new("4CE576FA-83DC-4F88-951C-9D0782B4E376");
    private static readonly Guid ClsidFrameworkInputPane = new("D5120AA3-46BA-44C5-822D-CA8092C1FC72");

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public bool IsVisible => (Right - Left) > 0 && (Bottom - Top) > 0;
    }

    [ComImport]
    [Guid("37C994E7-432B-4834-A2F7-DCE1F13B834B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITipInvocation
    {
        [PreserveSig]
        int Toggle(nint hwnd);
    }

    [ComImport]
    [Guid("226C537B-1E76-4D9E-A760-33DB29922F18")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFrameworkInputPaneHandler
    {
        [PreserveSig]
        int Showing(ref NativeRect prcInputPaneScreenLocation, [MarshalAs(UnmanagedType.Bool)] bool fEnsureFocusedElementInView);

        [PreserveSig]
        int Hiding([MarshalAs(UnmanagedType.Bool)] bool fEnsureFocusedElementInView);
    }

    [ComImport]
    [Guid("5752238B-24F0-495A-82F1-2FD593056796")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFrameworkInputPane
    {
        [PreserveSig]
        int Advise([MarshalAs(UnmanagedType.IUnknown)] object pWindow, IFrameworkInputPaneHandler pHandler, out uint pdwCookie);

        [PreserveSig]
        int AdviseWithHWND(nint hwnd, IFrameworkInputPaneHandler pHandler, out uint pdwCookie);

        [PreserveSig]
        int Unadvise(uint dwCookie);

        [PreserveSig]
        int Location(out NativeRect prcInputPaneScreenLocation);
    }

    [ClassInterface(ClassInterfaceType.None)]
    private sealed class InputPaneSink : IFrameworkInputPaneHandler
    {
        public int Showing(ref NativeRect prcInputPaneScreenLocation, bool fEnsureFocusedElementInView)
        {
            _autoHideConsumed = false;
            return 0;
        }

        public int Hiding(bool fEnsureFocusedElementInView)
        {
            Interlocked.Exchange(ref _lastInputPaneHiddenTicks, Environment.TickCount64);
            return 0;
        }
    }

    private static IFrameworkInputPane? _watchedPane;
    private static uint _watchCookie;
    private static InputPaneSink? _watchSink;
    private static long _lastInputPaneHiddenTicks;
    private static volatile bool _autoHideConsumed = true;
    private static int _oskToggleGate;

    public static void AttachInputPaneWatcher(nint hwnd)
    {
        try
        {
            DetachInputPaneWatcher();
            if (hwnd == 0) return;

            var comType = Type.GetTypeFromCLSID(ClsidFrameworkInputPane, throwOnError: false);
            if (comType is null) return;

            var obj = Activator.CreateInstance(comType);
            if (obj is IFrameworkInputPane pane)
            {
                var sink = new InputPaneSink();
                if (pane.AdviseWithHWND(hwnd, sink, out uint cookie) >= 0)
                {
                    _watchedPane = pane;
                    _watchSink = sink;
                    _watchCookie = cookie;
                    return;
                }
            }

            if (obj is not null && Marshal.IsComObject(obj))
            {
                try { Marshal.FinalReleaseComObject(obj); } catch { }
            }
        }
        catch
        {
            // Non-fatal: QueryTabTipVisible() still queries IFrameworkInputPane::Location directly
        }
    }

    public static void DetachInputPaneWatcher()
    {
        try
        {
            if (_watchedPane is not null)
            {
                if (_watchCookie != 0)
                {
                    try { _watchedPane.Unadvise(_watchCookie); } catch { }
                }
                if (Marshal.IsComObject(_watchedPane))
                {
                    try { Marshal.FinalReleaseComObject(_watchedPane); } catch { }
                }
            }
        }
        catch
        {
            // Ignore shutdown cleanup errors
        }
        finally
        {
            _watchedPane = null;
            _watchSink = null;
            _watchCookie = 0;
        }
    }

    public static bool QueryTabTipVisible()
    {
        bool visible = false;
        RunSta(() =>
        {
            object? obj = null;
            try
            {
                var t = Type.GetTypeFromCLSID(ClsidFrameworkInputPane, throwOnError: false);
                if (t is null) return;

                obj = Activator.CreateInstance(t);
                if (obj is IFrameworkInputPane pane && pane.Location(out var rc) >= 0)
                {
                    visible = rc.IsVisible;
                }
            }
            catch
            {
                visible = false;
            }
            finally
            {
                if (obj is not null && Marshal.IsComObject(obj))
                {
                    try { Marshal.FinalReleaseComObject(obj); } catch { }
                }
            }
        });
        return visible;
    }

    public static bool ShouldCloseOnUiClick()
    {
        if (QueryTabTipVisible())
        {
            return true;
        }

        nint oskHwnd = Win32Interop.FindWindowW("OSKMainClass", null);
        if (oskHwnd != 0 && Win32Interop.IsWindowVisible(oskHwnd))
        {
            return true;
        }

        if (!_autoHideConsumed)
        {
            long elapsed = Environment.TickCount64 - Interlocked.Read(ref _lastInputPaneHiddenTicks);
            if (elapsed is >= 0 and < 650)
            {
                return true;
            }
        }

        return false;
    }

    public static string ToggleOnScreenKeyboard(string preferredMode = "tabtip", bool fromUiClick = false, bool? forceClose = null)
    {
        if (Interlocked.CompareExchange(ref _oskToggleGate, 1, 0) != 0)
        {
            return "屏幕键盘切换中";
        }

        try
        {
            string mode = (preferredMode ?? "tabtip").Trim().ToLowerInvariant();
            if (mode == "osk")
            {
                nint oskHwnd = Win32Interop.FindWindowW("OSKMainClass", null);
                if (oskHwnd != 0 && Win32Interop.IsWindowVisible(oskHwnd))
                {
                    Win32Interop.PostMessageW(oskHwnd, Win32Interop.WM_SYSCOMMAND, Win32Interop.SC_CLOSE, 0);
                    Win32Interop.PostMessageW(oskHwnd, Win32Interop.WM_CLOSE, 0, 0);
                    return "已收起传统屏幕键盘";
                }

                Process.Start(new ProcessStartInfo { FileName = "osk.exe", UseShellExecute = true });
                return "已启动传统 Windows 屏幕键盘 (osk.exe)";
            }

            // Dismiss any legacy osk.exe before driving Windows 11 native TabTip/Gamepad Keyboard.
            TryCloseWindowByClass("OSKMainClass");

            bool shouldClose = forceClose ?? (fromUiClick ? ShouldCloseOnUiClick() : QueryTabTipVisible());
            if (shouldClose)
            {
                _autoHideConsumed = true;
                if (QueryTabTipVisible())
                {
                    // Step 1: Standalone Ctrl tap closes Windows 11 Gamepad keyboard layout reliably (matches GPD Tool 1.66)
                    Win32Interop.SendStandaloneCtrlTap();
                    if (!WaitUntilTabTipState(expectedVisible: false, timeoutMs: 175))
                    {
                        // Step 2: Standard/docked Touch keyboard layout toggles closed via ITipInvocation
                        TryInvokeTabTipComToggle();
                        if (!WaitUntilTabTipState(expectedVisible: false, timeoutMs: 210))
                        {
                            TryCloseWindowByClass("IPTip_Main_Window");
                        }
                    }
                }

                Interlocked.Exchange(ref _lastInputPaneHiddenTicks, Environment.TickCount64);
                return "已收起屏幕键盘";
            }

            _autoHideConsumed = false;
            EnsureTabTipProcessStarted();
            TryInvokeTabTipComToggle();
            if (!WaitUntilTabTipState(expectedVisible: true, timeoutMs: 280))
            {
                string tabTipPath = GetTabTipExePath();
                if (File.Exists(tabTipPath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo { FileName = tabTipPath, UseShellExecute = true });
                        WaitUntilTabTipState(expectedVisible: true, timeoutMs: 250);
                    }
                    catch
                    {
                        // Ignore shell fallback failure
                    }
                }
            }

            return "已呼出屏幕键盘";
        }
        finally
        {
            Thread.Sleep(80);
            Interlocked.Exchange(ref _oskToggleGate, 0);
        }
    }

    private static bool WaitUntilTabTipState(bool expectedVisible, int timeoutMs)
    {
        for (int elapsed = 0; elapsed < timeoutMs; elapsed += 35)
        {
            if (QueryTabTipVisible() == expectedVisible)
            {
                return true;
            }
            Thread.Sleep(35);
        }
        return QueryTabTipVisible() == expectedVisible;
    }

    private static string GetTabTipExePath()
    {
        string? common64 = Environment.GetEnvironmentVariable("CommonProgramW6432");
        if (string.IsNullOrWhiteSpace(common64))
        {
            common64 = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);
        }
        return Path.Combine(common64, @"microsoft shared\ink\TabTip.exe");
    }

    private static bool TryCloseWindowByClass(string className)
    {
        try
        {
            nint hwnd = Win32Interop.FindWindowW(className, null);
            if (hwnd != 0)
            {
                Win32Interop.PostMessageW(hwnd, Win32Interop.WM_SYSCOMMAND, Win32Interop.SC_CLOSE, 0);
                return Win32Interop.PostMessageW(hwnd, Win32Interop.WM_CLOSE, 0, 0);
            }
        }
        catch
        {
            // Ignore cross-integrity PostMessage rejections
        }
        return false;
    }

    public static string LaunchTouchKeyboard() => ToggleOnScreenKeyboard("tabtip");

    private static void EnsureTabTipProcessStarted()
    {
        try
        {
            if (Process.GetProcessesByName("TabTip").Length > 0)
            {
                return;
            }

            string tabTip = GetTabTipExePath();
            if (File.Exists(tabTip))
            {
                Process.Start(new ProcessStartInfo { FileName = tabTip, UseShellExecute = true });
                Thread.Sleep(250);
            }
        }
        catch
        {
            // Continue to COM / fallback
        }
    }

    private static bool TryInvokeTabTipComToggle()
    {
        bool ok = false;
        RunSta(() =>
        {
            object? host = null;
            try
            {
                var comType = Type.GetTypeFromCLSID(ClsidUiHostNoLaunch, throwOnError: false);
                if (comType is null) return;

                host = Activator.CreateInstance(comType);
                if (host is ITipInvocation tip)
                {
                    int hr = tip.Toggle(Win32Interop.GetDesktopWindow());
                    ok = hr >= 0;
                }
            }
            catch
            {
                ok = false;
            }
            finally
            {
                if (host is not null && Marshal.IsComObject(host))
                {
                    try { Marshal.FinalReleaseComObject(host); } catch { }
                }
            }
        });
        return ok;
    }

    private static void RunSta(Action action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            action();
            return;
        }

        using var done = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            finally
            {
                done.Set();
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        done.Wait(1500);
    }

    public void Dispose()
    {
        ResumeAllFrozenProcesses();
    }
}
