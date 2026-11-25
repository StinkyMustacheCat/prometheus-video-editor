param(
    [string]$ReleasePath = (Join-Path $PSScriptRoot '..\dist\Prometheus-3.19.8'),
    [switch]$Web,
    [switch]$Export
)

$ErrorActionPreference = 'Stop'
$previousPipe = $env:PROMETHEUS_MCP_PIPE_NAME
$env:PROMETHEUS_MCP_PIPE_NAME = 'PrometheusV2-Smoke-' + [Guid]::NewGuid().ToString('N')
$release = [IO.Path]::GetFullPath($ReleasePath)
$client = Join-Path $release 'Prometheus.Client.exe'
$bridge = Join-Path $release 'McpBridge\Prometheus.McpBridge.exe'
$ffmpeg = Join-Path $release 'ffmpeg.exe'
foreach ($path in @($client, $bridge, $ffmpeg)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Falta $path" }
}
$sample = Join-Path ([IO.Path]::GetTempPath()) ("prometheus-mcp-smoke-" + [Guid]::NewGuid().ToString('N') + '.png')
$captureFile = Join-Path ([IO.Path]::GetTempPath()) ("prometheus-mcp-capture-" + [Guid]::NewGuid().ToString('N') + '.png')
$projectFile = Join-Path ([IO.Path]::GetTempPath()) ("prometheus-mcp-project-" + [Guid]::NewGuid().ToString('N') + '.pthproj')
$exportFile = Join-Path ([IO.Path]::GetTempPath()) ("prometheus-mcp-export-" + [Guid]::NewGuid().ToString('N') + '.mp4')
$cancelFile = Join-Path ([IO.Path]::GetTempPath()) ("prometheus-mcp-cancel-" + [Guid]::NewGuid().ToString('N') + '.mp4')
& $ffmpeg -v error -f lavfi -i color=c=red:s=64x64:d=1 -frames:v 1 -y $sample
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $sample)) { throw 'No se generó la imagen de prueba' }

function Invoke-Rpc([string]$method, [hashtable]$params) {
    $request = @{ jsonrpc = '2.0'; id = 1; method = $method }
    if ($null -ne $params) { $request.params = $params }
    $json = $request | ConvertTo-Json -Compress -Depth 50
    $line = $json | & $bridge
    $reply = $line | ConvertFrom-Json
    if ($reply.error) { throw "JSON-RPC: $($reply.error.message)" }
    return $reply.result
}
function Tool([string]$name, [hashtable]$arguments, [bool]$allowError = $false) {
    $result = Invoke-Rpc 'tools/call' @{ name = $name; arguments = $arguments }
    $value = $result.content[0].text | ConvertFrom-Json
    if ($result.isError -and -not $allowError) { throw "${name}: $($result.content[0].text)" }
    return $value
}
function Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$app = $null
$server = $null
try {
    $app = Start-Process -FilePath $client -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 3
    $initial = Tool 'get_state' @{ scope = 'summary' }
    $sequence = Tool 'get_state' @{ scope = 'sequence' }
    $video = @($sequence.tracks | Where-Object { $_.Kind -eq 'video' } | Sort-Object Order)[0]
    $audio = @($sequence.tracks | Where-Object { $_.Kind -eq 'audio' } | Sort-Object Order)[0]
    Check ($null -ne $video -and $null -ne $audio) 'Faltan pistas iniciales'
    $filtered = Tool 'get_state' @{ scope='tracks'; track_ids=@($video.Id) }
    Check ($filtered.tracks.Count -eq 1 -and $filtered.tracks[0].Id -eq $video.Id) 'track_ids no filtró las pistas'
    $catalog = Tool 'get_catalog' @{ kind='actions'; filter='nested.' }
    Check ($catalog.actions.Count -ge 4) 'El catálogo filtrado omitió acciones anidadas'

    $actions = @(
        @{ op='media.import_local'; resource_path=$sample; track_id=$video.Id; at_seconds=0; result='img' },
        @{ op='effect.apply'; scope='clip'; clip_id='$img'; catalog_id='prometheus.color.basic'; result='fx' },
        @{ op='effect.set_parameter'; effect_id='$fx'; parameter_id='brightness'; number=0.25 },
        @{ op='clip.set_transform'; clip_id='$img'; parameter_id='scale'; number=0.8 }
    )
    $evaluation = Tool 'evaluate_changelist' @{ actions=$actions; expected_revision=$initial.project.Revision }
    Check ($evaluation.valid -eq $true) 'La evaluación local falló'
    $unchanged = Tool 'get_state' @{ scope='summary' }
    Check ($unchanged.project.Revision -eq $initial.project.Revision -and $unchanged.sequence_count -eq 1) 'Evaluate modificó el proyecto'
    $applied = Tool 'apply_changelist' @{ plan_id=$evaluation.plan_id; expected_revision=$initial.project.Revision }
    Check ($applied.applied -eq $true) 'El lote local no se aplicó'
    $clip = Tool 'inspect_clip' @{ clip_id=$applied.references.'$img' }
    Check ($clip.clip.visual.scale_x -eq 0.8) 'La escala no se aplicó'
    Check ($clip.clip.Effects[0].Parameters.brightness.Number -eq 0.25) 'El efecto no se aplicó'
    $selected = Tool 'editor_action' @{ id='selection.by_id'; clip_id=$clip.clip.Id }
    $selection = Tool 'get_state' @{ scope='selection' }
    Check ($selection.clip.Id -eq $clip.clip.Id) 'selection no devolvió el medio seleccionado'
    $cleared = Tool 'editor_action' @{ id='selection.clear' }
    $fullProject = Tool 'get_state' @{ scope='project'; offset=0; limit=1 }
    Check ($fullProject.assets.Count -eq 1 -and $fullProject.total_assets -eq 1) 'La paginación de assets falló'
    $capture = Tool 'capture_frame' @{ mode='save_png'; at_seconds=0; output_path=$captureFile }
    Check ((Test-Path -LiteralPath $captureFile) -and $capture.width -gt 0) 'No se capturó el fotograma'

    $bad = Tool 'evaluate_changelist' @{ actions=@(@{ op='clip.move'; clip_id=$clip.clip.Id; track_id=$audio.Id; at_seconds=0 }) } $true
    Check ($bad.valid -eq $false) 'Un movimiento incompatible fue aceptado'

    $undo = Tool 'editor_history' @{ operation='undo' }
    Check ($undo.changed -eq $true) 'Undo no funcionó'
    $empty = Tool 'get_state' @{ scope='tracks' }
    Check (@($empty.tracks | ForEach-Object { $_.clips }).Count -eq 0) 'Undo dejó clips'
    $redo = Tool 'editor_history' @{ operation='redo' }
    Check ($redo.changed -eq $true) 'Redo no funcionó'
    $guard = Tool 'project_file' @{ operation='new' } $true
    Check ($guard.error -like '*cambios sin guardar*') 'Se omitió el guardia de cambios sin guardar'
    $saved = Tool 'project_file' @{ operation='save'; file_path=$projectFile }
    Check ((Test-Path -LiteralPath $projectFile) -and $saved.dirty -eq $false) 'No se guardó el proyecto'
    $new = Tool 'project_file' @{ operation='new'; name='Nuevo MCP' }
    Check ($new.project_name -eq 'Nuevo MCP') 'No se creó el proyecto nuevo'
    $opened = Tool 'project_file' @{ operation='open'; file_path=$projectFile }
    Check ($opened.project_id -eq $initial.project.Id) 'No se reabrió el mismo proyecto'

    $exportStage = 'not-run'
    if ($Export) {
        $beforeExport = Tool 'get_state' @{ scope='summary' }
        $exportSetup = Tool 'evaluate_changelist' @{ expected_revision=$beforeExport.project.Revision; actions=@(
            @{ op='sequence.set_canvas'; width=256; height=256 },
            @{ op='sequence.set_export_out'; at_seconds=0.2 }
        ) }
        Check ($exportSetup.valid -eq $true) 'No se evaluó el rango de exportación'
        $setupApplied = Tool 'apply_changelist' @{ plan_id=$exportSetup.plan_id; expected_revision=$beforeExport.project.Revision }
        $preflight = Tool 'export_sequence' @{ operation='preflight'; output_path=$exportFile }
        Check ($preflight.ready -eq $true) "Preflight falló: $($preflight.issues -join ', ')"
        $range = Tool 'get_state' @{ scope='io' }
        Check ($range.clips.Count -eq 1 -and $range.out_seconds -eq 0.2) 'El scope io no respetó las marcas'
        $job = Tool 'export_sequence' @{ operation='start'; output_path=$exportFile; expected_revision=$setupApplied.new_revision }
        for ($i = 0; $i -lt 90; $i++) {
            Start-Sleep -Milliseconds 500
            $status = Tool 'export_sequence' @{ operation='status'; job_id=$job.job_id }
            if ($status.state -in @('completed','failed','cancelled')) { break }
        }
        $exportStage = $status.state
        Check ($exportStage -eq 'completed') "Exportación falló: $($status.error)"
        Check (Test-Path -LiteralPath $exportFile) 'No se creó MP4'
        $probe = & (Join-Path $release 'ffprobe.exe') -v error -show_entries stream=codec_type,nb_frames -of csv=p=0 $exportFile
        $probeText = $probe -join "`n"
        Check ($probeText -match 'video,12') "Fotogramas exportados inesperados: $probeText"
        Check ($probeText -match 'audio') 'La exportación no contiene audio'
        $existing = Tool 'export_sequence' @{ operation='preflight'; output_path=$exportFile }
        Check ($existing.ready -eq $false) 'Preflight permitió reemplazar el archivo sin overwrite'
    }

    $beforeNested = Tool 'get_state' @{ scope='summary' }
    $nestedPlan = Tool 'evaluate_changelist' @{ expected_revision=$beforeNested.project.Revision; actions=@(
        @{ op='clip.duplicate'; clip_id=$clip.clip.Id; track_id=$video.Id; at_seconds=6; result='dup' },
        @{ op='curve.set_keyframe'; clip_id=$clip.clip.Id; parameter_id='visual.opacity'; at_seconds=0.5; number=0.2 },
        @{ op='curve.set_keyframe'; clip_id=$clip.clip.Id; parameter_id='visual.opacity'; at_seconds=1; number=1 },
        @{ op='curve.set_interpolation'; clip_id=$clip.clip.Id; parameter_id='visual.opacity'; text='smooth' },
        @{ op='clip.group'; clip_ids=@($clip.clip.Id,'$dup') },
        @{ op='nested.create'; clip_ids=@($clip.clip.Id,'$dup'); result='nest' }
    ) }
    Check ($nestedPlan.valid -eq $true) 'No se evaluó el medio anidado'
    $nestedApply = Tool 'apply_changelist' @{ plan_id=$nestedPlan.plan_id; expected_revision=$beforeNested.project.Revision }
    $nestedClip = Tool 'inspect_clip' @{ clip_id=$nestedApply.references.'$nest' }
    Check ($nestedClip.clip.nested_content.clips.Count -eq 2) 'El anidado no tiene sus dos hijos'
    $beforeUnnest = Tool 'get_state' @{ scope='summary' }
    $unnest = Tool 'evaluate_changelist' @{ expected_revision=$beforeUnnest.project.Revision; actions=@(
        @{ op='nested.unnest'; clip_id=$nestedClip.clip.Id; result='children' }
    ) }
    Check ($unnest.valid -eq $true) 'No se evaluó desanidar'
    $unnestApply = Tool 'apply_changelist' @{ plan_id=$unnest.plan_id; expected_revision=$beforeUnnest.project.Revision }
    Check ($unnestApply.applied -eq $true) 'No se desanidó'

    $beforePlaceholder = Tool 'get_state' @{ scope='summary' }
    $placeholderPlan = Tool 'evaluate_changelist' @{ expected_revision=$beforePlaceholder.project.Revision; actions=@(
        @{ op='media.create_placeholder'; duration_seconds=5; track_id=$video.Id; at_seconds=15; result='ph' },
        @{ op='placeholder.fill_asset'; clip_id='$ph'; asset_id=$applied.references.'$img.asset_id' }
    ) }
    Check ($placeholderPlan.valid -eq $true) 'No se evaluó el espacio reservado'
    $placeholderApply = Tool 'apply_changelist' @{ plan_id=$placeholderPlan.plan_id; expected_revision=$beforePlaceholder.project.Revision }
    $filled = Tool 'inspect_clip' @{ clip_id=$placeholderApply.references.'$ph' }
    Check ($filled.clip.asset_id -eq $applied.references.'$img.asset_id') 'El espacio reservado no se llenó'

    $webStage = 'not-run'
    if ($Web) {
        $python = (Get-Command python -ErrorAction Stop).Source
        $port = Get-Random -Minimum 18000 -Maximum 26000
        $directory = Split-Path -Parent $sample
        $server = Start-Process -FilePath $python -ArgumentList @('-m','http.server',"$port",'--bind','127.0.0.1','--directory',"`"$directory`"") -PassThru -WindowStyle Hidden
        Start-Sleep -Seconds 2
        $url = "http://127.0.0.1:$port/" + [IO.Path]::GetFileName($sample)
        $beforeWeb = Tool 'get_state' @{ scope='summary' }
        $webPlan = Tool 'evaluate_changelist' @{ expected_revision=$beforeWeb.project.Revision; actions=@(
            @{ op='media.import_url'; url=$url; track_id=$video.Id; at_seconds=6; result='web' }
        ) }
        Check ($webPlan.valid -eq $true) 'No se evaluó el enlace web'
        $webApply = Tool 'apply_changelist' @{ plan_id=$webPlan.plan_id; expected_revision=$beforeWeb.project.Revision }
        Check ($webApply.applied -eq $true) 'No se inició la importación web'
        for ($i = 0; $i -lt 30; $i++) {
            Start-Sleep -Milliseconds 500
            $io = Tool 'get_state' @{ scope='io' }
            $webAsset = @($io.web | Where-Object { $_.Id -eq $webApply.references.'$web.asset_id' })[0]
            if ($webAsset -and $webAsset.pending -eq $false) { $webStage = $webAsset.stage; break }
        }
        Check ($webStage -eq 'ready') "La descarga web no terminó: $webStage"
        $afterReady = Tool 'get_state' @{ scope='summary' }
        $again = Tool 'evaluate_changelist' @{ expected_revision=$afterReady.project.Revision; actions=@(
            @{ op='media.import_url'; url=$url; track_id=$video.Id; at_seconds=12; result='again' }
        ) }
        Check ($again.valid -eq $true) 'No se evaluó el enlace web ya resuelto'
        $againApply = Tool 'apply_changelist' @{ plan_id=$again.plan_id; expected_revision=$afterReady.project.Revision }
        $againClip = Tool 'inspect_clip' @{ clip_id=$againApply.references.'$again' }
        Check ($againClip.clip.Name -ne 'Preparando medio…') 'El enlace resuelto creó otra cápsula provisional'
    }

    $addedCapture = Tool 'capture_frame' @{ mode='add_to_media'; at_seconds=0 }
    $afterCapture = Tool 'get_state' @{ scope='project' }
    Check (@($afterCapture.assets | Where-Object { $_.Id -eq $addedCapture.id }).Count -eq 1) 'La captura no se añadió a Medios'
    $overlayCapture = Tool 'capture_frame' @{ mode='overlay'; at_seconds=0 }
    $overlaid = Tool 'inspect_clip' @{ clip_id=$overlayCapture.id }
    Check ($overlaid.clip.Id -eq $overlayCapture.id) 'La captura superpuesta no creó clip'
    $insertCapture = Tool 'capture_frame' @{ mode='insert'; at_seconds=0 }
    $inserted = Tool 'inspect_clip' @{ clip_id=$insertCapture.id }
    Check ($inserted.clip.Id -eq $insertCapture.id) 'La captura insertada no creó clip'

    $cancelStage = 'not-run'
    if ($Export) {
        $beforeCancel = Tool 'get_state' @{ scope='summary' }
        $cancelPlan = Tool 'evaluate_changelist' @{ expected_revision=$beforeCancel.project.Revision; actions=@(
            @{ op='sequence.set_export_out'; at_seconds=3 }
        ) }
        Check ($cancelPlan.valid -eq $true) 'No se pudo preparar exportación para cancelar'
        $cancelSetup = Tool 'apply_changelist' @{ plan_id=$cancelPlan.plan_id; expected_revision=$beforeCancel.project.Revision }
        $cancelJob = Tool 'export_sequence' @{ operation='start'; output_path=$cancelFile; expected_revision=$cancelSetup.new_revision }
        $cancelRequest = Tool 'export_sequence' @{ operation='cancel'; job_id=$cancelJob.job_id }
        for ($i = 0; $i -lt 40; $i++) {
            Start-Sleep -Milliseconds 250
            $cancelStatus = Tool 'export_sequence' @{ operation='status'; job_id=$cancelJob.job_id }
            if ($cancelStatus.state -in @('completed','failed','cancelled')) { break }
        }
        $cancelStage = $cancelStatus.state
        Check ($cancelStage -eq 'cancelled') "No se canceló la exportación: $($cancelStatus.error)"
        Check (-not (Test-Path -LiteralPath $cancelFile)) 'La exportación cancelada dejó salida final'
    }

    [pscustomobject]@{
        release=$release; local_import='ok'; effect='ok'; evaluation_read_only='ok';
        incompatible_move='rejected'; undo_redo='ok'; capture='ok'; project_file='ok';
        nested='ok'; placeholder='ok'; keyframes='ok'; web=$webStage; export=$exportStage;
        capture_modes='ok'; export_cancel=$cancelStage
    } | ConvertTo-Json -Compress
}
finally {
    $env:PROMETHEUS_MCP_PIPE_NAME = $previousPipe
    if ($server) { Stop-Process -Id $server.Id -ErrorAction SilentlyContinue }
    if ($app) { Stop-Process -Id $app.Id -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $sample) { Remove-Item -LiteralPath $sample -Force }
    if (Test-Path -LiteralPath $captureFile) { Remove-Item -LiteralPath $captureFile -Force }
    if (Test-Path -LiteralPath $projectFile) { Remove-Item -LiteralPath $projectFile -Force }
    if (Test-Path -LiteralPath $exportFile) { Remove-Item -LiteralPath $exportFile -Force }
    if (Test-Path -LiteralPath $cancelFile) { Remove-Item -LiteralPath $cancelFile -Force }
    $assets = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $projectFile) (([IO.Path]::GetFileNameWithoutExtension($projectFile)) + '.assets')))
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($assets.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $assets)) { Remove-Item -LiteralPath $assets -Recurse -Force }
}
