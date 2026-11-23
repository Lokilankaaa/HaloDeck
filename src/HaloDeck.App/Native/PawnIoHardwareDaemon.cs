using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using HaloDeck.Core.Models;
using Microsoft.Win32;

namespace HaloDeck.App.Native;

public sealed class HardwareDaemonStatus
{
    public int DaemonVersion { get; set; }
    public bool ServiceInstalled { get; set; }
    public bool ServiceRunning { get; set; }
    public bool PawnIoInstalled { get; set; }
    public bool SmuReady { get; set; }
    public bool EcReady { get; set; }
    public string DmiBoard { get; set; } = string.Empty;
    public int AppliedTdpWatts { get; set; } = 40;
    public int AppliedSlowWatts { get; set; } = 45;
    public int AppliedFastWatts { get; set; } = 50;
    public string PmTableVersion { get; set; } = string.Empty;
    public double? HwStapmLimitWatts { get; set; }
    public double? HwSlowLimitWatts { get; set; }
    public double? HwFastLimitWatts { get; set; }
    public string FanMode { get; set; } = "auto";
    public int ManualFanPercent { get; set; } = 55;
    public int FanRpm { get; set; }
    public int AppliedFanPwm { get; set; }
    public bool GpdServiceExists { get; set; }
    public bool GpdServiceRunning { get; set; }
    public int CpuTempC { get; set; }
    public int CurrentFps { get; set; }
    public double FrameTimeMs { get; set; }
    public int FpsPid { get; set; }
    public string LastError { get; set; } = string.Empty;
}

/// <summary>
/// Privileged Session-0 Windows Service ("HaloDeckHardwareSvc") using only signed official PawnIO modules
/// (RyzenSMU.bin + LpcIO.bin) over a permission-scoped Named Pipe (HaloDeck.Hardware.v1).
/// Automatically restores BIOS Auto Fan control (EC 0x047A/0x047B = 0) on stop, shutdown, or watchdog expiry.
/// </summary>
public sealed class PawnIoHardwareDaemon : ServiceBase
{
    public const int CurrentDaemonVersion = 4;
    public const string WindowsServiceName = "HaloDeckHardwareSvc";
    public const string PipeName = "HaloDeck.Hardware.v1";

    private const string PawnIoDllPath = @"C:\Program Files\PawnIO\PawnIOLib.dll";
    private const int MinTdpWatts = 8;
    private const int MaxTdpWatts = 120;
    private const int MaxSlowWatts = 130;
    private const int MaxFastWatts = 140;

    // Strix Halo SMU mailbox addresses (RyzenSMU)
    private const uint Mp1CmdReg = 0x03B10928;
    private const uint Mp1RspReg = 0x03B10978;
    private const uint Mp1Arg0Reg = 0x03B10998;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private CancellationTokenSource? _cts;
    private Task? _pipeTask;
    private Task? _watchdogTask;
    private readonly object _hwLock = new();

    private nint _pawnIoLib;
    private nint _smuHandle;
    private nint _ecHandle;
    private bool _pawnIoInstalled;
    private bool _smuReady;
    private bool _ecReady;
    private string _dmiBoard = string.Empty;
    private string _lastError = string.Empty;

    private int _appliedTdpWatts = 40;
    private int _appliedSlowWatts = 45;
    private int _appliedFastWatts = 50;
    private string _pmTableVersion = string.Empty;
    private double? _hwStapmLimitWatts;
    private double? _hwSlowLimitWatts;
    private double? _hwFastLimitWatts;
    private bool _pmTableResolved;
    private bool _pmTableUnavailable;
    private DateTime _lastPmReadUtc = DateTime.MinValue;
    private DateTime _lastGpdSvcCheckUtc = DateTime.MinValue;
    private DateTime _lastPawnIoInitAttemptUtc = DateTime.MinValue;
    private bool _cachedGpdSvcExists;
    private bool _cachedGpdSvcRunning;
    private readonly ulong[] _pmTableBuf = new ulong[1024];

    private string _fanMode = "auto";
    private int _manualFanPercent = 55;
    private List<FanCurvePoint> _activeCurvePoints = FanCurveEvaluator.CreateDefaultCurves()[1].Points;
    private int _lastTempC = 48;
    private int _lastSmuCpuTempC;
    private int _lastFanRpm;
    private int _appliedFanPwm;
    private DateTime _lastFanHeartbeatUtc = DateTime.UtcNow;
    private EtwPresentFpsTracker? _fpsTracker;
    private volatile int _lastTargetPid;

    private delegate int PawnIoOpenDelegate(out nint handle);
    private delegate int PawnIoLoadDelegate(nint handle, byte[] blob, nuint size);
    private delegate int PawnIoExecuteDelegate(nint handle, [MarshalAs(UnmanagedType.LPStr)] string name, ulong[] inBuf, nuint inSize, ulong[] outBuf, nuint outSize, out nuint returnSize);
    private delegate int PawnIoCloseDelegate(nint handle);

    private PawnIoOpenDelegate? _pawnOpen;
    private PawnIoLoadDelegate? _pawnLoad;
    private PawnIoExecuteDelegate? _pawnExecute;
    private PawnIoCloseDelegate? _pawnClose;

    public PawnIoHardwareDaemon()
    {
        ServiceName = WindowsServiceName;
        CanStop = true;
        CanShutdown = true;
        AutoLog = false;
    }

    public static void RunService()
    {
        Run(new PawnIoHardwareDaemon());
    }

    protected override void OnStart(string[] args)
    {
        _cts = new CancellationTokenSource();
        InitializePawnIo();
        _fpsTracker = new EtwPresentFpsTracker();
        _fpsTracker.Start();
        _pipeTask = Task.Run(() => PipeServerLoopAsync(_cts.Token), _cts.Token);
        _watchdogTask = Task.Run(() => FanWatchdogLoopAsync(_cts.Token), _cts.Token);
    }

    protected override void OnStop()
    {
        RestoreAutoFanAndCleanup();
    }

    protected override void OnShutdown()
    {
        RestoreAutoFanAndCleanup();
        base.OnShutdown();
    }

    private void RestoreAutoFanAndCleanup()
    {
        try
        {
            _cts?.Cancel();
        }
        catch { }

        try
        {
            _fpsTracker?.Dispose();
            _fpsTracker = null;
        }
        catch { }

        lock (_hwLock)
        {
            if (_ecReady && _ecHandle != 0)
            {
                try
                {
                    WithNamedMutex(@"Global\Access_ISABUS.HTP.Method", () =>
                    {
                        EcWriteByte(0x047A, 0);
                        EcWriteByte(0x047B, 0);
                    });
                }
                catch { }
            }

            if (_pawnClose is not null)
            {
                if (_smuHandle != 0) { try { _pawnClose(_smuHandle); } catch { } _smuHandle = 0; }
                if (_ecHandle != 0) { try { _pawnClose(_ecHandle); } catch { } _ecHandle = 0; }
            }
        }
    }

    private void InitializePawnIo()
    {
        lock (_hwLock)
        {
            InitializePawnIoLocked();
        }
    }

    private void TryEnsurePawnIoReadyLocked()
    {
        bool needEc = IsSupportedGpdEcBoard(_dmiBoard) && (!_ecReady || _ecHandle == 0);
        if (_smuReady && _smuHandle != 0 && !needEc)
        {
            return;
        }

        if ((DateTime.UtcNow - _lastPawnIoInitAttemptUtc) < TimeSpan.FromSeconds(8))
        {
            return;
        }

        InitializePawnIoLocked();
    }

    private void InitializePawnIoLocked()
    {
        _lastPawnIoInitAttemptUtc = DateTime.UtcNow;
        if (string.IsNullOrEmpty(_dmiBoard))
        {
            _dmiBoard = ReadDmiBoardLabel();
        }

        _pawnIoInstalled = File.Exists(PawnIoDllPath);
        if (!_pawnIoInstalled)
        {
            _lastError = $"未检测到 PawnIO 驱动 ({PawnIoDllPath})，请先安装官方 PawnIO 驱动（pawnio.eu）";
            return;
        }

        if (_pawnIoLib == 0)
        {
            try
            {
                if (!NativeLibrary.TryLoad(PawnIoDllPath, out _pawnIoLib))
                {
                    _lastError = "加载 PawnIOLib.dll 失败";
                    return;
                }

                _pawnOpen = Marshal.GetDelegateForFunctionPointer<PawnIoOpenDelegate>(NativeLibrary.GetExport(_pawnIoLib, "pawnio_open"));
                _pawnLoad = Marshal.GetDelegateForFunctionPointer<PawnIoLoadDelegate>(NativeLibrary.GetExport(_pawnIoLib, "pawnio_load"));
                _pawnExecute = Marshal.GetDelegateForFunctionPointer<PawnIoExecuteDelegate>(NativeLibrary.GetExport(_pawnIoLib, "pawnio_execute"));
                _pawnClose = Marshal.GetDelegateForFunctionPointer<PawnIoCloseDelegate>(NativeLibrary.GetExport(_pawnIoLib, "pawnio_close"));
            }
            catch (Exception ex)
            {
                _lastError = $"初始化 PawnIO 导出函数失败: {ex.Message}";
                return;
            }
        }

        if (_pawnOpen is null || _pawnLoad is null || _pawnExecute is null || _pawnClose is null)
        {
            return;
        }

        _lastError = string.Empty;
        string modDir = Path.Combine(AppContext.BaseDirectory, "Assets", "PawnIO");

        if (!_smuReady || _smuHandle == 0)
        {
            string smuPath = Path.Combine(modDir, "RyzenSMU.bin");
            if (File.Exists(smuPath))
            {
                try
                {
                    byte[] smuBlob = File.ReadAllBytes(smuPath);
                    int hr = _pawnOpen(out _smuHandle);
                    if (hr >= 0 && _smuHandle != 0)
                    {
                        hr = _pawnLoad(_smuHandle, smuBlob, (nuint)smuBlob.Length);
                        if (hr >= 0)
                        {
                            _smuReady = true;
                        }
                        else
                        {
                            _lastError = $"加载 RyzenSMU.bin 失败 (0x{hr:X8})";
                            _pawnClose(_smuHandle);
                            _smuHandle = 0;
                        }
                    }
                    else
                    {
                        _lastError = FormatPawnIoOpenError("SMU", hr);
                    }
                }
                catch (Exception ex)
                {
                    _lastError = $"RyzenSMU 初始化异常: {ex.Message}";
                }
            }
        }

        // Only unlock SuperIO / EC writes on confirmed GPD handheld boards (G1618-05 Win5 / G1622-01 Win4/Mini)
        if ((!_ecReady || _ecHandle == 0) && IsSupportedGpdEcBoard(_dmiBoard))
        {
            string lpcPath = Path.Combine(modDir, "LpcIO.bin");
            if (File.Exists(lpcPath))
            {
                try
                {
                    byte[] lpcBlob = File.ReadAllBytes(lpcPath);
                    int hr = _pawnOpen(out _ecHandle);
                    if (hr >= 0 && _ecHandle != 0)
                    {
                        hr = _pawnLoad(_ecHandle, lpcBlob, (nuint)lpcBlob.Length);
                        if (hr >= 0)
                        {
                            WithNamedMutex(@"Global\Access_ISABUS.HTP.Method", () =>
                            {
                                var inBuf = new ulong[] { 1 }; // Slot 1 -> 0x4E/0x4F
                                var outBuf = Array.Empty<ulong>();
                                int rc = _pawnExecute(_ecHandle, "ioctl_select_slot", inBuf, 1, outBuf, 0, out _);
                                if (rc >= 0)
                                {
                                    _ecReady = true;
                                    _lastFanRpm = ReadFanRpmInternal();
                                }
                            });
                        }
                        else
                        {
                            _pawnClose(_ecHandle);
                            _ecHandle = 0;
                        }
                    }
                    else if (string.IsNullOrEmpty(_lastError))
                    {
                        _lastError = FormatPawnIoOpenError("EC", hr);
                    }
                }
                catch (Exception ex)
                {
                    _lastError = string.IsNullOrEmpty(_lastError) ? $"LpcIO 初始化异常: {ex.Message}" : _lastError;
                }
            }
        }
    }

    internal static string FormatPawnIoOpenError(string channel, int hr)
    {
        return unchecked((uint)hr) switch
        {
            0x80070002 => "PawnIO 内核驱动未启动或未安装 (0x80070002)：请安装官方 PawnIO 驱动（pawnio.eu）后重启电脑，或检查杀毒软件/内核隔离拦截",
            0x80070005 => "PawnIO 内核驱动访问被拒绝 (0x80070005)：请确认以系统服务模式运行并检查安全软件拦截",
            _ => $"pawnio_open({channel}) 失败 (0x{hr:X8})"
        };
    }

    private static string ReadDmiBoardLabel()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            string mfg = (key?.GetValue("SystemManufacturer") as string ?? "").Trim();
            string prod = (key?.GetValue("SystemProductName") as string ?? "").Trim();
            string board = (key?.GetValue("BaseBoardProduct") as string ?? "").Trim();
            return $"{mfg} {board} {prod}".Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsSupportedGpdEcBoard(string dmi)
    {
        if (string.IsNullOrWhiteSpace(dmi)) return false;
        return dmi.Contains("GPD", StringComparison.OrdinalIgnoreCase) &&
               (dmi.Contains("G1618-05", StringComparison.OrdinalIgnoreCase) ||
                dmi.Contains("G1622-01", StringComparison.OrdinalIgnoreCase));
    }

    private async Task PipeServerLoopAsync(CancellationToken ct)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = NamedPipeServerStreamAcl.Create(
                    PipeName,
                    PipeDirection.InOut,
                    4,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    4096,
                    4096,
                    security);

                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    var response = HandlePipeCommand(line);
                    await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOpts)).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(120, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task FanWatchdogLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(2000, ct).ConfigureAwait(false);
                lock (_hwLock)
                {
                    if (!_ecReady || _ecHandle == 0) continue;

                    WithNamedMutex(@"Global\Access_ISABUS.HTP.Method", () =>
                    {
                        _lastFanRpm = ReadFanRpmInternal();

                        if (string.Equals(_fanMode, "auto", StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }

                        // If curve mode hasn't received client temperature heartbeat in 15s, hand control back to BIOS auto
                        if ((_fanMode is "curve" or "silent" or "balanced") &&
                            (DateTime.UtcNow - _lastFanHeartbeatUtc) > TimeSpan.FromSeconds(15))
                        {
                            _fanMode = "auto";
                            _appliedFanPwm = 0;
                            EcWriteByte(0x047A, 0);
                            EcWriteByte(0x047B, 0);
                            return;
                        }

                        if (_fanMode == "manual" && _lastSmuCpuTempC > 0)
                        {
                            _lastTempC = _lastSmuCpuTempC;
                        }

                        ApplyFanModeLocked(_fanMode, _manualFanPercent, _lastTempC, forceWrite: false);
                    });
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Ignore transient cycle errors
            }
        }
    }

    private object HandlePipeCommand(string jsonLine)
    {
        using var doc = JsonDocument.Parse(jsonLine);
        var root = doc.RootElement;
        string op = root.TryGetProperty("op", out var opProp) ? (opProp.GetString() ?? "status") : "status";

        switch (op)
        {
            case "status":
            {
                int targetPid = root.TryGetProperty("targetPid", out var pidProp) && pidProp.TryGetInt32(out int parsedPid)
                    ? parsedPid
                    : 0;
                if (targetPid > 0) _lastTargetPid = targetPid;
                var fpsSample = _fpsTracker?.GetCurrentFps(targetPid) ?? (0, 0.0, 0);

                lock (_hwLock)
                {
                    TryEnsurePawnIoReadyLocked();
                    if (_ecReady && _ecHandle != 0)
                    {
                        WithNamedMutex(@"Global\Access_ISABUS.HTP.Method", () => _lastFanRpm = ReadFanRpmInternal());
                    }
                    TryRefreshPmTableLocked(force: false);
                    if (_smuReady && _smuHandle != 0)
                    {
                        WithNamedMutex(@"Global\Access_PCI", () =>
                        {
                            if (ReadSmuReg(0x00059800, out uint rawThm))
                            {
                                int t = ZenThermalDecoder.DecodeTctlTempC(rawThm);
                                if (t > 0) _lastSmuCpuTempC = t;
                            }
                        });
                    }
                    return new { ok = true, status = BuildStatusSnapshotLocked(fpsSample.Fps, fpsSample.FrameTimeMs, fpsSample.SourcePid) };
                }
            }

            case "set_tdp":
            {
                int watts = Math.Clamp(root.GetProperty("watts").GetInt32(), MinTdpWatts, MaxTdpWatts);
                int slowWatts = root.TryGetProperty("slowWatts", out var sp) && sp.ValueKind == JsonValueKind.Number
                    ? Math.Clamp(sp.GetInt32(), MinTdpWatts, MaxSlowWatts)
                    : watts;
                int fastWatts = root.TryGetProperty("fastWatts", out var fp) && fp.ValueKind == JsonValueKind.Number
                    ? Math.Clamp(fp.GetInt32(), MinTdpWatts, MaxFastWatts)
                    : Math.Min(MaxFastWatts, Math.Max(slowWatts, watts + 2));
                var fpsSample = _fpsTracker?.GetCurrentFps(_lastTargetPid) ?? (0, 0.0, 0);

                lock (_hwLock)
                {
                    TryEnsurePawnIoReadyLocked();
                    bool ok = ApplyStrixHaloTdpLocked(watts, slowWatts, fastWatts, out string err);
                    return new { ok, error = err, status = BuildStatusSnapshotLocked(fpsSample.Fps, fpsSample.FrameTimeMs, fpsSample.SourcePid) };
                }
            }

            case "set_fan":
            {
                string mode = (root.TryGetProperty("mode", out var mp) ? mp.GetString() : "auto") ?? "auto";
                int pct = Math.Clamp(root.TryGetProperty("percent", out var pp) ? pp.GetInt32() : 55, 20, 100);
                int tempC = root.TryGetProperty("tempC", out var tp) ? Math.Clamp(tp.GetInt32(), 0, 115) : _lastTempC;
                var fpsSample = _fpsTracker?.GetCurrentFps(_lastTargetPid) ?? (0, 0.0, 0);

                List<FanCurvePoint>? parsedCurve = null;
                if (root.TryGetProperty("curvePoints", out var cp) && cp.ValueKind == JsonValueKind.Array)
                {
                    parsedCurve = JsonSerializer.Deserialize<List<FanCurvePoint>>(cp.GetRawText(), JsonOpts);
                }

                lock (_hwLock)
                {
                    TryEnsurePawnIoReadyLocked();
                    _lastTempC = tempC;
                    _lastFanHeartbeatUtc = DateTime.UtcNow;
                    if (parsedCurve is { Count: > 0 })
                    {
                        _activeCurvePoints = FanCurveEvaluator.NormalizePoints(parsedCurve);
                    }

                    if (!_ecReady || _ecHandle == 0)
                    {
                        return new { ok = false, error = "EC 风扇通道仅支持 GPD G1618-05 / G1622-01", status = BuildStatusSnapshotLocked(fpsSample.Fps, fpsSample.FrameTimeMs, fpsSample.SourcePid) };
                    }

                    WithNamedMutex(@"Global\Access_ISABUS.HTP.Method", () =>
                    {
                        ApplyFanModeLocked(mode, pct, tempC, forceWrite: true);
                        _lastFanRpm = ReadFanRpmInternal();
                    });
                    return new { ok = true, error = string.Empty, status = BuildStatusSnapshotLocked(fpsSample.Fps, fpsSample.FrameTimeMs, fpsSample.SourcePid) };
                }
            }

            case "set_gpd_service":
            {
                bool paused = root.TryGetProperty("paused", out var p) && p.GetBoolean();
                int rc = SetGpdServicePaused(paused);
                var fpsSample = _fpsTracker?.GetCurrentFps(_lastTargetPid) ?? (0, 0.0, 0);
                lock (_hwLock)
                {
                    _lastGpdSvcCheckUtc = DateTime.MinValue;
                    return new
                    {
                        ok = rc == 0,
                        error = rc == 0 ? string.Empty : $"切换 GPDToolService 状态失败 (code={rc})",
                        status = BuildStatusSnapshotLocked(fpsSample.Fps, fpsSample.FrameTimeMs, fpsSample.SourcePid)
                    };
                }
            }

            default:
                return new { ok = false, error = $"Unknown op: {op}" };
        }
    }

    private HardwareDaemonStatus BuildStatusSnapshotLocked(int currentFps = 0, double frameTimeMs = 0.0, int fpsPid = 0)
    {
        if ((DateTime.UtcNow - _lastGpdSvcCheckUtc) > TimeSpan.FromSeconds(8))
        {
            (_cachedGpdSvcExists, _cachedGpdSvcRunning) = ProbeWindowsServiceState("GPDToolService");
            _lastGpdSvcCheckUtc = DateTime.UtcNow;
        }
        bool gpdExists = _cachedGpdSvcExists;
        bool gpdRunning = _cachedGpdSvcRunning;
        return new HardwareDaemonStatus
        {
            DaemonVersion = CurrentDaemonVersion,
            ServiceInstalled = true,
            ServiceRunning = true,
            PawnIoInstalled = _pawnIoInstalled,
            SmuReady = _smuReady,
            EcReady = _ecReady,
            DmiBoard = _dmiBoard,
            AppliedTdpWatts = _appliedTdpWatts,
            AppliedSlowWatts = _appliedSlowWatts,
            AppliedFastWatts = _appliedFastWatts,
            PmTableVersion = _pmTableVersion,
            HwStapmLimitWatts = _hwStapmLimitWatts,
            HwSlowLimitWatts = _hwSlowLimitWatts,
            HwFastLimitWatts = _hwFastLimitWatts,
            FanMode = _fanMode,
            ManualFanPercent = _manualFanPercent,
            FanRpm = _lastFanRpm,
            AppliedFanPwm = _appliedFanPwm,
            GpdServiceExists = gpdExists,
            GpdServiceRunning = gpdRunning,
            CpuTempC = _lastSmuCpuTempC,
            CurrentFps = currentFps,
            FrameTimeMs = frameTimeMs,
            FpsPid = fpsPid,
            LastError = _lastError
        };
    }

    private bool ApplyStrixHaloTdpLocked(int watts, int slowWatts, int fastWatts, out string error)
    {
        error = string.Empty;
        if (!_smuReady || _smuHandle == 0 || _pawnExecute is null)
        {
            error = string.IsNullOrEmpty(_lastError) ? "RyzenSMU 尚未就绪" : _lastError;
            return false;
        }

        uint stapmMw = (uint)(watts * 1000);
        uint slowMw = (uint)(slowWatts * 1000);
        uint fastMw = (uint)(fastWatts * 1000);

        bool success = false;
        string localErr = string.Empty;

        WithNamedMutex(@"Global\Access_PCI", () =>
        {
            bool okStapm = SendMp1MailboxCommand(0x14, stapmMw, out int rspCode);
            bool okFast = SendMp1MailboxCommand(0x15, fastMw, out _);
            bool okSlow = SendMp1MailboxCommand(0x16, slowMw, out _);

            if (okStapm)
            {
                success = true;
                return;
            }

            // Fallback to PSMU set_stapm_limit (0x31)
            var args = new ulong[] { 0x31, stapmMw, 0, 0, 0, 0, 0 };
            var outArgs = new ulong[6];
            int hr = _pawnExecute(_smuHandle, "ioctl_send_smu_command", args, 7, outArgs, 6, out _);
            if (hr >= 0)
            {
                success = true;
                return;
            }

            localErr = $"SMU TDP 写入失败 (MP1 rsp={rspCode}, PSMU hr=0x{hr:X8})";
        });

        if (success)
        {
            _appliedTdpWatts = watts;
            _appliedSlowWatts = slowWatts;
            _appliedFastWatts = fastWatts;
            _hwStapmLimitWatts = watts;
            _hwSlowLimitWatts = slowWatts;
            _hwFastLimitWatts = fastWatts;
            _lastPmReadUtc = DateTime.MinValue;
            _lastError = string.Empty;
        }
        else
        {
            error = localErr;
            _lastError = localErr;
        }
        return success;
    }

    private void TryRefreshPmTableLocked(bool force)
    {
        if (!_smuReady || _smuHandle == 0 || _pawnExecute is null || _pmTableUnavailable)
        {
            return;
        }

        if (!force && (DateTime.UtcNow - _lastPmReadUtc) < TimeSpan.FromSeconds(10))
        {
            return;
        }

        WithNamedMutex(@"Global\Access_PCI", () =>
        {
            if (!_pmTableResolved)
            {
                var verOut = new ulong[2];
                int hrResolve = _pawnExecute(_smuHandle, "ioctl_resolve_pm_table", Array.Empty<ulong>(), 0, verOut, 2, out _);
                if (hrResolve < 0 || verOut[0] == 0)
                {
                    _pmTableUnavailable = true;
                    return;
                }

                _pmTableVersion = $"0x{(uint)verOut[0]:X8}";
                _pmTableResolved = true;
            }

            int hrUpdate = _pawnExecute(_smuHandle, "ioctl_update_pm_table", Array.Empty<ulong>(), 0, Array.Empty<ulong>(), 0, out _);
            if (hrUpdate < 0)
            {
                return;
            }

            int hrRead = _pawnExecute(_smuHandle, "ioctl_read_pm_table", Array.Empty<ulong>(), 0, _pmTableBuf, (nuint)_pmTableBuf.Length, out nuint retQwords);
            if (hrRead < 0 || retQwords < 3)
            {
                return;
            }

            // Strix Halo PM table header floats (Watts): 0x00=STAPM_LIMIT, 0x08=PPT_LIMIT_FAST, 0x10=PPT_LIMIT_SLOW
            double? stapm = ReadPmFloatWatts(0);
            double? fast = ReadPmFloatWatts(1);
            double? slow = ReadPmFloatWatts(2);

            if (stapm.HasValue) _hwStapmLimitWatts = stapm;
            if (fast.HasValue) _hwFastLimitWatts = fast;
            if (slow.HasValue) _hwSlowLimitWatts = slow;
            _lastPmReadUtc = DateTime.UtcNow;
        });
    }

    private double? ReadPmFloatWatts(int qwordIndex)
    {
        float v = BitConverter.Int32BitsToSingle(unchecked((int)(_pmTableBuf[qwordIndex] & 0xFFFFFFFF)));
        if (float.IsNaN(v) || float.IsInfinity(v) || v < 1f || v > 300f)
        {
            return null;
        }
        return Math.Round(v, 1);
    }

    private bool SendMp1MailboxCommand(uint msgId, uint arg0, out int responseCode)
    {
        responseCode = 0;
        if (_pawnExecute is null || _smuHandle == 0) return false;

        // Wait rsp != 0
        uint rsp = 0;
        for (int i = 0; i < 2000; i++)
        {
            if (!ReadSmuReg(Mp1RspReg, out rsp)) return false;
            if (rsp != 0) break;
        }
        if (rsp == 0) return false;

        if (!WriteSmuReg(Mp1RspReg, 0)) return false;
        if (!WriteSmuReg(Mp1Arg0Reg, arg0)) return false;
        for (uint j = 1; j < 6; j++)
        {
            WriteSmuReg(Mp1Arg0Reg + j * 4, 0);
        }
        if (!WriteSmuReg(Mp1CmdReg, msgId)) return false;

        rsp = 0;
        for (int i = 0; i < 2000; i++)
        {
            if (!ReadSmuReg(Mp1RspReg, out rsp)) return false;
            if (rsp != 0) break;
        }
        responseCode = (int)rsp;
        return rsp == 1;
    }

    private bool ReadSmuReg(uint addr, out uint value)
    {
        value = 0;
        if (_pawnExecute is null || _smuHandle == 0) return false;
        var inBuf = new ulong[] { addr };
        var outBuf = new ulong[1];
        int hr = _pawnExecute(_smuHandle, "ioctl_read_smu_register", inBuf, 1, outBuf, 1, out _);
        if (hr < 0) return false;
        value = (uint)(outBuf[0] & 0xFFFFFFFF);
        return true;
    }

    private bool WriteSmuReg(uint addr, uint value)
    {
        if (_pawnExecute is null || _smuHandle == 0) return false;
        var inBuf = new ulong[] { addr, value };
        int hr = _pawnExecute(_smuHandle, "ioctl_write_smu_register", inBuf, 2, Array.Empty<ulong>(), 0, out _);
        return hr >= 0;
    }

    private DateTime _lastEcPwmWriteUtc = DateTime.MinValue;

    private void ApplyFanModeLocked(string mode, int manualPct, int tempC, bool forceWrite = false)
    {
        string normalized = mode.Trim().ToLowerInvariant();
        string prevMode = _fanMode;
        _fanMode = normalized switch
        {
            "auto" => "auto",
            "manual" => "manual",
            "curve" => "curve",
            "silent" or "balanced" => "curve",
            "turbo" => "manual",
            _ => "auto"
        };
        _manualFanPercent = normalized == "turbo" ? 100 : Math.Clamp(manualPct, 20, 100);

        if (_fanMode == "auto")
        {
            if (forceWrite || prevMode != "auto" || _appliedFanPwm != 0)
            {
                _appliedFanPwm = 0;
                EcWriteByte(0x047A, 0);
                EcWriteByte(0x047B, 0);
                _lastEcPwmWriteUtc = DateTime.UtcNow;
            }
            return;
        }

        int pct = _fanMode == "manual"
            ? FanCurveEvaluator.ApplySafetyFloor(tempC, _manualFanPercent)
            : FanCurveEvaluator.Evaluate(tempC, _activeCurvePoints);

        byte pwm = (byte)Math.Clamp((int)Math.Round(pct / 100.0 * 244.0), 36, 244);
        bool regDrifted = !forceWrite && (EcReadByte(0x047A) != pwm || EcReadByte(0x047B) != pwm);
        if (forceWrite || regDrifted || prevMode != _fanMode || _appliedFanPwm != pwm || (DateTime.UtcNow - _lastEcPwmWriteUtc) >= TimeSpan.FromSeconds(8))
        {
            _appliedFanPwm = pwm;
            EcWriteByte(0x047A, pwm);
            EcWriteByte(0x047B, pwm);
            _lastEcPwmWriteUtc = DateTime.UtcNow;
        }
    }

    private int ReadFanRpmInternal()
    {
        byte hi = EcReadByte(0x0478);
        byte lo = EcReadByte(0x0479);
        int rpm = (hi << 8) | lo;
        return rpm is >= 0 and <= 12000 ? rpm : 0;
    }

    private byte EcReadByte(ushort offset)
    {
        if (_pawnExecute is null || _ecHandle == 0) return 0;
        SuperIoOut(0x2E, 0x11);
        SuperIoOut(0x2F, (byte)(offset >> 8));
        SuperIoOut(0x2E, 0x10);
        SuperIoOut(0x2F, (byte)(offset & 0xFF));
        SuperIoOut(0x2E, 0x12);
        return SuperIoIn(0x2F);
    }

    private void EcWriteByte(ushort offset, byte value)
    {
        if (_pawnExecute is null || _ecHandle == 0) return;
        SuperIoOut(0x2E, 0x11);
        SuperIoOut(0x2F, (byte)(offset >> 8));
        SuperIoOut(0x2E, 0x10);
        SuperIoOut(0x2F, (byte)(offset & 0xFF));
        SuperIoOut(0x2E, 0x12);
        SuperIoOut(0x2F, value);
    }

    private void SuperIoOut(byte reg, byte val)
    {
        if (_pawnExecute is null || _ecHandle == 0) return;
        var inBuf = new ulong[] { reg, val };
        _pawnExecute(_ecHandle, "ioctl_superio_outb", inBuf, 2, Array.Empty<ulong>(), 0, out _);
    }

    private byte SuperIoIn(byte reg)
    {
        if (_pawnExecute is null || _ecHandle == 0) return 0;
        var inBuf = new ulong[] { reg };
        var outBuf = new ulong[1];
        int hr = _pawnExecute(_ecHandle, "ioctl_superio_inb", inBuf, 1, outBuf, 1, out _);
        return hr >= 0 ? (byte)(outBuf[0] & 0xFF) : (byte)0;
    }

    private static void WithNamedMutex(string name, Action action)
    {
        Mutex? mutex = null;
        bool acquired = false;
        try
        {
            mutex = new Mutex(false, name);
            try
            {
                acquired = mutex.WaitOne(350);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }
            action();
        }
        catch
        {
            // Fallback: still attempt if mutex open fails cleanly
            try { action(); } catch { }
        }
        finally
        {
            if (acquired && mutex is not null)
            {
                try { mutex.ReleaseMutex(); } catch { }
            }
            mutex?.Dispose();
        }
    }

    // ---------------- Elevation CLI Install / Uninstall Helpers ----------------

    private static void EnsurePawnIoDriverRunning()
    {
        try
        {
            // Kernel driver service "PawnIO" may be installed with Demand start or stopped by another tool.
            // Ensure it is configured for Auto start and running before HaloDeckHardwareSvc opens \\.\PawnIO.
            if (RunScCommand("query PawnIO") == 0)
            {
                RunScCommand("config PawnIO start= auto");
                RunScCommand("start PawnIO");
                Thread.Sleep(250);
            }
        }
        catch
        {
            // Non-fatal: HaloDeckHardwareSvc will surface the exact diagnostic error if PawnIO is unavailable.
        }
    }

    public static int InstallAndStartService()
    {
        try
        {
            EnsurePawnIoDriverRunning();

            string exePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("无法确定当前 HaloDeck.exe 路径。");
            string quotedBin = $"\\\"{exePath}\\\" --hw-service";

            var (exists, running) = ProbeWindowsServiceState(WindowsServiceName);
            if (exists && running)
            {
                RunScCommand($"stop {WindowsServiceName}");
                Thread.Sleep(600);
            }

            if (exists)
            {
                RunScCommand($"config {WindowsServiceName} binPath= \"{quotedBin}\" start= auto");
            }
            else
            {
                int createRc = RunScCommand(
                    $"create {WindowsServiceName} binPath= \"{quotedBin}\" start= auto DisplayName= \"HaloDeck Hardware Control Service\"");
                if (createRc != 0)
                {
                    return createRc;
                }
                RunScCommand($"description {WindowsServiceName} \"Provides signed PawnIO Ryzen SMU TDP & GPD EC fan control for HaloDeck without repeated UAC prompts.\"");
            }

            using var sc = new ServiceController(WindowsServiceName);
            if (sc.Status != ServiceControllerStatus.Running)
            {
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(8));
            }
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    public static int UninstallService()
    {
        try
        {
            var (exists, running) = ProbeWindowsServiceState(WindowsServiceName);
            if (!exists) return 0;

            if (running)
            {
                RunScCommand($"stop {WindowsServiceName}");
                Thread.Sleep(500);
            }
            return RunScCommand($"delete {WindowsServiceName}");
        }
        catch
        {
            return 1;
        }
    }

    public static int SetGpdServicePaused(bool paused)
    {
        try
        {
            var (exists, _) = ProbeWindowsServiceState("GPDToolService");
            if (!exists) return 0;

            if (paused)
            {
                RunScCommand("config GPDToolService start= demand");
                RunScCommand("stop GPDToolService");
                return 0;
            }
            else
            {
                RunScCommand("start GPDToolService");
                return 0;
            }
        }
        catch
        {
            return 1;
        }
    }

    public static (bool Exists, bool Running) ProbeWindowsServiceState(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            var status = sc.Status;
            return (true, status == ServiceControllerStatus.Running);
        }
        catch
        {
            return (false, false);
        }
    }

    private static int RunScCommand(string arguments)
    {
        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        if (proc is null) return 1;
        proc.WaitForExit(10000);
        return proc.ExitCode;
    }
}
