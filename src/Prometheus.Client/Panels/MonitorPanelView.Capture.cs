using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;
using SkiaSharp;

namespace Prometheus.Client.Panels;

public partial class MonitorPanelView
{
    private enum CaptureAction { Save, Copy, AddToMedia, Overlay, Insert }
    private bool _captureBusy;

    private async Task CaptureFrameAsync(CaptureAction action = CaptureAction.Save)
    {
        if (_captureBusy) return;
        _captureBusy = true;
        CaptureFrameButton.IsEnabled = false;
        CaptureFrameButton.Content = "Preparando…";
        try
        {
            var controller = ProjectController.Instance ?? throw new InvalidOperationException("No hay proyecto abierto.");
            var project = controller.CurrentProject;
            var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay montaje activo.");
            _transport?.Pause();
            var time = _transport?.PositionSeconds ?? _requestedTime;
            controller.ReportStatus("Preparando captura del fotograma…");
            var jpeg = action == CaptureAction.Save && _captureSettings.Format == "jpg";
            var background = !jpeg && _captureSettings.TransparentBackground
                ? SKColors.Transparent : _captureSettings.OpaqueColor;
            using var frame = await RenderExactFrameAsync(time, requireVisual: true, background);
            using var image = SKImage.FromBitmap(frame);
            using var encoded = image.Encode(jpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png,
                jpeg ? 95 : 100) ?? throw new InvalidOperationException("No se pudo codificar la captura.");
            var bytes = encoded.ToArray();
            if (action == CaptureAction.Copy)
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard
                    ?? throw new InvalidOperationException("No hay portapapeles disponible.");
                using var bitmapStream = new MemoryStream(bytes);
                using var bitmap = new Avalonia.Media.Imaging.Bitmap(bitmapStream);
                var data = new DataObject();
                data.Set("Bitmap", bitmap);
                data.Set("image/png", bytes);
                await clipboard.SetDataObjectAsync(data);
                controller.ReportStatus("Fotograma copiado al portapapeles");
                return;
            }
            if (action != CaptureAction.Save)
            {
                await StoreCapturedMediaAsync(controller, project.Id, sequence.Id, time, sequence.Fps.Seconds, bytes, action);
                return;
            }
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider
                ?? throw new InvalidOperationException("No hay selector de archivos disponible.");
            var safeName = string.Concat(sequence.Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            var timecode = TimelinePanelView.FormatTimecode(time, sequence.Fps.Seconds).Replace(':', '-');
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Guardar fotograma de previsualización",
                SuggestedFileName = $"{safeName}-{timecode}.{(jpeg ? "jpg" : "png")}",
                DefaultExtension = jpeg ? "jpg" : "png",
                FileTypeChoices = jpeg
                    ? [new FilePickerFileType("Imagen JPEG") { Patterns = ["*.jpg", "*.jpeg"] }]
                    : [new FilePickerFileType("Imagen PNG") { Patterns = ["*.png"] }]
            });
            if (file == null) return;
            await using var output = await file.OpenWriteAsync();
            if (output.CanSeek) output.SetLength(0);
            await output.WriteAsync(bytes);
            controller.ReportStatus($"Fotograma guardado: {file.Name} ({sequence.Width}×{sequence.Height})");
        }
        catch (Exception ex)
        {
            ProjectController.Instance?.ReportStatus("Captura: " + ex.Message);
            PreviewMessage.Text = "Captura: " + ex.Message;
        }
        finally
        {
            CaptureFrameButton.IsEnabled = true;
            CaptureFrameButton.Content = "Capturar ▾";
            _captureBusy = false;
        }
    }

    internal async Task<SKBitmap> RenderExactFrameAsync(double time, bool requireVisual = false,
        SKColor? background = null, CancellationToken cancellation = default)
    {
        var controller = ProjectController.Instance ?? throw new InvalidOperationException("No hay proyecto abierto.");
        var project = controller.CurrentProject;
        var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay montaje activo.");
        var plan = SubtitleRenderPlan.At(project, time, "video");
        if (requireVisual && plan.Count == 0)
            throw new InvalidOperationException("Sitúa el lector sobre un medio visual para capturar.");
        if (plan.Count == 0)
            return GpuCaptureComposer.Compose(sequence.Width, sequence.Height, [], background);

        var tolerance = 0.5 / Math.Max(1, sequence.Fps.Seconds);
        var ready = false;
        for (var attempt = 0; attempt < 150; attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!_busy) ShowTime(time);
            await Task.Delay(40, cancellation);
            ready = !_busy && !_compositeIncomplete && Math.Abs(_displayedTime - time) <= tolerance;
            if (ready) break;
        }
        if (!ready) throw new InvalidOperationException("El fotograma no estuvo listo después de 6 segundos.");
        if (controller.CurrentProject.Id != project.Id || controller.CurrentProject.ActiveSequenceId != sequence.Id)
            throw new InvalidOperationException("El montaje cambió durante el render.");

        var bitmaps = new List<SKBitmap>();
        try
        {
            var layers = new List<GpuCaptureComposer.Layer>();
            if (_groupLayer != null && _groupLayer.Visual.Opacity > 0)
            {
                SKBitmap source;
                if (_groupLayer.Gpu.Width != sequence.Width || _groupLayer.Gpu.Height != sequence.Height)
                {
                    // A reduced monitor composite cannot recover fine mask edges by
                    // scaling its bitmap. Render this capture once at montage size.
                    var snapshot = Prometheus.Core.Storage.ProjectStore.Clone(project);
                    var resourcePath = controller.ResourceProjectPath;
                    source = await Task.Run(async () =>
                    {
                        using var renderer = new GpuExportFrameRenderer(snapshot, resourcePath);
                        await renderer.RenderAsync(time, cancellation);
                        return renderer.Readback();
                    }, cancellation);
                    if (!ReferenceEquals(controller.CurrentProject, project))
                    {
                        source.Dispose();
                        throw new InvalidOperationException("El proyecto cambió durante la captura.");
                    }
                }
                else source = _groupLayer.Gpu.ReadbackBitmap();
                bitmaps.Add(source);
                layers.Add(new GpuCaptureComposer.Layer(source, new ClipVisualState()));
            }
            else if (NestedLayerHost.IsVisible)
            {
                var valid = plan.Select(layer => (Layer: layer,
                        Path: SubtitleRenderPlan.Asset(project, layer.Clip)?.EffectiveLocator?.Path))
                    .Where(item => !string.IsNullOrWhiteSpace(item.Path) && File.Exists(item.Path)).ToList();
                if (valid.Count != _nestedLayers.Count || _nestedLayers.Any(layer => layer.Failed))
                    throw new InvalidOperationException("Una capa aún no está lista para capturar.");
                foreach (var entry in _nestedLayers.OrderBy(layer => layer.SourceIndex))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (entry.Gpu == null || !entry.Gpu.TryGetCurrentFrame(out _))
                        throw new InvalidOperationException("Una capa aún no tiene fotograma.");
                    var source = entry.Gpu.ReadbackBitmap();
                    bitmaps.Add(source);
                    var planned = valid[entry.SourceIndex].Layer;
                    layers.Add(new GpuCaptureComposer.Layer(source,
                        DynamicMotionEffect.Evaluate(planned.Clip, planned.LocalTimelineSeconds)));
                }
            }
            else
            {
                var layer = plan[^1];
                var path = SubtitleRenderPlan.Asset(project, layer.Clip)?.EffectiveLocator?.Path;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    var source = await DecodeFallbackCaptureAsync(path, layer.SourceSeconds);
                    bitmaps.Add(source);
                    layers.Add(new GpuCaptureComposer.Layer(source,
                        DynamicMotionEffect.Evaluate(layer.Clip, layer.LocalTimelineSeconds)));
                }
            }
            return GpuCaptureComposer.Compose(sequence.Width, sequence.Height, layers, background);
        }
        finally { foreach (var bitmap in bitmaps) bitmap.Dispose(); }
    }

    internal async Task<object> CaptureForMcpAsync(string mode, double? atSeconds, string? outputPath)
    {
        if (mode is not ("save_png" or "add_to_media" or "insert" or "overlay"))
            throw new ArgumentException("mode debe ser save_png, add_to_media, insert u overlay");
        var controller = ProjectController.Instance ?? throw new InvalidOperationException("No hay proyecto abierto");
        var project = controller.CurrentProject;
        var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay montaje activo");
        var time = atSeconds ?? controller.Transport.PositionSeconds;
        if (!double.IsFinite(time) || time < 0 || time > sequence.DurationSeconds)
            throw new ArgumentOutOfRangeException(nameof(atSeconds));
        controller.Transport.Pause();
        using var frame = await RenderExactFrameAsync(time, background: SKColors.Black);
        using var image = SKImage.FromBitmap(frame);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("No se pudo codificar la captura");
        var bytes = encoded.ToArray();
        if (mode == "save_png")
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Prometheus", "CapturedFrames", project.Id);
            var path = outputPath == null ? Path.Combine(folder,
                "Fotograma-" + Guid.NewGuid().ToString("N") + ".png") : Path.GetFullPath(outputPath);
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("output_path debe terminar en .png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes);
            return new { mode, output_path = path, width = frame.Width, height = frame.Height, at_seconds = time };
        }
        var action = mode switch
        {
            "add_to_media" => CaptureAction.AddToMedia,
            "insert" => CaptureAction.Insert,
            _ => CaptureAction.Overlay
        };
        var id = await StoreCapturedMediaAsync(controller, project.Id, sequence.Id, time,
            sequence.Fps.Seconds, bytes, action);
        return new { mode, id, at_seconds = time };
    }

    private static async Task<string> StoreCapturedMediaAsync(ProjectController controller, string projectId,
        string sequenceId, double time, double fps, byte[] bytes, CaptureAction action)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Prometheus", "CapturedFrames", projectId);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "Fotograma-" + Guid.NewGuid().ToString("N") + ".png");
        var committed = false;
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            var asset = await LocalMediaService.ProbeAsync(path);
            asset.Name = "Fotograma " + TimelinePanelView.FormatTimecode(time, fps);
            if (controller.CurrentProject.Id != projectId || controller.CurrentProject.ActiveSequenceId != sequenceId)
                throw new InvalidOperationException("El montaje cambió durante la captura.");
            string? placedId = null;
            controller.UndoRedo.Commit(project =>
            {
                if (action == CaptureAction.AddToMedia) project.Assets.Add(asset);
                else if (action == CaptureAction.Overlay)
                    placedId = CapturedFramePlacement.Overlay(project, asset, time).Id;
                else placedId = CapturedFramePlacement.Insert(project, asset, time).Id;
            }, "Capturar fotograma");
            committed = true;
            if (placedId != null) controller.SetSelectedClip(placedId);
            controller.ReportStatus(action switch
            {
                CaptureAction.AddToMedia => "Fotograma añadido a Medios",
                CaptureAction.Overlay => "Fotograma superpuesto en una pista libre",
                _ => "Fotograma incrustado; contenido posterior desplazado"
            });
            return placedId ?? asset.Id;
        }
        finally { if (!committed && File.Exists(path)) File.Delete(path); }
    }

    private static async Task<SKBitmap> DecodeFallbackCaptureAsync(string path, double seconds)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(LocalMediaService.Tool("ffmpeg"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        }};
        foreach (var arg in new[] { "-v", "error", "-ss", seconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                     "-i", path, "-frames:v", "1", "-f", "image2pipe", "-vcodec", "png", "pipe:1" })
            process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        using var buffer = new MemoryStream();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardOutput.BaseStream.CopyToAsync(buffer);
        await process.WaitForExitAsync();
        var error = await stderr;
        if (process.ExitCode != 0 || buffer.Length == 0) throw new InvalidOperationException(error.Trim());
        buffer.Position = 0;
        return SKBitmap.Decode(buffer) ?? throw new InvalidOperationException("No se pudo leer el fotograma.");
    }
}
