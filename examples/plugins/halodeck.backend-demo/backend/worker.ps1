# Minimal HaloDeck stdio JSON-RPC 2.0 plugin backend (PowerShell)
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

[Console]::Error.WriteLine("backend-demo worker started for plugin: $env:HALODECK_PLUGIN_ID")

while ($null -ne ($line = [Console]::In.ReadLine())) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    try {
        $req = $line | ConvertFrom-Json
        $id = $req.id
        $method = $req.method

        if ($method -eq "ping") {
            $evt = @{
                jsonrpc = "2.0"
                method = "event"
                params = @{ name = "progress"; data = @{ percent = 50; step = "正在处理后端任务..." } }
            } | ConvertTo-Json -Compress -Depth 6
            [Console]::Out.WriteLine($evt)
            [Console]::Out.Flush()

            $res = @{
                jsonrpc = "2.0"
                id = $id
                result = @{
                    ok = $true
                    echo = $req.params.message
                    pid = $PID
                    dataDir = $env:HALODECK_PLUGIN_DATA_DIR
                    serverTime = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
                }
            } | ConvertTo-Json -Compress -Depth 6
            [Console]::Out.WriteLine($res)
            [Console]::Out.Flush()
        }
        elseif ($null -ne $id) {
            $err = @{
                jsonrpc = "2.0"
                id = $id
                error = @{ code = -32601; message = "Unknown method: $method" }
            } | ConvertTo-Json -Compress -Depth 4
            [Console]::Out.WriteLine($err)
            [Console]::Out.Flush()
        }
    }
    catch {
        [Console]::Error.WriteLine("worker error: $($_.Exception.Message)")
    }
}
