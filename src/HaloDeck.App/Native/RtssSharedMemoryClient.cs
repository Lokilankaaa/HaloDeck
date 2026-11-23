using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using Microsoft.Win32;

namespace HaloDeck.App.Native;

public sealed record RtssEnsureResult(
    bool Running,
    bool Installed,
    bool Launched,
    bool InstalledNow,
    string Message);

/// <summary>
/// Zero-hook client for RivaTuner Statistics Server shared memory (RTSSSharedMemoryV2).
/// Never injects DLLs into games and never bundles RTSS; cleanly auto-detects, launches,
/// or installs official RTSS (via winget Guru3D.RTSS) when requested by the user.
/// </summary>
public sealed class RtssSharedMemoryClient : IDisposable
{
    private const string SharedMemoryName = "RTSSSharedMemoryV2";
    private const string OwnerName = "HaloDeck";
    private const string OfficialRtssDownloadUrl = "https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/";
    private const uint RtssSignature = 0x52545353; // 'RTSS'
    private const uint RtssVersion20 = 0x00020000;
    private const uint RtssVersion27 = 0x00020007;
    private const uint RtssVersion212 = 0x0002000C;
    private const uint RtssVersion220 = 0x00020014;

    private const int OffsetSignature = 0;
    private const int OffsetVersion = 4;
    private const int OffsetOsdEntrySize = 20;
    private const int OffsetOsdArrOffset = 24;
    private const int OffsetOsdArrSize = 28;
    private const int OffsetOsdFrame = 32;

    private const int OsdLegacyOffset = 0;
    private const int OsdLegacySize = 256;
    private const int OsdOwnerOffset = 256;
    private const int OsdOwnerSize = 256;
    private const int OsdExOffset = 512;
    private const int OsdExSize = 4096;
    private const int OsdObjBufferOffset = 4608;
    private const int OsdObjBufferSize = 262144;
    private const int OsdEx2Offset = 266752;
    private const int OsdEx2Size = 32768;

    private readonly object _sync = new();
    private readonly SemaphoreSlim _prepareGate = new(1, 1);
    private DateTime _lastProbeUtc = DateTime.MinValue;
    private DateTime _lastInstallProbeUtc = DateTime.MinValue;
    private bool _cachedRunning;
    private string? _cachedExePath;
    private bool _slotActive;

    public bool IsRtssInstalled(bool forceProbe = false)
    {
        return !string.IsNullOrWhiteSpace(FindRtssExecutablePath(forceProbe));
    }

    public string? FindRtssExecutablePath(bool forceProbe = false)
    {
        lock (_sync)
        {
            if (!forceProbe &&
                (DateTime.UtcNow - _lastInstallProbeUtc) < TimeSpan.FromSeconds(5) &&
                (_cachedExePath is null || File.Exists(_cachedExePath)))
            {
                return _cachedExePath;
            }

            _lastInstallProbeUtc = DateTime.UtcNow;
            _cachedExePath = LocateRtssExecutableUncached();
            return _cachedExePath;
        }
    }

    public async Task<RtssEnsureResult> EnsureRtssRunningAsync(bool allowInstall, CancellationToken ct = default)
    {
        await _prepareGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (IsRtssRunning(forceProbe: true))
            {
                return new RtssEnsureResult(true, true, false, false, "RTSS 已在运行并就绪");
            }

            string? rtssExe = FindRtssExecutablePath(forceProbe: true);
            bool installedNow = false;

            if (string.IsNullOrWhiteSpace(rtssExe))
            {
                if (!allowInstall)
                {
                    return new RtssEnsureResult(false, false, false, false, "未检测到已安装的 RTSS");
                }

                var (okInstall, installErr) = await TryInstallRtssViaWingetAsync(ct).ConfigureAwait(false);
                rtssExe = FindRtssExecutablePath(forceProbe: true);
                if (okInstall && !string.IsNullOrWhiteSpace(rtssExe))
                {
                    installedNow = true;
                }
                else
                {
                    TryOpenOfficialDownloadPage();
                    return new RtssEnsureResult(
                        false,
                        false,
                        false,
                        false,
                        string.IsNullOrWhiteSpace(installErr)
                            ? "自动安装 RTSS 未完成，已为你打开官方下载页，安装后点「立即启动 RTSS」即可"
                            : $"{installErr}；已打开官方下载页供手动安装");
                }
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = rtssExe!,
                    WorkingDirectory = Path.GetDirectoryName(rtssExe!) ?? Environment.SystemDirectory,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Minimized
                });
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return new RtssEnsureResult(
                    false,
                    true,
                    false,
                    installedNow,
                    "已取消启动 RTSS 的管理员授权，暂保持原生悬浮窗显示");
            }
            catch (Exception ex)
            {
                return new RtssEnsureResult(
                    false,
                    true,
                    false,
                    installedNow,
                    $"启动 RTSS 失败：{ex.Message}");
            }

            // Wait up to 5.5s for RTSSSharedMemoryV2 to initialize
            for (int i = 0; i < 22; i++)
            {
                await Task.Delay(250, ct).ConfigureAwait(false);
                if (IsRtssRunning(forceProbe: true))
                {
                    return new RtssEnsureResult(
                        true,
                        true,
                        true,
                        installedNow,
                        installedNow
                            ? "已自动安装并启动 RTSS，OSD 穿透通道已就绪"
                            : "已自动启动本机 RTSS，OSD 穿透通道已就绪");
                }
            }

            return new RtssEnsureResult(
                false,
                true,
                true,
                installedNow,
                "已拉起 RTSS 进程，但共享内存尚未就绪，请稍等片刻或确认 RTSS 托盘图标已启动");
        }
        finally
        {
            _prepareGate.Release();
        }
    }

    private static string? LocateRtssExecutableUncached()
    {
        foreach (string regPath in new[]
        {
            @"SOFTWARE\WOW6432Node\Unwinder\RTSS",
            @"SOFTWARE\Unwinder\RTSS"
        })
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(regPath, writable: false);
                if (key is null) continue;
                if (key.GetValue("InstallPath") is string p && !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                {
                    return p;
                }
                if (key.GetValue("InstallDir") is string d && !string.IsNullOrWhiteSpace(d))
                {
                    string candidate = Path.Combine(d, "RTSS.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch { }
        }

        foreach (string candidate in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "RivaTuner Statistics Server", "RTSS.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RivaTuner Statistics Server", "RTSS.exe")
        })
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (string uninstallRoot in new[]
        {
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
        })
        {
            try
            {
                using var root = Registry.LocalMachine.OpenSubKey(uninstallRoot, writable: false);
                if (root is null) continue;
                foreach (string subName in root.GetSubKeyNames())
                {
                    using var sub = root.OpenSubKey(subName, writable: false);
                    if (sub is null) continue;
                    string display = sub.GetValue("DisplayName") as string ?? string.Empty;
                    if (!display.Contains("RivaTuner", StringComparison.OrdinalIgnoreCase) &&
                        !display.Contains("RTSS", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (sub.GetValue("InstallLocation") is string loc && !string.IsNullOrWhiteSpace(loc))
                    {
                        string exe = Path.Combine(loc.Trim('"'), "RTSS.exe");
                        if (File.Exists(exe)) return exe;
                    }
                    if (sub.GetValue("DisplayIcon") is string icon && !string.IsNullOrWhiteSpace(icon))
                    {
                        string cleaned = icon.Trim().Trim('"').Split(',')[0].Trim('"');
                        string? dir = Path.GetDirectoryName(cleaned);
                        if (!string.IsNullOrWhiteSpace(dir))
                        {
                            string exe = Path.Combine(dir, "RTSS.exe");
                            if (File.Exists(exe)) return exe;
                        }
                    }
                }
            }
            catch { }
        }

        return null;
    }

    private static string? ResolveWingetPath()
    {
        string localWinget = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "winget.exe");
        if (File.Exists(localWinget)) return localWinget;

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string candidate = Path.Combine(dir.Trim(), "winget.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
        }
        return null;
    }

    private static async Task<(bool Ok, string Error)> TryInstallRtssViaWingetAsync(CancellationToken ct)
    {
        string? winget = ResolveWingetPath();
        if (string.IsNullOrWhiteSpace(winget))
        {
            return (false, "系统未安装 winget，无法静默拉取 RTSS");
        }

        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = winget,
                Arguments = "install --id Guru3D.RTSS --exact --silent --accept-source-agreements --accept-package-agreements",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (proc is null)
            {
                return (false, "无法拉起 winget 安装 RTSS");
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(3));
            await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            return proc.ExitCode == 0
                ? (true, string.Empty)
                : (false, $"winget 安装 RTSS 失败 (退出码 {proc.ExitCode})");
        }
        catch (OperationCanceledException)
        {
            return (false, "安装 RTSS 超时或已取消");
        }
        catch (Exception ex)
        {
            return (false, $"自动安装 RTSS 异常：{ex.Message}");
        }
    }

    private static void TryOpenOfficialDownloadPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(OfficialRtssDownloadUrl) { UseShellExecute = true });
        }
        catch { }
    }

    public bool IsRtssRunning(bool forceProbe = false)
    {
        lock (_sync)
        {
            if (!forceProbe && (DateTime.UtcNow - _lastProbeUtc) < TimeSpan.FromMilliseconds(1500))
            {
                return _cachedRunning;
            }

            _lastProbeUtc = DateTime.UtcNow;
            _cachedRunning = ProbeLiveHeader();
            if (!_cachedRunning)
            {
                _slotActive = false;
            }
            return _cachedRunning;
        }
    }

    public bool UpdateOsd(string text, byte[]? embeddedObjectBuffer = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            ClearOsd();
            return false;
        }

        lock (_sync)
        {
            try
            {
                using var mmf = MemoryMappedFile.OpenExisting(SharedMemoryName, MemoryMappedFileRights.ReadWrite);
                using var view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);

                if (!TryReadHeader(view, checkFullBounds: true, out uint version, out uint entrySize, out uint arrOffset, out uint arrSize))
                {
                    _cachedRunning = false;
                    _slotActive = false;
                    return false;
                }

                _cachedRunning = true;
                _lastProbeUtc = DateTime.UtcNow;

                int slot = FindOrClaimSlot(view, entrySize, arrOffset, arrSize);
                if (slot < 1)
                {
                    return false;
                }

                long entryPos = arrOffset + (long)slot * entrySize;
                WriteNullTerminatedAscii(view, entryPos + OsdOwnerOffset, OsdOwnerSize, OwnerName);

                bool hasEx2 = version >= RtssVersion220 && entrySize >= (OsdEx2Offset + OsdEx2Size);
                bool hasEx = version >= RtssVersion27 && entrySize >= (OsdExOffset + OsdExSize);
                bool hasObjBuf = version >= RtssVersion212 && entrySize >= (OsdObjBufferOffset + OsdObjBufferSize);

                if (hasEx2)
                {
                    view.Write(entryPos + OsdLegacyOffset, (byte)0);
                    view.Write(entryPos + OsdExOffset, (byte)0);
                    WriteNullTerminatedAscii(view, entryPos + OsdEx2Offset, OsdEx2Size, text);
                }
                else if (hasEx)
                {
                    view.Write(entryPos + OsdLegacyOffset, (byte)0);
                    WriteNullTerminatedAscii(view, entryPos + OsdExOffset, OsdExSize, text);
                }
                else
                {
                    WriteNullTerminatedAscii(view, entryPos + OsdLegacyOffset, OsdLegacySize, text);
                }

                if (hasObjBuf)
                {
                    if (embeddedObjectBuffer is { Length: > 0 })
                    {
                        int copyLen = Math.Min(embeddedObjectBuffer.Length, OsdObjBufferSize);
                        view.WriteArray(entryPos + OsdObjBufferOffset, embeddedObjectBuffer, 0, copyLen);
                    }
                    else
                    {
                        view.Write(entryPos + OsdObjBufferOffset, 0u);
                    }
                }

                uint frame = view.ReadUInt32(OffsetOsdFrame);
                view.Write(OffsetOsdFrame, unchecked(frame + 1));
                _slotActive = true;
                return true;
            }
            catch (FileNotFoundException)
            {
                _cachedRunning = false;
                _slotActive = false;
                _lastProbeUtc = DateTime.UtcNow;
                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    public void ClearOsd()
    {
        lock (_sync)
        {
            if (!_slotActive && !_cachedRunning)
            {
                return;
            }

            try
            {
                using var mmf = MemoryMappedFile.OpenExisting(SharedMemoryName, MemoryMappedFileRights.ReadWrite);
                using var view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);

                if (!TryReadHeader(view, checkFullBounds: true, out uint version, out uint entrySize, out uint arrOffset, out uint arrSize))
                {
                    _cachedRunning = false;
                    _slotActive = false;
                    return;
                }

                bool hasEx = version >= RtssVersion27 && entrySize >= (OsdExOffset + OsdExSize);
                bool hasEx2 = version >= RtssVersion220 && entrySize >= (OsdEx2Offset + OsdEx2Size);
                bool clearedAny = false;

                for (uint i = 1; i < arrSize; i++)
                {
                    long entryPos = arrOffset + (long)i * entrySize;
                    string owner = ReadNullTerminatedAscii(view, entryPos + OsdOwnerOffset, OsdOwnerSize);
                    if (string.Equals(owner, OwnerName, StringComparison.Ordinal))
                    {
                        view.Write(entryPos + OsdLegacyOffset, (byte)0);
                        view.Write(entryPos + OsdOwnerOffset, (byte)0);
                        if (hasEx)
                        {
                            view.Write(entryPos + OsdExOffset, (byte)0);
                        }
                        if (hasEx2)
                        {
                            view.Write(entryPos + OsdEx2Offset, (byte)0);
                        }
                        clearedAny = true;
                    }
                }

                if (clearedAny)
                {
                    uint frame = view.ReadUInt32(OffsetOsdFrame);
                    view.Write(OffsetOsdFrame, unchecked(frame + 1));
                }

                _slotActive = false;
            }
            catch
            {
                _slotActive = false;
            }
        }
    }

    public void Dispose()
    {
        ClearOsd();
    }

    private static bool ProbeLiveHeader()
    {
        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(SharedMemoryName, MemoryMappedFileRights.Read);
            using var view = mmf.CreateViewAccessor(0, 64, MemoryMappedFileAccess.Read);
            return TryReadHeader(view, checkFullBounds: false, out _, out _, out _, out _);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadHeader(
        MemoryMappedViewAccessor view,
        bool checkFullBounds,
        out uint version,
        out uint entrySize,
        out uint arrOffset,
        out uint arrSize)
    {
        version = 0;
        entrySize = 0;
        arrOffset = 0;
        arrSize = 0;

        if (view.Capacity < 40)
        {
            return false;
        }

        uint sig = view.ReadUInt32(OffsetSignature);
        if (sig != RtssSignature)
        {
            return false;
        }

        version = view.ReadUInt32(OffsetVersion);
        if (version < RtssVersion20)
        {
            return false;
        }

        entrySize = view.ReadUInt32(OffsetOsdEntrySize);
        arrOffset = view.ReadUInt32(OffsetOsdArrOffset);
        arrSize = view.ReadUInt32(OffsetOsdArrSize);

        bool headerValid = entrySize >= (OsdOwnerOffset + OsdOwnerSize) &&
                           arrSize >= 2 &&
                           arrSize <= 64 &&
                           arrOffset >= 36;
        if (!headerValid)
        {
            return false;
        }

        return !checkFullBounds || ((long)arrOffset + (long)arrSize * entrySize <= view.Capacity);
    }

    private static int FindOrClaimSlot(MemoryMappedViewAccessor view, uint entrySize, uint arrOffset, uint arrSize)
    {
        int firstEmpty = -1;
        // Slot 0 is reserved by RTSS itself; external apps use slots 1..arrSize-1
        for (uint i = 1; i < arrSize; i++)
        {
            long entryPos = arrOffset + (long)i * entrySize;
            string owner = ReadNullTerminatedAscii(view, entryPos + OsdOwnerOffset, OsdOwnerSize);
            if (string.Equals(owner, OwnerName, StringComparison.Ordinal))
            {
                return (int)i;
            }
            if (firstEmpty < 0 && string.IsNullOrEmpty(owner))
            {
                firstEmpty = (int)i;
            }
        }
        return firstEmpty;
    }

    private static string ReadNullTerminatedAscii(MemoryMappedViewAccessor view, long offset, int maxLen)
    {
        byte[] buf = new byte[Math.Min(maxLen, 64)];
        view.ReadArray(offset, buf, 0, buf.Length);
        int zeroIdx = Array.IndexOf(buf, (byte)0);
        int len = zeroIdx >= 0 ? zeroIdx : buf.Length;
        return len == 0 ? string.Empty : Encoding.ASCII.GetString(buf, 0, len);
    }

    private static void WriteNullTerminatedAscii(MemoryMappedViewAccessor view, long offset, int maxLen, string text)
    {
        byte[] encoded = Encoding.ASCII.GetBytes(text);
        int copyLen = Math.Min(encoded.Length, maxLen - 1);
        if (copyLen > 0)
        {
            view.WriteArray(offset, encoded, 0, copyLen);
        }
        view.Write(offset + copyLen, (byte)0);
    }
}
