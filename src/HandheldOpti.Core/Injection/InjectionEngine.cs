using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using HandheldOpti.Core.Components;
using HandheldOpti.Core.Config;
using HandheldOpti.Core.Models;

namespace HandheldOpti.Core.Injection;

public sealed class InjectionEngine
{
    public static readonly string[] AutoProxyPriority =
    {
        "dxgi.dll",
        "winmm.dll",
        "version.dll",
        "dbghelp.dll",
        "winhttp.dll"
    };

    public static readonly string[] SupportedProxyNames =
    {
        "auto",
        "dxgi.dll",
        "winmm.dll",
        "version.dll",
        "dbghelp.dll",
        "d3d12.dll",
        "wininet.dll",
        "winhttp.dll"
    };

    private const string BackupFolderName = ".handheldopti_backup";
    private const string ManifestFileName = "manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly ComponentManager _components;
    private readonly string _indexFilePath;
    private readonly object _indexLock = new();

    public InjectionEngine(ComponentManager components, string? appDataRoot = null)
    {
        _components = components;
        var root = appDataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HandheldOpti");
        Directory.CreateDirectory(root);
        _indexFilePath = Path.Combine(root, "injected_index.json");
    }

    public Task<InjectionResult> InjectAsync(
        GameEntry game,
        OptiScalerConfig config,
        string activeOptiScalerSlot,
        string presetId,
        IReadOnlyCollection<string>? disabledCustomDlls = null,
        bool allowAntiCheatOverride = false,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var targetDir = !string.IsNullOrWhiteSpace(game.TargetRenderDirectory)
                ? game.TargetRenderDirectory
                : game.InstallDirectory;

            if (string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir))
            {
                return Fail($"目标游戏渲染目录不存在: {targetDir}");
            }

            if (!game.IsWritable)
            {
                return Fail("目标目录没有写入权限（可能是受保护的 Xbox/WindowsApps 目录），请尝试将其设为非受保护盘符目录。");
            }

            if (game.AntiCheat.HasAntiCheat && !allowAntiCheatOverride)
            {
                return Fail($"安全拦截：{game.Title} 检测到反作弊系统（{string.Join(", ", game.AntiCheat.DetectedSystems)}），为防止封号已阻止注入。");
            }

            if (game.Architecture == PeArchitecture.X86)
            {
                return Fail($"{game.Title} 为 32 位 (x86) 程序，OptiScaler 仅支持 64 位 (x64) 游戏。");
            }

            var optiRoot = _components.ResolveActiveOptiScalerRoot(activeOptiScalerSlot);
            if (optiRoot is null)
            {
                return Fail("尚未下载或导入 OptiScaler 核心组件，请先前往「组件中心」安装 OptiScaler。");
            }

            var coreDllPath = FindOptiScalerMainBinary(optiRoot);
            if (coreDllPath is null)
            {
                return Fail($"组件目录 {optiRoot} 中未找到 OptiScaler.dll 或 nvngx.dll，请重新下载或导入完整组件包。");
            }

            // Read existing manifest first so re-injection cleanly cleans previous managed files
            var existingManifest = ReadManifest(targetDir);
            var existingBackedUpOriginals = existingManifest?.BackedUpFiles
                .ToDictionary(b => b.RelativePath, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, BackedUpOriginalFile>(StringComparer.OrdinalIgnoreCase);

            var existingCreatedFiles = existingManifest?.CreatedFiles
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var warnings = new List<string>();
            var (selectedProxy, conflictAvoided, conflictNote) = SelectProxyDllName(
                targetDir,
                config.ProxyDllName,
                existingManifest?.ProxyDllName,
                existingCreatedFiles);

            if (!string.IsNullOrWhiteSpace(conflictNote))
            {
                warnings.Add(conflictNote);
            }

            // If user switches proxy DLL name during re-injection, remove the old proxy DLL if we created it
            if (existingManifest is not null &&
                !string.Equals(existingManifest.ProxyDllName, selectedProxy, StringComparison.OrdinalIgnoreCase) &&
                existingCreatedFiles.Contains(existingManifest.ProxyDllName))
            {
                var oldProxyPath = Path.Combine(targetDir, existingManifest.ProxyDllName);
                TryDeleteManagedFile(oldProxyPath);
                existingCreatedFiles.Remove(existingManifest.ProxyDllName);
            }

            var backupDir = Path.Combine(targetDir, BackupFolderName);
            var originalsDir = Path.Combine(backupDir, "originals");
            Directory.CreateDirectory(originalsDir);

            var manifest = new InjectionManifest
            {
                GameId = game.Id,
                GameTitle = game.Title,
                TargetRenderDirectory = targetDir,
                ProxyDllName = selectedProxy,
                PresetId = presetId,
                OptiScalerVersion = ResolveSlotVersion(activeOptiScalerSlot),
                InjectedAtUtc = DateTimeOffset.UtcNow,
                AppliedConfig = config.Clone(),
                BackedUpFiles = existingManifest?.BackedUpFiles.ToList() ?? new List<BackedUpOriginalFile>()
            };

            var createdSet = new HashSet<string>(existingCreatedFiles, StringComparer.OrdinalIgnoreCase);
            var backedUpSet = new HashSet<string>(
                manifest.BackedUpFiles.Select(b => b.RelativePath),
                StringComparer.OrdinalIgnoreCase);

            void DeployFile(string sourcePath, string relativeDestPath, bool allowOverwriteBackup = true)
            {
                var destPath = Path.Combine(targetDir, relativeDestPath);
                var destParent = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrWhiteSpace(destParent))
                {
                    Directory.CreateDirectory(destParent);
                }

                if (File.Exists(destPath) && !createdSet.Contains(relativeDestPath) && !backedUpSet.Contains(relativeDestPath))
                {
                    if (!allowOverwriteBackup)
                    {
                        warnings.Add($"跳过已存在的第三方文件: {relativeDestPath}");
                        return;
                    }

                    var backupFileName = $"{Guid.NewGuid():N}_{Path.GetFileName(relativeDestPath)}";
                    var backupPath = Path.Combine(originalsDir, backupFileName);
                    File.Copy(destPath, backupPath, overwrite: true);
                    var sha = ComputeSha256(destPath);

                    manifest.BackedUpFiles.Add(new BackedUpOriginalFile
                    {
                        RelativePath = relativeDestPath,
                        BackupFileName = backupFileName,
                        Sha256 = sha
                    });
                    backedUpSet.Add(relativeDestPath);
                }

                File.Copy(sourcePath, destPath, overwrite: true);
                if (!backedUpSet.Contains(relativeDestPath))
                {
                    createdSet.Add(relativeDestPath);
                }
            }

            // 1. Deploy main proxy DLL
            DeployFile(coreDllPath, selectedProxy);

            // 2. Deploy companion files/subdirectories from OptiScaler archive (skip batch/shell/docs/Licenses/README/main dll/ini)
            foreach (var file in Directory.EnumerateFiles(optiRoot, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(optiRoot, file);
                var fileName = Path.GetFileName(file);
                var ext = Path.GetExtension(fileName).ToLowerInvariant();

                if (ext is ".bat" or ".sh" or ".md" or ".txt" or ".ps1")
                {
                    continue;
                }
                if (rel.StartsWith("Licenses" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith("Licenses/", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("!! README", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (string.Equals(file, coreDllPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (string.Equals(fileName, "OptiScaler.dll", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fileName, "nvngx.dll", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fileName, "OptiScaler.ini", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fileName, "fakenvapi.dll", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fileName, "fakenvapi.ini", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                DeployFile(file, rel, allowOverwriteBackup: true);
            }

            // 3. Deploy OptiPatcher if enabled (or remove managed copy if disabled on re-inject)
            var effectiveConfig = config.Clone();
            if (effectiveConfig.EnableOptiPatcher)
            {
                var asiPath = _components.ResolveOptiPatcherAsiPath();
                if (asiPath is not null)
                {
                    DeployFile(asiPath, Path.Combine("plugins", Path.GetFileName(asiPath)));
                    manifest.HasOptiPatcher = true;
                }
                else
                {
                    warnings.Add("未安装 OptiPatcher 组件，已跳过部署 OptiPatcher.asi。");
                }
            }
            else
            {
                var staleAsiRel = Path.Combine("plugins", "OptiPatcher.asi");
                if (createdSet.Remove(staleAsiRel))
                {
                    TryDeleteManagedFile(Path.Combine(targetDir, staleAsiRel));
                }
            }

            // 4. Deploy Fakenvapi if enabled (prefer standalone Fakenvapi slot, fallback to OptiScaler bundled copy)
            if (effectiveConfig.EnableFakenvapi)
            {
                var fakeRoot = _components.ResolveFakenvapiRoot();
                if (fakeRoot is not null)
                {
                    foreach (var file in Directory.EnumerateFiles(fakeRoot, "*", SearchOption.TopDirectoryOnly))
                    {
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext is ".dll" or ".ini")
                        {
                            DeployFile(file, Path.GetFileName(file));
                        }
                    }
                    manifest.HasFakenvapi = true;
                }
                else
                {
                    var bundledDll = Path.Combine(optiRoot, "fakenvapi.dll");
                    var bundledIni = Path.Combine(optiRoot, "fakenvapi.ini");
                    if (File.Exists(bundledDll))
                    {
                        DeployFile(bundledDll, "fakenvapi.dll");
                        if (File.Exists(bundledIni))
                        {
                            DeployFile(bundledIni, "fakenvapi.ini");
                        }
                        manifest.HasFakenvapi = true;
                    }
                    else
                    {
                        warnings.Add("未安装 Fakenvapi 组件，已跳过部署 nvapi 转换层。");
                    }
                }
            }
            else
            {
                foreach (var staleFakeRel in new[] { "fakenvapi.dll", "fakenvapi.ini" })
                {
                    if (createdSet.Remove(staleFakeRel))
                    {
                        TryDeleteManagedFile(Path.Combine(targetDir, staleFakeRel));
                    }
                }
            }

            // 5. Deploy enabled Custom Extensions (.asi -> plugins/, .dll/.ini -> root)
            if (effectiveConfig.EnableCustomDlls)
            {
                foreach (var custom in _components.GetCustomExtensions(disabledCustomDlls))
                {
                    if (!custom.Enabled)
                    {
                        continue;
                    }

                    var ext = Path.GetExtension(custom.FileName).ToLowerInvariant();
                    if (ext == ".asi")
                    {
                        DeployFile(custom.StoredPath, Path.Combine("plugins", custom.FileName));
                        effectiveConfig.EnableOptiPatcher = true; // Ensure LoadAsiPlugins=true
                    }
                    else if (ext is ".dll" or ".ini")
                    {
                        DeployFile(custom.StoredPath, custom.FileName);
                    }
                }
            }

            // 6. Generate / update OptiScaler.ini cleanly preserving template/existing comments
            var targetIniPath = Path.Combine(targetDir, "OptiScaler.ini");
            var templateIniPath = Path.Combine(optiRoot, "OptiScaler.ini");
            IniDocument iniDoc;
            if (File.Exists(targetIniPath))
            {
                if (!createdSet.Contains("OptiScaler.ini") && !backedUpSet.Contains("OptiScaler.ini"))
                {
                    var backupName = $"{Guid.NewGuid():N}_OptiScaler.ini";
                    File.Copy(targetIniPath, Path.Combine(originalsDir, backupName), true);
                    manifest.BackedUpFiles.Add(new BackedUpOriginalFile
                    {
                        RelativePath = "OptiScaler.ini",
                        BackupFileName = backupName,
                        Sha256 = ComputeSha256(targetIniPath)
                    });
                    backedUpSet.Add("OptiScaler.ini");
                }
                iniDoc = IniDocument.Load(targetIniPath);
            }
            else if (File.Exists(templateIniPath))
            {
                iniDoc = IniDocument.Load(templateIniPath);
                createdSet.Add("OptiScaler.ini");
            }
            else
            {
                iniDoc = new IniDocument();
                createdSet.Add("OptiScaler.ini");
            }

            PresetCatalog.ApplyToIni(iniDoc, effectiveConfig);
            iniDoc.Save(targetIniPath);

            manifest.CreatedFiles = createdSet.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            WriteManifest(targetDir, manifest);
            UpsertIndex(manifest);

            return new InjectionResult
            {
                Success = true,
                SelectedProxyDll = selectedProxy,
                ConflictAvoided = conflictAvoided,
                ConflictNote = conflictNote,
                Manifest = manifest,
                Warnings = warnings,
                Message = conflictAvoided
                    ? $"已自动避让冲突文件并通过 {selectedProxy} 成功注入 {game.Title}"
                    : $"已通过 {selectedProxy} 成功注入 {game.Title}"
            };
        }, ct);
    }

    public Task<InjectionResult> RestoreCleanAsync(GameEntry game, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var targetDir = !string.IsNullOrWhiteSpace(game.TargetRenderDirectory)
                ? game.TargetRenderDirectory
                : game.InstallDirectory;

            var manifest = ReadManifest(targetDir);
            if (manifest is null)
            {
                return Fail("未找到本工具的注入清单，目录可能未被注入或已被手动清理。");
            }

            var backupDir = Path.Combine(targetDir, BackupFolderName);
            var originalsDir = Path.Combine(backupDir, "originals");

            // 1. Delete created files
            foreach (var relPath in manifest.CreatedFiles)
            {
                var fullPath = Path.Combine(targetDir, relPath);
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                try
                {
                    File.Delete(fullPath);
                }
                catch (IOException ex)
                {
                    return Fail($"文件 {relPath} 正被游戏进程占用，请先关闭游戏再卸载还原。（{ex.Message}）");
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Fail($"无权删除 {relPath}：{ex.Message}");
                }
            }

            // 2. Restore backed up original files and verify SHA256
            foreach (var backup in manifest.BackedUpFiles)
            {
                var sourceBackup = Path.Combine(originalsDir, backup.BackupFileName);
                var destOriginal = Path.Combine(targetDir, backup.RelativePath);
                if (!File.Exists(sourceBackup))
                {
                    continue;
                }

                var parent = Path.GetDirectoryName(destOriginal);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                try
                {
                    File.Copy(sourceBackup, destOriginal, overwrite: true);
                }
                catch (IOException ex)
                {
                    return Fail($"原文件 {backup.RelativePath} 正被游戏占用，请先关闭游戏。（{ex.Message}）");
                }

                var restoredSha = ComputeSha256(destOriginal);
                if (!string.Equals(restoredSha, backup.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return Fail($"还原文件 {backup.RelativePath} SHA256 校验不一致，已中断以保护原文件。");
                }
            }

            // 3. Remove runtime OptiScaler.log if created
            var optiLog = Path.Combine(targetDir, "OptiScaler.log");
            if (File.Exists(optiLog))
            {
                try { File.Delete(optiLog); } catch { }
            }

            // 4. Clean empty plugins dir, D3D12_Optiscaler dir & backup dir
            foreach (var subName in new[] { "plugins", "D3D12_Optiscaler" })
            {
                var subDir = Path.Combine(targetDir, subName);
                if (Directory.Exists(subDir) && !Directory.EnumerateFileSystemEntries(subDir).Any())
                {
                    try { Directory.Delete(subDir, false); } catch { }
                }
            }

            if (Directory.Exists(backupDir))
            {
                try { Directory.Delete(backupDir, recursive: true); } catch { }
            }

            RemoveFromIndex(game.Id, targetDir);

            return new InjectionResult
            {
                Success = true,
                Message = $"已无损卸载并还原 {game.Title} 的原始游戏目录"
            };
        }, ct);
    }

    public InjectionManifest? ReadManifest(string targetDir)
    {
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            return null;
        }

        var path = Path.Combine(targetDir, BackupFolderName, ManifestFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<InjectionManifest>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<InjectionManifest> GetAllTrackedInjections()
    {
        lock (_indexLock)
        {
            var entries = LoadIndexInternal();
            var alive = new List<InjectionManifest>();
            foreach (var dir in entries.Values.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var manifest = ReadManifest(dir);
                if (manifest is not null)
                {
                    alive.Add(manifest);
                }
            }
            return alive;
        }
    }

    public static string? ReadOptiScalerLogSnippet(string targetDir, int maxLines = 25)
    {
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            return null;
        }

        var logPath = Path.Combine(targetDir, "OptiScaler.log");
        if (!File.Exists(logPath))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var lines = new Queue<string>(maxLines);
            while (reader.ReadLine() is { } line)
            {
                if (lines.Count == maxLines)
                {
                    lines.Dequeue();
                }
                lines.Enqueue(line);
            }
            return string.Join(Environment.NewLine, lines);
        }
        catch
        {
            return null;
        }
    }

    private (string SelectedProxy, bool ConflictAvoided, string? ConflictNote) SelectProxyDllName(
        string targetDir,
        string requestedProxy,
        string? existingManagedProxy,
        HashSet<string> existingCreatedFiles)
    {
        if (!string.IsNullOrWhiteSpace(requestedProxy) &&
            !string.Equals(requestedProxy, "auto", StringComparison.OrdinalIgnoreCase))
        {
            var candidatePath = Path.Combine(targetDir, requestedProxy);
            bool conflict = File.Exists(candidatePath) &&
                            !existingCreatedFiles.Contains(requestedProxy) &&
                            !string.Equals(existingManagedProxy, requestedProxy, StringComparison.OrdinalIgnoreCase) &&
                            !IsOptiScalerBinary(candidatePath);

            return (
                requestedProxy,
                conflict,
                conflict ? $"目标目录原有 {requestedProxy} 已自动备份至 .handheldopti_backup/originals。" : null
            );
        }

        var avoidedFiles = new List<string>();
        foreach (var candidate in AutoProxyPriority)
        {
            var fullPath = Path.Combine(targetDir, candidate);
            if (!File.Exists(fullPath) ||
                existingCreatedFiles.Contains(candidate) ||
                string.Equals(existingManagedProxy, candidate, StringComparison.OrdinalIgnoreCase) ||
                IsOptiScalerBinary(fullPath))
            {
                bool avoided = avoidedFiles.Count > 0;
                var note = avoided
                    ? $"检测到第三方 {string.Join(", ", avoidedFiles)}（如 ReShade / Special K），已自动避让并选用 {candidate}。"
                    : null;
                return (candidate, avoided, note);
            }

            avoidedFiles.Add(candidate);
        }

        return ("winmm.dll", true, "常见代理文件名均已存在，已回退至 winmm.dll 并自动备份原文件。");
    }

    private static bool IsOptiScalerBinary(string filePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(filePath);
            return (info.ProductName?.Contains("OptiScaler", StringComparison.OrdinalIgnoreCase) ?? false) ||
                   (info.FileDescription?.Contains("OptiScaler", StringComparison.OrdinalIgnoreCase) ?? false);
        }
        catch
        {
            return false;
        }
    }

    private static string? FindOptiScalerMainBinary(string optiRoot)
    {
        var directOpti = Path.Combine(optiRoot, "OptiScaler.dll");
        if (File.Exists(directOpti)) return directOpti;

        var directNvngx = Path.Combine(optiRoot, "nvngx.dll");
        if (File.Exists(directNvngx)) return directNvngx;

        return Directory.EnumerateFiles(optiRoot, "OptiScaler.dll", SearchOption.AllDirectories).FirstOrDefault()
            ?? Directory.EnumerateFiles(optiRoot, "nvngx.dll", SearchOption.AllDirectories).FirstOrDefault();
    }

    private string? ResolveSlotVersion(string activeSlotName)
    {
        if (Enum.TryParse<ComponentSlot>(activeSlotName, out var slot))
        {
            return _components.GetComponent(slot).InstalledVersion;
        }
        return _components.GetComponent(ComponentSlot.OptiScalerStable).InstalledVersion;
    }

    private static void WriteManifest(string targetDir, InjectionManifest manifest)
    {
        var backupDir = Path.Combine(targetDir, BackupFolderName);
        Directory.CreateDirectory(backupDir);
        File.WriteAllText(Path.Combine(backupDir, ManifestFileName), JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private Dictionary<string, string> LoadIndexInternal()
    {
        if (!File.Exists(_indexFilePath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_indexFilePath), JsonOptions)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void UpsertIndex(InjectionManifest manifest)
    {
        lock (_indexLock)
        {
            var idx = LoadIndexInternal();
            idx[manifest.GameId] = manifest.TargetRenderDirectory;
            File.WriteAllText(_indexFilePath, JsonSerializer.Serialize(idx, JsonOptions));
        }
    }

    private void RemoveFromIndex(string gameId, string targetDir)
    {
        lock (_indexLock)
        {
            var idx = LoadIndexInternal();
            idx.Remove(gameId);
            foreach (var key in idx.Where(kv => string.Equals(kv.Value, targetDir, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList())
            {
                idx.Remove(key);
            }
            File.WriteAllText(_indexFilePath, JsonSerializer.Serialize(idx, JsonOptions));
        }
    }

    private static void TryDeleteManagedFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore
        }
    }

    public static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    private static InjectionResult Fail(string message) => new()
    {
        Success = false,
        Message = message
    };
}
