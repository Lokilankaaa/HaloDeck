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
}

/// <summary>
/// Crash-safe Windows PowrProf optimizer for Steady &amp; Eco Mode.
/// Snapshots exact user AC/DC values to steady_eco_backup.json before any modification
/// and guarantees full rollback on game exit, mode disable, app shutdown, or next boot after power loss.
/// </summary>
public sealed class PowerPeripheralOptimizer : IDisposable
{
    public static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
    public static readonly Guid SettingPerfEpp0 = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");
    public static readonly Guid SettingPerfEpp1 = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6864");
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

    public string BackupFilePath => _backupFilePath;

    /// <summary>
    /// Checks if a stale steady_eco_backup.json survived an unexpected crash or power loss
    /// and restores original AC/DC settings before starting normal operation.
    /// </summary>
    public bool RecoverIfStaleBackupExists()
    {
        lock (_lock)
        {
            if (!File.Exists(_backupFilePath))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(_backupFilePath);
                var doc = JsonSerializer.Deserialize<SteadyEcoBackupDocument>(json, BackupJsonOptions);
                if (doc is not null && doc.Entries.Count > 0)
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
                Restore();
                return false;
            }

            Guid scheme;
            try
            {
                scheme = _backend.GetActiveScheme();
            }
            catch
            {
                return false;
            }

            // If power scheme changed externally since backup was created, restore old scheme first
            if (_activeBackup is not null && _activeBackup.SchemeGuid != scheme)
            {
                RestoreFromDocumentLocked(_activeBackup);
                _activeBackup = null;
            }

            _activeBackup ??= new SteadyEcoBackupDocument
            {
                SchemeGuid = scheme,
                CapturedAtUtc = DateTime.UtcNow
            };

            var desired = BuildDesiredTargets(options);
            var desiredKeys = new HashSet<string>(desired.Select(d => d.Key), StringComparer.OrdinalIgnoreCase);

            // 1. Capture original values for any newly requested keys BEFORE writing
            bool backupDirty = false;
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

            // Persist backup snapshot to disk BEFORE modifying OS power settings
            if (backupDirty || !File.Exists(_backupFilePath))
            {
                PersistBackupLocked(_activeBackup);
            }

            // 2. Restore any previously applied keys whose sub-switch was just turned off
            foreach (var existing in _activeBackup.Entries.ToList())
            {
                if (!desiredKeys.Contains(existing.Key))
                {
                    _backend.TryWriteAcDcValues(
                        scheme,
                        existing.SubGroupGuid,
                        existing.SettingGuid,
                        existing.OriginalAcValue,
                        existing.OriginalDcValue);
                    _activeBackup.Entries.Remove(existing);
                    backupDirty = true;
                }
            }

            if (backupDirty)
            {
                PersistBackupLocked(_activeBackup);
            }

            // 3. Write desired steady/eco AC & DC values
            bool anyWritten = false;
            foreach (var target in desired)
            {
                // Only write keys that existed & were backed up on this machine
                if (_activeBackup.Entries.Any(e => string.Equals(e.Key, target.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    if (_backend.TryWriteAcDcValues(scheme, target.SubGroup, target.Setting, target.AcValue, target.DcValue))
                    {
                        anyWritten = true;
                    }
                }
            }

            if (anyWritten)
            {
                _backend.ActivateScheme(scheme);
                _isApplied = true;
            }

            return _isApplied;
        }
    }

    public bool Restore()
    {
        lock (_lock)
        {
            SteadyEcoBackupDocument? doc = _activeBackup;
            if (doc is null && File.Exists(_backupFilePath))
            {
                try
                {
                    doc = JsonSerializer.Deserialize<SteadyEcoBackupDocument>(
                        File.ReadAllText(_backupFilePath),
                        BackupJsonOptions);
                }
                catch
                {
                    doc = null;
                }
            }

            bool restored = false;
            if (doc is not null && doc.Entries.Count > 0)
            {
                restored = RestoreFromDocumentLocked(doc);
            }

            _activeBackup = null;
            _isApplied = false;
            TryDeleteBackupFileLocked();
            return restored;
        }
    }

    public void Dispose()
    {
        Restore();
    }

    private static List<(string Key, Guid SubGroup, Guid Setting, uint AcValue, uint DcValue)> BuildDesiredTargets(
        SteadyEcoPowerOptions options)
    {
        var list = new List<(string Key, Guid SubGroup, Guid Setting, uint AcValue, uint DcValue)>();

        if (options.EnableCpuCoreEco)
        {
            uint eppAc = (uint)Math.Clamp(options.CpuEppIndex, 45, 95);
            uint eppDc = (uint)Math.Clamp(Math.Max(options.CpuEppIndex, 75), 45, 95);

            list.Add(("cpu_epp0", SubProcessor, SettingPerfEpp0, eppAc, eppDc));
            list.Add(("cpu_epp1", SubProcessor, SettingPerfEpp1, eppAc, eppDc));
            // Allow up to 75% of cores (e.g. 6 of 8 cores on 388, or secondary CCD on 395) to enter deep CC6 sleep when idle
            list.Add(("cpu_min_cores", SubProcessor, SettingCoreParkMin, 25u, 25u));

            if (options.TopologyKind == CpuTopologyKind.HybridBigLittle)
            {
                // Dynamic hetero scheduling (4) pairs foreground game threads to Zen 5 and EcoQoS background threads to Zen 5c
                list.Add(("cpu_hetero_policy", SubProcessor, SettingHeteroPolicy, 4u, 4u));
            }
        }

        if (options.EnablePeripheralEco)
        {
            // Fast NVMe APST idle transition: keeps secondary/non-game SSD in milliwatt PS3/PS4 sleep
            list.Add(("nvme_primary_idle_ms", SubDisk, SettingPrimaryNvmeIdleMs, 100u, 30u));
            list.Add(("nvme_secondary_idle_ms", SubDisk, SettingSecondaryNvmeIdleMs, 100u, 60u));
            list.Add(("ahci_hipm_dipm", SubDisk, SettingAhciLinkPower, 2u, 2u));

            // Wi-Fi RF power saving: max power save (3) for single-player to cut RF draw & background scan DPC spikes;
            // gentle low power save (1) when online anti-cheat multiplayer is running to preserve network latency.
            uint wifiIdx = options.IsOnlineAntiCheatGame ? 1u : 3u;
            list.Add(("wifi_power_save", SubWireless, SettingWirelessPowerSave, wifiIdx, wifiIdx));

            // Prevent background browser/launcher video decoders from locking SoC clocks high
            list.Add(("media_quality_bias", SubMultimedia, SettingVideoQualityBias, 0u, 0u));
            list.Add(("media_play_policy", SubMultimedia, SettingWhenPlayingVideo, 2u, 2u));
        }

        return list;
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
