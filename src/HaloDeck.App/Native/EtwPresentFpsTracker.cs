using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace HaloDeck.App.Native;

/// <summary>
/// Hook-free real-time ETW Present frame-rate tracker (DXGI / D3D9 / DxgKrnl).
/// Zero DLL injection, zero process memory access, and anti-cheat safe.
/// Runs inside HaloDeckHardwareSvc (Session-0 LocalSystem) or an elevated host.
/// </summary>
public sealed class EtwPresentFpsTracker : IDisposable
{
    private const string SessionName = "HaloDeckFpsTrace";
    private const uint EventTraceControlStop = 1;
    private const uint EventControlCodeEnableProvider = 1;
    private const byte TraceLevelInformation = 4;

    private const uint WnodeFlagTracedGuid = 0x00020000;
    private const uint EventTraceRealTimeMode = 0x00000100;
    private const uint EventTraceNoPerProcessorBuffering = 0x10000000;
    private const uint ProcessTraceModeRealTime = 0x00000100;
    private const uint ProcessTraceModeEventRecord = 0x10000000;
    private const ulong InvalidProcessTraceHandle = ulong.MaxValue;

    // Live Windows 11 ETW Present provider GUIDs (DXGI -> D3D9 -> DxgKrnl fallback)
    private static readonly Guid DxgiProviderGuid = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3d9ProviderGuid = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private static readonly Guid DxgKrnlProviderGuid = new("802EC45A-1E99-4B83-9920-87C98277BA9D");

    private const int SourceDxgi = 0;
    private const int SourceD3d9 = 1;
    private const int SourceDxgKrnl = 2;
    private const int SourceCount = 3;
    private const int RingCapacity = 240;

    private static readonly HashSet<string> ExcludedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "idle",
        "system",
        "dwm",
        "csrss",
        "svchost",
        "explorer",
        "halodeck",
        "HaloDeck.App",
        "msedgewebview2",
        "shellexperiencehost",
        "searchhost",
        "startmenuexperiencehost",
        "applicationframehost",
        "textinputhost",
        "tabtip",
        "gpdkeyboard",
        "osk",
        "lockapp",
        "systemsettings",
        "taskmgr",
        "perfmon",
        "sihost",
        "ctfmon"
    };

    private delegate uint EventTraceBufferCallback(IntPtr logfile);
    private delegate void EventRecordCallback(IntPtr eventRecord);

    [StructLayout(LayoutKind.Sequential)]
    private struct WNODE_HEADER
    {
        public uint BufferSize;
        public uint ProviderId;
        public ulong HistoricalContext;
        public long TimeStamp;
        public Guid Guid;
        public uint ClientContext;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EVENT_TRACE_PROPERTIES
    {
        public WNODE_HEADER Wnode;
        public uint BufferSize;
        public uint MinimumBuffers;
        public uint MaximumBuffers;
        public uint MaximumFileSize;
        public uint LogFileMode;
        public uint FlushTimer;
        public uint EnableFlags;
        public int AgeLimit;
        public uint NumberOfBuffers;
        public uint FreeBuffers;
        public uint EventsLost;
        public uint BuffersWritten;
        public uint LogBuffersLost;
        public uint RealTimeBuffersLost;
        public IntPtr LoggerThreadId;
        public uint LogFileNameOffset;
        public uint LoggerNameOffset;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EVENT_TRACE_HEADER
    {
        public ushort Size;
        public ushort FieldTypeFlags;
        public uint Version;
        public uint ThreadId;
        public uint ProcessId;
        public long TimeStamp;
        public Guid Guid;
        public ulong ProcessorTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EVENT_TRACE
    {
        public EVENT_TRACE_HEADER Header;
        public uint InstanceId;
        public uint ParentInstanceId;
        public Guid ParentGuid;
        public IntPtr MofData;
        public uint MofLength;
        public uint ClientContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEMTIME
    {
        public ushort Year;
        public ushort Month;
        public ushort DayOfWeek;
        public ushort Day;
        public ushort Hour;
        public ushort Minute;
        public ushort Second;
        public ushort Milliseconds;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TIME_ZONE_INFORMATION
    {
        public int Bias;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string StandardName;
        public SYSTEMTIME StandardDate;
        public int StandardBias;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DaylightName;
        public SYSTEMTIME DaylightDate;
        public int DaylightBias;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TRACE_LOGFILE_HEADER
    {
        public uint BufferSize;
        public uint Version;
        public uint ProviderVersion;
        public uint NumberOfProcessors;
        public long EndTime;
        public uint TimerResolution;
        public uint MaximumFileSize;
        public uint LogFileMode;
        public uint BuffersWritten;
        public Guid LogInstanceGuid;
        public IntPtr LoggerName;
        public IntPtr LogFileName;
        public TIME_ZONE_INFORMATION TimeZone;
        public long BootTime;
        public long PerfFreq;
        public long StartTime;
        public uint ReservedFlags;
        public uint BuffersLost;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct EVENT_TRACE_LOGFILEW
    {
        public string? LogFileName;
        public string? LoggerName;
        public long CurrentTime;
        public uint BuffersRead;
        public uint ProcessTraceMode;
        public EVENT_TRACE CurrentEvent;
        public TRACE_LOGFILE_HEADER LogfileHeader;
        public EventTraceBufferCallback? BufferCallback;
        public uint BufferSize;
        public uint Filled;
        public uint EventsLost;
        public EventRecordCallback? EventRecordCallback;
        public uint IsKernelTrace;
        public IntPtr Context;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint StartTraceW(out ulong sessionHandle, string instanceName, IntPtr properties);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint ControlTraceW(ulong sessionHandle, string instanceName, IntPtr properties, uint controlCode);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern uint EnableTraceEx2(
        ulong traceHandle,
        in Guid providerId,
        uint controlCode,
        byte level,
        ulong matchAnyKeyword,
        ulong matchAllKeyword,
        uint timeout,
        IntPtr enableParameters);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern ulong OpenTraceW(ref EVENT_TRACE_LOGFILEW logfile);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern uint ProcessTrace(
        [In] ulong[] handleArray,
        uint handleCount,
        IntPtr startTime,
        IntPtr endTime);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern uint CloseTrace(ulong traceHandle);

    private sealed class SourceRing
    {
        public readonly long[] Samples = new long[RingCapacity];
        public int WriteIndex;
        public int Count;
        public long LastTimestamp;

        public void Add(long ts)
        {
            Samples[WriteIndex] = ts;
            WriteIndex = (WriteIndex + 1) % RingCapacity;
            if (Count < RingCapacity) Count++;
            LastTimestamp = ts;
        }
    }

    private sealed class PidBucket
    {
        public readonly object Sync = new();
        public readonly SourceRing[] Rings = { new(), new(), new() };
    }

    private readonly ConcurrentDictionary<int, PidBucket> _pidBuckets = new();
    private readonly ConcurrentDictionary<int, (bool Excluded, DateTime ExpiresAtUtc)> _pidFilterCache = new();
    private readonly object _lifecycleLock = new();

    private ulong _sessionHandle;
    private ulong _traceHandle = InvalidProcessTraceHandle;
    private Thread? _consumerThread;
    private volatile bool _disposed;
    private volatile bool _running;

    // Keep delegates rooted on the instance so GC cannot collect them while ProcessTrace runs
    private readonly EventRecordCallback _recordCallback;
    private readonly EventTraceBufferCallback _bufferCallback;

    public bool IsRunning => _running;

    public EtwPresentFpsTracker()
    {
        _recordCallback = OnEventRecord;
        _bufferCallback = _ => _disposed ? 0u : 1u;
    }

    public void Start()
    {
        lock (_lifecycleLock)
        {
            if (_disposed || _running) return;

            IntPtr propsPtr = IntPtr.Zero;
            try
            {
                StopExistingSession();
                propsPtr = AllocateSessionProperties(out uint totalBytes);
                uint rc = StartTraceW(out _sessionHandle, SessionName, propsPtr);
                if (rc != 0 || _sessionHandle == 0)
                {
                    _sessionHandle = 0;
                    return;
                }

                // Enable DXGI (SwapChain Present), D3D9 (IDirect3DDevice9 Present), and DxgKrnl fallback
                EnableTraceEx2(_sessionHandle, in DxgiProviderGuid, EventControlCodeEnableProvider, TraceLevelInformation, 0x8000000000000002UL, 0, 0, IntPtr.Zero);
                EnableTraceEx2(_sessionHandle, in D3d9ProviderGuid, EventControlCodeEnableProvider, TraceLevelInformation, 0x8000000000000002UL, 0, 0, IntPtr.Zero);
                EnableTraceEx2(_sessionHandle, in DxgKrnlProviderGuid, EventControlCodeEnableProvider, TraceLevelInformation, 0x4000000008000001UL, 0, 0, IntPtr.Zero);

                var logfile = new EVENT_TRACE_LOGFILEW
                {
                    LoggerName = SessionName,
                    ProcessTraceMode = ProcessTraceModeRealTime | ProcessTraceModeEventRecord,
                    BufferCallback = _bufferCallback,
                    EventRecordCallback = _recordCallback
                };

                _traceHandle = OpenTraceW(ref logfile);
                if (_traceHandle == InvalidProcessTraceHandle || _traceHandle == 0)
                {
                    _traceHandle = InvalidProcessTraceHandle;
                    StopExistingSession();
                    _sessionHandle = 0;
                    return;
                }

                _running = true;
                ulong localTraceHandle = _traceHandle;
                _consumerThread = new Thread(() => RunProcessTraceLoop(localTraceHandle))
                {
                    IsBackground = true,
                    Name = "HaloDeck-EtwFps"
                };
                _consumerThread.Start();
            }
            catch
            {
                _running = false;
            }
            finally
            {
                if (propsPtr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(propsPtr);
                }
            }
        }
    }

    public (int Fps, double FrameTimeMs, int SourcePid) GetCurrentFps(int preferredPid = 0)
    {
        if (!_running)
        {
            return (0, 0.0, 0);
        }

        long now = Stopwatch.GetTimestamp();
        long staleCutoff = (long)(1.5 * Stopwatch.Frequency);

        if (preferredPid > 4 && !IsExcludedProcess(preferredPid) && _pidBuckets.TryGetValue(preferredPid, out var prefBucket))
        {
            var prefSample = SampleBucketFps(prefBucket, now, staleCutoff);
            if (prefSample.Fps > 0)
            {
                return (prefSample.Fps, prefSample.FrameTimeMs, preferredPid);
            }
        }

        int bestFps = 0;
        double bestFrameTime = 0.0;
        int bestPid = 0;
        long bestLastTs = 0;

        foreach (var kv in _pidBuckets)
        {
            int pid = kv.Key;
            if (pid <= 4 || IsExcludedProcess(pid))
            {
                continue;
            }

            var sample = SampleBucketFps(kv.Value, now, staleCutoff);
            if (sample.Fps <= 0)
            {
                continue;
            }

            // Prefer the most recently active or highest FPS foreground renderer
            if (sample.Fps > bestFps || (sample.Fps == bestFps && sample.LastTs > bestLastTs))
            {
                bestFps = sample.Fps;
                bestFrameTime = sample.FrameTimeMs;
                bestPid = pid;
                bestLastTs = sample.LastTs;
            }
        }

        return (bestFps, bestFrameTime, bestPid);
    }

    private static (int Fps, double FrameTimeMs, long LastTs) SampleBucketFps(PidBucket bucket, long now, long staleCutoff)
    {
        long window1s = Stopwatch.Frequency;
        long window2s = Stopwatch.Frequency * 2;

        lock (bucket.Sync)
        {
            // Strict priority order prevents double-counting when DXGI and DxgKrnl fire for the same Present call
            for (int src = 0; src < SourceCount; src++)
            {
                var ring = bucket.Rings[src];
                if (ring.Count < 3 || (now - ring.LastTimestamp) > staleCutoff)
                {
                    continue;
                }

                long newest = 0;
                long oldest = long.MaxValue;
                int inWindow = 0;

                for (int i = 0; i < ring.Count; i++)
                {
                    long ts = ring.Samples[i];
                    if (now - ts <= window1s)
                    {
                        inWindow++;
                        if (ts > newest) newest = ts;
                        if (ts < oldest) oldest = ts;
                    }
                }

                // Fallback 2s window for low-FPS menus/cutscenes
                if (inWindow < 3)
                {
                    newest = 0;
                    oldest = long.MaxValue;
                    inWindow = 0;
                    for (int i = 0; i < ring.Count; i++)
                    {
                        long ts = ring.Samples[i];
                        if (now - ts <= window2s)
                        {
                            inWindow++;
                            if (ts > newest) newest = ts;
                            if (ts < oldest) oldest = ts;
                        }
                    }
                }

                if (inWindow >= 3 && newest > oldest)
                {
                    double seconds = (newest - oldest) / (double)Stopwatch.Frequency;
                    double rawFps = (inWindow - 1) / seconds;
                    int rounded = Math.Clamp((int)Math.Round(rawFps), 1, 500);
                    double frameMs = Math.Round(1000.0 / Math.Max(1.0, rawFps), 1);
                    return (rounded, frameMs, ring.LastTimestamp);
                }
            }
        }

        return (0, 0.0, 0);
    }

    private void RunProcessTraceLoop(ulong traceHandle)
    {
        try
        {
            ProcessTrace(new[] { traceHandle }, 1, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // Trace ended or OS closed handle
        }
        finally
        {
            _running = false;
        }
    }

    private void OnEventRecord(IntPtr eventRecordPtr)
    {
        if (_disposed || eventRecordPtr == IntPtr.Zero) return;

        // EVENT_HEADER offsets (80B total on x64):
        // 12 = ProcessId (int), 24 = ProviderId (Guid, 16B), 45 = Opcode (byte), 46 = Task (ushort)
        int pid = Marshal.ReadInt32(eventRecordPtr, 12);
        if (pid <= 4 || IsExcludedProcess(pid)) return;

        byte opcode = Marshal.ReadByte(eventRecordPtr, 45);
        if (opcode != 1) return; // win:Start only

        ushort task = unchecked((ushort)Marshal.ReadInt16(eventRecordPtr, 46));
        Guid provider = Marshal.PtrToStructure<Guid>(eventRecordPtr + 24);

        int source;
        if (provider == DxgiProviderGuid && task == 9)
        {
            source = SourceDxgi;
        }
        else if (provider == D3d9ProviderGuid && task == 1)
        {
            source = SourceD3d9;
        }
        else if (provider == DxgKrnlProviderGuid && task == 6)
        {
            source = SourceDxgKrnl;
        }
        else
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        var bucket = _pidBuckets.GetOrAdd(pid, _ => new PidBucket());
        lock (bucket.Sync)
        {
            bucket.Rings[source].Add(now);
        }
    }

    private bool IsExcludedProcess(int pid)
    {
        var now = DateTime.UtcNow;
        if (_pidFilterCache.TryGetValue(pid, out var cached) && now < cached.ExpiresAtUtc)
        {
            return cached.Excluded;
        }

        bool excluded = false;
        try
        {
            using var proc = Process.GetProcessById(pid);
            excluded = ExcludedProcessNames.Contains(proc.ProcessName);
        }
        catch
        {
            excluded = true;
        }

        _pidFilterCache[pid] = (excluded, now.AddSeconds(30));
        if (_pidFilterCache.Count > 512)
        {
            foreach (var key in _pidFilterCache.Keys)
            {
                if (_pidFilterCache.TryGetValue(key, out var item) && now >= item.ExpiresAtUtc)
                {
                    _pidFilterCache.TryRemove(key, out _);
                }
            }
        }
        return excluded;
    }

    private static IntPtr AllocateSessionProperties(out uint totalBytes)
    {
        int propsSize = Marshal.SizeOf<EVENT_TRACE_PROPERTIES>();
        byte[] nameBytes = Encoding.Unicode.GetBytes(SessionName + "\0");
        totalBytes = (uint)(propsSize + Math.Max(256, nameBytes.Length));
        IntPtr ptr = Marshal.AllocHGlobal((int)totalBytes);
        for (int i = 0; i < totalBytes; i += 8)
        {
            int rem = (int)totalBytes - i;
            if (rem >= 8) Marshal.WriteInt64(ptr, i, 0);
            else
            {
                for (int b = 0; b < rem; b++) Marshal.WriteByte(ptr, i + b, 0);
            }
        }

        var props = new EVENT_TRACE_PROPERTIES
        {
            Wnode = new WNODE_HEADER
            {
                BufferSize = totalBytes,
                ClientContext = 1, // QPC timestamps
                Flags = WnodeFlagTracedGuid
            },
            BufferSize = 64,
            MinimumBuffers = 4,
            MaximumBuffers = 16,
            LogFileMode = EventTraceRealTimeMode | EventTraceNoPerProcessorBuffering,
            FlushTimer = 1,
            LoggerNameOffset = (uint)propsSize
        };
        Marshal.StructureToPtr(props, ptr, false);
        Marshal.Copy(nameBytes, 0, ptr + propsSize, nameBytes.Length);
        return ptr;
    }

    private static void StopExistingSession()
    {
        IntPtr ptr = IntPtr.Zero;
        try
        {
            ptr = AllocateSessionProperties(out _);
            ControlTraceW(0, SessionName, ptr, EventTraceControlStop);
        }
        catch
        {
            // Session did not exist
        }
        finally
        {
            if (ptr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _running = false;

        lock (_lifecycleLock)
        {
            if (_traceHandle != InvalidProcessTraceHandle && _traceHandle != 0)
            {
                try { CloseTrace(_traceHandle); } catch { }
                _traceHandle = InvalidProcessTraceHandle;
            }

            StopExistingSession();
            _sessionHandle = 0;

            try { _consumerThread?.Join(500); } catch { }
            _consumerThread = null;
        }
    }
}
