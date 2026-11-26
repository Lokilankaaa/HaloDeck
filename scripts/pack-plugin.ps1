<#
.SYNOPSIS
    HaloDeck 社区插件本地合规校验与打包工具（与官方 Decky CI 审核规则一致）

.DESCRIPTION
    1. 校验 plugin.json 清单字段、权限白名单、入口与后端路径；
    2. 执行与 GitHub Actions (verify-plugin-store.yml) 一致的禁黑盒二进制检查与高危脚本静态扫描；
    3. 可选编译插件内的 C# (.csproj) 独立后端；
    4. 生成标准 .zip 插件包，输出 SHA-256 校验和及官方商店 PR 提交清单模板。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\pack-plugin.ps1 -PluginDir .\examples\plugins\halodeck.skin-studio
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PluginDir,

    [string]$OutDir = ".\dist\plugins",

    [switch]$BuildCSharpBackend,

    [switch]$AllowBuiltBackend,

    [switch]$AuditOnly
)

$ErrorActionPreference = 'Stop'

$resolvedDir = (Resolve-Path $PluginDir).Path
$manifestPath = Join-Path $resolvedDir "plugin.json"
if (-not (Test-Path $manifestPath)) {
    throw "未找到 plugin.json：$manifestPath"
}

$manifest = Get-Content $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json

# 1. Manifest schema validation
if ($manifest.schemaVersion -ne 1) {
    throw "不支持的 schemaVersion=$($manifest.schemaVersion)（必须为 1）"
}
if (-not ($manifest.id -match '^[a-z0-9][a-z0-9._-]{2,63}$') -or $manifest.id.Contains('..')) {
    throw "插件 id '$($manifest.id)' 不合法（需 3–64 位小写字母/数字/._-，不含 ..）"
}
if ([string]::IsNullOrWhiteSpace($manifest.name)) {
    throw "插件 name 不能为空"
}
$version = if ([string]::IsNullOrWhiteSpace($manifest.version)) { "1.0.0" } else { $manifest.version.Trim() }

$allowedPerms = @(
    'games:read',
    'telemetry:read',
    'events:game',
    'storage',
    'shell:openFolder',
    'shell:openUrl',
    'ui:theme',
    'backend',
    'input:sendKey'
)
$perms = @($manifest.permissions)
foreach ($perm in $perms) {
    if ($allowedPerms -notcontains $perm) {
        throw "包含未知或未授权权限：'$perm'（允许值：$($allowedPerms -join ', ')）"
    }
}

$entryRel = if ($manifest.entry) { $manifest.entry } else { "ui/index.html" }
$entryFull = Join-Path $resolvedDir ($entryRel -replace '/', '\')
if (-not (Test-Path $entryFull)) {
    throw "未找到前端入口文件：$entryRel"
}

$hasBackend = ($null -ne $manifest.backend) -and (-not [string]::IsNullOrWhiteSpace($manifest.backend.executable))
if ($hasBackend -and ($perms -notcontains 'backend')) {
    throw "配置了 backend.executable 时必须在 permissions 中声明 'backend'"
}

# 2. Decky zero-binary rule check (reject pre-compiled opaque binaries in source tree)
$forbiddenExts = @('.exe', '.dll', '.sys', '.ocx', '.scr', '.com', '.msi', '.pyd', '.so', '.dylib')
if (-not $AllowBuiltBackend) {
    $binaries = Get-ChildItem -Path $resolvedDir -Recurse -File | Where-Object {
        $forbiddenExts -contains $_.Extension.ToLowerInvariant()
    }
    if ($binaries) {
        $list = ($binaries | ForEach-Object { $_.FullName.Substring($resolvedDir.Length).TrimStart('\') }) -join ', '
        throw "检测到未开源预编译二进制文件（Decky 规范禁止提交黑盒二进制）：$list`n若为本地调试已编译后端，请追加 -AllowBuiltBackend 参数。"
    }
}

# 3. Static heuristic security scan (same rules as verify-plugin-store.yml)
$suspiciousPatterns = @(
    '(?i)-enc(odedcommand)?\b',
    '(?i)\bInvoke-Expression\b',
    '(?i)\bIEX\s*\(',
    '(?i)DownloadString\s*\(',
    '(?i)FromBase64String\s*\(',
    '(?i)\bschtasks(\.exe)?\b',
    '(?i)HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run',
    '(?i)HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Run'
)

$scanExts = @('.js', '.ts', '.html', '.ps1', '.cmd', '.bat', '.cs', '.py')
$textFiles = Get-ChildItem -Path $resolvedDir -Recurse -File | Where-Object {
    $scanExts -contains $_.Extension.ToLowerInvariant()
}
foreach ($tf in $textFiles) {
    $content = Get-Content $tf.FullName -Raw -ErrorAction SilentlyContinue
    if (-not $content) { continue }
    foreach ($pat in $suspiciousPatterns) {
        if ($content -match $pat) {
            $rel = $tf.FullName.Substring($resolvedDir.Length).TrimStart('\')
            throw "静态安全扫描触发高危规则 '$pat'，位置：$rel"
        }
    }
}

Write-Host "[OK] 清单校验、零二进制检查与静态安全扫描全部通过：$($manifest.id) v$version" -ForegroundColor Green

if ($AuditOnly) {
    exit 0
}

# 4. Stage & optionally compile C# backend
$stageRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("HaloDeck_Pack_" + [Guid]::NewGuid().ToString("N"))
$stagePlugin = Join-Path $stageRoot $manifest.id
New-Item -ItemType Directory -Force -Path $stagePlugin | Out-Null

try {
    Get-ChildItem -Path $resolvedDir -Force | Where-Object {
        $_.Name -notin @('.git', 'bin', 'obj', 'node_modules')
    } | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination (Join-Path $stagePlugin $_.Name) -Recurse -Force
    }

    if ($BuildCSharpBackend) {
        $csproj = Get-ChildItem -Path $stagePlugin -Filter "*.csproj" -Recurse | Select-Object -First 1
        if (-not $csproj) {
            throw "指定了 -BuildCSharpBackend，但插件目录中未找到 .csproj 文件"
        }
        Write-Host "[BUILD] 正在编译 C# 后端：$($csproj.Name)" -ForegroundColor Cyan
        $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
        $dotnetExe = if ($dotnetCmd) { $dotnetCmd.Source } else { Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe" }
        & $dotnetExe publish $csproj.FullName -c Release -r win-x64 --self-contained false -o (Join-Path $stagePlugin "backend")
        if ($LASTEXITCODE -ne 0) {
            throw "C# 后端编译失败（退出码 $LASTEXITCODE）"
        }
    }

    if ($hasBackend) {
        $backendFull = Join-Path $stagePlugin ($manifest.backend.executable -replace '/', '\')
        if (-not (Test-Path $backendFull)) {
            throw "打包目录中缺少声明的后端入口：$($manifest.backend.executable)"
        }
    }

    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    $zipPath = Join-Path (Resolve-Path $OutDir).Path "$($manifest.id)-$version.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path $stagePlugin -DestinationPath $zipPath -Force

    $sha256 = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $sizeKb = [Math]::Round((Get-Item $zipPath).Length / 1KB, 1)

    Write-Host ""
    Write-Host "================ 打包完成 ================" -ForegroundColor Cyan
    Write-Host "插件 ID   : $($manifest.id)"
    Write-Host "版本      : v$version"
    Write-Host "输出文件  : $zipPath ($sizeKb KB)"
    Write-Host "SHA-256   : $sha256"
    Write-Host "权限声明  : $(if ($perms.Count -gt 0) { $perms -join ', ' } else { '无（纯展示）' })"
    Write-Host "==========================================" -ForegroundColor Cyan
}
finally {
    if (Test-Path $stageRoot) {
        Remove-Item $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
