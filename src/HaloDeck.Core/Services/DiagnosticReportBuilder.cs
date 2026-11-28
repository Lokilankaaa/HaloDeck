using System.Text;
using System.Text.RegularExpressions;
using HaloDeck.Core.Config;
using HaloDeck.Core.Models;

namespace HaloDeck.Core.Services;

public sealed class DiagnosticComponentEntry
{
    public string Name { get; set; } = string.Empty;
    public bool Installed { get; set; }
    public string Version { get; set; } = string.Empty;
}

public sealed class DiagnosticPluginEntry
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool IsValid { get; set; }
}

public sealed class DiagnosticReportInput
{
    public string AppVersion { get; set; } = "0.6.3";
    public bool IsElevated { get; set; }
    public string OsVersion { get; set; } = Environment.OSVersion.VersionString;
    public string CpuName { get; set; } = string.Empty;
    public int DedicatedVramMb { get; set; }
    public double TotalRamGb { get; set; }
    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }
    public int RefreshRateHz { get; set; }
    public bool IsDynamicRefreshRate { get; set; }
    public bool HardwareServiceRunning { get; set; }
    public int HardwareDaemonVersion { get; set; }
    public bool SmuReady { get; set; }
    public bool EcReady { get; set; }
    public int CurrentTdpWatts { get; set; }
    public double? HwStapmLimitWatts { get; set; }
    public string FanMode { get; set; } = "auto";
    public int FanRpm { get; set; }
    public bool GpdConnected { get; set; }
    public string GpdFirmwareX { get; set; } = string.Empty;
    public string GpdFirmwareK { get; set; } = string.Empty;
    public bool InterceptPhysicalMenuButton { get; set; } = true;
    public bool InterceptGuideMenuButton { get; set; } = true;
    public string ActiveOptiScalerSlot { get; set; } = nameof(ComponentSlot.OptiScalerNightly);
    public string DownloadMirrorPrefix { get; set; } = string.Empty;
    public string UserDataDirectory { get; set; } = string.Empty;
    public string LogsDirectory { get; set; } = string.Empty;
    public IReadOnlyList<DiagnosticComponentEntry> Components { get; set; } = Array.Empty<DiagnosticComponentEntry>();
    public IReadOnlyList<DiagnosticPluginEntry> Plugins { get; set; } = Array.Empty<DiagnosticPluginEntry>();
    public string? UpdateLogTail { get; set; }
}

public static class DiagnosticReportBuilder
{
    private static readonly Regex WindowsUserPathRegex = new(
        @"(?<prefix>[A-Za-z]:\\Users\\)(?<user>[^\\/\r\n""'<>|]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ForwardSlashUserPathRegex = new(
        @"(?<prefix>[A-Za-z]:/Users/)(?<user>[^\\/\r\n""'<>|]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string SanitizePaths(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        string s = WindowsUserPathRegex.Replace(input, "${prefix}***");
        s = ForwardSlashUserPathRegex.Replace(s, "${prefix}***");
        return s;
    }

    public static string ReadUpdateLogTail(string? customAppDataRoot = null, int maxLines = 12)
    {
        try
        {
            string primary = HaloDeckPaths.ResolveUpdateLogPath(customAppDataRoot);
            string legacy = HaloDeckPaths.ResolveLegacyUpdateLogPath(customAppDataRoot);
            string? path = File.Exists(primary) ? primary : (File.Exists(legacy) ? legacy : null);
            if (path is null)
            {
                return string.Empty;
            }

            var lines = File.ReadLines(path, Encoding.UTF8)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .TakeLast(Math.Max(1, maxLines))
                .ToArray();

            return lines.Length == 0 ? string.Empty : SanitizePaths(string.Join("\n", lines));
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string Build(DiagnosticReportInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var sb = new StringBuilder(1024);
        sb.AppendLine("=== HaloDeck 环境诊断报告 ===");
        sb.AppendLine($"版本与权限: HaloDeck v{input.AppVersion} ({(input.IsElevated ? "管理员权限" : "标准用户权限")})");
        sb.AppendLine($"操作系统: {input.OsVersion}");

        string vramStr = input.DedicatedVramMb > 0
            ? $"{input.DedicatedVramMb} MB ({input.DedicatedVramMb / 1024.0:F1} GB DXGI 物理专用显存)"
            : "未检测到 DXGI 专用显存";
        string ramStr = input.TotalRamGb > 0 ? $"{input.TotalRamGb:F0} GB" : "未知";
        string cpuStr = string.IsNullOrWhiteSpace(input.CpuName) ? "AMD Strix Halo" : input.CpuName;
        sb.AppendLine($"处理器 / 显存 / 内存: {cpuStr} | VRAM: {vramStr} | RAM: {ramStr}");

        if (input.ScreenWidth > 0 && input.ScreenHeight > 0)
        {
            sb.AppendLine($"显示输出: {input.ScreenWidth}x{input.ScreenHeight} @ {input.RefreshRateHz}Hz{(input.IsDynamicRefreshRate ? " (DRR/VRR)" : "")}");
        }

        string svcState = input.HardwareServiceRunning
            ? $"运行中 (协议 v{input.HardwareDaemonVersion}) | PawnIO/SMU={(input.SmuReady ? "就绪" : "未就绪")} | EC={(input.EcReady ? "就绪" : "未就绪")}"
            : "未启动 (标准用户态)";
        sb.AppendLine($"硬件守护与驱动: HaloDeckHardwareSvc {svcState}");

        string splStr = input.HwStapmLimitWatts.HasValue ? $"{input.HwStapmLimitWatts.Value:F0}W" : "--";
        sb.AppendLine($"功耗与散热: 目标 TDP={input.CurrentTdpWatts}W (PM SPL={splStr}) | 风扇模式={input.FanMode} ({input.FanRpm} RPM)");

        string fwStr = input.GpdConnected
            ? $"已连接 (FW X{(string.IsNullOrWhiteSpace(input.GpdFirmwareX) ? "?" : input.GpdFirmwareX)} / K{(string.IsNullOrWhiteSpace(input.GpdFirmwareK) ? "?" : input.GpdFirmwareK)})"
            : "未检测到手柄";
        sb.AppendLine($"手柄与菜单键: {fwStr} | 0xFF00 HID/F13={(input.InterceptPhysicalMenuButton ? "已启用" : "已关闭")} | Guide={(input.InterceptGuideMenuButton ? "已启用" : "已关闭")}");

        var installedComps = input.Components.Where(c => c.Installed).ToList();
        string compSummary = installedComps.Count == 0
            ? "无"
            : string.Join(", ", installedComps.Select(c => $"{c.Name}({(string.IsNullOrWhiteSpace(c.Version) ? "已安装" : c.Version)})"));
        sb.AppendLine($"核心组件 ({installedComps.Count}/{input.Components.Count}, 默认内核 {input.ActiveOptiScalerSlot}): {compSummary}");

        var enabledPlugins = input.Plugins.Where(p => p.Enabled && p.IsValid).ToList();
        string pluginSummary = enabledPlugins.Count == 0
            ? "无"
            : string.Join(", ", enabledPlugins.Select(p => $"{p.Id}@{p.Version}"));
        sb.AppendLine($"社区插件 ({enabledPlugins.Count}/{input.Plugins.Count} 已启用): {pluginSummary}");

        string mirrorText = string.IsNullOrWhiteSpace(input.DownloadMirrorPrefix)
            ? "GitHub 官方直连"
            : input.DownloadMirrorPrefix.Trim();
        sb.AppendLine($"全局下载线路: {mirrorText}");

        if (!string.IsNullOrWhiteSpace(input.UserDataDirectory))
        {
            sb.AppendLine($"用户数据目录: {input.UserDataDirectory}");
        }
        if (!string.IsNullOrWhiteSpace(input.LogsDirectory))
        {
            sb.AppendLine($"运行日志目录: {input.LogsDirectory}");
        }

        if (!string.IsNullOrWhiteSpace(input.UpdateLogTail))
        {
            sb.AppendLine("--- 最近更新日志摘要 (last-update.log) ---");
            sb.AppendLine(input.UpdateLogTail.Trim());
        }

        return SanitizePaths(sb.ToString().TrimEnd());
    }
}
