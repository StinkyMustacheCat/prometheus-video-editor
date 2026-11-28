using System;
using System.Linq;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;
using Prometheus.Core.Primitives;

namespace Prometheus.Client.Panels;

public partial class TimelinePanelView
{
    public void PasteContextual()
    {
        if (_copiedClips.Count > 0 && _copiedProjectId == _controller?.CurrentProject.Id &&
            (_copiedClipboardRevision == 0 || _copiedClipboardRevision == GetClipboardSequenceNumber()))
            PasteSelection();
        else _ = PasteExternalAsync();
    }

    private async Task PasteExternalAsync()
    {
        try { await PasteExternalCoreAsync(); }
        catch (Exception ex) { _controller?.ReportStatus("No se pudo pegar el medio: " + ex.Message); }
    }

    private async Task PasteExternalCoreAsync()
    {
        if (_controller == null) return;
        var content = await AdaptiveClipboard.ReadAsync(this);
        if (content.Error != null) { _controller.ReportStatus(content.Error); return; }
        if (content.Files.Length == 0)
        {
            if (WebMediaService.TryUrl(content.Text, out var uri))
            {
                await PasteWebAsync(uri);
            }
            else if (!string.IsNullOrWhiteSpace(content.Text))
            {
                var sequence = _controller.CurrentProject.ActiveSequence
                    ?? throw new InvalidOperationException("No hay montaje activo.");
                var aspect = sequence.Width == sequence.Height ? "square"
                    : sequence.Height > sequence.Width ? "vertical"
                    : Math.Abs((double)sequence.Width / sequence.Height - 4d / 3) < 0.04 ? "classic"
                    : "horizontal";
                var asset = MgtPasteStore.Import(content.Text, aspect);
                var pasteAt = TimelineCanvas.PlayheadSeconds;
                var beforeVideo = sequence.Tracks.Count(track => track.Kind == "video");
                var selectedClipId = SelectedIds().FirstOrDefault();
                string? clipId = null;
                try
                {
                    _controller.UndoRedo.Commit(project =>
                    {
                        project.Assets.Add(asset);
                        var target = project.ActiveSequence!;
                        var selected = target.Clips.FirstOrDefault(clip => clip.Id == selectedClipId);
                        var selectedTrack = target.Tracks.FirstOrDefault(track => track.Id == selected?.TrackId &&
                            track.Kind == "video" && track.Role != "subtitle" && !track.Locked);
                        var track = selectedTrack != null
                            ? ImportPlacement.PreferredFreeTrack(target, selectedTrack.Id, pasteAt,
                                asset.RecommendedDuration?.Seconds ?? 5)
                            : ImportPlacement.LowestFreeTrack(target, "video", pasteAt,
                                asset.RecommendedDuration?.Seconds ?? 5);
                        clipId = ImportPlacement.PlaceAsset(project, asset.Id, track.Id, pasteAt).PrimaryClipId;
                    }, "Pegar MGT en la línea de tiempo");
                }
                catch
                {
                    if (System.IO.File.Exists(asset.Source.Uri)) System.IO.File.Delete(asset.Source.Uri);
                    throw;
                }
                if (_controller.CurrentProject.ActiveSequence!.Tracks.Count(track => track.Kind == "video") > beforeVideo)
                    TimelineCanvas.VideoScrollOffset = TimelineCanvas.VideoScrollMaximum;
                if (clipId != null) TimelineCanvas.Selection.SelectClip(clipId);
                _controller.ReportStatus("MGT pegado en el cabezal: " + asset.Name);
            }
            else _controller.ReportStatus("El portapapeles no contiene medios compatibles");
            return;
        }
        var at = TimelineCanvas.PlayheadSeconds;
        foreach (var path in content.Files)
        {
            try
            {
                var asset = await LocalMediaService.ProbeAsync(path);
                var video = asset.Streams.FirstOrDefault(s => s.Kind is "video" or "image");
                var duration = asset.RecommendedDuration ?? Rational.Time(5);
                var addedVideoTrack = false;
                var addedAudioTrack = false;
                _controller.UndoRedo.Commit(p => {
                    p.Assets.Add(asset);
                    var target = p.ActiveSequence!;
                    var kind = video != null ? "video" : "audio";
                    var beforeVideo = target.Tracks.Count(t => t.Kind == "video");
                    var beforeAudio = target.Tracks.Count(t => t.Kind == "audio");
                    var track = ImportPlacement.LowestFreeTrack(target, kind, at, duration.Seconds);
                    ImportPlacement.PlaceAsset(p, asset.Id, track.Id, at);
                    addedVideoTrack = target.Tracks.Count(t => t.Kind == "video") > beforeVideo;
                    addedAudioTrack = target.Tracks.Count(t => t.Kind == "audio") > beforeAudio;
                }, "Pegar medio desde portapapeles");
                if (addedVideoTrack) TimelineCanvas.VideoScrollOffset = TimelineCanvas.VideoScrollMaximum;
                if (addedAudioTrack) TimelineCanvas.AudioScrollOffset = TimelineCanvas.AudioScrollMaximum;
                at += duration.Seconds;
            }
            catch (Exception ex) { _controller.ReportStatus("No se pudo pegar " + path + ": " + ex.Message); }
        }
    }

    private async Task ImportWebAtAsync(string trackId, double at)
    {
        if (_controller == null) return;
        try
        {
            var projectId = _controller.CurrentProject.Id;
            var sequenceId = _controller.CurrentProject.ActiveSequenceId;
            var request = await WebLinkDialog.AskAsync(this, allowRange: true, fps: _controller.CurrentProject.ActiveSequence!.Fps.Seconds);
            if (request != null && _controller.CurrentProject.Id == projectId && _controller.CurrentProject.ActiveSequenceId == sequenceId)
                await WebMediaService.ImportAsync(_controller, request.Url, trackId, at, quality: request.Quality,
                    sourceIn: request.SourceIn, selectedDuration: request.Duration);
        }
        catch (Exception ex) { _controller.ReportStatus("No se pudo importar el enlace: " + ex.Message); }
    }

    private async Task FillPlaceholderFromWebAsync(string clipId)
    {
        if (_controller == null) return;
        var request = await WebLinkDialog.AskAsync(this);
        if (request == null) return;
        var sourceStart = await AskSourceStartAsync();
        if (!sourceStart.HasValue) return;
        try { await WebMediaService.ImportIntoPlaceholderAsync(_controller, request.Url, clipId, sourceStart.Value, request.Quality); }
        catch (Exception ex) { _controller.ReportStatus("No se pudo conectar el enlace: " + ex.Message); }
    }
    private void CreatePlaceholder(string trackId, double at, double duration)
    {
        if (_controller == null) return;
        string? createdId = null;
        _controller.UndoRedo.Commit(p => createdId = PlaceholderService.Create(p, trackId, at, duration).Id, "Crear espacio reservado");
        if (createdId != null)
        {
            TimelineCanvas.Selection.SelectClip(createdId);
            TimelineCanvas.InvalidateVisual();
        }
    }

    private void CreatePlaceholderInGap(string trackId, double start, double end)
    {
        if (_controller == null) return;
        string? createdId = null;
        _controller.UndoRedo.Commit(p => createdId = PlaceholderService.CreateInGap(p, trackId, start, end).Id, "Convertir hueco en reserva");
        if (createdId != null)
        {
            TimelineCanvas.Selection.SelectClip(createdId);
            TimelineCanvas.InvalidateVisual();
        }
    }

    private async Task FillPlaceholderFromLibraryAsync(string clipId)
    {
        if (_controller == null || TopLevel.GetTopLevel(this) is not Window parent) return;
        var seq = _controller.CurrentProject.ActiveSequence!;
        var clip = seq.Clips.FirstOrDefault(c => c.Id == clipId);
        var track = seq.Tracks.FirstOrDefault(t => t.Id == clip?.TrackId);
        if (track == null) return;
        var assets = _controller.CurrentProject.Assets.Where(a => !PlaceholderService.IsPlaceholder(a) && a.Streams.Any(s => track.Kind == "audio" ? s.Kind == "audio" : s.Kind is "video" or "image")).ToArray();
        if (assets.Length == 0) { _controller.ReportStatus("No hay medios compatibles en la bandeja"); return; }
        var picker = new ComboBox { ItemsSource = assets.Select(a => a.Name).ToArray(), SelectedIndex = 0, Margin = new Thickness(12) };
        var start = new TextBox { Text = "00:00:00:00", Margin = new Thickness(12, 2), Watermark = "Inicio en la fuente (HH:MM:SS:FF)" };
        var connect = new Button { Content = "Conectar", Width = 82 };
        var cancel = new Button { Content = "Cancelar", Width = 82 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(12) };
        row.Children.Add(connect); row.Children.Add(cancel);
        var body = new StackPanel(); body.Children.Add(picker); body.Children.Add(start); body.Children.Add(row);
        var dialog = new Window { Title = "Definir contenido", Width = 400, Height = 195, Content = body, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        connect.Click += (_, _) => dialog.Close(picker.SelectedIndex);
        cancel.Click += (_, _) => dialog.Close(-1);
        var index = await dialog.ShowDialog<int>(parent);
        if (index < 0 || index >= assets.Length) return;
        var assetId = assets[index].Id;
        if (!TryReadSourceStart(start.Text, out var sourceStart)) { _controller.ReportStatus("Usa HH:MM:SS:FF para el inicio del medio"); return; }
        try { _controller.UndoRedo.Commit(p => PlaceholderService.Replace(p, clipId, assetId, sourceStart), "Definir contenido reservado"); }
        catch (Exception ex) { _controller.ReportStatus("No se pudo conectar el medio: " + ex.Message); }
    }

    private async Task FillPlaceholderFromFileAsync(string clipId)
    {
        if (_controller == null) return;
        var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Definir contenido del espacio reservado", AllowMultiple = false });
        if (files.Count == 0) return;
        try
        {
            var asset = await LocalMediaService.ProbeAsync(files[0].Path.LocalPath);
            var sourceStart = asset.Streams.Any(s => s.Kind is "audio" or "video") ? await AskSourceStartAsync() : 0;
            if (!sourceStart.HasValue) return;
            _controller.UndoRedo.Commit(p => { p.Assets.Add(asset); PlaceholderService.Replace(p, clipId, asset.Id, sourceStart.Value); }, "Definir contenido reservado desde archivo");
        }
        catch (Exception ex) { _controller.ReportStatus("No se pudo conectar el archivo: " + ex.Message); }
    }

    private async Task<double?> AskSourceStartAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window parent) return null;
        var input = new TextBox { Text = "00:00:00:00", Margin = new Thickness(12) };
        var accept = new Button { Content = "Aceptar", Width = 82 };
        var cancel = new Button { Content = "Cancelar", Width = 82 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(12) };
        row.Children.Add(accept); row.Children.Add(cancel);
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "Inicio en la fuente (HH:MM:SS:FF)", Margin = new Thickness(12, 8, 12, 0) });
        body.Children.Add(input); body.Children.Add(row);
        var dialog = new Window { Title = "Inicio del medio", Width = 370, Height = 165, Content = body, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        accept.Click += (_, _) => dialog.Close(input.Text);
        cancel.Click += (_, _) => dialog.Close(null);
        var answer = await dialog.ShowDialog<string?>(parent);
        if (answer == null) return null;
        if (TryReadSourceStart(answer, out var seconds)) return seconds;
        _controller?.ReportStatus("Usa HH:MM:SS:FF para el inicio del medio");
        return null;
    }

    private bool TryReadSourceStart(string? text, out double seconds)
    {
        seconds = 0;
        var fields = text?.Split(':');
        if (fields?.Length != 4 || fields.Any(f => !int.TryParse(f, NumberStyles.None, CultureInfo.InvariantCulture, out _))) return false;
        var values = fields.Select(f => int.Parse(f, CultureInfo.InvariantCulture)).ToArray();
        var fps = Math.Max(1, (int)Math.Round(_controller?.CurrentProject.ActiveSequence?.Fps.Seconds ?? 60));
        if (values[1] >= 60 || values[2] >= 60 || values[3] >= fps) return false;
        seconds = values[0] * 3600d + values[1] * 60d + values[2] + values[3] / (double)fps;
        return true;
    }
}
