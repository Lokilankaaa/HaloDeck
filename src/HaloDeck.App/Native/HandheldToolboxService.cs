using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using HaloDeck.Core.Services;

namespace HaloDeck.App.Native;

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
        bool? forceCloseOsk = null,
        bool fromPhysicalChord = false)
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
                return ToggleOnScreenKeyboard(preferredOskMode, fromUiClick, forceCloseOsk, fromPhysicalChord);

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

            case "toggle_offline_wifi":
                return ToggleOfflineWifi();

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
    [Guid("5752238B-24F0-495A-82F1-2FD593056796")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFrameworkInputPane
    {
        [PreserveSig]
        int Advise([MarshalAs(UnmanagedType.IUnknown)] object pWindow, [MarshalAs(UnmanagedType.IUnknown)] object pHandler, out uint pdwCookie);

        [PreserveSig]
        int AdviseWithHWND(nint hwnd, [MarshalAs(UnmanagedType.IUnknown)] object pHandler, out uint pdwCookie);

        [PreserveSig]
        int Unadvise(uint dwCookie);

        [PreserveSig]
        int Location(out NativeRect prcInputPaneScreenLocation);
    }

    private static readonly OskToggleStateGuard OskGuard = new();
    private static readonly object TabTipStaLock = new();
    private static readonly ConcurrentQueue<(Action Work, ManualResetEventSlim Done)> TabTipStaQueue = new();
    private static AutoResetEvent? _tabTipWakeEvent;
    private static Thread? _tabTipStaThread;
    private static volatile bool _tabTipMonitorStopRequested;
    private static int _tabTipStaThreadId;
    private static IFrameworkInputPane? _staInputPane;
    private static int _oskToggleGate;

    public static void AttachInputPaneWatcher(nint hwnd)
    {
        EnsureTabTipMonitorStarted();
    }

    public static void DetachInputPaneWatcher()
    {
        Thread? thread;
        lock (TabTipStaLock)
        {
            thread = _tabTipStaThread;
            _tabTipMonitorStopRequested = true;
            try { _tabTipWakeEvent?.Set(); } catch { }
        }

        if (thread is { IsAlive: true } && Environment.CurrentManagedThreadId != _tabTipStaThreadId)
        {
            try { thread.Join(300); } catch { }
        }

        lock (TabTipStaLock)
        {
            if (_tabTipStaThread == thread && thread is { IsAlive: false })
            {
                _tabTipStaThread = null;
            }
        }
    }

    private static void EnsureTabTipMonitorStarted()
    {
        if (!_tabTipMonitorStopRequested && _tabTipStaThread is { IsAlive: true })
        {
            return;
        }

        lock (TabTipStaLock)
        {
            if (!_tabTipMonitorStopRequested && _tabTipStaThread is { IsAlive: true })
            {
                return;
            }

            if (_tabTipStaThread is { IsAlive: true })
            {
                try { _tabTipStaThread.Join(300); } catch { }
            }

            _tabTipMonitorStopRequested = false;
            _tabTipWakeEvent ??= new AutoResetEvent(false);

            var ready = new ManualResetEventSlim(false);
            var thread = new Thread(() => TabTipStaWorkerLoop(ready))
            {
                Name = "HaloDeck.TabTipStaMonitor",
                IsBackground = true
            };
            thread.SetApartmentState(ApartmentState.STA);
            _tabTipStaThread = thread;
            thread.Start();
            ready.Wait(500);
        }
    }

    private static void TabTipStaWorkerLoop(ManualResetEventSlim ready)
    {
        _tabTipStaThreadId = Environment.CurrentManagedThreadId;
        try { ready.Set(); } catch { }

        try
        {
            while (!_tabTipMonitorStopRequested)
            {
                DrainTabTipStaQueue();
                SampleTabTipOnStaThread();
                _tabTipWakeEvent?.WaitOne(100);
                DrainTabTipStaQueue();
            }
        }
        finally
        {
            DrainTabTipStaQueue();
            ReleaseStaInputPane();
            _tabTipStaThreadId = 0;
        }
    }

    private static void DrainTabTipStaQueue()
    {
        while (TabTipStaQueue.TryDequeue(out var item))
        {
            try
            {
                item.Work();
            }
            catch
            {
                // Individual STA actions tolerate errors locally
            }
            finally
            {
                try { item.Done.Set(); } catch { }
            }
        }
    }

    private static bool SampleTabTipOnStaThread()
    {
        try
        {
            if (_staInputPane is null)
            {
                var t = Type.GetTypeFromCLSID(ClsidFrameworkInputPane, throwOnError: false);
                if (t is null)
                {
                    OskGuard.RecordSample(false, Environment.TickCount64);
                    return false;
                }

                var obj = Activator.CreateInstance(t);
                if (obj is IFrameworkInputPane pane)
                {
                    _staInputPane = pane;
                }
                else
                {
                    if (obj is not null && Marshal.IsComObject(obj))
                    {
                        try { Marshal.FinalReleaseComObject(obj); } catch { }
                    }
                    OskGuard.RecordSample(false, Environment.TickCount64);
                    return false;
                }
            }

            int hr = _staInputPane.Location(out var rc);
            if (hr < 0)
            {
                ReleaseStaInputPane();
                OskGuard.RecordSample(false, Environment.TickCount64);
                return false;
            }

            bool visible = rc.IsVisible;
            OskGuard.RecordSample(visible, Environment.TickCount64);
            return visible;
        }
        catch
        {
            ReleaseStaInputPane();
            OskGuard.RecordSample(false, Environment.TickCount64);
            return false;
        }
    }

    private static void ReleaseStaInputPane()
    {
        var pane = _staInputPane;
        _staInputPane = null;
        if (pane is not null && Marshal.IsComObject(pane))
        {
            try { Marshal.FinalReleaseComObject(pane); } catch { }
        }
    }

    public static bool QueryTabTipVisible()
    {
        EnsureTabTipMonitorStarted();
        bool visible = false;
        RunOnTabTipSta(() =>
        {
            visible = SampleTabTipOnStaThread();
        });
        return visible;
    }

    public static bool ShouldCloseFromHookSnapshot()
    {
        EnsureTabTipMonitorStarted();
        if (OskGuard.ShouldCloseFromSnapshot(Environment.TickCount64, autoDismissGraceMs: 550))
        {
            return true;
        }

        nint oskHwnd = Win32Interop.FindWindowW("OSKMainClass", null);
        return oskHwnd != 0 && Win32Interop.IsWindowVisible(oskHwnd);
    }

    public static bool ShouldCloseOnUiClick()
    {
        EnsureTabTipMonitorStarted();
        if (OskGuard.ShouldCloseWithLiveQuery(QueryTabTipVisible(), Environment.TickCount64, autoDismissGraceMs: 750))
        {
            return true;
        }

        nint oskHwnd = Win32Interop.FindWindowW("OSKMainClass", null);
        return oskHwnd != 0 && Win32Interop.IsWindowVisible(oskHwnd);
    }

    public static string ToggleOnScreenKeyboard(
        string preferredMode = "tabtip",
        bool fromUiClick = false,
        bool? forceClose = null,
        bool fromPhysicalChord = false)
    {
        EnsureTabTipMonitorStarted();
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

            bool shouldClose = forceClose ?? (fromUiClick
                ? ShouldCloseOnUiClick()
                : OskGuard.ShouldCloseWithLiveQuery(QueryTabTipVisible(), Environment.TickCount64, autoDismissGraceMs: 350));

            if (shouldClose)
            {
                OskGuard.BeginIntentionalClose(Environment.TickCount64);
                try
                {
                    if ((fromPhysicalChord || fromUiClick) && QueryTabTipVisible())
                    {
                        // Physical Ctrl+Win keydown or UI pointer-down often already initiated Gamepad TabTip's slide-down close.
                        WaitUntilTabTipState(expectedVisible: false, timeoutMs: fromPhysicalChord ? 320 : 220);
                    }

                    if (QueryTabTipVisible())
                    {
                        // Standalone Ctrl tap closes Windows 11 Gamepad keyboard layout cleanly; allow full slide-down animation up to 650ms.
                        Win32Interop.SendStandaloneCtrlTap();
                        if (!WaitUntilTabTipState(expectedVisible: false, timeoutMs: 650))
                        {
                            // Fallback only if still open after 650ms (e.g., Standard/Docked Touch layout where Ctrl tap does not dismiss it).
                            TryInvokeTabTipComToggle();
                            if (!WaitUntilTabTipState(expectedVisible: false, timeoutMs: 500))
                            {
                                TryCloseWindowByClass("IPTip_Main_Window");
                            }
                        }
                    }
                }
                finally
                {
                    OskGuard.CompleteIntentionalClose(Environment.TickCount64, postCloseSuppressMs: 350);
                }

                return "已收起屏幕键盘";
            }

            OskGuard.BeginIntentionalOpen();
            EnsureTabTipProcessStarted();
            TryInvokeTabTipComToggle();
            if (!WaitUntilTabTipState(expectedVisible: true, timeoutMs: 420))
            {
                string tabTipPath = GetTabTipExePath();
                if (File.Exists(tabTipPath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo { FileName = tabTipPath, UseShellExecute = true });
                        WaitUntilTabTipState(expectedVisible: true, timeoutMs: 350);
                    }
                    catch
                    {
                        // Ignore shell fallback failure
                    }
                }
            }

            QueryTabTipVisible();
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
        RunOnTabTipSta(() =>
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

    private static void RunOnTabTipSta(Action action, int timeoutMs = 1500)
    {
        if (Environment.CurrentManagedThreadId == _tabTipStaThreadId)
        {
            action();
            return;
        }

        EnsureTabTipMonitorStarted();
        if (_tabTipMonitorStopRequested || _tabTipStaThread is not { IsAlive: true })
        {
            RunFallbackSta(action, timeoutMs);
            return;
        }

        using var done = new ManualResetEventSlim(false);
        TabTipStaQueue.Enqueue((action, done));
        try { _tabTipWakeEvent?.Set(); } catch { }
        done.Wait(timeoutMs);
    }

    private static void RunFallbackSta(Action action, int timeoutMs)
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
                try { done.Set(); } catch { }
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        done.Wait(timeoutMs);
    }

    private readonly object _wifiLock = new();
    private DateTime _lastWifiQueryUtc = DateTime.MinValue;
    private bool _cachedWifiSupported;
    private bool _cachedWifiRadioOff;
    private bool _wifiSuspendedBySession;

    public (bool Supported, bool IsRadioOff) GetWifiRadioStatus(bool forceRefresh = false)
    {
        lock (_wifiLock)
        {
            if (!forceRefresh && (DateTime.UtcNow - _lastWifiQueryUtc) < TimeSpan.FromSeconds(3))
            {
                return (_cachedWifiSupported, _cachedWifiRadioOff);
            }

            bool supported = false;
            bool anyOn = false;
            bool anyFound = false;

            try
            {
                if (WlanOpenHandle(2u, 0, out _, out nint hClient) == 0 && hClient != 0)
                {
                    try
                    {
                        if (WlanEnumInterfaces(hClient, 0, out nint pIfList) == 0 && pIfList != 0)
                        {
                            try
                            {
                                int count = Marshal.ReadInt32(pIfList, 0);
                                int itemSize = Marshal.SizeOf<WLAN_INTERFACE_INFO>();
                                for (int i = 0; i < count; i++)
                                {
                                    nint itemPtr = pIfList + 8 + (i * itemSize);
                                    var info = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(itemPtr);
                                    Guid ifGuid = info.InterfaceGuid;
                                    if (WlanQueryInterface(hClient, ref ifGuid, WlanIntfOpcodeRadioState, 0, out uint dataSize, out nint pData, out _) == 0 &&
                                        pData != 0)
                                    {
                                        try
                                        {
                                            if (dataSize >= 16)
                                            {
                                                int numPhys = Math.Clamp(Marshal.ReadInt32(pData, 0), 1, 16);
                                                anyFound = true;
                                                for (int p = 0; p < numPhys; p++)
                                                {
                                                    int swState = Marshal.ReadInt32(pData, 4 + (p * 12) + 4);
                                                    if (swState == Dot11RadioStateOn)
                                                    {
                                                        anyOn = true;
                                                    }
                                                }
                                            }
                                        }
                                        finally
                                        {
                                            WlanFreeMemory(pData);
                                        }
                                    }
                                }
                            }
                            finally
                            {
                                WlanFreeMemory(pIfList);
                            }
                        }
                    }
                    finally
                    {
                        WlanCloseHandle(hClient, 0);
                    }
                }
            }
            catch
            {
                // WLAN service disabled or unavailable
            }

            supported = anyFound;
            _cachedWifiSupported = supported;
            _cachedWifiRadioOff = supported && !anyOn;
            _lastWifiQueryUtc = DateTime.UtcNow;
            if (!_cachedWifiRadioOff)
            {
                _wifiSuspendedBySession = false;
            }
            return (_cachedWifiSupported, _cachedWifiRadioOff);
        }
    }

    public string ToggleOfflineWifi(bool? targetRadioOff = null)
    {
        var (supported, currentlyOff) = GetWifiRadioStatus(forceRefresh: true);
        if (!supported)
        {
            throw new InvalidOperationException("未检测到受控的内置 Wi-Fi 无线网卡，或 WLAN AutoConfig 服务未运行。");
        }

        bool turnOff = targetRadioOff ?? !currentlyOff;
        SetWifiSoftwareRadioState(turnOff);

        lock (_wifiLock)
        {
            _cachedWifiRadioOff = turnOff;
            _lastWifiQueryUtc = DateTime.UtcNow;
            _wifiSuspendedBySession = turnOff;
        }

        return turnOff
            ? "已挂起 Wi-Fi 射频（单机离线省电生效，蓝牙手柄/耳机保持正常连接）"
            : "已重新唤醒 Wi-Fi 无线网络";
    }

    private static void SetWifiSoftwareRadioState(bool turnOff)
    {
        uint rcOpen = WlanOpenHandle(2u, 0, out _, out nint hClient);
        if (rcOpen != 0 || hClient == 0)
        {
            throw new InvalidOperationException($"打开 WLAN 控制器失败 (错误码 {rcOpen})。");
        }

        try
        {
            uint rcEnum = WlanEnumInterfaces(hClient, 0, out nint pIfList);
            if (rcEnum != 0 || pIfList == 0)
            {
                throw new InvalidOperationException($"枚举 Wi-Fi 接口失败 (错误码 {rcEnum})。");
            }

            try
            {
                int count = Marshal.ReadInt32(pIfList, 0);
                if (count <= 0)
                {
                    throw new InvalidOperationException("当前未发现可用的 Wi-Fi 无线接口。");
                }

                bool anySucceeded = false;
                uint lastErr = 0;
                int itemSize = Marshal.SizeOf<WLAN_INTERFACE_INFO>();
                for (int i = 0; i < count; i++)
                {
                    nint itemPtr = pIfList + 8 + (i * itemSize);
                    var info = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(itemPtr);
                    Guid ifGuid = info.InterfaceGuid;

                    int numPhys = 1;
                    if (WlanQueryInterface(hClient, ref ifGuid, WlanIntfOpcodeRadioState, 0, out uint dataSize, out nint pData, out _) == 0 && pData != 0)
                    {
                        try
                        {
                            if (dataSize >= 16)
                            {
                                numPhys = Math.Clamp(Marshal.ReadInt32(pData, 0), 1, 16);
                            }
                        }
                        finally
                        {
                            WlanFreeMemory(pData);
                        }
                    }

                    for (uint phyIdx = 0; phyIdx < (uint)numPhys; phyIdx++)
                    {
                        var state = new WLAN_PHY_RADIO_STATE
                        {
                            dwPhyIndex = phyIdx,
                            dot11SoftwareRadioState = turnOff ? Dot11RadioStateOff : Dot11RadioStateOn,
                            dot11HardwareRadioState = Dot11RadioStateOn
                        };

                        uint rcSet = WlanSetInterface(
                            hClient,
                            ref ifGuid,
                            WlanIntfOpcodeRadioState,
                            (uint)Marshal.SizeOf<WLAN_PHY_RADIO_STATE>(),
                            ref state,
                            0);
                        if (rcSet == 0)
                        {
                            anySucceeded = true;
                        }
                        else
                        {
                            lastErr = rcSet;
                        }
                    }
                }

                if (!anySucceeded)
                {
                    throw new InvalidOperationException($"切换 Wi-Fi 射频状态失败 (Win32={lastErr})。");
                }
            }
            finally
            {
                WlanFreeMemory(pIfList);
            }
        }
        finally
        {
            WlanCloseHandle(hClient, 0);
        }
    }

    private const int WlanIntfOpcodeRadioState = 4;
    private const int Dot11RadioStateOn = 1;
    private const int Dot11RadioStateOff = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WLAN_INTERFACE_INFO
    {
        public Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strInterfaceDescription;
        public int isState;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WLAN_PHY_RADIO_STATE
    {
        public uint dwPhyIndex;
        public int dot11SoftwareRadioState;
        public int dot11HardwareRadioState;
    }

    [DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(uint dwClientVersion, nint pReserved, out uint pdwNegotiatedVersion, out nint phClientHandle);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(nint hClientHandle, nint pReserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanEnumInterfaces(nint hClientHandle, nint pReserved, out nint ppInterfaceList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanQueryInterface(
        nint hClientHandle,
        ref Guid pInterfaceGuid,
        int OpCode,
        nint pReserved,
        out uint pdwDataSize,
        out nint ppData,
        out int pWlanOpcodeValueType);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanSetInterface(
        nint hClientHandle,
        ref Guid pInterfaceGuid,
        int OpCode,
        uint dwDataSize,
        ref WLAN_PHY_RADIO_STATE pData,
        nint pReserved);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(nint pMemory);

    public void Dispose()
    {
        ResumeAllFrozenProcesses();
        bool restoreWifi;
        lock (_wifiLock)
        {
            restoreWifi = _wifiSuspendedBySession;
            _wifiSuspendedBySession = false;
        }

        if (restoreWifi)
        {
            try { SetWifiSoftwareRadioState(turnOff: false); } catch { }
        }
    }
}
