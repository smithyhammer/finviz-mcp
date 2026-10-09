# Smoke-tests the Finviz MCP server over stdio. Requires FINVIZ_API_KEY in the environment.
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\bin\Debug\net10.0\win-x64\FinvizMcpServer.exe'),
    [int]$TimeoutSec = 45
)

$psi = [System.Diagnostics.ProcessStartInfo]::new((Resolve-Path $Exe))
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$p = [System.Diagnostics.Process]::Start($psi)
$stderrTask = $p.StandardError.ReadToEndAsync()

function Send($obj) {
    $p.StandardInput.WriteLine(($obj | ConvertTo-Json -Depth 10 -Compress))
    $p.StandardInput.Flush()
}

function Receive($id) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSec)
    while ([DateTime]::UtcNow -lt $deadline) {
        $task = $p.StandardOutput.ReadLineAsync()
        if (-not $task.Wait(($deadline - [DateTime]::UtcNow))) { break }
        $msg = $task.Result | ConvertFrom-Json
        if ($msg.id -eq $id) { return $msg }
    }
    throw "Timed out waiting for response $id"
}

$script:nextId = 1
function Call($method, $params) {
    $id = $script:nextId++
    Send @{ jsonrpc = '2.0'; id = $id; method = $method; params = $params }
    Receive $id
}

function Show($label, $resp, [int]$maxLines = 6) {
    Write-Host "=== $label ===" -ForegroundColor Cyan
    if ($resp.error) { Write-Host "JSON-RPC ERROR: $($resp.error.message)" -ForegroundColor Red; return }
    $text = ($resp.result.content | ForEach-Object { $_.text }) -join "`n"
    $status = if ($resp.result.isError) { 'isError=true' } else { 'ok' }
    Write-Host "[$status]"
    ($text -split "`n" | Select-Object -First $maxLines) -join "`n" | Write-Host
}

try {
    $init = Call 'initialize' @{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'smoke'; version = '1' } }
    Write-Host "Server: $($init.result.serverInfo.name) $($init.result.serverInfo.version)"
    Send @{ jsonrpc = '2.0'; method = 'notifications/initialized' }

    $tools = Call 'tools/list' @{}
    Write-Host "Tools: $(($tools.result.tools.name) -join ', ')"

    function Tool($name, $args_) { Call 'tools/call' @{ name = $name; arguments = $args_ } }

    Show 'screen_stocks (large cap, div>3%)' (Tool 'finviz_screen_stocks' @{ filters = 'cap_largeover,fa_div_o3'; order = '-marketcap'; limit = 5 })
    Show 'get_price_history AAPL d' (Tool 'finviz_get_price_history' @{ ticker = 'AAPL'; timeframe = 'd'; limit = 5 })
    Show 'get_news NVDA' (Tool 'finviz_get_news' @{ tickers = 'NVDA'; limit = 5 })
    Show 'get_group_performance sector (default view)' (Tool 'finviz_get_group_performance' @{ group = 'sector'; limit = 5 })
    Show 'get_group_performance industry overview' (Tool 'finviz_get_group_performance' @{ group = 'industry'; view = '110'; limit = 3 })
    Show 'group invalid view 130 (expect error)' (Tool 'finviz_get_group_performance' @{ group = 'sector'; view = '130' })
    Show 'premarket_movers losers (default)' (Tool 'finviz_get_premarket_movers' @{}) 11
    Show 'premarket_movers gainers, $10+/500K vol/US' (Tool 'finviz_get_premarket_movers' @{ direction = 'gainers'; limit = 5; minPrice = 10; minAvgVolumeK = 500; extraFilters = 'geo_usa' })
    Show 'premarket_movers invalid direction (expect error)' (Tool 'finviz_get_premarket_movers' @{ direction = 'sideways' })
    Show 'premarket_movers invalid minPrice (expect error)' (Tool 'finviz_get_premarket_movers' @{ minPrice = 6 })
    Show 'premarket_movers invalid minAvgVolumeK (expect error)' (Tool 'finviz_get_premarket_movers' @{ minAvgVolumeK = 123 })
    Show 'invalid ticker (expect error)' (Tool 'finviz_get_price_history' @{ ticker = 'AAPL;evil' })
    Show 'invalid timeframe (expect error)' (Tool 'finviz_get_price_history' @{ ticker = 'AAPL'; timeframe = 'x' })
    Show 'invalid filter chars (expect error)' (Tool 'finviz_screen_stocks' @{ filters = 'cap_large&auth=x' })
}
finally {
    if (-not $p.HasExited) { $p.Kill() }
    $err = $stderrTask.Result
    if ($err -match 'auth=') { Write-Host 'WARNING: stderr contains "auth=" (possible token leak)' -ForegroundColor Red }
    else { Write-Host 'stderr check: no "auth=" found in logs' -ForegroundColor Green }
}
