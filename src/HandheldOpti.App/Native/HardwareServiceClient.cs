using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using HandheldOpti.Core.Models;

namespace HandheldOpti.App.Native;

/// <summary>
/// Non-admin Named Pipe client talking to HaloDeckHardwareSvc.
/// Falls back quickly when the service is not yet installed so UI polling never blocks.
/// </summary>
public sealed class HardwareServiceClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private HardwareDaemonStatus _cachedStatus = new();
    private DateTime _lastStatusAtUtc = DateTime.MinValue;

    public HardwareDaemonStatus GetStatus(bool forceRefresh = false)
    {
        if (!forceRefresh && (DateTime.UtcNow - _lastStatusAtUtc) < TimeSpan.FromMilliseconds(1400))
        {
            return _cachedStatus;
        }

        var (installed, running) = PawnIoHardwareDaemon.ProbeWindowsServiceState(PawnIoHardwareDaemon.WindowsServiceName);
        var (gpdExists, gpdRunning) = PawnIoHardwareDaemon.ProbeWindowsServiceState("GPDToolService");

        if (!installed || !running)
        {
            _cachedStatus = new HardwareDaemonStatus
            {
                ServiceInstalled = installed,
                ServiceRunning = running,
                PawnIoInstalled = File.Exists(@"C:\Program Files\PawnIO\PawnIOLib.dll"),
                GpdServiceExists = gpdExists,
                GpdServiceRunning = gpdRunning
            };
            _lastStatusAtUtc = DateTime.UtcNow;
            return _cachedStatus;
        }

        var (ok, _, status) = SendPipeRequest(new { op = "status" });
        if (ok && status is not null)
        {
            status.ServiceInstalled = true;
            status.ServiceRunning = true;
            _cachedStatus = status;
        }
        else
        {
            _cachedStatus = new HardwareDaemonStatus
            {
                ServiceInstalled = installed,
                ServiceRunning = running,
                PawnIoInstalled = File.Exists(@"C:\Program Files\PawnIO\PawnIOLib.dll"),
                GpdServiceExists = gpdExists,
                GpdServiceRunning = gpdRunning,
                LastError = "硬件服务管道响应超时"
            };
        }
        _lastStatusAtUtc = DateTime.UtcNow;
        return _cachedStatus;
    }

    public (bool Ok, string Error, HardwareDaemonStatus Status) SetTdp(int stapmWatts, int? slowWatts = null, int? fastWatts = null)
    {
        bool v1Daemon = _cachedStatus.ServiceRunning && _cachedStatus.DaemonVersion < PawnIoHardwareDaemon.CurrentDaemonVersion;
        int w = v1Daemon ? Math.Clamp(stapmWatts, 12, 85) : Math.Clamp(stapmWatts, 8, 120);
        int slow = Math.Clamp(slowWatts ?? w, 8, 130);
        int fast = Math.Clamp(fastWatts ?? Math.Min(140, Math.Max(slow, w + 2)), 8, 140);

        var res = SendPipeRequest(new { op = "set_tdp", watts = w, slowWatts = slow, fastWatts = fast });
        if (res.Status is not null) _cachedStatus = res.Status;
        _lastStatusAtUtc = DateTime.UtcNow;
        return (res.Ok, res.Error, _cachedStatus);
    }

    public (bool Ok, string Error, HardwareDaemonStatus Status) SetFan(
        string mode,
        int percent,
        int tempC,
        IEnumerable<FanCurvePoint>? curvePoints = null)
    {
        bool v1Daemon = _cachedStatus.ServiceRunning && _cachedStatus.DaemonVersion < PawnIoHardwareDaemon.CurrentDaemonVersion;
        string normalized = (mode ?? "auto").Trim().ToLowerInvariant();
        if (v1Daemon && normalized == "curve")
        {
            int evaluatedPct = FanCurveEvaluator.Evaluate(tempC, curvePoints);
            var fallbackRes = SendPipeRequest(new { op = "set_fan", mode = "manual", percent = evaluatedPct, tempC });
            if (fallbackRes.Status is not null) _cachedStatus = fallbackRes.Status;
            _lastStatusAtUtc = DateTime.UtcNow;
            return (fallbackRes.Ok, fallbackRes.Error, _cachedStatus);
        }

        var pts = curvePoints is null ? null : FanCurveEvaluator.NormalizePoints(curvePoints);
        var res = SendPipeRequest(new
        {
            op = "set_fan",
            mode = normalized,
            percent = Math.Clamp(percent, 20, 100),
            tempC,
            curvePoints = pts
        });
        if (res.Status is not null) _cachedStatus = res.Status;
        _lastStatusAtUtc = DateTime.UtcNow;
        return (res.Ok, res.Error, _cachedStatus);
    }

    public (bool Ok, string Error, HardwareDaemonStatus Status) SetGpdServicePaused(bool paused)
    {
        var res = SendPipeRequest(new { op = "set_gpd_service", paused });
        if (res.Status is not null) _cachedStatus = res.Status;
        _lastStatusAtUtc = DateTime.UtcNow;
        return (res.Ok, res.Error, _cachedStatus);
    }

    private (bool Ok, string Error, HardwareDaemonStatus? Status) SendPipeRequest(object payload)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PawnIoHardwareDaemon.PipeName, PipeDirection.InOut, PipeOptions.None);
            client.Connect(320);

            using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);

            writer.WriteLine(JsonSerializer.Serialize(payload, JsonOpts));
            string? line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                return (false, "硬件服务返回空响应", null);
            }

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            bool ok = root.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();
            string err = root.TryGetProperty("error", out var errProp) ? (errProp.GetString() ?? "") : "";
            HardwareDaemonStatus? st = root.TryGetProperty("status", out var stProp)
                ? JsonSerializer.Deserialize<HardwareDaemonStatus>(stProp.GetRawText(), JsonOpts)
                : null;
            return (ok, err, st);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }
}
