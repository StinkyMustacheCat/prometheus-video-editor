param(
    [string]$RuntimeRoot = $env:PROMETHEUS_TEST_RUNTIME
)

$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$root = $PSScriptRoot
$bin = Join-Path $root 'bin'
$tests = Join-Path $root 'tests'

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "       PROMETHEUS 1.0.0 - SUITE DE PRUEBAS AUTOMATIZADA     " -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$testSuites = @(
    @{ Name = "Regresion 0.10.58 (Pre-V1 Core)"; File = Join-Path $tests 'PreV1Regression01058.cs' },
    @{ Name = "Regresion 0.10.59 (Transiciones y Audio)"; File = Join-Path $tests 'Regression01059.cs' },
    @{ Name = "Regresion 0.10.62 (Timeline y Anidamiento)"; File = Join-Path $tests 'Regression01062.cs' },
    @{ Name = "Hito 0.10.63 (Desbloqueo de Exportacion y Diagnostico)"; File = Join-Path $tests 'TestExportErrorReporting.cs' },
    @{ Name = "Hito 0.10.64 (Perfiles de Preparacion HDR / Entrelazado)"; File = Join-Path $tests 'TestPrepProfiles.cs' },
    @{ Name = "Hito 0.10.66 (Modos de Fusion Diferencia y Multiplicar)"; File = Join-Path $tests 'TestCompositionVisual.cs' },
    @{ Name = "Hito 0.10.66 (Eliminacion de Fotograma Negro)"; File = Join-Path $tests 'TestBlackFrame.cs' },
    @{ Name = "Pipeline de Exportacion Acelerado (GPU NVENC / CPU libx264)"; File = Join-Path $tests 'TestGpuAndCpuExport.cs' }
)

$missingSuites = @($testSuites | Where-Object { -not (Test-Path $_.File) })
if ($missingSuites.Count -gt 0) {
    $missingPaths = $missingSuites | ForEach-Object { $_.File }
    Write-Error "Faltan suites de prueba obligatorias:`n$($missingPaths -join "`n")"
    exit 1
}

if ($testSuites.Count -ne 8) {
    Write-Error "Se esperaban exactamente 8 suites de prueba; se encontraron $($testSuites.Count)."
    exit 1
}

if ([string]::IsNullOrWhiteSpace($RuntimeRoot)) {
    $RuntimeRoot = $root
}

try {
    $RuntimeRoot = [System.IO.Path]::GetFullPath($RuntimeRoot)
} catch {
    Write-Error "La ruta del runtime de pruebas no es válida: $RuntimeRoot"
    exit 1
}

$ffmpeg = Join-Path $RuntimeRoot 'runtime\ffmpeg.exe'
$ffprobe = Join-Path $RuntimeRoot 'runtime\ffprobe.exe'
$missingRuntime = @()
if (-not (Test-Path $ffmpeg -PathType Leaf)) { $missingRuntime += $ffmpeg }
if (-not (Test-Path $ffprobe -PathType Leaf)) { $missingRuntime += $ffprobe }
if ($missingRuntime.Count -gt 0) {
    Write-Error "No se puede ejecutar la suite completa. Faltan FFmpeg/FFprobe en el runtime '$RuntimeRoot':`n$($missingRuntime -join "`n")`nColoca ambos ejecutables en runtime\ o indica el paquete con -RuntimeRoot / PROMETHEUS_TEST_RUNTIME."
    exit 1
}

if (-not (Test-Path $taskCompiler -PathType Leaf)) {
    Write-Error "No se encontró el compilador de .NET Framework: $taskCompiler"
    exit 1
}

$env:PROMETHEUS_TEST_RUNTIME = $RuntimeRoot

# Paso 1: Asegurar compilacion de librerias base
if (-not (Test-Path (Join-Path $bin 'Prometheus.Core.dll')) -or
    -not (Test-Path (Join-Path $bin 'Prometheus.Media.dll')) -or
    -not (Test-Path (Join-Path $bin 'Prometheus.Rendering.dll'))) {
    Write-Host "Compilando dependencias base con Build.ps1..." -ForegroundColor Yellow
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'Build.ps1') -OutDir $bin
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build.ps1 terminó con código $LASTEXITCODE."
        exit 1
    }
}

$requiredLibraries = @('Prometheus.Core.dll', 'Prometheus.Media.dll', 'Prometheus.Rendering.dll')
$missingLibraries = @($requiredLibraries | Where-Object { -not (Test-Path (Join-Path $bin $_) -PathType Leaf) })
if ($missingLibraries.Count -gt 0) {
    Write-Error "No se generaron las bibliotecas requeridas: $($missingLibraries -join ', ')"
    exit 1
}

$passed = 0
$failed = 0
$results = @()

foreach ($t in $testSuites) {
    $testBaseName = [IO.Path]::GetFileNameWithoutExtension($t.File)
    $testExe = Join-Path $bin "$testBaseName.exe"
    if (Test-Path $testExe) { Remove-Item -Force $testExe }
    Write-Host "Compilando: $($t.Name)..." -NoNewline

    & $taskCompiler /nologo /optimize+ /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/reference:$(Join-Path $bin 'Prometheus.Core.dll')" "/reference:$(Join-Path $bin 'Prometheus.Media.dll')" "/reference:$(Join-Path $bin 'Prometheus.Rendering.dll')" "/out:$testExe" $t.File
    if ($LASTEXITCODE -ne 0) {
        Write-Host " [ERROR DE COMPILACION]" -ForegroundColor Red
        $failed++
        $results += [PSCustomObject]@{ Suite = $t.Name; Status = "FAIL (Build)" }
        continue
    }

    try {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        & $testExe
        $exitCode = $LASTEXITCODE
        $sw.Stop()
        if ($exitCode -eq 0) {
            Write-Host " [PASS] ($([Math]::Round($sw.Elapsed.TotalSeconds, 2))s)" -ForegroundColor Green
            $passed++
            $results += [PSCustomObject]@{ Suite = $t.Name; Status = "PASS"; Time = "$([Math]::Round($sw.Elapsed.TotalSeconds, 2))s" }
        } else {
            Write-Host " [FAIL]" -ForegroundColor Red
            $failed++
            $results += [PSCustomObject]@{ Suite = $t.Name; Status = "FAIL (ExitCode $exitCode)"; Time = "$([Math]::Round($sw.Elapsed.TotalSeconds, 2))s" }
        }
    } catch {
        Write-Host " [FAIL: $_]" -ForegroundColor Red
        $failed++
        $results += [PSCustomObject]@{ Suite = $t.Name; Status = "FAIL (Exception)"; Time = "0s" }
    } finally {
        if (Test-Path $testExe) { Remove-Item -Force $testExe -ErrorAction SilentlyContinue }
    }
}

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "                      RESUMEN DE PRUEBAS                    " -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
$results | Format-Table -AutoSize
Write-Host "Total: $($passed + $failed) | Aprobadas: $passed | Fallidas: $failed"

if ($failed -eq 0 -and $passed -eq 8) {
    Write-Host "`n>>> TODAS LAS 8 PRUEBAS SUPERADAS CON EXITO <<<`n" -ForegroundColor Green
    exit 0
} else {
    Write-Host "`n>>> SE DETECTARON FALLOS EN LA SUITE <<<`n" -ForegroundColor Red
    exit 1
}
