[CmdletBinding()]
param(
    [switch]$LaunchAfterInstall = $false
)

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir
$AppCsproj = Join-Path $RepoRoot "src\HandheldOpti.App\HandheldOpti.App.csproj"
$InstallDir = Join-Path $env:LOCALAPPDATA "Programs\HandheldOpti"
$AppExe = Join-Path $InstallDir "HandheldOpti.exe"
$UninstallScript = Join-Path $InstallDir "uninstall.ps1"

function Resolve-DotnetPath {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidates = @(
        (Join-Path $env:ProgramFiles "dotnet\dotnet.exe"),
        (Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe")
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }
    throw "未找到 dotnet SDK/CLI，请先安装 .NET 8 SDK。"
}

Write-Host "[1/5] 停止正在运行的 HandheldOpti 实例（如有）..."
Get-Process -Name "HandheldOpti" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

$dotnet = Resolve-DotnetPath
Write-Host "[2/5] 发布自包含 Win-x64 应用至 $InstallDir ..."
if (Test-Path $InstallDir) {
    Remove-Item -Path $InstallDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

& $dotnet publish $AppCsproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $InstallDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败（退出码 $LASTEXITCODE）。"
}
if (-not (Test-Path $AppExe)) {
    throw "发布完成但未找到目标可执行文件: $AppExe"
}

Write-Host "[3/5] 写入卸载脚本与创建桌面/开始菜单快捷方式..."
$uninstallContent = @'
$ErrorActionPreference = 'SilentlyContinue'
Get-Process -Name "HandheldOpti" | Stop-Process -Force

$desktopLnk = Join-Path ([Environment]::GetFolderPath('Desktop')) "HandheldOpti.lnk"
$programsLnk = Join-Path ([Environment]::GetFolderPath('Programs')) "HandheldOpti.lnk"
Remove-Item -Path $desktopLnk -Force
Remove-Item -Path $programsLnk -Force

Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "HandheldOpti"
Remove-Item -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\HandheldOpti" -Recurse -Force

$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
Start-Process -FilePath "cmd.exe" -ArgumentList "/c ping 127.0.0.1 -n 2 > nul & rmdir /s /q `"$dir`"" -WindowStyle Hidden
'@
Set-Content -Path $UninstallScript -Value $uninstallContent -Encoding UTF8

$shell = New-Object -ComObject WScript.Shell
$shortcutTargets = @(
    (Join-Path ([Environment]::GetFolderPath('Desktop')) "HandheldOpti.lnk"),
    (Join-Path ([Environment]::GetFolderPath('Programs')) "HandheldOpti.lnk")
)
foreach ($lnkPath in $shortcutTargets) {
    $shortcut = $shell.CreateShortcut($lnkPath)
    $shortcut.TargetPath = $AppExe
    $shortcut.WorkingDirectory = $InstallDir
    $shortcut.IconLocation = "$AppExe,0"
    $shortcut.Description = "HandheldOpti · GPD Win 5 自动 OptiScaler 控制台"
    $shortcut.Save()
}

Write-Host "[4/5] 注册当前用户已安装应用信息..."
$uninstallRegPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\HandheldOpti"
New-Item -Path $uninstallRegPath -Force | Out-Null
Set-ItemProperty -Path $uninstallRegPath -Name "DisplayName" -Value "HandheldOpti"
Set-ItemProperty -Path $uninstallRegPath -Name "DisplayVersion" -Value "0.1.1"
Set-ItemProperty -Path $uninstallRegPath -Name "Publisher" -Value "HandheldOpti"
Set-ItemProperty -Path $uninstallRegPath -Name "DisplayIcon" -Value $AppExe
Set-ItemProperty -Path $uninstallRegPath -Name "InstallLocation" -Value $InstallDir
Set-ItemProperty -Path $uninstallRegPath -Name "UninstallString" -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$UninstallScript`""
Set-ItemProperty -Path $uninstallRegPath -Name "NoModify" -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallRegPath -Name "NoRepair" -Value 1 -Type DWord

Write-Host "[5/5] 安装完成！"
Write-Host "  安装路径: $AppExe"
Write-Host "  桌面快捷方式: $($shortcutTargets[0])"
Write-Host "  开始菜单快捷方式: $($shortcutTargets[1])"

if ($LaunchAfterInstall) {
    Start-Process -FilePath $AppExe -WorkingDirectory $InstallDir | Out-Null
}
