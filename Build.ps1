[CmdletBinding()]
param([string]$Output, [string]$ToolsDirectory,
      [string]$RuntimeDirectory, [string]$Dotnet = 'dotnet', [switch]$Offline,
      [string]$DependenciesDirectory = (Join-Path $PSScriptRoot 'Dependencies'))
$ErrorActionPreference = 'Stop'
if (-not $Output) {
    $version = ([xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
    $Output = Join-Path $PSScriptRoot "dist\Prometheus-$version"
}
Push-Location -LiteralPath $PSScriptRoot
try {
    foreach ($component in @('Prometheus.Client','Prometheus.McpBridge','Prometheus.MgtLive')) {
        $project = Join-Path $PSScriptRoot "src\$component\$component.csproj"
        $target = if ($component -eq 'Prometheus.Client') { $Output } elseif ($component -eq 'Prometheus.McpBridge') {
            Join-Path $Output 'McpBridge'
        } else { Join-Path $Output 'MgtLive' }
        $restoreArguments = @('restore', $project, '-r', 'win-x64', '--locked-mode', '-p:Configuration=Release')
        if ($Offline) { $restoreArguments += @('--configfile', (Join-Path $DependenciesDirectory 'NuGet.Config')) }
        & $Dotnet @restoreArguments
        if ($LASTEXITCODE -ne 0) { throw "No se restauraron las dependencias de $component" }
        & $Dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -o $target '-p:DebugType=None' '-p:DebugSymbols=false'
        if ($LASTEXITCODE -ne 0) { throw "No se compilÃƒÂ³ $component" }
    }
    foreach ($name in @('LICENSE','README.md','CHANGELOG.md','licenses','docs')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $Output -Recurse -Force
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'tools\Configure-Mcp.ps1') -Destination $Output -Force
    if ($ToolsDirectory) {
        $multimedia = Join-Path $ToolsDirectory 'ffmpeg'
        if (-not (Test-Path -LiteralPath $multimedia -PathType Container)) { $multimedia = Join-Path $ToolsDirectory 'Runtime\ffmpeg' }
        if (Test-Path -LiteralPath $multimedia -PathType Container) {
            $destination = Join-Path $Output 'Runtime\ffmpeg'
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
            Copy-Item -Path (Join-Path $multimedia '*') -Destination $destination -Recurse -Force
        }
        foreach ($name in @('ffmpeg.exe','ffprobe.exe','yt-dlp.exe')) {
            if ($name -ne 'yt-dlp.exe' -and (Test-Path -LiteralPath $multimedia -PathType Container)) { continue }
            Copy-Item -LiteralPath (Join-Path $ToolsDirectory $name) -Destination $Output -Force
        }
    }
    if ($RuntimeDirectory) {
        robocopy $RuntimeDirectory (Join-Path $Output 'Runtime') /E /XD __pycache__ .git /XF *.pyc /NFL /NDL /NJH /NJS /NP
        if ($LASTEXITCODE -ge 8) { throw 'No se copiaron los componentes de IA' }
    }
    Copy-Item -Path (Join-Path $PSScriptRoot 'src\Prometheus.Client\Runtime\*.py') -Destination (Join-Path $Output 'Runtime') -Force
    Write-Host "CompilaciÃƒÂ³n preparada en $Output. Para redistribuir, comprueba docs/PUBLICACION.md y los paquetes de fuentes."
} finally { Pop-Location }
