param(
    [string]$Endpoint = 'http://127.0.0.1:6012/mcp',
    [string]$Token = $env:RD6012_MCP_TOKEN,
    [switch]$ReadStatus
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Token)) {
    throw '请设置 RD6012_MCP_TOKEN 环境变量，或使用 -Token 参数传入上位机复制的令牌。'
}
$headers = @{
    Authorization = "Bearer $Token"
    Accept = 'application/json, text/event-stream'
}
function Invoke-McpRequest([int]$Id, [string]$Method, [object]$Parameters) {
    $request = @{ jsonrpc = '2.0'; id = $Id; method = $Method; params = $Parameters }
    $response = Invoke-WebRequest -Uri $Endpoint -Method Post -Headers $headers -ContentType 'application/json' `
        -Body (ConvertTo-Json -InputObject $request -Depth 12 -Compress)
    $body = [string]$response.Content
    if ($body.TrimStart().StartsWith('{')) { return $body | ConvertFrom-Json }
    foreach ($line in ($body -split "`n")) {
        if ($line.StartsWith('data:')) {
            $data = $line.Substring(5).Trim()
            if ($data.StartsWith('{')) {
                $parsed = $data | ConvertFrom-Json
                if ($parsed.id -eq $Id) { return $parsed }
            }
        }
    }
    throw '未收到预期的 MCP JSON-RPC 响应。'
}
$initialize = Invoke-McpRequest 1 'initialize' @{
    protocolVersion = '2025-11-25'
    capabilities = @{}
    clientInfo = @{ name = 'rd6012-readonly-smoke'; version = '1.0.0' }
}
if ($initialize.error) { throw ($initialize.error | ConvertTo-Json -Compress) }
$headers['MCP-Protocol-Version'] = $initialize.result.protocolVersion
$notification = '{"jsonrpc":"2.0","method":"notifications/initialized"}'
$null = Invoke-WebRequest -Uri $Endpoint -Method Post -Headers $headers -ContentType 'application/json' -Body $notification
$tools = Invoke-McpRequest 2 'tools/list' @{}
if ($tools.error) { throw ($tools.error | ConvertTo-Json -Compress) }
Write-Output "MCP 握手成功；协议版本：$($initialize.result.protocolVersion)"
$tools.result.tools | Select-Object name, description
$capabilities = Invoke-McpRequest 3 'tools/call' @{ name = 'power_supply_get_capabilities'; arguments = @{} }
if ($capabilities.error -or $capabilities.result.isError) { throw ($capabilities | ConvertTo-Json -Depth 15) }
$capabilities.result.structuredContent | ConvertTo-Json -Depth 10
if ($ReadStatus) {
    $status = Invoke-McpRequest 4 'tools/call' @{ name = 'power_supply_get_status'; arguments = @{} }
    $status.result.structuredContent | ConvertTo-Json -Depth 10
    if ($status.error -or $status.result.isError) { throw '状态读取失败，请查看上方响应并检查上位机连接。' }
}
Write-Output '只读检查完成，没有发送任何设置参数或开关输出指令。'
