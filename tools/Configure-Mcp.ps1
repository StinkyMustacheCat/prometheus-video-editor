[CmdletBinding()]
param([string]$PortableDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$bridge = Join-Path ([IO.Path]::GetFullPath($PortableDirectory)) 'McpBridge\Prometheus.McpBridge.exe'
if (!(Test-Path -LiteralPath $bridge)) { throw 'Selecciona la carpeta del portable que contiene McpBridge.' }
@{mcpServers=@{prometheus=@{command=$bridge}}} | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath (Join-Path $PortableDirectory 'mcp_config.antigravity.json') -Encoding utf8
if ($bridge.Contains("'")) { throw 'La ruta contiene una comilla simple. Usa el JSON generado o una ruta sin comillas.' }
$toml = "[mcp_servers.prometheus]`ncommand = '" + $bridge + "'`n"
[IO.File]::WriteAllText((Join-Path $PortableDirectory 'mcp_config.codex.toml'), $toml)
Write-Host 'Configuraciones generadas. Cópialas al cliente MCP elegido. Prometheus debe estar abierto con MCP activado.'
