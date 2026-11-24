using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HaloDeck.Core.Plugins;

public sealed class PluginBackendConfig
{
    public string Type { get; set; } = "stdio-jsonrpc";
    public string Executable { get; set; } = string.Empty;
    public List<string> Args { get; set; } = new();
    public int IdleTimeoutSeconds { get; set; } = 300;
}

public sealed class PluginManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Homepage { get; set; } = string.Empty;
    public string MinHaloDeckVersion { get; set; } = "0.6.0";
    public string Icon { get; set; } = string.Empty;
    public List<string> Mount { get; set; } = new() { "console", "qam" };
    public List<string> Activation { get; set; } = new() { "onView" };
    public string Entry { get; set; } = "ui/index.html";
    public PluginBackendConfig? Backend { get; set; }
    public List<string> Permissions { get; set; } = new();

    public static readonly Regex ValidIdRegex = new(@"^[a-z0-9][a-z0-9._-]{2,63}$", RegexOptions.Compiled);

    public static readonly HashSet<string> AllowedPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "games:read",
        "telemetry:read",
        "events:game",
        "storage",
        "shell:openFolder",
        "ui:theme",
        "backend",
        "input:sendKey"
    };

    public static readonly HashSet<string> AllowedMounts = new(StringComparer.OrdinalIgnoreCase)
    {
        "console",
        "qam"
    };

    public static readonly HashSet<string> AllowedActivations = new(StringComparer.OrdinalIgnoreCase)
    {
        "onView",
        "onGameLaunch"
    };

    public bool HasPermission(string permission)
        => Permissions.Any(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase));

    public bool SupportsMount(string surface)
        => Mount.Any(m => string.Equals(m, surface, StringComparison.OrdinalIgnoreCase));

    public bool SupportsActivation(string trigger)
        => Activation.Any(a => string.Equals(a, trigger, StringComparison.OrdinalIgnoreCase));

    public static (bool Valid, string Error) Validate(PluginManifest? manifest, string? pluginRootDir = null)
    {
        if (manifest is null) return (false, "plugin.json 内容为空或格式无效");
        if (manifest.SchemaVersion != 1) return (false, $"不支持的插件 schemaVersion={manifest.SchemaVersion}（当前仅支持 1）");

        string id = (manifest.Id ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(id) || !ValidIdRegex.IsMatch(id) || id.Contains("..", StringComparison.Ordinal))
        {
            return (false, $"插件 ID \"{id}\" 不合法（需 3–64 位小写字母/数字/._- 且不能含 ..）");
        }
        manifest.Id = id;

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            return (false, "插件名称 (name) 不能为空");
        }

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            manifest.Version = "1.0.0";
        }

        manifest.Mount = (manifest.Mount ?? new List<string>())
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (manifest.Mount.Count == 0)
        {
            manifest.Mount.Add("console");
        }
        foreach (string m in manifest.Mount)
        {
            if (!AllowedMounts.Contains(m))
            {
                return (false, $"不支持的挂载点 mount=\"{m}\"（仅支持 console / qam）");
            }
        }

        manifest.Activation = (manifest.Activation ?? new List<string>())
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (manifest.Activation.Count == 0)
        {
            manifest.Activation.Add("onView");
        }
        foreach (string a in manifest.Activation)
        {
            if (!AllowedActivations.Contains(a))
            {
                return (false, $"不支持的激活触发器 activation=\"{a}\"（仅支持 onView / onGameLaunch）");
            }
        }

        manifest.Permissions = (manifest.Permissions ?? new List<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (string p in manifest.Permissions)
        {
            if (!AllowedPermissions.Contains(p))
            {
                return (false, $"包含未知权限声明 \"{p}\"");
            }
        }

        if (!TryValidateRelativePath(manifest.Entry, out string normalizedEntry))
        {
            return (false, $"前端入口路径 entry=\"{manifest.Entry}\" 非法");
        }
        manifest.Entry = normalizedEntry;

        if (!string.IsNullOrWhiteSpace(manifest.Icon))
        {
            if (!TryValidateRelativePath(manifest.Icon, out string normalizedIcon))
            {
                return (false, $"图标路径 icon=\"{manifest.Icon}\" 非法");
            }
            manifest.Icon = normalizedIcon;
        }

        if (manifest.Backend is not null && !string.IsNullOrWhiteSpace(manifest.Backend.Executable))
        {
            if (!string.Equals(manifest.Backend.Type, "stdio-jsonrpc", StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"不支持的后端协议类型 backend.type=\"{manifest.Backend.Type}\"（仅支持 stdio-jsonrpc）");
            }
            if (!TryValidateRelativePath(manifest.Backend.Executable, out string normalizedExe))
            {
                return (false, $"后端可执行文件路径 backend.executable=\"{manifest.Backend.Executable}\" 非法");
            }
            manifest.Backend.Executable = normalizedExe;
            manifest.Backend.IdleTimeoutSeconds = Math.Clamp(
                manifest.Backend.IdleTimeoutSeconds <= 0 ? 300 : manifest.Backend.IdleTimeoutSeconds,
                30,
                3600);
            if (!manifest.HasPermission("backend"))
            {
                return (false, "配置了独立后端进程 (backend.executable) 时必须在 permissions 中声明 \"backend\" 权限");
            }
        }

        if (!string.IsNullOrWhiteSpace(pluginRootDir))
        {
            if (!TryResolveSafeChildPath(pluginRootDir, manifest.Entry, out string fullEntry) || !File.Exists(fullEntry))
            {
                return (false, $"未找到前端入口文件：{manifest.Entry}");
            }

            if (manifest.Backend is not null && !string.IsNullOrWhiteSpace(manifest.Backend.Executable))
            {
                if (!TryResolveSafeChildPath(pluginRootDir, manifest.Backend.Executable, out string fullBackend) || !File.Exists(fullBackend))
                {
                    return (false, $"未找到后端程序文件：{manifest.Backend.Executable}");
                }
            }
        }

        return (true, string.Empty);
    }

    public static bool TryValidateRelativePath(string? relPath, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(relPath)) return false;

        string candidate = relPath.Trim().Replace('\\', '/');
        if (candidate.StartsWith('/') || candidate.Contains(':') || Path.IsPathRooted(candidate))
        {
            return false;
        }

        string[] parts = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        foreach (string part in parts)
        {
            if (part is "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return false;
            }
        }

        normalized = string.Join('/', parts);
        return true;
    }

    public static bool TryResolveSafeChildPath(string rootDir, string relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(rootDir) || !TryValidateRelativePath(relativePath, out string norm))
        {
            return false;
        }

        string normalizedRoot = Path.GetFullPath(rootDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string combined = Path.GetFullPath(Path.Combine(normalizedRoot, norm.Replace('/', Path.DirectorySeparatorChar)));
        if (!combined.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        fullPath = combined;
        return true;
    }
}

public sealed class PluginStateEntry
{
    public bool Enabled { get; set; }
    public bool ConsentAccepted { get; set; }
    public List<string> ApprovedPermissions { get; set; } = new();
    public bool RevokedByStore { get; set; }
    public string RevokedReason { get; set; } = string.Empty;
    public DateTimeOffset InstalledAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class InstalledPluginInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Homepage { get; set; } = string.Empty;
    public List<string> Mount { get; set; } = new();
    public List<string> Activation { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public string EntryUrl { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
    public bool HasBackend { get; set; }
    public bool Enabled { get; set; }
    public bool ConsentAccepted { get; set; }
    public bool RevokedByStore { get; set; }
    public string RevokedReason { get; set; } = string.Empty;
    public bool BackendRunning { get; set; }
    public bool IsValid { get; set; } = true;
    public string ValidationError { get; set; } = string.Empty;
    [JsonIgnore]
    public string RootDirectory { get; set; } = string.Empty;
    [JsonIgnore]
    public string DataDirectory { get; set; } = string.Empty;
    [JsonIgnore]
    public PluginManifest Manifest { get; set; } = new();
}
