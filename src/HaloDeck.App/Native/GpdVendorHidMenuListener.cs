using System.Runtime.InteropServices;
using HaloDeck.Core.Services;
using Microsoft.Win32.SafeHandles;

namespace HaloDeck.App.Native;

/// <summary>
/// Dedicated overlapped reader for the GPD Win 5 Vendor HID collection (VID_2F24, UsagePage 0xFF00, 64B).
/// Mirrors GPDTool.exe's native listener so firmware batches that emit 01 A5/B4 ... [8]=0x68 only
/// on the vendor HID interrupt pipe (without emitting standard VK_F13 or XInput Guide) still trigger QAM.
/// </summary>
public sealed class GpdVendorHidMenuListener : IDisposable
{
    private const uint GenericReadWrite = 0xC0000000u;
    private const uint FileShareReadWrite = 0x00000003u;
    private const uint OpenExisting = 3u;
    private const uint FileFlagOverlapped = 0x40000000u;
    private const int ErrorIoPending = 997;
    private const uint WaitObject0 = 0x00000000u;
    private const uint WaitTimeout = 0x00000102u;

    private readonly CancellationTokenSource _cts = new();
    private readonly GpdVendorHidMenuParser _parser = new();
    private Task? _loopTask;

    public bool IsConnected { get; private set; }
    public string ConnectedDeviceLabel { get; private set; } = string.Empty;
    public string LastOpenError { get; private set; } = string.Empty;

    public event Action<byte[]>? RawReportReceived;
    public event Action? MenuButtonPressed;

    public void Start()
    {
        if (_loopTask is not null)
        {
            return;
        }
        _loopTask = Task.Factory.StartNew(
            () => RunLoop(_cts.Token),
            _cts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private void RunLoop(CancellationToken ct)
    {
        nint readBuf = Marshal.AllocHGlobal(64);
        nint ovPtr = Marshal.AllocHGlobal(Marshal.SizeOf<OVERLAPPED>());
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? devPath = GpdControllerService.FindVendorHidInputPath(out ushort vid, out ushort pid);
                if (string.IsNullOrWhiteSpace(devPath))
                {
                    IsConnected = false;
                    ConnectedDeviceLabel = string.Empty;
                    LastOpenError = "未检测到 VID_2F24 (UsagePage=0xFF00) 硬件通道";
                    _parser.Reset();
                    ct.WaitHandle.WaitOne(2000);
                    continue;
                }

                using var handle = CreateFile(
                    devPath,
                    GenericReadWrite,
                    FileShareReadWrite,
                    0,
                    OpenExisting,
                    FileFlagOverlapped,
                    0);

                if (handle.IsInvalid)
                {
                    IsConnected = false;
                    ConnectedDeviceLabel = string.Empty;
                    LastOpenError = $"打开 {vid:X4}:{pid:X4} HID 失败 (Win32={Marshal.GetLastWin32Error()})";
                    _parser.Reset();
                    ct.WaitHandle.WaitOne(2000);
                    continue;
                }

                nint hEvent = CreateEventW(0, true, false, null);
                if (hEvent == 0)
                {
                    IsConnected = false;
                    LastOpenError = $"CreateEvent 失败 (Win32={Marshal.GetLastWin32Error()})";
                    ct.WaitHandle.WaitOne(2000);
                    continue;
                }

                try
                {
                    IsConnected = true;
                    ConnectedDeviceLabel = $"{vid:X4}:{pid:X4} (UP=0xFF00)";
                    LastOpenError = string.Empty;
                    _parser.Reset();
                    ReadLoopForDevice(handle, hEvent, readBuf, ovPtr, ct);
                }
                finally
                {
                    IsConnected = false;
                    ConnectedDeviceLabel = string.Empty;
                    try { CancelIo(handle); } catch { }
                    CloseHandle(hEvent);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ovPtr);
            Marshal.FreeHGlobal(readBuf);
        }
    }

    private void ReadLoopForDevice(SafeFileHandle handle, nint hEvent, nint readBuf, nint ovPtr, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            ResetEvent(hEvent);
            var ov = new OVERLAPPED { hEvent = hEvent };
            Marshal.StructureToPtr(ov, ovPtr, false);

            bool immediate = ReadFile(handle, readBuf, 64, out uint bytesRead, ovPtr);
            if (!immediate)
            {
                int err = Marshal.GetLastWin32Error();
                if (err != ErrorIoPending)
                {
                    LastOpenError = $"ReadFile 失败 (Win32={err})";
                    return;
                }

                bool completed = false;
                while (!ct.IsCancellationRequested)
                {
                    uint wait = WaitForSingleObject(hEvent, 300);
                    if (wait == WaitObject0)
                    {
                        if (!GetOverlappedResult(handle, ovPtr, out bytesRead, false))
                        {
                            LastOpenError = $"GetOverlappedResult 失败 (Win32={Marshal.GetLastWin32Error()})";
                            return;
                        }
                        completed = true;
                        break;
                    }
                    if (wait != WaitTimeout)
                    {
                        LastOpenError = $"WaitForSingleObject 异常 (0x{wait:X8})";
                        break;
                    }
                }

                if (!completed)
                {
                    CancelIo(handle);
                    GetOverlappedResult(handle, ovPtr, out _, true);
                    return;
                }
            }

            if (bytesRead == 0)
            {
                continue;
            }

            int copyLen = (int)Math.Min(bytesRead, 64u);
            var packet = new byte[copyLen];
            Marshal.Copy(readBuf, packet, 0, copyLen);

            if (GpdControllerService.IsConfigTransferActive)
            {
                continue;
            }

            RawReportReceived?.Invoke(packet);
            if (_parser.ProcessReport(packet))
            {
                MenuButtonPressed?.Invoke();
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loopTask?.Wait(600);
        }
        catch { }
        _cts.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OVERLAPPED
    {
        public nuint Internal;
        public nuint InternalHigh;
        public uint Offset;
        public uint OffsetHigh;
        public nint hEvent;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(
        SafeFileHandle hFile,
        nint lpBuffer,
        uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead,
        nint lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOverlappedResult(
        SafeFileHandle hFile,
        nint lpOverlapped,
        out uint lpNumberOfBytesTransferred,
        [MarshalAs(UnmanagedType.Bool)] bool bWait);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelIo(SafeFileHandle hFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateEventW(
        nint lpEventAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bManualReset,
        [MarshalAs(UnmanagedType.Bool)] bool bInitialState,
        string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ResetEvent(nint hEvent);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);
}
