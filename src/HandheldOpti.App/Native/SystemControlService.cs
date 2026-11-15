using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace HandheldOpti.App.Native;

public sealed class SystemControlState
{
    public string CpuName { get; set; } = "AMD Strix Halo APU";
    public int CpuUsagePercent { get; set; }
    public double TotalRamGb { get; set; }
    public double UsedRamGb { get; set; }
    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }
    public int RefreshRateHz { get; set; }
    public List<int> SupportedRefreshRates { get; set; } = new();
    public List<string> SupportedResolutions { get; set; } = new();
    public bool BrightnessSupported { get; set; }
    public int BrightnessPercent { get; set; } = 70;
    public int VolumePercent { get; set; } = 50;
    public bool IsMuted { get; set; }
    public string CpuBoostMode { get; set; } = "aggressive"; // "off" | "efficient" | "aggressive"
    public bool IsAcOnline { get; set; }
    public bool HasBattery { get; set; } = true;
    public bool IsCharging { get; set; }
    public int BatteryPercent { get; set; } = 100;
    public double? BatteryPowerWatts { get; set; }
    public double BatteryRemainingWh { get; set; }
}

/// <summary>
/// Provides native Windows / WMI / CoreAudio / PowrProf handheld controls (display Hz/res,
/// brightness, volume, Zen 5 CPU boost policy, and battery/power telemetry).
/// </summary>
public sealed class SystemControlService
{
    private const int EnumCurrentSettings = -1;
    private const int DispChangeSuccessful = 0;
    private const uint CdsUpdateRegistry = 0x00000001;
    private const uint DmPelsWidth = 0x00080000;
    private const uint DmPelsHeight = 0x00100000;
    private const uint DmDisplayFrequency = 0x00400000;

    private static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid PerfBoostMode = new("be337238-0d82-4146-a960-4f3749d470c7");

    private static readonly (int Width, int Height)[] StandardResolutions =
    {
        (1920, 1080),
        (1600, 900),
        (1280, 720)
    };

    private readonly string _cpuName;
    private readonly object _cpuLock = new();
    private readonly object _audioLock = new();
    private readonly object _wmiCacheLock = new();

    private bool _hasPrevCpuTimes;
    private ulong _prevIdle;
    private ulong _prevKernel;
    private ulong _prevUser;
    private int _lastCpuPercent;

    private DateTime _wmiCachedAtUtc = DateTime.MinValue;
    private bool _cachedBrightnessSupported;
    private int _cachedBrightnessPercent = 70;
    private bool _cachedIsCharging;
    private double? _cachedBatteryWatts;
    private double _cachedRemainingWh;

    public SystemControlService()
    {
        _cpuName = ReadCpuNameFromRegistry();
        // Seed initial GetSystemTimes baseline so the next GetState call yields a valid delta
        SampleCpuUsagePercent();
    }

    public SystemControlState GetState()
    {
        var state = new SystemControlState
        {
            CpuName = _cpuName,
            CpuUsagePercent = SampleCpuUsagePercent()
        };

        PopulateMemoryStats(state);
        PopulateDisplayInfo(state);
        PopulatePowerStatus(state);
        PopulateWmiCachedInfo(state);
        PopulateAudioInfo(state);
        state.CpuBoostMode = ReadCpuBoostMode(state.IsAcOnline);

        return state;
    }

    public SystemControlState SetRefreshRate(int targetHz)
    {
        if (targetHz < 30 || targetHz > 360)
        {
            throw new ArgumentOutOfRangeException(nameof(targetHz), "不支持的目标刷新率。");
        }

        var devName = ResolvePrimaryDisplayDevice();
        var dm = CreateDevMode();
        if (!EnumDisplaySettingsA(devName, EnumCurrentSettings, ref dm))
        {
            throw new InvalidOperationException("无法读取当前显示模式。");
        }

        var supported = EnumerateRefreshRatesForResolution(devName, dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayOrientation);
        if (supported.Count > 0 && !supported.Contains(targetHz))
        {
            throw new InvalidOperationException($"当前分辨率 {dm.dmPelsWidth}x{dm.dmPelsHeight} 不支持 {targetHz}Hz。");
        }

        dm.dmDisplayFrequency = (uint)targetHz;
        dm.dmFields = DmDisplayFrequency;
        ApplyDisplayMode(devName, ref dm);

        return GetState();
    }

    public SystemControlState SetResolution(int width, int height)
    {
        if (width < 640 || height < 480)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "不支持的目标分辨率。");
        }

        var devName = ResolvePrimaryDisplayDevice();
        var current = CreateDevMode();
        if (!EnumDisplaySettingsA(devName, EnumCurrentSettings, ref current))
        {
            throw new InvalidOperationException("无法读取当前显示模式。");
        }

        var targetRates = EnumerateRefreshRatesForResolution(devName, (uint)width, (uint)height, current.dmDisplayOrientation);
        if (targetRates.Count == 0)
        {
            throw new InvalidOperationException($"主显示器不支持 {width}×{height} 分辨率。");
        }

        int currentHz = (int)current.dmDisplayFrequency;
        int chosenHz = targetRates.Contains(currentHz)
            ? currentHz
            : targetRates.Where(hz => hz <= 120).DefaultIfEmpty(targetRates[^1]).Max();

        current.dmPelsWidth = (uint)width;
        current.dmPelsHeight = (uint)height;
        current.dmDisplayFrequency = (uint)chosenHz;
        current.dmFields = DmPelsWidth | DmPelsHeight | DmDisplayFrequency;
        ApplyDisplayMode(devName, ref current);

        return GetState();
    }

    public SystemControlState SetBrightness(int percent)
    {
        int clamped = Math.Clamp(percent, 5, 100);
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods");
            using var results = searcher.Get();
            bool applied = false;
            foreach (ManagementBaseObject obj in results)
            {
                using (obj)
                {
                    if (obj is ManagementObject mo)
                    {
                        mo.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)clamped });
                        applied = true;
                    }
                }
            }

            if (!applied)
            {
                throw new InvalidOperationException("当前显示器不支持 WMI 硬件亮度调节（可能是外接屏幕）。");
            }
        }
        catch (ManagementException ex)
        {
            throw new InvalidOperationException($"无法调节内置屏幕亮度：{ex.Message}", ex);
        }

        lock (_wmiCacheLock)
        {
            _cachedBrightnessSupported = true;
            _cachedBrightnessPercent = clamped;
            _wmiCachedAtUtc = DateTime.UtcNow;
        }

        return GetState();
    }

    public SystemControlState SetVolume(int percent)
    {
        int clamped = Math.Clamp(percent, 0, 100);
        WithAudioEndpoint(vol =>
        {
            var ctx = Guid.Empty;
            int hr = vol.SetMasterVolumeLevelScalar(clamped / 100f, ref ctx);
            Marshal.ThrowExceptionForHR(hr);
            if (clamped > 0 && vol.GetMute(out bool isMuted) == 0 && isMuted)
            {
                vol.SetMute(false, ref ctx);
            }
            return true;
        });

        return GetState();
    }

    public SystemControlState SetMute(bool muted)
    {
        WithAudioEndpoint(vol =>
        {
            var ctx = Guid.Empty;
            int hr = vol.SetMute(muted, ref ctx);
            Marshal.ThrowExceptionForHR(hr);
            return true;
        });

        return GetState();
    }

    public SystemControlState SetCpuBoostMode(string mode)
    {
        var normalized = (mode ?? "aggressive").Trim().ToLowerInvariant();
        uint index = normalized switch
        {
            "off" or "disabled" => 0u,
            "efficient" or "efficient_aggressive" => 4u,
            "aggressive" or "on" => 2u,
            _ => throw new ArgumentException($"未知的 CPU 睿频模式: {mode}", nameof(mode))
        };

        Guid scheme = GetActivePowerSchemeGuid();
        var sub = SubProcessor;
        var setting = PerfBoostMode;

        uint rcAc = PowerWriteACValueIndex(0, ref scheme, ref sub, ref setting, index);
        uint rcDc = PowerWriteDCValueIndex(0, ref scheme, ref sub, ref setting, index);
        if (rcAc != 0 && rcDc != 0)
        {
            throw new InvalidOperationException($"写入 CPU 睿频电源设置失败（AC={rcAc}, DC={rcDc}）。");
        }

        uint rcApply = PowerSetActiveScheme(0, ref scheme);
        if (rcApply != 0)
        {
            throw new InvalidOperationException($"应用 CPU 睿频设置失败（错误码 {rcApply}）。");
        }

        return GetState();
    }

    private static string ReadCpuNameFromRegistry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            var raw = key?.GetValue("ProcessorNameString") as string;
            if (!string.IsNullOrWhiteSpace(raw))
            {
                return raw.Trim();
            }
        }
        catch
        {
            // Fall back to default label
        }

        return "AMD Strix Halo APU";
    }

    private int SampleCpuUsagePercent()
    {
        lock (_cpuLock)
        {
            if (!GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
            {
                return _lastCpuPercent;
            }

            ulong idle = FileTimeToUInt64(idleFt);
            ulong kernel = FileTimeToUInt64(kernelFt);
            ulong user = FileTimeToUInt64(userFt);

            if (_hasPrevCpuTimes)
            {
                ulong idleDelta = idle >= _prevIdle ? idle - _prevIdle : 0;
                ulong kernelDelta = kernel >= _prevKernel ? kernel - _prevKernel : 0;
                ulong userDelta = user >= _prevUser ? user - _prevUser : 0;
                ulong total = kernelDelta + userDelta;
                if (total > 0)
                {
                    ulong busy = total >= idleDelta ? total - idleDelta : 0;
                    _lastCpuPercent = Math.Clamp((int)Math.Round(busy * 100.0 / total), 0, 100);
                }
            }

            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;
            _hasPrevCpuTimes = true;
            return _lastCpuPercent;
        }
    }

    private static void PopulateMemoryStats(SystemControlState state)
    {
        var stat = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref stat))
        {
            return;
        }

        const double gb = 1024.0 * 1024.0 * 1024.0;
        double total = stat.ullTotalPhys / gb;
        double avail = stat.ullAvailPhys / gb;
        state.TotalRamGb = Math.Round(total, 1);
        state.UsedRamGb = Math.Round(Math.Max(0, total - avail), 1);
    }

    private static void PopulateDisplayInfo(SystemControlState state)
    {
        try
        {
            var devName = ResolvePrimaryDisplayDevice();
            var dm = CreateDevMode();
            if (!EnumDisplaySettingsA(devName, EnumCurrentSettings, ref dm))
            {
                return;
            }

            state.ScreenWidth = (int)dm.dmPelsWidth;
            state.ScreenHeight = (int)dm.dmPelsHeight;
            state.RefreshRateHz = (int)dm.dmDisplayFrequency;

            var rates = EnumerateRefreshRatesForResolution(devName, dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayOrientation);
            if (rates.Count == 0 && state.RefreshRateHz > 0)
            {
                rates.Add(state.RefreshRateHz);
            }
            state.SupportedRefreshRates = rates;

            var supportedRes = new List<string>();
            var probe = CreateDevMode();
            var seenRes = new HashSet<(uint W, uint H)>();
            for (int i = 0; EnumDisplaySettingsA(devName, i, ref probe); i++)
            {
                if (probe.dmBitsPerPel == 32 && probe.dmDisplayOrientation == dm.dmDisplayOrientation)
                {
                    seenRes.Add((probe.dmPelsWidth, probe.dmPelsHeight));
                }
            }

            foreach (var (w, h) in StandardResolutions)
            {
                if (seenRes.Contains(((uint)w, (uint)h)))
                {
                    supportedRes.Add($"{w}x{h}");
                }
            }

            var currentTag = $"{dm.dmPelsWidth}x{dm.dmPelsHeight}";
            if (!supportedRes.Contains(currentTag) && dm.dmPelsWidth > 0 && dm.dmPelsHeight > 0)
            {
                supportedRes.Insert(0, currentTag);
            }
            state.SupportedResolutions = supportedRes;
        }
        catch
        {
            // Keep default values on headless sessions
        }
    }

    private static List<int> EnumerateRefreshRatesForResolution(string? devName, uint width, uint height, uint orientation)
    {
        var rates = new SortedSet<int>();
        var dm = CreateDevMode();
        for (int i = 0; EnumDisplaySettingsA(devName, i, ref dm); i++)
        {
            if (dm.dmBitsPerPel == 32 &&
                dm.dmDisplayOrientation == orientation &&
                dm.dmPelsWidth == width &&
                dm.dmPelsHeight == height &&
                dm.dmDisplayFrequency >= 40)
            {
                rates.Add((int)dm.dmDisplayFrequency);
            }
        }
        return rates.ToList();
    }

    private static void ApplyDisplayMode(string? devName, ref DEVMODEA dm)
    {
        int rc = ChangeDisplaySettingsExA(devName, ref dm, 0, CdsUpdateRegistry, 0);
        if (rc != DispChangeSuccessful)
        {
            rc = ChangeDisplaySettingsExA(devName, ref dm, 0, 0, 0);
        }

        if (rc != DispChangeSuccessful)
        {
            throw new InvalidOperationException($"切换显示模式失败（DISP_CHANGE={rc}）。");
        }
    }

    private static string? ResolvePrimaryDisplayDevice()
    {
        try
        {
            return Screen.PrimaryScreen?.DeviceName;
        }
        catch
        {
            return null;
        }
    }

    private static DEVMODEA CreateDevMode() => new()
    {
        dmSize = (ushort)Marshal.SizeOf<DEVMODEA>()
    };

    private static void PopulatePowerStatus(SystemControlState state)
    {
        if (!GetSystemPowerStatus(out var sps))
        {
            return;
        }

        state.IsAcOnline = sps.ACLineStatus == 1;
        state.HasBattery = (sps.BatteryFlag & 128) == 0;
        state.BatteryPercent = sps.BatteryLifePercent <= 100 ? sps.BatteryLifePercent : 100;
        state.IsCharging = state.HasBattery && ((sps.BatteryFlag & 8) != 0);
    }

    private void PopulateWmiCachedInfo(SystemControlState state)
    {
        lock (_wmiCacheLock)
        {
            if ((DateTime.UtcNow - _wmiCachedAtUtc) > TimeSpan.FromSeconds(3))
            {
                RefreshWmiCacheLocked();
            }

            state.BrightnessSupported = _cachedBrightnessSupported;
            state.BrightnessPercent = _cachedBrightnessPercent;
            if (_cachedIsCharging)
            {
                state.IsCharging = true;
            }
            state.BatteryPowerWatts = _cachedBatteryWatts;
            state.BatteryRemainingWh = _cachedRemainingWh;
        }
    }

    private void RefreshWmiCacheLocked()
    {
        _cachedBrightnessSupported = false;
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            using var results = searcher.Get();
            foreach (ManagementBaseObject obj in results)
            {
                using (obj)
                {
                    if (obj["CurrentBrightness"] is { } raw)
                    {
                        _cachedBrightnessSupported = true;
                        _cachedBrightnessPercent = Math.Clamp(Convert.ToInt32(raw), 0, 100);
                        break;
                    }
                }
            }
        }
        catch
        {
            _cachedBrightnessSupported = false;
        }

        _cachedIsCharging = false;
        _cachedBatteryWatts = null;
        _cachedRemainingWh = 0;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\wmi",
                "SELECT Discharging, Charging, DischargeRate, ChargeRate, RemainingCapacity FROM BatteryStatus");
            using var results = searcher.Get();
            double totalWh = 0;
            foreach (ManagementBaseObject obj in results)
            {
                using (obj)
                {
                    bool charging = obj["Charging"] is true;
                    bool discharging = obj["Discharging"] is true;
                    uint chargeMw = obj["ChargeRate"] is null ? 0u : Convert.ToUInt32(obj["ChargeRate"]);
                    uint dischargeMw = obj["DischargeRate"] is null ? 0u : Convert.ToUInt32(obj["DischargeRate"]);
                    uint capMwh = obj["RemainingCapacity"] is null ? 0u : Convert.ToUInt32(obj["RemainingCapacity"]);

                    totalWh += capMwh / 1000.0;
                    if (discharging && dischargeMw is > 0 and < 250000)
                    {
                        _cachedIsCharging = false;
                        _cachedBatteryWatts = Math.Round(dischargeMw / 1000.0, 1);
                    }
                    else if (charging && chargeMw is > 0 and < 250000)
                    {
                        _cachedIsCharging = true;
                        _cachedBatteryWatts = Math.Round(chargeMw / 1000.0, 1);
                    }
                    else if (charging)
                    {
                        _cachedIsCharging = true;
                    }
                }
            }
            _cachedRemainingWh = Math.Round(totalWh, 1);
        }
        catch
        {
            // Ignore WMI battery telemetry errors on systems without Smart Battery WMI
        }

        _wmiCachedAtUtc = DateTime.UtcNow;
    }

    private void PopulateAudioInfo(SystemControlState state)
    {
        try
        {
            var (vol, muted) = WithAudioEndpoint(ep =>
            {
                float scalar = 0.5f;
                bool isMuted = false;
                ep.GetMasterVolumeLevelScalar(out scalar);
                ep.GetMute(out isMuted);
                return (Math.Clamp((int)Math.Round(scalar * 100f), 0, 100), isMuted);
            });
            state.VolumePercent = vol;
            state.IsMuted = muted;
        }
        catch
        {
            // Keep defaults when no audio endpoint is active
        }
    }

    private T WithAudioEndpoint<T>(Func<IAudioEndpointVolume, T> action)
    {
        lock (_audioLock)
        {
            IMMDeviceEnumerator? enumerator = null;
            IMMDevice? device = null;
            IAudioEndpointVolume? endpoint = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 1, out device));
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, 0, out object raw));
                endpoint = (IAudioEndpointVolume)raw;
                return action(endpoint);
            }
            finally
            {
                if (endpoint is not null && Marshal.IsComObject(endpoint)) Marshal.ReleaseComObject(endpoint);
                if (device is not null && Marshal.IsComObject(device)) Marshal.ReleaseComObject(device);
                if (enumerator is not null && Marshal.IsComObject(enumerator)) Marshal.ReleaseComObject(enumerator);
            }
        }
    }

    private static string ReadCpuBoostMode(bool isAcOnline)
    {
        try
        {
            Guid scheme = GetActivePowerSchemeGuid();
            var sub = SubProcessor;
            var setting = PerfBoostMode;
            uint rc = isAcOnline
                ? PowerReadACValueIndex(0, ref scheme, ref sub, ref setting, out uint idx)
                : PowerReadDCValueIndex(0, ref scheme, ref sub, ref setting, out idx);
            if (rc != 0)
            {
                return "aggressive";
            }

            return idx switch
            {
                0 => "off",
                3 or 4 or 5 or 6 => "efficient",
                _ => "aggressive"
            };
        }
        catch
        {
            return "aggressive";
        }
    }

    private static Guid GetActivePowerSchemeGuid()
    {
        uint rc = PowerGetActiveScheme(0, out nint ptr);
        if (rc != 0 || ptr == 0)
        {
            throw new InvalidOperationException($"无法读取当前 Windows 电源计划（错误码 {rc}）。");
        }

        try
        {
            return Marshal.PtrToStructure<Guid>(ptr);
        }
        finally
        {
            LocalFree(ptr);
        }
    }

    private static ulong FileTimeToUInt64(FILETIME ft) =>
        ((ulong)ft.dwHighDateTime << 32) | ft.dwLowDateTime;

    #region Native P/Invokes & CoreAudio COM

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODEA
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

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

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettingsA(string? lpszDeviceName, int iModeNum, ref DEVMODEA lpDevMode);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern int ChangeDisplaySettingsExA(
        string? lpszDeviceName,
        ref DEVMODEA lpDevMode,
        nint hwnd,
        uint dwFlags,
        nint lParam);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint hMem);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(nint UserRootPowerKey, out nint ActivePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(
        nint RootPowerKey,
        ref Guid SchemeGuid,
        ref Guid SubGroupOfPowerSettingsGuid,
        ref Guid PowerSettingGuid,
        out uint AcValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(
        nint RootPowerKey,
        ref Guid SchemeGuid,
        ref Guid SubGroupOfPowerSettingsGuid,
        ref Guid PowerSettingGuid,
        out uint DcValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(
        nint RootPowerKey,
        ref Guid SchemeGuid,
        ref Guid SubGroupOfPowerSettingsGuid,
        ref Guid PowerSettingGuid,
        uint AcValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(
        nint RootPowerKey,
        ref Guid SchemeGuid,
        ref Guid SubGroupOfPowerSettingsGuid,
        ref Guid PowerSettingGuid,
        uint DcValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(nint UserRootPowerKey, ref Guid SchemeGuid);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int NotImpl1();
        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int dwClsCtx, nint pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(nint pNotify);
        int UnregisterControlChangeNotify(nint pNotify);
        int GetChannelCount(out uint pnChannelCount);
        int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig]
        int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        int GetMasterVolumeLevel(out float pfLevelDB);
        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float pfLevel);
        int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    }

    #endregion
}
