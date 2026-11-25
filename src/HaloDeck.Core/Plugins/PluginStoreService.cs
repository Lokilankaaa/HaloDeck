using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using HaloDeck.Core.Config;

namespace HaloDeck.Core.Plugins;

public sealed class PluginThemeOverride
{
    public string PluginId { get; set; } = string.Empty;
    public Dictionary<string, string> Variables { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Manages Decky-style HaloDeck community plugins: discovery, manifest validation,
/// Zip-Slip-safe archive installation, enable/disable consent state, uninstall, and isolated per-plugin KV storage.
/// </summary>
public sealed class PluginStoreService
{
    public const string PluginVirtualHost = "plugins.halodeck.local";
    public const long MaxArchiveUncompressedBytes = 100L * 1024L * 1024L; // 100 MB safety ceiling

    public static readonly HashSet<string> AllowedThemeVariables = new(StringComparer.Ordinal)
    {
        // Primary HaloDeck styles.css design tokens
        "--bg-base",
        "--bg-elevated",
        "--bg-card",
        "--bg-card-solid",
        "--bg-subtile",
        "--bg-row",
        "--bg-well",
        "--text-primary",
        "--text-secondary",
        "--text-muted",
        "--border",
        "--border-strong",
        "--accent",
        "--accent-hover",
        "--accent-soft",
        "--accent-glow",
        "--accent-grad",
        "--radius-sm",
        "--radius-md",
        "--radius-lg",
        "--radius-card",
        "--radius-inner",
        // Legacy aliases kept for backwards compatibility
        "--bg-app",
        "--bg-panel",
        "--surface-subtle"
    };

    private static readonly Regex SafeCssValueRegex = new(@"^[#a-zA-Z0-9(),.%\s-]+$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly object _sync = new();
    private readonly string _pluginsRoot;
    private readonly string _pluginDataRoot;
    private readonly string _stateFilePath;
    private readonly string _themeOverridePath;
    private Dictionary<string, PluginStateEntry> _stateMap = new(StringComparer.OrdinalIgnoreCase);
    private PluginThemeOverride? _activeThemeOverride;

    public string PluginsRoot => _pluginsRoot;
    public string PluginDataRoot => _pluginDataRoot;

    public PluginStoreService(string? customAppDataRoot = null)
    {
        string appData = HaloDeckPaths.ResolveAppDataRoot(customAppDataRoot);
        _pluginsRoot = Path.Combine(appData, "plugins");
        _pluginDataRoot = Path.Combine(appData, "plugin_data");
        _stateFilePath = Path.Combine(appData, "plugins_state.json");
        _themeOverridePath = Path.Combine(appData, "plugin_theme_override.json");
        Directory.CreateDirectory(_pluginsRoot);
        Directory.CreateDirectory(_pluginDataRoot);
        LoadStateLocked();
        LoadThemeOverrideLocked();
    }

    public List<InstalledPluginInfo> ListPlugins()
    {
        lock (_sync)
        {
            Directory.CreateDirectory(_pluginsRoot);
            var list = new List<InstalledPluginInfo>();

            foreach (string dir in Directory.EnumerateDirectories(_pluginsRoot))
            {
                string folderName = Path.GetFileName(dir);
                string manifestPath = Path.Combine(dir, "plugin.json");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                try
                {
                    string raw = File.ReadAllText(manifestPath);
                    var manifest = JsonSerializer.Deserialize<PluginManifest>(raw, JsonOpts);
                    var (valid, err) = PluginManifest.Validate(manifest, dir);
                    if (!valid || manifest is null)
                    {
                        list.Add(new InstalledPluginInfo
                        {
                            Id = string.IsNullOrWhiteSpace(manifest?.Id) ? folderName : manifest.Id,
                            Name = string.IsNullOrWhiteSpace(manifest?.Name) ? folderName : manifest.Name,
                            Version = manifest?.Version ?? "?",
                            Author = manifest?.Author ?? "",
                            Description = manifest?.Description ?? "",
                            IsValid = false,
                            ValidationError = err,
                            RootDirectory = dir,
                            DataDirectory = Path.Combine(_pluginDataRoot, folderName)
                        });
                        continue;
                    }

                    // Ensure folder name matches validated manifest.Id so virtual-host URLs are canonical
                    if (!string.Equals(folderName, manifest.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add(new InstalledPluginInfo
                        {
                            Id = manifest.Id,
                            Name = manifest.Name,
                            Version = manifest.Version,
                            Author = manifest.Author,
                            Description = manifest.Description,
                            IsValid = false,
                            ValidationError = $"插件目录名 ({folderName}) 与 plugin.json 中的 id ({manifest.Id}) 不一致",
                            RootDirectory = dir,
                            DataDirectory = Path.Combine(_pluginDataRoot, manifest.Id)
                        });
                        continue;
                    }

                    _stateMap.TryGetValue(manifest.Id, out var st);
                    bool hasBackend = manifest.Backend is not null && !string.IsNullOrWhiteSpace(manifest.Backend.Executable);
                    string entryUrl = $"https://{PluginVirtualHost}/{Uri.EscapeDataString(manifest.Id)}/{manifest.Entry}";
                    string iconUrl = !string.IsNullOrWhiteSpace(manifest.Icon) &&
                                     PluginManifest.TryResolveSafeChildPath(dir, manifest.Icon, out string iconPath) &&
                                     File.Exists(iconPath)
                        ? $"https://{PluginVirtualHost}/{Uri.EscapeDataString(manifest.Id)}/{manifest.Icon}"
                        : string.Empty;

                    string dataDir = Path.Combine(_pluginDataRoot, manifest.Id);
                    list.Add(new InstalledPluginInfo
                    {
                        Id = manifest.Id,
                        Name = manifest.Name,
                        Version = manifest.Version,
                        Author = manifest.Author,
                        Description = manifest.Description,
                        Homepage = manifest.Homepage,
                        Mount = manifest.Mount.ToList(),
                        Activation = manifest.Activation.ToList(),
                        Permissions = manifest.Permissions.ToList(),
                        EntryUrl = entryUrl,
                        IconUrl = iconUrl,
                        HasBackend = hasBackend,
                        Enabled = (st?.Enabled ?? false) && !(st?.RevokedByStore ?? false),
                        ConsentAccepted = st?.ConsentAccepted ?? false,
                        RevokedByStore = st?.RevokedByStore ?? false,
                        RevokedReason = st?.RevokedReason ?? string.Empty,
                        IsValid = true,
                        RootDirectory = dir,
                        DataDirectory = dataDir,
                        Manifest = manifest
                    });
                }
                catch (Exception ex)
                {
                    list.Add(new InstalledPluginInfo
                    {
                        Id = folderName,
                        Name = folderName,
                        IsValid = false,
                        ValidationError = $"解析 plugin.json 失败：{ex.Message}",
                        RootDirectory = dir,
                        DataDirectory = Path.Combine(_pluginDataRoot, folderName)
                    });
                }
            }

            return list
                .OrderByDescending(p => p.Enabled)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public InstalledPluginInfo? GetPlugin(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) return null;
        return ListPlugins().FirstOrDefault(p => string.Equals(p.Id, pluginId, StringComparison.OrdinalIgnoreCase));
    }

    public InstalledPluginInfo InstallFromZip(
        string zipPath,
        bool enableAfterInstall = false,
        bool consentAccepted = false,
        Action<string>? beforeOverwrite = null)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
        {
            throw new FileNotFoundException("未找到指定的插件 .zip 文件", zipPath);
        }

        string stageRoot = Path.Combine(Path.GetTempPath(), "HaloDeck_PluginStage_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stageRoot);
        try
        {
            ExtractZipSafely(zipPath, stageRoot);
            string sourceDir = LocatePluginRootInStage(stageRoot);
            return InstallValidatedDirectoryCore(sourceDir, enableAfterInstall, consentAccepted, beforeOverwrite);
        }
        finally
        {
            try { Directory.Delete(stageRoot, recursive: true); } catch { }
        }
    }

    public InstalledPluginInfo InstallFromDirectory(
        string sourcePluginDir,
        bool enableAfterInstall = false,
        bool consentAccepted = false,
        Action<string>? beforeOverwrite = null)
    {
        if (string.IsNullOrWhiteSpace(sourcePluginDir) || !Directory.Exists(sourcePluginDir))
        {
            throw new DirectoryNotFoundException($"未找到插件源码目录：{sourcePluginDir}");
        }

        string normalizedSource = Path.GetFullPath(sourcePluginDir);
        string manifestPath = Path.Combine(normalizedSource, "plugin.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException("所选目录根部缺少 plugin.json");
        }

        return InstallValidatedDirectoryCore(normalizedSource, enableAfterInstall, consentAccepted, beforeOverwrite);
    }

    private InstalledPluginInfo InstallValidatedDirectoryCore(
        string sourceDir,
        bool enableAfterInstall,
        bool consentAccepted,
        Action<string>? beforeOverwrite)
    {
        string manifestPath = Path.Combine(sourceDir, "plugin.json");
        var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath), JsonOpts);
        var (valid, err) = PluginManifest.Validate(manifest, sourceDir);
        if (!valid || manifest is null)
        {
            throw new InvalidOperationException($"插件校验失败：{err}");
        }

        // Stop any running backend worker BEFORE attempting to overwrite the plugin directory.
        beforeOverwrite?.Invoke(manifest.Id);

        lock (_sync)
        {
            string destDir = Path.Combine(_pluginsRoot, manifest.Id);
            string normalizedRoot = Path.GetFullPath(_pluginsRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullDest = Path.GetFullPath(destDir);
            if (!fullDest.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("插件安装目标路径越界");
            }

            string fullSource = Path.GetFullPath(sourceDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullDestTrim = fullDest.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!string.Equals(fullSource, fullDestTrim, StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(fullDest))
                {
                    DeleteDirectoryWithRetry(fullDest);
                }

                CopyDirectoryRecursive(sourceDir, fullDest);
            }
            Directory.CreateDirectory(Path.Combine(_pluginDataRoot, manifest.Id));

            if (!_stateMap.TryGetValue(manifest.Id, out var state))
            {
                state = new PluginStateEntry { InstalledAtUtc = DateTimeOffset.UtcNow };
                _stateMap[manifest.Id] = state;
            }
            else if (state.ConsentAccepted && !consentAccepted)
            {
                // Permission-escalation guard: if an updated version requests ANY new permission
                // not previously approved by the user, revoke consent and disable until re-approved.
                var approvedSet = new HashSet<string>(state.ApprovedPermissions ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                bool escalated = manifest.Permissions.Any(p => !approvedSet.Contains(p));
                if (escalated)
                {
                    state.ConsentAccepted = false;
                    state.Enabled = false;
                    ClearThemeOverrideIfOwnedLocked(manifest.Id);
                }
            }

            if (consentAccepted)
            {
                state.ConsentAccepted = true;
                state.ApprovedPermissions = manifest.Permissions.ToList();
            }
            if (enableAfterInstall && state.ConsentAccepted && !state.RevokedByStore)
            {
                state.Enabled = true;
            }
            SaveStateLocked();
        }

        return GetPlugin(manifest.Id)
               ?? throw new InvalidOperationException("插件已安装但重新加载失败");
    }

    private static void DeleteDirectoryWithRetry(string path)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                if (!Directory.Exists(path)) return;
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == 7) throw;
                Thread.Sleep(100);
            }
        }
    }

    public InstalledPluginInfo SetEnabled(string pluginId, bool enabled, bool consentAccepted = false)
    {
        lock (_sync)
        {
            var plugin = ListPlugins().FirstOrDefault(p => string.Equals(p.Id, pluginId, StringComparison.OrdinalIgnoreCase))
                         ?? throw new InvalidOperationException($"未找到插件：{pluginId}");

            if (enabled && !plugin.IsValid)
            {
                throw new InvalidOperationException($"无法启用无效插件：{plugin.ValidationError}");
            }

            if (!_stateMap.TryGetValue(plugin.Id, out var st))
            {
                st = new PluginStateEntry();
                _stateMap[plugin.Id] = st;
            }

            if (enabled && st.RevokedByStore)
            {
                throw new InvalidOperationException($"该插件已被官方商店安全熔断停用：{(string.IsNullOrWhiteSpace(st.RevokedReason) ? "检测到安全风险" : st.RevokedReason)}");
            }

            if (consentAccepted)
            {
                st.ConsentAccepted = true;
                st.ApprovedPermissions = plugin.Permissions.ToList();
            }
            if (enabled && !st.ConsentAccepted)
            {
                throw new InvalidOperationException("首次启用第三方插件前需确认权限与安全提示");
            }

            st.Enabled = enabled;
            SaveStateLocked();
            if (!enabled)
            {
                ClearThemeOverrideIfOwnedLocked(plugin.Id);
            }
            Directory.CreateDirectory(plugin.DataDirectory);
        }

        return GetPlugin(pluginId) ?? throw new InvalidOperationException($"插件状态更新后读取失败：{pluginId}");
    }

    /// <summary>
    /// Applies official store kill-switch revocations: immediately disables matching installed plugins
    /// and clears any active theme override they own. Returns the IDs that were newly revoked/disabled.
    /// </summary>
    public List<string> ApplyStoreRevocations(IEnumerable<string> revokedIds, string reason = "官方商店安全熔断已下架该插件")
    {
        var revokedSet = new HashSet<string>(
            (revokedIds ?? Array.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var affected = new List<string>();
        lock (_sync)
        {
            bool changed = false;
            var installedIds = ListPlugins().Select(p => p.Id).ToList();
            foreach (string id in installedIds)
            {
                bool shouldRevoke = revokedSet.Contains(id);
                if (shouldRevoke)
                {
                    if (!_stateMap.TryGetValue(id, out var st))
                    {
                        st = new PluginStateEntry();
                        _stateMap[id] = st;
                    }
                    if (!st.RevokedByStore || st.Enabled)
                    {
                        st.RevokedByStore = true;
                        st.RevokedReason = reason;
                        st.Enabled = false;
                        ClearThemeOverrideIfOwnedLocked(id);
                        affected.Add(id);
                        changed = true;
                    }
                }
                else if (_stateMap.TryGetValue(id, out var st) && st.RevokedByStore)
                {
                    st.RevokedByStore = false;
                    st.RevokedReason = string.Empty;
                    changed = true;
                }
            }

            if (changed)
            {
                SaveStateLocked();
            }
        }
        return affected;
    }

    public void Uninstall(string pluginId, bool deleteData = false)
    {
        if (string.IsNullOrWhiteSpace(pluginId) || !PluginManifest.ValidIdRegex.IsMatch(pluginId))
        {
            throw new ArgumentException("无效的插件 ID", nameof(pluginId));
        }

        lock (_sync)
        {
            string targetDir = Path.Combine(_pluginsRoot, pluginId);
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, recursive: true);
            }

            if (deleteData)
            {
                string dataDir = Path.Combine(_pluginDataRoot, pluginId);
                if (Directory.Exists(dataDir))
                {
                    Directory.Delete(dataDir, recursive: true);
                }
            }

            _stateMap.Remove(pluginId);
            SaveStateLocked();
            ClearThemeOverrideIfOwnedLocked(pluginId);
        }
    }

    public PluginThemeOverride? GetActiveThemeOverride()
    {
        lock (_sync)
        {
            if (_activeThemeOverride is null || string.IsNullOrWhiteSpace(_activeThemeOverride.PluginId))
            {
                return null;
            }
            if (!_stateMap.TryGetValue(_activeThemeOverride.PluginId, out var st) || !st.Enabled)
            {
                return null;
            }
            return new PluginThemeOverride
            {
                PluginId = _activeThemeOverride.PluginId,
                Variables = new Dictionary<string, string>(_activeThemeOverride.Variables, StringComparer.Ordinal)
            };
        }
    }

    public PluginThemeOverride SetThemeOverride(string pluginId, Dictionary<string, string> requestedVars)
    {
        if (string.IsNullOrWhiteSpace(pluginId) || !PluginManifest.ValidIdRegex.IsMatch(pluginId))
        {
            throw new ArgumentException("无效的插件 ID", nameof(pluginId));
        }

        var sanitized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in requestedVars)
        {
            string key = (kv.Key ?? string.Empty).Trim();
            string val = (kv.Value ?? string.Empty).Trim();
            if (!AllowedThemeVariables.Contains(key))
            {
                throw new ArgumentException($"不支持的皮肤 CSS 变量：{key}");
            }
            if (string.IsNullOrWhiteSpace(val) || val.Length > 64 ||
                val.Contains("url(", StringComparison.OrdinalIgnoreCase) ||
                val.Contains("expression", StringComparison.OrdinalIgnoreCase) ||
                !SafeCssValueRegex.IsMatch(val))
            {
                throw new ArgumentException($"皮肤 CSS 变量 {key} 的值不合法");
            }
            sanitized[key] = val;
        }

        lock (_sync)
        {
            _activeThemeOverride = sanitized.Count == 0
                ? null
                : new PluginThemeOverride { PluginId = pluginId, Variables = sanitized };
            SaveThemeOverrideLocked();
            return _activeThemeOverride ?? new PluginThemeOverride { PluginId = pluginId };
        }
    }

    public void ClearThemeOverride(string? onlyIfPluginId = null)
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(onlyIfPluginId))
            {
                _activeThemeOverride = null;
                SaveThemeOverrideLocked();
            }
            else
            {
                ClearThemeOverrideIfOwnedLocked(onlyIfPluginId);
            }
        }
    }

    private void ClearThemeOverrideIfOwnedLocked(string pluginId)
    {
        if (_activeThemeOverride is not null &&
            string.Equals(_activeThemeOverride.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))
        {
            _activeThemeOverride = null;
            SaveThemeOverrideLocked();
        }
    }

    private void LoadThemeOverrideLocked()
    {
        if (!File.Exists(_themeOverridePath))
        {
            _activeThemeOverride = null;
            return;
        }
        try
        {
            _activeThemeOverride = JsonSerializer.Deserialize<PluginThemeOverride>(File.ReadAllText(_themeOverridePath), JsonOpts);
        }
        catch
        {
            _activeThemeOverride = null;
        }
    }

    private void SaveThemeOverrideLocked()
    {
        try
        {
            if (_activeThemeOverride is null || _activeThemeOverride.Variables.Count == 0)
            {
                if (File.Exists(_themeOverridePath)) File.Delete(_themeOverridePath);
                return;
            }
            string tmp = _themeOverridePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_activeThemeOverride, JsonOpts));
            File.Move(tmp, _themeOverridePath, overwrite: true);
        }
        catch { }
    }

    public JsonElement? StorageGet(string pluginId, string key)
    {
        ValidateStorageKey(key);
        lock (_sync)
        {
            var dict = LoadPluginKvLocked(pluginId);
            return dict.TryGetValue(key, out var val) ? val.Clone() : null;
        }
    }

    public void StorageSet(string pluginId, string key, JsonElement value)
    {
        ValidateStorageKey(key);
        lock (_sync)
        {
            var dict = LoadPluginKvLocked(pluginId);
            dict[key] = value.Clone();
            SavePluginKvLocked(pluginId, dict);
        }
    }

    public void StorageRemove(string pluginId, string key)
    {
        ValidateStorageKey(key);
        lock (_sync)
        {
            var dict = LoadPluginKvLocked(pluginId);
            if (dict.Remove(key))
            {
                SavePluginKvLocked(pluginId, dict);
            }
        }
    }

    public string EnsurePluginDataDirectory(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId) || !PluginManifest.ValidIdRegex.IsMatch(pluginId))
        {
            throw new ArgumentException("无效的插件 ID", nameof(pluginId));
        }

        string dataDir = Path.Combine(_pluginDataRoot, pluginId);
        Directory.CreateDirectory(dataDir);
        return dataDir;
    }

    private static void ValidateStorageKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
        {
            throw new ArgumentException("存储键名不能为空且长度不能超过 128 字符", nameof(key));
        }
    }

    private Dictionary<string, JsonElement> LoadPluginKvLocked(string pluginId)
    {
        string path = Path.Combine(EnsurePluginDataDirectory(pluginId), "kv.json");
        if (!File.Exists(path)) return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path), JsonOpts)
                   ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }
    }

    private void SavePluginKvLocked(string pluginId, Dictionary<string, JsonElement> dict)
    {
        string dir = EnsurePluginDataDirectory(pluginId);
        string path = Path.Combine(dir, "kv.json");
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(dict, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }

    private void LoadStateLocked()
    {
        if (!File.Exists(_stateFilePath))
        {
            _stateMap = new Dictionary<string, PluginStateEntry>(StringComparer.OrdinalIgnoreCase);
            return;
        }
        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<string, PluginStateEntry>>(File.ReadAllText(_stateFilePath), JsonOpts);
            _stateMap = loaded is not null
                ? new Dictionary<string, PluginStateEntry>(loaded, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, PluginStateEntry>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _stateMap = new Dictionary<string, PluginStateEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SaveStateLocked()
    {
        string tmp = _stateFilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_stateMap, JsonOpts));
        File.Move(tmp, _stateFilePath, overwrite: true);
    }

    private static void ExtractZipSafely(string zipPath, string destRoot)
    {
        string normalizedRoot = Path.GetFullPath(destRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        long totalUncompressed = 0;

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.FullName)) continue;

            string entryNormalized = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            string destPath = Path.GetFullPath(Path.Combine(normalizedRoot, entryNormalized));
            if (!destPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"压缩包包含非法越界路径 (Zip-Slip)：{entry.FullName}");
            }

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(destPath);
                continue;
            }

            totalUncompressed += Math.Max(0, entry.Length);
            if (totalUncompressed > MaxArchiveUncompressedBytes)
            {
                throw new InvalidOperationException("插件包解压后体积超过 100 MB 安全上限");
            }

            string? parent = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            using var src = entry.Open();
            using var dst = File.Create(destPath);
            byte[] buffer = new byte[81920];
            long writtenEntry = 0;
            int read;
            while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
            {
                writtenEntry += read;
                if (writtenEntry > MaxArchiveUncompressedBytes)
                {
                    throw new InvalidOperationException("插件包单文件解压体积超过安全上限");
                }
                dst.Write(buffer, 0, read);
            }
        }
    }

    private static string LocatePluginRootInStage(string stageRoot)
    {
        if (File.Exists(Path.Combine(stageRoot, "plugin.json")))
        {
            return stageRoot;
        }

        string[] subDirs = Directory.GetDirectories(stageRoot);
        if (subDirs.Length == 1 && File.Exists(Path.Combine(subDirs[0], "plugin.json")))
        {
            return subDirs[0];
        }

        throw new InvalidOperationException("插件压缩包根目录（或单层子目录）未找到 plugin.json");
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (string file in Directory.EnumerateFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        }
        foreach (string sub in Directory.EnumerateDirectories(sourceDir))
        {
            CopyDirectoryRecursive(sub, Path.Combine(destDir, Path.GetFileName(sub)));
        }
    }
}
