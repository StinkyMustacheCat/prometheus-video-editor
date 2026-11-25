[CmdletBinding()]
param([string]$Dotnet = 'dotnet', [string]$Filter = '', [string]$RuntimeDirectory)
$ErrorActionPreference = 'Stop'
$previousRuntime = $env:PROMETHEUS_RUNTIME_ROOT
Push-Location -LiteralPath $PSScriptRoot
try {
    if ($RuntimeDirectory) { $env:PROMETHEUS_RUNTIME_ROOT = $RuntimeDirectory }
    $arguments = @('test','tests/Prometheus.UnitTests/Prometheus.UnitTests.csproj','-c','Release','--logger','trx','--results-directory','TestResults')
    if ($Filter) { $arguments += @('--filter',$Filter) }
    & $Dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Hay pruebas fallidas. Consulta TestResults.' }
} finally {
    $env:PROMETHEUS_RUNTIME_ROOT = $previousRuntime
    Pop-Location
}
