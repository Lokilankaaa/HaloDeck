using System.Runtime.InteropServices;
using System.Text.Json;
using HaloDeck.Core.Config;

namespace HaloDeck.Core.Services;

public sealed class PowerSettingEntryBackup
{
    public string Key { get; set; } = string.Empty;
    public Guid SubGroupGuid { get; set; }
    public Guid SettingGuid { get; set; }
    public uint OriginalAcValue { get; set; }
    public uint OriginalDcValue { get; set; }
}

public sealed class SteadyEcoBackupDocument
{
    public Guid SchemeGuid { get; set; }
    public bool HasOriginalOverlayScheme { get; set; }
    public Guid OriginalOverlaySchemeGuid { get; set; }
    public DateTime CapturedAtUtc { get; set; }
    public List<PowerSettingEntryBackup> Entries { get; set; } = new();
}

public sealed record SteadyEcoPowerOptions(
    bool EnableCpuCoreEco,
    bool EnablePeripheralEco,
    int CpuEppIndex,
    CpuTopologyKind TopologyKind,
    bool IsOnlineAntiCheatGame);

public interface IPowerSchemeBackend
{
    Guid GetActiveScheme();
    bool TryReadAcDcValues(Guid scheme, Guid subGroup, Guid setting, out uint acValue, out uint dcValue);
    bool TryWriteAcDcValues(Guid scheme, Guid subGroup, Guid setting, uint acValue, uint dcValue);
    bool ActivateScheme(Guid scheme);
    bool TryGetOverlayScheme(out Guid overlayScheme);
    bool TrySetOverlayScheme(Guid overlayScheme);
}

public sealed class Win32PowerSchemeBackend : IPowerSchemeBackend
{
    public Guid GetActiveScheme()
    {
        uint rc = PowerGetActiveScheme(0, out nint ptr);
        if (rc != 0 || ptr == 0)
        {
            throw new InvalidOperationException($"无法读取当前 Windows 电源方案 (错误码 {rc})。");
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

    public bool TryReadAcDcValues(Guid scheme, Guid subGroup, Guid setting, out uint acValue, out uint dcValue)
    {
        uint rcAc = PowerReadACValueIndex(0, ref scheme, ref subGroup, ref setting, out acValue);
        uint rcDc = PowerReadDCValueIndex(0, ref scheme, ref subGroup, ref setting, out dcValue);
        return rcAc == 0 && rcDc == 0;
    }

    public bool TryWriteAcDcValues(Guid scheme, Guid subGroup, Guid setting, uint acValue, uint dcValue)
    {
        uint rcAc = PowerWriteACValueIndex(0, ref scheme, ref subGroup, ref setting, acValue);
        uint rcDc = PowerWriteDCValueIndex(0, ref scheme, ref subGroup, ref setting, dcValue);
        return rcAc == 0 || rcDc == 0;
    }

    public bool ActivateScheme(Guid scheme)
    {
        return PowerSetActiveScheme(0, ref scheme) == 0;
    }

    public bool TryGetOverlayScheme(out Guid overlayScheme)
    {
        try
        {
            return PowerGetEffectiveOverlayScheme(out overlayScheme) == 0;
        }
        catch
        {
            overlayScheme = Guid.Empty;
            return false;
        }
    }

    public bool TrySetOverlayScheme(Guid overlayScheme)
    {
        try
        {
            return PowerSetActiveOverlayScheme(ref overlayScheme) == 0;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint hMem);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(nint UserRootPowerKey, out nint ActivePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetEffectiveOverlayScheme(out Guid EffectiveOverlayPolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveOverlayScheme([In] ref Guid OverlaySchemeGuid);

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
}

/// <summary>
/// Crash-safe Windows PowrProf &amp; Power Mode Overlay coordinator for Steady &amp; Eco Mode and CPU Frequency Cap.
/// Snapshots exact user AC/DC values and Windows 11 Power Overlay GUID to steady_eco_backup.json before any modification
/// and guarantees full rollback on game exit, mode disable, uncapping, app shutdown, or next boot after power loss.
/// Never modifies NVMe APST sleep, SATA link sleep, Wi-Fi Max Power Save, or homogeneous core parking to prevent frame stutters.
/// </summary>
public sealed class PowerPeripheralOptimizer : IDisposable
{
    public static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
    public static readonly Guid SettingPerfEpp0 = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");
    public static readonly Guid SettingPerfEpp1 = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6864");
    public static readonly Guid SettingProcThrottleMin = new("893dee8e-2bef-41e0-89c6-b55d0929964c");
    public static readonly Guid SettingProcFreqMax = new("75b0ae3f-bce0-45a7-8c89-c9611c25e100");
    public static readonly Guid SettingProcFreqMax1 = new("75b0ae3f-bce0-45a7-8c89-c9611c25e101");
    public static readonly Guid SettingCoreParkMin = new("0cc5b647-c1df-4637-891a-dec35c318583");
    public static readonly Guid SettingHeteroPolicy = new("7f2f5cfa-f10c-4823-b5e1-e93ae85f46b5");

    public static readonly Guid SubDisk = new("0012ee47-9041-4b5d-9b77-535fba8b1442");
    public static readonly Guid SettingPrimaryNvmeIdleMs = new("d639518a-e56d-4345-8af2-b9f32fb26109");
    public static readonly Guid SettingSecondaryNvmeIdleMs = new("d3d55efd-c1ff-424e-9dc3-441be7833010");
    public static readonly Guid SettingAhciLinkPower = new("0b2d69d7-a2a1-449c-9680-f91c70521c60");

    public static readonly Guid SubWireless = new("19cbb8fa-5279-450e-9fac-8a3d5fedd0c1");
    public static readonly Guid SettingWirelessPowerSave = new("12bbebe6-58d6-4636-95bb-3217ef867c1a");

    public static readonly Guid SubMultimedia = new("9596fb26-9850-41fd-ac3e-f7c3c00afd4b");
    public static readonly Guid SettingVideoQualityBias = new("10778347-1370-4ee0-8bbd-33bdacaade49");
    public static readonly Guid SettingWhenPlayingVideo = new("34c7b99f-9a6d-4b3c-8dc7-b6693b78cef4");

    private static readonly JsonSerializerOptions BackupJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly IPowerSchemeBackend _backend;
    private readonly string _backupFilePath;
    private readonly object _lock = new();
    private SteadyEcoBackupDocument? _activeBackup;
    private SteadyEcoPowerOptions? _currentSteadyOptions;
    private int _currentCpuMaxFreqMhz;
    private bool _isApplied;

    public PowerPeripheralOptimizer(IPowerSchemeBackend? backend = null, string? customBackupPath = null)
    {
        _backend = backend ?? new Win32PowerSchemeBackend();
        _backupFilePath = !string.IsNullOrWhiteSpace(customBackupPath)
            ? customBackupPath
            : Path.Combine(HaloDeckPaths.ResolveAppDataRoot(), "steady_eco_backup.json");
    }

    public bool IsApplied
    {
        get
        {
            lock (_lock)
            {
                return _isApplied;
            }
        }
    }

    public int CurrentCpuMaxFreqMhz
    {
        get
        {
            lock (_lock)
            {
                return _currentCpuMaxFreqMhz;
            }
        }
    }

    public string BackupFilePath => _backupFilePath;

    /// <summary>
    /// Checks if a stale steady_eco_backup.json survived an unexpected crash or power loss.
    /// When persistedCpuMaxFreqMhz &lt;= 0, restores all original AC/DC settings and Windows 11 Power Overlay.
    /// When persistedCpuMaxFreqMhz &gt; 0, restores any stale SteadyEco keys while preserving the original Overlay backup and re-applying the user's CPU cap.
    /// </summary>
    public bool RecoverIfStaleBackupExists(int persistedCpuMaxFreqMhz = 0)
    {
        lock (_lock)
        {
            if (persistedCpuMaxFreqMhz > 0)
            {
                _currentSteadyOptions = null;
                _currentCpuMaxFreqMhz = Math.Clamp(persistedCpuMaxFreqMhz, 1600, 5500);
                ReconcileLocked();
                return true;
            }

            if (!File.Exists(_backupFilePath))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(_backupFilePath);
                var doc = JsonSerializer.Deserialize<SteadyEcoBackupDocument>(json, BackupJsonOptions);
                if (doc is not null && (doc.Entries.Count > 0 || doc.HasOriginalOverlayScheme))
                {
                    RestoreFromDocumentLocked(doc);
                }
            }
            catch
            {
                // Ignore malformed stale backup file
            }
            finally
            {
                TryDeleteBackupFileLocked();
                _activeBackup = null;
                _currentSteadyOptions = null;
                _currentCpuMaxFreqMhz = 0;
                _isApplied = false;
            }

            return true;
        }
    }

    public bool Apply(SteadyEcoPowerOptions options)
    {
        lock (_lock)
        {
            if (!options.EnableCpuCoreEco && !options.EnablePeripheralEco)
            {
                _currentSteadyOptions = null;
                ReconcileLocked();
                return false;
            }

            _currentSteadyOptions = options;
            ReconcileLocked();
            return _isApplied;
        }
    }

    public bool SetCpuMaxFreqMhz(int mhz)
    {
        lock (_lock)
        {
            _currentCpuMaxFreqMhz = mhz <= 0 ? 0 : Math.Clamp(mhz, 1600, 5500);
            return ReconcileLocked();
        }
    }

    /// <summary>
    /// Restores Steady &amp; Eco power changes while keeping any explicit global CPU Max Frequency cap active.
    /// If no CPU Max Frequency cap is active, restores all backed-up settings and the original Windows 11 Power Overlay.
    /// </summary>
    public bool Restore()
    {
        lock (_lock)
        {
            _currentSteadyOptions = null;
            if (_currentCpuMaxFreqMhz > 0)
            {
                return ReconcileLocked();
            }

            return RestoreAllLocked();
        }
    }

    public void Dispose()
    {
        Restore();
    }

    private bool RestoreAllLocked()
    {
        SteadyEcoBackupDocument? doc = _activeBackup ?? TryLoadBackupDocumentLocked();
        bool restored = false;
        if (doc is not null && (doc.Entries.Count > 0 || doc.HasOriginalOverlayScheme))
        {
            restored = RestoreFromDocumentLocked(doc);
        }

        _activeBackup = null;
        _currentSteadyOptions = null;
        _isApplied = false;
        TryDeleteBackupFileLocked();
        return restored;
    }

    private bool ReconcileLocked()
    {
        bool enableCpuEco = _currentSteadyOptions?.EnableCpuCoreEco == true;
        bool enablePerEco = _currentSteadyOptions?.EnablePeripheralEco == true;
        uint clampedMhz = _currentCpuMaxFreqMhz <= 0 ? 0u : (uint)Math.Clamp(_currentCpuMaxFreqMhz, 1600, 5500);
        bool needOverlayBypass = enableCpuEco || clampedMhz > 0;

        var desired = BuildDesiredTargets(_currentSteadyOptions, clampedMhz);
        if (desired.Count == 0 && !needOverlayBypass)
        {
            return RestoreAllLocked();
        }

        Guid scheme;
        try
        {
            scheme = _backend.GetActiveScheme();
        }
        catch
        {
            _isApplied = false;
            return false;
        }

        _activeBackup ??= TryLoadBackupDocumentLocked();

        // If power scheme changed externally since backup was created, restore old scheme first
        if (_activeBackup is not null && _activeBackup.SchemeGuid != Guid.Empty && _activeBackup.SchemeGuid != scheme)
        {
            RestoreFromDocumentLocked(_activeBackup);
            _activeBackup = null;
        }

        _activeBackup ??= new SteadyEcoBackupDocument
        {
            SchemeGuid = scheme,
            CapturedAtUtc = DateTime.UtcNow
        };

        var desiredKeys = new HashSet<string>(desired.Select(d => d.Key), StringComparer.OrdinalIgnoreCase);
        bool backupDirty = false;

        // 1. Capture original Windows 11 Power Mode Overlay GUID BEFORE setting Guid.Empty
        if (needOverlayBypass && !_activeBackup.HasOriginalOverlayScheme)
        {
            if (_backend.TryGetOverlayScheme(out Guid currentOverlay))
            {
                _activeBackup.OriginalOverlaySchemeGuid = currentOverlay;
                _activeBackup.HasOriginalOverlayScheme = true;
                backupDirty = true;
            }
        }

        // 2. Capture original AC/DC values for any newly requested keys BEFORE writing
        foreach (var target in desired)
        {
            if (_activeBackup.Entries.All(e => !string.Equals(e.Key, target.Key, StringComparison.OrdinalIgnoreCase)))
            {
                if (_backend.TryReadAcDcValues(scheme, target.SubGroup, target.Setting, out uint origAc, out uint origDc))
                {
                    _activeBackup.Entries.Add(new PowerSettingEntryBackup
                    {
                        Key = target.Key,
                        SubGroupGuid = target.SubGroup,
                        SettingGuid = target.Setting,
                        OriginalAcValue = origAc,
                        OriginalDcValue = origDc
                    });
                    backupDirty = true;
                }
            }
        }

        // Persist backup snapshot to disk BEFORE modifying OS power settings or overlay
        if (backupDirty || !File.Exists(_backupFilePath))
        {
            PersistBackupLocked(_activeBackup);
        }

        // 3. Restore any previously applied keys whose feature/sub-switch was turned off
        bool anyRestored = false;
        foreach (var existing in _activeBackup.Entries.ToList())
        {
            if (!desiredKeys.Contains(existing.Key))
            {
                if (_backend.TryWriteAcDcValues(
                    scheme,
                    existing.SubGroupGuid,
                    existing.SettingGuid,
                    existing.OriginalAcValue,
                    existing.OriginalDcValue))
                {
                    anyRestored = true;
                }
                _activeBackup.Entries.Remove(existing);
                backupDirty = true;
            }
        }

        bool restoreOverlayAfterActivate = false;
        Guid overlayToRestore = Guid.Empty;
        if (!needOverlayBypass && _activeBackup.HasOriginalOverlayScheme)
        {
            restoreOverlayAfterActivate = true;
            overlayToRestore = _activeBackup.OriginalOverlaySchemeGuid;
            _activeBackup.HasOriginalOverlayScheme = false;
            backupDirty = true;
        }

        if (backupDirty)
        {
            PersistBackupLocked(_activeBackup);
        }

        // 4. Bypass Windows 11 "Best Performance" Overlay so PROCFREQMAX, PROCTHROTTLEMIN & PERFEPP take hardware effect
        if (needOverlayBypass)
        {
            _backend.TrySetOverlayScheme(Guid.Empty);
        }

        // 5. Write desired AC & DC values
        bool anyWritten = false;
        foreach (var target in desired)
        {
            if (_activeBackup.Entries.Any(e => string.Equals(e.Key, target.Key, StringComparison.OrdinalIgnoreCase)))
            {
                if (_backend.TryWriteAcDcValues(scheme, target.SubGroup, target.Setting, target.AcValue, target.DcValue))
                {
                    anyWritten = true;
                }
            }
        }

        if (anyWritten || anyRestored)
        {
            _backend.ActivateScheme(scheme);
        }

        if (restoreOverlayAfterActivate)
        {
            _backend.TrySetOverlayScheme(overlayToRestore);
        }

        _isApplied = (enableCpuEco || enablePerEco) && anyWritten;
        return anyWritten || anyRestored || needOverlayBypass;
    }

    private static List<(string Key, Guid SubGroup, Guid Setting, uint AcValue, uint DcValue)> BuildDesiredTargets(
        SteadyEcoPowerOptions? steadyOptions,
        uint clampedCpuMaxFreqMhz)
    {
        var list = new List<(string Key, Guid SubGroup, Guid Setting, uint AcValue, uint DcValue)>();
        bool enableCpuEco = steadyOptions?.EnableCpuCoreEco == true;
        bool enablePerEco = steadyOptions?.EnablePeripheralEco == true;

        if (enableCpuEco || clampedCpuMaxFreqMhz > 0)
        {
            // Allow Zen 5 cores to scale cleanly below 80% (e.g. 2.6 GHz cap or ~800 MHz idle downclock)
            list.Add(("cpu_throttle_min", SubProcessor, SettingProcThrottleMin, 5u, 5u));
        }

        if (clampedCpuMaxFreqMhz > 0)
        {
            list.Add(("cpu_freq_max0", SubProcessor, SettingProcFreqMax, clampedCpuMaxFreqMhz, clampedCpuMaxFreqMhz));
            list.Add(("cpu_freq_max1", SubProcessor, SettingProcFreqMax1, clampedCpuMaxFreqMhz, clampedCpuMaxFreqMhz));
        }

        if (enableCpuEco && steadyOptions is not null)
        {
            uint eppAc = (uint)Math.Clamp(steadyOptions.CpuEppIndex, 45, 95);
            uint eppDc = (uint)Math.Clamp(Math.Max(steadyOptions.CpuEppIndex, 75), 45, 95);

            list.Add(("cpu_epp0", SubProcessor, SettingPerfEpp0, eppAc, eppDc));
            list.Add(("cpu_epp1", SubProcessor, SettingPerfEpp1, eppAc, eppDc));

            // NOTE: Homogeneous 8-core APUs (388) intentionally keep user's unparked core count (no CPMINCORES=25)
            // to guarantee zero core-unpark scheduling stalls during action game combat.
            if (steadyOptions.TopologyKind == CpuTopologyKind.HybridBigLittle)
            {
                list.Add(("cpu_hetero_policy", SubProcessor, SettingHeteroPolicy, 4u, 4u));
            }
        }

        if (enablePerEco)
        {
            // Zero-stutter background media decode policy only.
            // NVMe APST sleep (30-100ms), SATA HIPM/DIPM, and Wi-Fi Max Power Save (3) are strictly excluded
            // because they induce periodic 20ms-300ms asset-streaming and NDIS DPC frame-time spikes in games.
            list.Add(("media_quality_bias", SubMultimedia, SettingVideoQualityBias, 0u, 0u));
            list.Add(("media_play_policy", SubMultimedia, SettingWhenPlayingVideo, 2u, 2u));
        }

        return list;
    }

    private SteadyEcoBackupDocument? TryLoadBackupDocumentLocked()
    {
        if (!File.Exists(_backupFilePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SteadyEcoBackupDocument>(
                File.ReadAllText(_backupFilePath),
                BackupJsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private bool RestoreFromDocumentLocked(SteadyEcoBackupDocument doc)
    {
        Guid scheme = doc.SchemeGuid;
        if (scheme == Guid.Empty)
        {
            try { scheme = _backend.GetActiveScheme(); }
            catch { return false; }
        }

        bool anyRestored = false;
        foreach (var entry in doc.Entries)
        {
            if (_backend.TryWriteAcDcValues(
                scheme,
                entry.SubGroupGuid,
                entry.SettingGuid,
                entry.OriginalAcValue,
                entry.OriginalDcValue))
            {
                anyRestored = true;
            }
        }

        if (anyRestored)
        {
            _backend.ActivateScheme(scheme);
        }

        try
        {
            Guid currentActive = _backend.GetActiveScheme();
            if (currentActive != scheme)
            {
                _backend.ActivateScheme(currentActive);
            }
        }
        catch
        {
            // Ignore secondary scheme activation check
        }

        if (doc.HasOriginalOverlayScheme)
        {
            if (_backend.TrySetOverlayScheme(doc.OriginalOverlaySchemeGuid))
            {
                anyRestored = true;
            }
        }

        return anyRestored;
    }

    private void PersistBackupLocked(SteadyEcoBackupDocument doc)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_backupFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tmp = _backupFilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(doc, BackupJsonOptions));
            File.Move(tmp, _backupFilePath, overwrite: true);
        }
        catch
        {
            // Best-effort backup persistence
        }
    }

    private void TryDeleteBackupFileLocked()
    {
        try
        {
            if (File.Exists(_backupFilePath))
            {
                File.Delete(_backupFilePath);
            }
        }
        catch
        {
            // Ignore file lock during cleanup
        }
    }
}
