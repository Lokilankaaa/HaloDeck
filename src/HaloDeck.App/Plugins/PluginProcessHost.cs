using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using HaloDeck.Core.Plugins;

namespace HaloDeck.App.Plugins;

/// <summary>
/// Manages isolated community plugin backend worker processes communicating via line-delimited
/// UTF-8 JSON-RPC 2.0 over stdin/stdout, bound to a Win32 Job Object so zero orphan workers
/// survive HaloDeck exit or crash.
/// </summary>
public sealed class PluginProcessHost : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly ConcurrentDictionary<string, WorkerSession> _workers = new(StringComparer.OrdinalIgnoreCase);
    private readonly nint _jobHandle;
    private bool _disposed;

    public event Action<string, string, JsonElement>? PluginBackendEvent;

    public PluginProcessHost()
    {
        _jobHandle = CreateKillOnCloseJob();
    }

    public bool IsRunning(string pluginId)
        => _workers.TryGetValue(pluginId, out var w) && w.IsAlive;

    public List<string> GetRecentLogs(string pluginId)
        => _workers.TryGetValue(pluginId, out var w) ? w.GetLogsSnapshot() : new List<string>();

    public async Task<JsonElement> CallAsync(
        InstalledPluginInfo plugin,
        string method,
        JsonElement? parameters,
        object? context,
        int timeoutMs = 30000,
        CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PluginProcessHost));
        if (!plugin.Enabled || !plugin.IsValid)
        {
            throw new InvalidOperationException($"插件 {plugin.Id} 未启用或校验失败");
        }
        if (!plugin.HasBackend || !plugin.Manifest.HasPermission("backend"))
        {
            throw new InvalidOperationException($"插件 {plugin.Id} 未声明 backend 权限或未配置后端程序");
        }

        var session = GetOrStartWorker(plugin);
        return await session.CallAsync(method, parameters, context, timeoutMs, ct).ConfigureAwait(false);
    }

    public async Task NotifyAsync(
        InstalledPluginInfo plugin,
        string method,
        object? parameters,
        object? context,
        bool startIfNotRunning = false)
    {
        if (_disposed || !plugin.Enabled || !plugin.IsValid || !plugin.HasBackend || !plugin.Manifest.HasPermission("backend"))
        {
            return;
        }

        WorkerSession? session;
        if (startIfNotRunning)
        {
            try
            {
                session = GetOrStartWorker(plugin);
            }
            catch
            {
                return;
            }
        }
        else if (!_workers.TryGetValue(plugin.Id, out session) || !session.IsAlive)
        {
            return;
        }

        await session.NotifyAsync(method, parameters, context).ConfigureAwait(false);
    }

    public void StopPlugin(string pluginId)
    {
        if (_workers.TryRemove(pluginId, out var session))
        {
            session.Dispose();
        }
    }

    public void ReapIdleWorkers()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _workers)
        {
            if (kv.Value.IsIdleExpired(now))
            {
                if (_workers.TryRemove(kv.Key, out var removed))
                {
                    removed.Dispose();
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var kv in _workers)
        {
            kv.Value.Dispose();
        }
        _workers.Clear();

        if (_jobHandle != 0)
        {
            CloseHandle(_jobHandle);
        }
    }

    private WorkerSession GetOrStartWorker(InstalledPluginInfo plugin)
    {
        while (true)
        {
            if (_workers.TryGetValue(plugin.Id, out var existing) && existing.IsAlive)
            {
                return existing;
            }

            if (existing is not null && _workers.TryRemove(plugin.Id, out var dead))
            {
                dead.Dispose();
            }

            var created = StartNewWorker(plugin);
            if (_workers.TryAdd(plugin.Id, created))
            {
                return created;
            }

            created.Dispose();
        }
    }

    private WorkerSession StartNewWorker(InstalledPluginInfo plugin)
    {
        var backend = plugin.Manifest.Backend
                      ?? throw new InvalidOperationException("插件未配置 backend");

        if (!PluginManifest.TryResolveSafeChildPath(plugin.RootDirectory, backend.Executable, out string fullExe) ||
            !File.Exists(fullExe))
        {
            throw new FileNotFoundException($"找不到插件后端文件：{backend.Executable}", fullExe);
        }

        Directory.CreateDirectory(plugin.DataDirectory);

        var psi = BuildStartInfo(plugin, backend, fullExe);
        var proc = Process.Start(psi)
                   ?? throw new InvalidOperationException($"无法启动插件后端进程：{backend.Executable}");

        if (_jobHandle != 0)
        {
            try
            {
                AssignProcessToJobObject(_jobHandle, proc.Handle);
            }
            catch
            {
                // Best-effort job assignment (nested job policies on older OS configs)
            }
        }

        return new WorkerSession(
            plugin.Id,
            proc,
            TimeSpan.FromSeconds(backend.IdleTimeoutSeconds),
            (eventName, data) => PluginBackendEvent?.Invoke(plugin.Id, eventName, data));
    }

    private static ProcessStartInfo BuildStartInfo(
        InstalledPluginInfo plugin,
        PluginBackendConfig backend,
        string fullExe)
    {
        string ext = Path.GetExtension(fullExe).ToLowerInvariant();
        string fileName;
        var args = new List<string>();

        if (ext == ".ps1")
        {
            fileName = "powershell.exe";
            args.AddRange(new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", fullExe });
        }
        else if (ext is ".cmd" or ".bat")
        {
            fileName = "cmd.exe";
            args.AddRange(new[] { "/c", fullExe });
        }
        else
        {
            fileName = fullExe;
        }

        if (backend.Args is { Count: > 0 })
        {
            args.AddRange(backend.Args);
        }

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = plugin.RootDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }

        psi.Environment["HALODECK_PLUGIN_ID"] = plugin.Id;
        psi.Environment["HALODECK_PLUGIN_DIR"] = plugin.RootDirectory;
        psi.Environment["HALODECK_PLUGIN_DATA_DIR"] = plugin.DataDirectory;
        psi.Environment["HALODECK_PLUGIN_API"] = "1";
        return psi;
    }

    private sealed class WorkerSession : IDisposable
    {
        private readonly string _pluginId;
        private readonly Process _process;
        private readonly TimeSpan _idleTimeout;
        private readonly Action<string, JsonElement> _onEvent;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
        private readonly object _logLock = new();
        private readonly Queue<string> _stderrLogs = new();
        private readonly CancellationTokenSource _cts = new();
        private long _reqSeq;
        private DateTime _lastActivityUtc = DateTime.UtcNow;
        private bool _disposed;

        public bool IsAlive
        {
            get
            {
                if (_disposed) return false;
                try { return !_process.HasExited; } catch { return false; }
            }
        }

        public WorkerSession(
            string pluginId,
            Process process,
            TimeSpan idleTimeout,
            Action<string, JsonElement> onEvent)
        {
            _pluginId = pluginId;
            _process = process;
            _idleTimeout = idleTimeout;
            _onEvent = onEvent;

            _ = Task.Run(ReadStdoutLoopAsync);
            _ = Task.Run(ReadStderrLoopAsync);
        }

        public bool IsIdleExpired(DateTime utcNow)
            => _pending.IsEmpty && (utcNow - _lastActivityUtc) > _idleTimeout;

        public List<string> GetLogsSnapshot()
        {
            lock (_logLock)
            {
                return _stderrLogs.ToList();
            }
        }

        public async Task<JsonElement> CallAsync(
            string method,
            JsonElement? parameters,
            object? context,
            int timeoutMs,
            CancellationToken ct)
        {
            if (!IsAlive) throw new InvalidOperationException($"插件后端 {_pluginId} 已退出");

            string id = $"p_{Interlocked.Increment(ref _reqSeq)}";
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;
            _lastActivityUtc = DateTime.UtcNow;

            try
            {
                string line = JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id,
                    method,
                    @params = parameters,
                    context
                }, JsonOpts);

                await WriteLineAsync(line, ct).ConfigureAwait(false);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
                timeoutCts.CancelAfter(Math.Clamp(timeoutMs, 1000, 120000));
                using var reg = timeoutCts.Token.Register(() =>
                {
                    if (_pending.TryRemove(id, out var pendingTcs))
                    {
                        pendingTcs.TrySetException(new TimeoutException($"插件后端调用 {method} 超时 ({timeoutMs}ms)"));
                    }
                });

                JsonElement res = await tcs.Task.ConfigureAwait(false);
                _lastActivityUtc = DateTime.UtcNow;
                return res;
            }
            catch
            {
                _pending.TryRemove(id, out _);
                throw;
            }
        }

        public async Task NotifyAsync(string method, object? parameters, object? context)
        {
            if (!IsAlive) return;
            try
            {
                _lastActivityUtc = DateTime.UtcNow;
                string line = JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    method,
                    @params = parameters,
                    context
                }, JsonOpts);
                await WriteLineAsync(line, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort notification
            }
        }

        private async Task WriteLineAsync(string line, CancellationToken ct)
        {
            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _process.StandardInput.WriteLineAsync(line).ConfigureAwait(false);
                await _process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private async Task ReadStdoutLoopAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    string? line = await _process.StandardOutput.ReadLineAsync(_cts.Token).ConfigureAwait(false);
                    if (line is null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("id", out var idProp) && idProp.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                        {
                            string id = idProp.ValueKind == JsonValueKind.String ? (idProp.GetString() ?? "") : idProp.GetRawText();
                            if (_pending.TryRemove(id, out var tcs))
                            {
                                if (root.TryGetProperty("error", out var errProp) && errProp.ValueKind != JsonValueKind.Null)
                                {
                                    string msg = errProp.ValueKind == JsonValueKind.Object && errProp.TryGetProperty("message", out var mp)
                                        ? (mp.GetString() ?? errProp.GetRawText())
                                        : errProp.GetRawText();
                                    tcs.TrySetException(new InvalidOperationException(msg));
                                }
                                else if (root.TryGetProperty("result", out var resProp))
                                {
                                    tcs.TrySetResult(resProp.Clone());
                                }
                                else
                                {
                                    tcs.TrySetResult(default);
                                }
                            }
                        }
                        else if (root.TryGetProperty("method", out var mProp) &&
                                 string.Equals(mProp.GetString(), "event", StringComparison.OrdinalIgnoreCase) &&
                                 root.TryGetProperty("params", out var pProp) &&
                                 pProp.ValueKind == JsonValueKind.Object)
                        {
                            string evName = pProp.TryGetProperty("name", out var np) ? (np.GetString() ?? "message") : "message";
                            JsonElement evData = pProp.TryGetProperty("data", out var dp) ? dp.Clone() : default;
                            _onEvent(evName, evData);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"[stdout-parse] {ex.Message}: {line[..Math.Min(line.Length, 160)]}");
                    }
                }
            }
            catch
            {
                // Process exited or cancelled
            }
            finally
            {
                foreach (var kv in _pending)
                {
                    if (_pending.TryRemove(kv.Key, out var tcs))
                    {
                        tcs.TrySetException(new InvalidOperationException($"插件后端 {_pluginId} 意外退出"));
                    }
                }
            }
        }

        private async Task ReadStderrLoopAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    string? line = await _process.StandardError.ReadLineAsync(_cts.Token).ConfigureAwait(false);
                    if (line is null) break;
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        AppendLog(line);
                    }
                }
            }
            catch
            {
                // Ignore stream close
            }
        }

        private void AppendLog(string line)
        {
            lock (_logLock)
            {
                string stamp = DateTime.Now.ToString("HH:mm:ss");
                _stderrLogs.Enqueue($"[{stamp}] {line}");
                while (_stderrLogs.Count > 60)
                {
                    _stderrLogs.Dequeue();
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();

            try
            {
                if (!_process.HasExited)
                {
                    try { _process.StandardInput.Close(); } catch { }
                    if (!_process.WaitForExit(350))
                    {
                        _process.Kill(entireProcessTree: true);
                    }
                }
            }
            catch
            {
                // Ignore teardown race
            }

            try { _process.Dispose(); } catch { }
            _writeLock.Dispose();
            _cts.Dispose();
        }
    }

    // ---------------- Win32 Job Object (Kill child workers on host exit) ----------------

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateJobObjectW(nint lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        nint hJob,
        int infoType,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(nint hJob, nint hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    private static nint CreateKillOnCloseJob()
    {
        try
        {
            nint hJob = CreateJobObjectW(0, null);
            if (hJob == 0) return 0;

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                }
            };

            if (!SetInformationJobObject(
                    hJob,
                    JobObjectExtendedLimitInformation,
                    ref info,
                    (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
            {
                CloseHandle(hJob);
                return 0;
            }

            return hJob;
        }
        catch
        {
            return 0;
        }
    }
}
