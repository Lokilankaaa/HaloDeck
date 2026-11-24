param()
[Console]::InputEncoding  = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$dataDir = $env:HALODECK_PLUGIN_DATA_DIR
if (-not $dataDir) { $dataDir = Join-Path $env:TEMP "halodeck_diag_fallback" }
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

function Send-Json([hashtable]$obj) {
    $line = ($obj | ConvertTo-Json -Compress -Depth 8)
    [Console]::Out.WriteLine($line)
    [Console]::Out.Flush()
}

[Console]::Error.WriteLine("[diag-worker] started pid=$PID dataDir=$dataDir")

while ($null -ne ($line = [Console]::In.ReadLine())) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    try {
        $req = $line | ConvertFrom-Json
        $id = $req.id
        $method = [string]$req.method
        $p = $req.params

        switch ($method) {
            "ping" {
                Send-Json @{
                    jsonrpc = "2.0"
                    id = $id
                    result = @{ pong = $true; workerPid = $PID; os = [Environment]::OSVersion.VersionString }
                }
            }
            "exportDiagnosticReport" {
                Send-Json @{ jsonrpc = "2.0"; method = "progress"; params = @{ percent = 25; text = "正在收集系统与硬件信息..." } }
                $os = Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue
                $cpu = Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue | Select-Object -First 1
                $gpu = Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue | Select-Object -First 2

                Send-Json @{ jsonrpc = "2.0"; method = "progress"; params = @{ percent = 70; text = "正在写入诊断报告文件..." } }
                $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
                $fileName = "HaloDeck_Diag_$stamp.txt"
                $outPath = Join-Path $dataDir $fileName

                $lines = New-Object System.Collections.Generic.List[string]
                $lines.Add("===== HaloDeck 掌机硬件与运行诊断报告 =====")
                $lines.Add("生成时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
                $lines.Add("系统版本: $($os.Caption) ($($os.Version) Build $($os.BuildNumber))")
                $lines.Add("处理器: $($cpu.Name)")
                foreach ($g in $gpu) {
                    $lines.Add("显卡: $($g.Name) · 驱动 $($g.DriverVersion)")
                }
                if ($p -and $p.telemetry) {
                    $t = $p.telemetry
                    $lines.Add("")
                    $lines.Add("--- 实时遥测快照 ---")
                    $lines.Add("APU 功耗: $($t.apuPowerWatts) W · CPU 温度: $($t.cpuTempC) C · GPU 温度: $($t.gpuTempC) C")
                    $lines.Add("电池电量: $($t.batteryPercent)% · 外接供电: $($t.isAcOnline)")
                    $lines.Add("显示模式: $($t.screenWidth)x$($t.screenHeight) @ $($t.refreshRateHz) Hz")
                }
                if ($p -and $p.gameCount -ne $null) {
                    $lines.Add("")
                    $lines.Add("--- 游戏库摘要 ---")
                    $lines.Add("已扫描游戏数量: $($p.gameCount)")
                }
                [System.IO.File]::WriteAllLines($outPath, $lines, [System.Text.UTF8Encoding]::new($false))

                Send-Json @{ jsonrpc = "2.0"; method = "progress"; params = @{ percent = 100; text = "报告导出完成" } }
                Send-Json @{
                    jsonrpc = "2.0"
                    id = $id
                    result = @{ ok = $true; fileName = $fileName; path = $outPath }
                }
            }
            default {
                Send-Json @{ jsonrpc = "2.0"; id = $id; error = @{ code = -32601; message = "Method not found: $method" } }
            }
        }
    } catch {
        [Console]::Error.WriteLine("[diag-worker] error: $($_.Exception.Message)")
        Send-Json @{ jsonrpc = "2.0"; id = $req.id; error = @{ code = -32000; message = $_.Exception.Message } }
    }
}
