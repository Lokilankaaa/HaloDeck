using System.Runtime.InteropServices;

namespace HandheldOpti.App.Native;

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
    public int VramUsedMb { get; set; }
}

/// <summary>
/// User-mode AMD ADL2 GPU/APU telemetry provider via atiadlxx.dll (requires zero admin privileges).
/// Maintains a 60-sample circular history buffer for live monitor sparklines.
/// </summary>
public sealed class AmdGpuTelemetryService : IDisposable
{
    private const int AdlOk = 0;
    private const int HistoryCapacity = 60;

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

    private readonly List<int> _apuWattsHistory = new(HistoryCapacity);
    private readonly List<int> _gpuUsageHistory = new(HistoryCapacity);
    private readonly List<int> _cpuUsageHistory = new(HistoryCapacity);
    private readonly List<int> _tempHistory = new(HistoryCapacity);
    private DateTime _lastHistorySampleUtc = DateTime.MinValue;

    public AmdGpuTelemetryService()
    {
        TryInitializeAdl();
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
        var snap = new AmdTelemetrySnapshot { Available = _adlAvailable };
        lock (_lock)
        {
            if (_disposed) return snap;

            if (!_adlAvailable)
            {
                return snap;
            }

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
        return snap;
    }

    public void RecordHistoryPoint(AmdTelemetrySnapshot amd, int cpuUsagePercent)
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
            if (_context != 0)
            {
                try { ADL2_Main_Control_Destroy(_context); } catch { }
                _context = 0;
            }
            _adlAvailable = false;
        }
    }
}
