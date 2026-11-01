param(
    [string]$OutDir = "$PSScriptRoot\bin",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$out = [System.IO.Path]::GetFullPath($OutDir)
$lib = Join-Path $repoRoot "lib"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "       PROMETHEUS 1.0.0 - PROCESO DE COMPILACION           " -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Directorio de salida: $out"
Write-Host "Configuracion:        $Configuration (x64)"
Write-Host ""

New-Item -ItemType Directory -Force -Path $out | Out-Null

# Copiar dependencias de runtime y enlaces WebView2
Write-Host "Copiando librerias nativas y ensamblados de lib/..." -ForegroundColor Yellow
Copy-Item (Join-Path $lib "*.dll") -Destination $out -Force
if (Test-Path "$repoRoot\src\Prometheus\Prometheus.exe.config") {
    Copy-Item "$repoRoot\src\Prometheus\Prometheus.exe.config" -Destination "$out\Prometheus.exe.config" -Force
}

# Intentar compilar mediante MSBuild (Estandar Visual Studio)
$msbuildPaths = @(
    (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\msbuild.exe"),
    "msbuild.exe"
)
$msbuildExe = $msbuildPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($msbuildExe) {
    Write-Host "Compilando solucion completa con MSBuild ($msbuildExe)..." -ForegroundColor Green
    & $msbuildExe "$repoRoot\Prometheus.sln" /p:Configuration=$Configuration /p:Platform=x64 "/p:OutputPath=$out\" /v:minimal /nologo
    if ($LASTEXITCODE -eq 0) {
        Write-Host ""
        Write-Host ">>> COMPILACION EXITOSA DE PROMETHEUS 1.0.0 (MSBuild) <<<" -ForegroundColor Green
        exit 0
    }
}

# Modo de reserva: Compilacion directa rapida con csc.exe
Write-Host "Compilando proyectos mediante csc.exe..." -ForegroundColor Yellow
$cscExe = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

Write-Host "1/4 Compilando Prometheus.Core.dll..."
$coreFiles = Get-ChildItem "$repoRoot\src\Prometheus.Core\*.cs" | ForEach-Object { $_.FullName }
& $cscExe /nologo /target:library /platform:x64 /optimize+ /reference:System.Web.Extensions.dll "/out:$out\Prometheus.Core.dll" $coreFiles
if ($LASTEXITCODE -ne 0) { throw "Error al compilar Prometheus.Core" }

Write-Host "2/4 Compilando Prometheus.Media.dll..."
$mediaFiles = Get-ChildItem "$repoRoot\src\Prometheus.Media\*.cs" | ForEach-Object { $_.FullName }
& $cscExe /nologo /target:library /platform:x64 /optimize+ /reference:System.Web.Extensions.dll /reference:System.Drawing.dll "/reference:$out\Prometheus.Core.dll" "/out:$out\Prometheus.Media.dll" $mediaFiles
if ($LASTEXITCODE -ne 0) { throw "Error al compilar Prometheus.Media" }

Write-Host "3/4 Compilando Prometheus.Rendering.dll..."
$renderingFiles = Get-ChildItem "$repoRoot\src\Prometheus.Rendering\*.cs" | ForEach-Object { $_.FullName }
& $cscExe /nologo /target:library /platform:x64 /optimize+ /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/reference:$out\Prometheus.Core.dll" "/reference:$out\Prometheus.Media.dll" "/out:$out\Prometheus.Rendering.dll" $renderingFiles
if ($LASTEXITCODE -ne 0) { throw "Error al compilar Prometheus.Rendering" }

Write-Host "4/4 Compilando Prometheus.exe..."
$appFiles = Get-ChildItem "$repoRoot\src\Prometheus\*.cs" | ForEach-Object { $_.FullName }
$icon = "$repoRoot\assets\Prometheus.ico"
$iconArg = if (Test-Path $icon) { "/win32icon:$icon" } else { "" }
& $cscExe /nologo /target:winexe /platform:x64 /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/reference:$out\Microsoft.Web.WebView2.Core.dll" "/reference:$out\Microsoft.Web.WebView2.WinForms.dll" "/reference:$out\Prometheus.Core.dll" "/reference:$out\Prometheus.Media.dll" "/reference:$out\Prometheus.Rendering.dll" $iconArg "/out:$out\Prometheus.exe" $appFiles
if ($LASTEXITCODE -ne 0) { throw "Error al compilar Prometheus.exe" }

Write-Host ""
Write-Host ">>> COMPILACION EXITOSA DE PROMETHEUS 1.0.0 (csc.exe) <<<" -ForegroundColor Green
