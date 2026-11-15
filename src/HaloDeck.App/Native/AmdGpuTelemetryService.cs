using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HaloDeck.App.Native;

public sealed class AmdTelemetrySnapshot
{
    public bool Available { get; set; }
    public string AdapterLabel { get; set; } = "AMD Radeon 8060S";
    public int GpuUsagePercent { get; set; }
    public int GfxClockMhz { get; set; }
    public int MemClockMhz { get; set; }
    public int SocClockMhz { get; set; }
    public int CpuClockMhz { get; set; }
    public int ApuPowerWatts { get; set; }
    public int GpuTempC { get; set; }
    public int SocTempC { get; set; }
    public int CpuTempC { get; set; }
    public bool CpuTempDirect { get; set; }
    public int VramUsedMb { get; set; }
    public int VramTotalMb { get; set; }
    public int SharedVramUsedMb { get; set; }
    public int SharedVramTotalMb { get; set; }
}

/// <summary>
/// User-mode AMD ADL2 + DXGI + Windows GPU Adapter Memory telemetry provider (zero admin privileges).
/// Maintains a 60-sample circular history buffer for live monitor sparklines.
/// </summary>
public sealed class AmdGpuTelemetryService : IDisposable
{
    private const int AdlOk = 0;
    private const int HistoryCapacity = 60;
    private static readonly Guid IidDxgiFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint AdlMainMallocCallback(int size);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_Main_Control_Create(AdlMainMallocCallback callback, int iEnumConnectedAdapters, out nint context);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_Main_Control_Destroy(nint context);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_Adapter_NumberOfAdapters_Get(nint context, out int lpNumAdapters);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_Adapter_DedicatedVRAMUsage_Get(nint context, int iAdapterIndex, out int lpVRAMUsage);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_New_QueryPMLogData_Get(nint context, int iAdapterIndex, ref ADLPMLogDataOutput lpDataOutput);

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(in Guid riid, out IntPtr ppFactory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(IntPtr factory, uint adapterIndex, out IntPtr adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Delegate(IntPtr adapter, out DXGI_ADAPTER_DESC1 desc);

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public LUID AdapterLuid;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ADLSingleSensorData
    {
        public int supported;
        public int value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ADLPMLogDataOutput
    {
        public int size;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ADLSingleSensorData[] sensors;
    }

    private static readonly AdlMainMallocCallback AllocDelegate = size => Marshal.AllocHGlobal(Math.Max(4, size));

    private readonly object _lock = new();
    private nint _context;
    private int _adapterIndex;
    private bool _adlAvailable;
    private bool _disposed;

    private string _dxgiAdapterLabel = "AMD Radeon 8060S";
    private string? _dxgiLuidPrefix;
    private int _dxgiDedicatedTotalMb;
    private int _dxgiSharedTotalMb;

    private PerformanceCounter? _vramDedicatedCounter;
    private PerformanceCounter? _vramSharedCounter;
    private DateTime _lastCounterInitUtc = DateTime.MinValue;
    private DateTime _lastVramPerfSampleUtc = DateTime.MinValue;
    private int _cachedPerfDedicatedMb;
    private int _cachedPerfSharedMb;

    private readonly List<int> _apuWattsHistory = new(HistoryCapacity);
    private readonly List<int> _gpuUsageHistory = new(HistoryCapacity);
    private readonly List<int> _cpuUsageHistory = new(HistoryCapacity);
    private readonly List<int> _tempHistory = new(HistoryCapacity);
    private readonly List<int> _fpsHistory = new(HistoryCapacity);
    private DateTime _lastHistorySampleUtc = DateTime.MinValue;

    public AmdGpuTelemetryService()
    {
        TryInitializeDxgiAdapterMetadata();
        TryInitializeAdl();
    }

    private void TryInitializeDxgiAdapterMetadata()
    {
        IntPtr factoryPtr = IntPtr.Zero;
        try
        {
            if (CreateDXGIFactory1(in IidDxgiFactory1, out factoryPtr) < 0 || factoryPtr == IntPtr.Zero)
            {
                return;
            }

            IntPtr vtable = Marshal.ReadIntPtr(factoryPtr);
            var enumAdapters1 = Marshal.GetDelegateForFunctionPointer<EnumAdapters1Delegate>(Marshal.ReadIntPtr(vtable, 12 * IntPtr.Size));

            nuint bestDed = 0;
            string bestDesc = _dxgiAdapterLabel;
            nuint bestShared = 0;
            LUID bestLuid = default;
            bool foundAmd = false;

            for (uint idx = 0; idx < 8; idx++)
            {
                if (enumAdapters1(factoryPtr, idx, out IntPtr adapterPtr) < 0 || adapterPtr == IntPtr.Zero)
                {
                    break;
                }

                try
                {
                    IntPtr aVtable = Marshal.ReadIntPtr(adapterPtr);
                    var getDesc1 = Marshal.GetDelegateForFunctionPointer<GetDesc1Delegate>(Marshal.ReadIntPtr(aVtable, 10 * IntPtr.Size));
                    if (getDesc1(adapterPtr, out var desc) >= 0 && (desc.Flags & 2) == 0)
                    {
                        bool isAmd = desc.VendorId == 0x1002;
                        if (isAmd && (!foundAmd || desc.DedicatedVideoMemory > bestDed))
                        {
                            foundAmd = true;
                            bestDed = desc.DedicatedVideoMemory;
                            bestShared = desc.SharedSystemMemory;
                            bestDesc = string.IsNullOrWhiteSpace(desc.Description) ? _dxgiAdapterLabel : desc.Description.Trim();
                            bestLuid = desc.AdapterLuid;
                        }
                        else if (!foundAmd && desc.DedicatedVideoMemory > bestDed)
                        {
                            bestDed = desc.DedicatedVideoMemory;
                            bestShared = desc.SharedSystemMemory;
                            bestDesc = string.IsNullOrWhiteSpace(desc.Description) ? _dxgiAdapterLabel : desc.Description.Trim();
                            bestLuid = desc.AdapterLuid;
                        }
                    }
                }
                finally
                {
                    Marshal.Release(adapterPtr);
                }
            }

            if (bestDed > 0)
            {
                _dxgiAdapterLabel = bestDesc;
                _dxgiDedicatedTotalMb = (int)Math.Round(bestDed / (1024.0 * 1024.0));
                _dxgiSharedTotalMb = (int)Math.Round(bestShared / (1024.0 * 1024.0));
                _dxgiLuidPrefix = $"luid_0x{unchecked((uint)bestLuid.HighPart):X8}_0x{bestLuid.LowPart:X8}_";
            }
        }
        catch
        {
            // Keep default DXGI metadata
        }
        finally
        {
            if (factoryPtr != IntPtr.Zero)
            {
                try { Marshal.Release(factoryPtr); } catch { }
            }
        }
    }

    private void EnsureVramCountersInitialized()
    {
        if (_vramDedicatedCounter is not null) return;
        if ((DateTime.UtcNow - _lastCounterInitUtc) < TimeSpan.FromSeconds(10)) return;
        _lastCounterInitUtc = DateTime.UtcNow;

        try
        {
            var cat = new PerformanceCounterCategory("GPU Adapter Memory");
            string[] instances = cat.GetInstanceNames();
            if (instances.Length == 0) return;

            string? chosen = null;
            if (!string.IsNullOrEmpty(_dxgiLuidPrefix))
            {
                chosen = instances.FirstOrDefault(i => i.StartsWith(_dxgiLuidPrefix, StringComparison.OrdinalIgnoreCase));
            }

            chosen ??= instances.FirstOrDefault(i => i.EndsWith("_phys_0", StringComparison.OrdinalIgnoreCase)) ?? instances[0];
            _vramDedicatedCounter = new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", chosen, readOnly: true);
            _vramSharedCounter = new PerformanceCounter("GPU Adapter Memory", "Shared Usage", chosen, readOnly: true);
            // Warm up initial values
            _vramDedicatedCounter.NextValue();
            _vramSharedCounter.NextValue();
        }
        catch
        {
            DisposeVramCounters();
        }
    }

    private void DisposeVramCounters()
    {
        try { _vramDedicatedCounter?.Dispose(); } catch { }
        try { _vramSharedCounter?.Dispose(); } catch { }
        _vramDedicatedCounter = null;
        _vramSharedCounter = null;
    }

    private void TryInitializeAdl()
    {
        try
        {
            if (!NativeLibrary.TryLoad("atiadlxx.dll", out _))
            {
                return;
            }

            if (ADL2_Main_Control_Create(AllocDelegate, 1, out _context) != AdlOk || _context == 0)
            {
                _context = 0;
                return;
            }

            int count = 0;
            if (ADL2_Adapter_NumberOfAdapters_Get(_context, out count) != AdlOk || count <= 0)
            {
                count = 1;
            }

            // Pick the first adapter reporting live sensors
            for (int i = 0; i < Math.Min(count, 8); i++)
            {
                var log = new ADLPMLogDataOutput { sensors = new ADLSingleSensorData[256] };
                log.size = Marshal.SizeOf<ADLPMLogDataOutput>();
                if (ADL2_New_QueryPMLogData_Get(_context, i, ref log) == AdlOk &&
                    (log.sensors[1].supported != 0 || log.sensors[19].supported != 0 || log.sensors[23].supported != 0))
                {
                    _adapterIndex = i;
                    _adlAvailable = true;
                    return;
                }
            }

            // Fallback to adapter 0 if present
            _adapterIndex = 0;
            _adlAvailable = true;
        }
        catch
        {
            _adlAvailable = false;
            _context = 0;
        }
    }

    public AmdTelemetrySnapshot Sample()
    {
        var snap = new AmdTelemetrySnapshot
        {
            Available = _adlAvailable,
            AdapterLabel = _dxgiAdapterLabel,
            VramTotalMb = _dxgiDedicatedTotalMb,
            SharedVramTotalMb = _dxgiSharedTotalMb
        };
        lock (_lock)
        {
            if (_disposed) return snap;

            try
            {
                if ((DateTime.UtcNow - _lastVramPerfSampleUtc) >= TimeSpan.FromMilliseconds(850))
                {
                    _lastVramPerfSampleUtc = DateTime.UtcNow;
                    EnsureVramCountersInitialized();
                    if (_vramDedicatedCounter is not null && _vramSharedCounter is not null)
                    {
                        float dedBytes = _vramDedicatedCounter.NextValue();
                        float shBytes = _vramSharedCounter.NextValue();
                        _cachedPerfDedicatedMb = Math.Max(0, (int)Math.Round(dedBytes / (1024.0 * 1024.0)));
                        _cachedPerfSharedMb = Math.Max(0, (int)Math.Round(shBytes / (1024.0 * 1024.0)));
                    }
                }
            }
            catch
            {
                DisposeVramCounters();
            }

            if (_adlAvailable)
            {
                try
                {
                    var log = new ADLPMLogDataOutput { sensors = new ADLSingleSensorData[256] };
                    log.size = Marshal.SizeOf<ADLPMLogDataOutput>();
                    if (ADL2_New_QueryPMLogData_Get(_context, _adapterIndex, ref log) == AdlOk && log.sensors is { Length: >= 36 })
                    {
                        snap.GfxClockMhz = ReadSensor(log.sensors, 1);
                        snap.MemClockMhz = ReadSensor(log.sensors, 2);
                        snap.SocClockMhz = ReadSensor(log.sensors, 3);
                        snap.GpuUsagePercent = Math.Clamp(ReadSensor(log.sensors, 19), 0, 100);
                        snap.ApuPowerWatts = Math.Max(0, ReadSensor(log.sensors, 23));
                        snap.GpuTempC = ReadSensor(log.sensors, 28);
                        snap.SocTempC = ReadSensor(log.sensors, 29);
                        int cpuTempRaw = ReadSensor(log.sensors, 32);
                        snap.CpuTempDirect = cpuTempRaw > 0;
                        snap.CpuTempC = cpuTempRaw > 0 ? cpuTempRaw : snap.SocTempC;
                        snap.CpuClockMhz = ReadSensor(log.sensors, 34);
                    }

                    if (ADL2_Adapter_DedicatedVRAMUsage_Get(_context, _adapterIndex, out int vramMb) == AdlOk && vramMb >= 0)
                    {
                        snap.VramUsedMb = vramMb;
                    }
                }
                catch
                {
                    // Keep defaults on transient driver reset
                }
            }

            if (_cachedPerfDedicatedMb > 0)
            {
                snap.VramUsedMb = _cachedPerfDedicatedMb;
            }
            snap.SharedVramUsedMb = _cachedPerfSharedMb;
            if (snap.VramTotalMb <= 0 && snap.VramUsedMb > 0)
            {
                snap.VramTotalMb = 16384;
            }
        }
        return snap;
    }

    public void RecordHistoryPoint(AmdTelemetrySnapshot amd, int cpuUsagePercent, int currentFps = 0)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastHistorySampleUtc) < TimeSpan.FromMilliseconds(900))
            {
                return;
            }
            _lastHistorySampleUtc = now;

            PushPoint(_apuWattsHistory, Math.Clamp(amd.ApuPowerWatts, 0, 140));
            PushPoint(_gpuUsageHistory, Math.Clamp(amd.GpuUsagePercent, 0, 100));
            PushPoint(_cpuUsageHistory, Math.Clamp(cpuUsagePercent, 0, 100));
            int peakTemp = Math.Max(amd.GpuTempC, amd.CpuTempC);
            PushPoint(_tempHistory, Math.Clamp(peakTemp, 0, 115));
            PushPoint(_fpsHistory, Math.Clamp(currentFps, 0, 500));
        }
    }

    public (int[] ApuWatts, int[] GpuUsage, int[] CpuUsage, int[] TempC) GetHistoryArrays()
    {
        lock (_lock)
        {
            return (
                _apuWattsHistory.ToArray(),
                _gpuUsageHistory.ToArray(),
                _cpuUsageHistory.ToArray(),
                _tempHistory.ToArray());
        }
    }

    public int[] GetFpsHistory()
    {
        lock (_lock)
        {
            return _fpsHistory.ToArray();
        }
    }

    private static void PushPoint(List<int> list, int val)
    {
        if (list.Count >= HistoryCapacity)
        {
            list.RemoveAt(0);
        }
        list.Add(val);
    }

    private static int ReadSensor(ADLSingleSensorData[] sensors, int index) =>
        (index >= 0 && index < sensors.Length && sensors[index].supported != 0)
            ? sensors[index].value
            : 0;

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            DisposeVramCounters();
            if (_context != 0)
            {
                try { ADL2_Main_Control_Destroy(_context); } catch { }
                _context = 0;
            }
            _adlAvailable = false;
        }
    }
}
