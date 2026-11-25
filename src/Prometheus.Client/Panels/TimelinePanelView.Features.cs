using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Interactivity;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public partial class TimelinePanelView
{
    private List<Clip> _copiedClips = new();
    private List<string[]> _copiedLinks = new();
    private List<string[]> _copiedEditGroups = new();
    private Clip? _copiedAttributes;
    private string? _copiedProjectId;
    private uint _copiedClipboardRevision;
    private Sequence? _copiedSourceSnapshot;
    private string[] _copiedSourceIds = Array.Empty<string>();
    private ContextMenu? _activeTimelineMenu;
    private bool _compactTrackHeader;
    private double TrackHeaderWidth => _compactTrackHeader ? 44 : 150;

    private void InitializeTimelineFeatures()
    {
        AddTrackBtn.Click += (_, _) => ShowMenu(AddTrackBtn, new[] {
            Menu("Añadir pista de vídeo", () => AddTrack("video")),
            Menu("Añadir pista de audio", () => AddTrack("audio"))
        });
        CompactTrackHeaderBtn.Click += (_, _) => SetTrackHeaderCompact(!_compactTrackHeader);
        TimelineCanvas.TimelineMenuRequested += ShowTimelineContext;
        TimelineCanvas.TransitionSelected += SelectTransition;
        TimelineCanvas.TransitionMenuRequested += ShowTransitionMenu;
        TimelineCanvas.SelectedClipClickedAgain += RemoveSelectedPlaceholder;
        DragDrop.SetAllowDrop(TimelineCanvas, true);
        TimelineCanvas.AddHandler(DragDrop.DragOverEvent, TimelineDragOver);
        TimelineCanvas.AddHandler(DragDrop.DropEvent, TimelineDrop);
        TimelineCanvas.AddHandler(DragDrop.DragLeaveEvent, (_, _) => { TimelineCanvas.DropPlaceholderId = null; TimelineCanvas.PreviewExternalNewTrack(null, 0); TimelineCanvas.SetTransitionDrop(null, null); });
    }

    private void SetTrackHeaderCompact(bool compact)
    {
        _compactTrackHeader = compact;
        var width = new GridLength(TrackHeaderWidth);
        RulerGrid.ColumnDefinitions[0].Width = width;
        TrackBodyGrid.ColumnDefinitions[0].Width = width;
        OverviewGrid.ColumnDefinitions[0].Width = width;
        AddTrackBtn.IsVisible = !compact;
        CompactTrackHeaderBtn.Content = compact ? "›" : "‹";
        ToolTip.SetTip(CompactTrackHeaderBtn, compact ? "Mostrar cabeceras de pistas" : "Reducir cabeceras de pistas");
        if (_controller?.CurrentProject.ActiveSequence is { } sequence) RefreshTrackHeaders(sequence);
    }

    private void TimelineDragOver(object? sender, DragEventArgs e)
    {
        try
        {
        var point = e.GetPosition(TimelineCanvas);
        var effectIds = EffectApplicationService.ReadDragIds(e.Data);
        if (effectIds.FirstOrDefault(AudioTransitionOperations.Supports) is { } audioTransition)
        {
            var target = FindAudioTransitionTarget(point, audioTransition);
            var valid = target is { } candidate && _controller?.CurrentProject is { ActiveSequence: { } seq } project &&
                AudioTransitionOperations.MaximumSeconds(project, seq, audioTransition, candidate.Clip.Id, candidate.Right?.Id) > 0;
            TimelineCanvas.SetTransitionDrop(valid ? target!.Value.Clip.TrackId : null, valid ? target!.Value.Time : null);
            e.DragEffects = valid ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (effectIds.FirstOrDefault(id => MgtTransitionCatalog.Supports(_controller?.CurrentProject, id)) is { } transition)
        {
            var cut = FindMediaCut(TimelineCanvas.TrackAtY(point.Y), TimelineCanvas.PixelsToTime(point.X), "video");
            var mgt = MgtTransitionCatalog.Find(_controller?.CurrentProject, transition);
            var valid = cut is { } pair && _controller?.CurrentProject is { ActiveSequence: { } seq } project &&
                VideoTransitionOperations.MaximumFrames(project, seq, pair.Left.Id, pair.Right.Id, transition, mgt) >=
                VideoTransitionOperations.MinimumFrames(transition, seq.Fps.Seconds, mgt);
            TimelineCanvas.SetTransitionDrop(valid ? cut!.Value.Left.TrackId : null,
                valid ? cut!.Value.Right.TimelineStart.Seconds : null);
            e.DragEffects = valid ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (effectIds.Length > 0)
        {
            var clip = TimelineCanvas.ClipAt(point);
            e.DragEffects = EffectApplicationService.CanApply(_controller!.CurrentProject, clip?.Id, effectIds)
                ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
            return;
        }
        var placeholder = PlaceholderAt(point);
        if (placeholder != null)
        {
            TimelineCanvas.PreviewExternalNewTrack(null, 0);
            var targetTrack = _controller?.CurrentProject.ActiveSequence?.Tracks.FirstOrDefault(t => t.Id == placeholder.TrackId);
            var draggedId = e.Data.Get("Prometheus.AssetId") as string;
            var draggedAsset = _controller?.CurrentProject.Assets.FirstOrDefault(a => a.Id == draggedId);
            var shelf = ShelfItem(e.Data);
            var placeholderCompatible = targetTrack != null && targetTrack.Role != "subtitle" && !targetTrack.Locked &&
                (draggedAsset != null ? PlaceholderService.Accepts(_controller!.CurrentProject, placeholder, draggedAsset) :
                shelf != null ? ShelfSupportsTrack(shelf, targetTrack.Kind) :
                BrowserMediaData.TryGetUrl(e.Data, out var url) ? WebMediaService.CompatibleWithKnownExtension(url, targetTrack.Kind) :
                e.Data.GetFiles()?.Count() == 1 || targetTrack.Kind == "video" && BrowserMediaData.HasImage(e.Data));
            TimelineCanvas.DropPlaceholderId = placeholderCompatible ? placeholder.Id : null;
            e.DragEffects = placeholderCompatible ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
            return;
        }
        TimelineCanvas.DropPlaceholderId = null;
        var autoKind = AutoDropKind(point.Y);
        var track = autoKind == null ? TimelineCanvas.TrackAtY(point.Y) : null;
        var assetId = e.Data.Get("Prometheus.AssetId") as string;
        var asset = _controller?.CurrentProject.Assets.FirstOrDefault(a => a.Id == assetId);
        var shelfItem = ShelfItem(e.Data);
        if (autoKind != null && asset != null && !asset.Streams.Any(s => autoKind == "audio" ? s.Kind == "audio" : s.Kind is "video" or "image"))
            autoKind = null;
        if (autoKind != null && shelfItem != null && !ShelfSupportsTrack(shelfItem, autoKind)) autoKind = null;
        var urlAvailable = BrowserMediaData.TryGetUrl(e.Data, out var dropUrl);
        var hasFiles = e.Data.GetFiles()?.Any() == true;
        var hasImage = BrowserMediaData.HasImage(e.Data);
        if (autoKind != null && urlAvailable && !WebMediaService.CompatibleWithKnownExtension(dropUrl, autoKind)) autoKind = null;
        var compatible = track != null && track.Role != "subtitle" && !track.Locked &&
            (asset != null ? asset.Streams.Any(s => track.Kind == "audio" ? s.Kind == "audio" : s.Kind is "video" or "image") :
            shelfItem != null ? ShelfSupportsTrack(shelfItem, track.Kind) :
            urlAvailable ? WebMediaService.CompatibleWithKnownExtension(dropUrl, track.Kind) :
            hasFiles || track.Kind == "video" && hasImage);
        e.DragEffects = compatible || autoKind != null && (asset != null || shelfItem != null || urlAvailable || hasFiles || hasImage && autoKind == "video")
            ? DragDropEffects.Copy : DragDropEffects.None;
        TimelineCanvas.PreviewExternalNewTrack(e.DragEffects == DragDropEffects.None ? null : autoKind, point.Y);
        e.Handled = true;
        }
        catch { TimelineCanvas.DropPlaceholderId = null; TimelineCanvas.PreviewExternalNewTrack(null, 0); e.DragEffects = DragDropEffects.None; e.Handled = true; }
    }

    private Clip? PlaceholderAt(Point point)
    {
        var clip = TimelineCanvas.ClipAt(point);
        return clip != null && _controller != null && PlaceholderService.IsPlaceholder(
            _controller.CurrentProject.Assets.FirstOrDefault(a => a.Id == clip.AssetId)) ? clip : null;
    }

    private string? AutoDropKind(double y) => TimelineCanvas.NewTrackDropKind(y);

    private LinkShelfItem? ShelfItem(IDataObject data) =>
        data.Get("Prometheus.LinkShelfId") is string id
            ? _controller?.CurrentProject.LinkShelf.FirstOrDefault(x => x.Id == id) : null;

    private bool ShelfSupportsTrack(LinkShelfItem item, string kind)
    {
        var imported = _controller?.CurrentProject.Assets.FirstOrDefault(a => a.Source.Uri == item.Source);
        if (imported != null && imported.Streams.Count > 0) return AssetSupportsTrack(imported, kind);
        if (item.MediaKind is "audio" or "video" or "image")
            return kind == (item.MediaKind == "audio" ? "audio" : "video");
        var extension = item.Kind == "web" && WebMediaService.TryUrl(item.Source, out var uri)
            ? System.IO.Path.GetExtension(uri.AbsolutePath).ToLowerInvariant()
            : System.IO.Path.GetExtension(item.Source).ToLowerInvariant();
        if (extension is ".mp3" or ".m4a" or ".wav" or ".aac" or ".flac" or ".ogg") return kind == "audio";
        if (extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp") return kind == "video";
        return kind == "video" || item.Kind == "web";
    }

    private async void TimelineDrop(object? sender, DragEventArgs e)
    {
        if (_controller == null) return;
        try
        {
        var point = e.GetPosition(TimelineCanvas);
        TimelineCanvas.DropPlaceholderId = null;
        TimelineCanvas.PreviewExternalNewTrack(null, 0);
        TimelineCanvas.SetTransitionDrop(null, null);
        var effectIds = EffectApplicationService.ReadDragIds(e.Data);
        if (effectIds.FirstOrDefault(AudioTransitionOperations.Supports) is { } audioTransition)
        {
            if (FindAudioTransitionTarget(point, audioTransition) is { } target)
                ApplyAudioTransition(target.Clip.Id, target.Right?.Id, audioTransition);
            e.Handled = true;
            return;
        }
        if (effectIds.FirstOrDefault(id => MgtTransitionCatalog.Supports(_controller?.CurrentProject, id)) is { } transition)
        {
            if (FindMediaCut(TimelineCanvas.TrackAtY(point.Y), TimelineCanvas.PixelsToTime(point.X), "video") is { } cut)
                ApplyVideoTransition(cut.Left.Id, cut.Right.Id, transition);
            e.Handled = true;
            return;
        }
        if (effectIds.Length > 0)
        {
            EffectApplicationService.Apply(_controller, TimelineCanvas.ClipAt(point)?.Id, effectIds);
            e.Handled = true;
            return;
        }
        if (PlaceholderAt(point) is { } placeholder)
        {
            await FillPlaceholderFromDropAsync(placeholder.Id, e.Data);
            e.Handled = true;
            return;
        }
        var autoKind = AutoDropKind(point.Y);
        var newVideoArea = autoKind == "video" ? TimelineCanvas.VideoAreaHeightForNewTrack(point.Y) : (double?)null;
        var track = autoKind == null ? TimelineCanvas.TrackAtY(point.Y) : null;
        if (track?.Locked == true || track?.Role == "subtitle" || track == null && autoKind == null) return;
        var targetTrackId = track?.Id;
        if (autoKind != null)
        {
            var draggedId = e.Data.Get("Prometheus.AssetId") as string;
            var draggedAsset = _controller.CurrentProject.Assets.FirstOrDefault(a => a.Id == draggedId);
            if (draggedAsset != null && !draggedAsset.Streams.Any(s => autoKind == "audio" ? s.Kind == "audio" : s.Kind is "video" or "image"))
            { _controller.ReportStatus("El medio no corresponde a la zona de pistas"); return; }
        }
        var at = TimelineCanvas.PixelsToTime(point.X);
        var id = e.Data.Get("Prometheus.AssetId") as string;
        var shelf = ShelfItem(e.Data);
        if (shelf != null)
        {
            if (autoKind != null && !ShelfSupportsTrack(shelf, autoKind)) return;
            targetTrackId ??= CreateDropTrack(autoKind!, newVideoArea);
            var projectId = _controller.CurrentProject.Id;
            var sequenceId = _controller.CurrentProject.ActiveSequence?.Id;
            if (shelf.Kind == "web")
                await WebMediaService.ImportAsync(_controller, shelf.Source, targetTrackId, at,
                    shelf.WebMetadata, shelf.DurationSeconds);
            else
            {
                var imported = await LinkShelfService.PromoteAsync(_controller, shelf.Id);
                if (imported != null && _controller.CurrentProject.Id == projectId &&
                    _controller.CurrentProject.ActiveSequence?.Id == sequenceId)
                    PlaceAssetAt(imported, targetTrackId, at, null, null);
            }
        }
        else if (id != null)
        {
            var asset = _controller.CurrentProject.Assets.FirstOrDefault(a => a.Id == id);
            if (asset != null) PlaceAssetAt(asset, targetTrackId, at, autoKind, newVideoArea);
        }
        else if (BrowserMediaData.TryGetHtmlMediaUrl(e.Data, out var htmlMedia))
        {
            if (autoKind != null && !WebMediaService.CompatibleWithKnownExtension(htmlMedia, autoKind))
            { _controller.ReportStatus("El enlace no corresponde a la zona de pistas"); return; }
            targetTrackId ??= CreateDropTrack(autoKind!, newVideoArea);
            await WebMediaService.ImportAsync(_controller, htmlMedia.ToString(), targetTrackId, at);
        }
        else if (e.Data.GetFiles()?.Any() == true)
        {
            foreach (var file in e.Data.GetFiles()!.Where(f => f.Path.IsFile))
            {
                try
                {
                    var asset = await LocalMediaService.ProbeAsync(file.Path.LocalPath);
                    if (autoKind != null && !AssetSupportsTrack(asset, autoKind)) { _controller.ReportStatus("El archivo no corresponde a la zona de pistas"); continue; }
                    _controller.UndoRedo.Commit(p => { targetTrackId ??= AddDropTrack(p, autoKind!, newVideoArea); p.Assets.Add(asset); ImportPlacement.PlaceAsset(p, asset.Id, targetTrackId, at); }, "Importar medio arrastrado");
                    at += asset.RecommendedDuration?.Seconds ?? 5;
                }
                catch (Exception ex) { _controller.ReportStatus("No se pudo importar: " + ex.Message); }
            }
        }
        else
        {
            var image = BrowserMediaData.SaveImage(BrowserMediaData.GetImageData(e.Data));
            if (image != null)
            {
                try
                {
                    var asset = await LocalMediaService.ProbeAsync(image);
                    if (autoKind == "audio") { _controller.ReportStatus("La imagen requiere una pista de vídeo"); return; }
                    _controller.UndoRedo.Commit(p => { targetTrackId ??= AddDropTrack(p, autoKind!, newVideoArea); p.Assets.Add(asset); ImportPlacement.PlaceAsset(p, asset.Id, targetTrackId, at); }, "Importar imagen arrastrada");
                }
                catch (Exception ex) { _controller.ReportStatus("No se pudo importar imagen: " + ex.Message); }
            }
            else if (BrowserMediaData.TryGetUrl(e.Data, out var url))
            {
                if (autoKind != null && !WebMediaService.CompatibleWithKnownExtension(url, autoKind))
                { _controller.ReportStatus("El enlace no corresponde a la zona de pistas"); return; }
                targetTrackId ??= CreateDropTrack(autoKind!, newVideoArea);
                await WebMediaService.ImportAsync(_controller, url.ToString(), targetTrackId, at);
            }
            else _controller.ReportStatus("El navegador no entregó una imagen, archivo o enlace compatible");
        }
        e.Handled = true;
        }
        catch (Exception ex) { _controller.ReportStatus("No se pudo importar el arrastre: " + ex.Message); e.Handled = true; }
    }

    private static bool AssetSupportsTrack(Asset asset, string kind) =>
        asset.Streams.Any(s => kind == "audio" ? s.Kind == "audio" : s.Kind is "video" or "image");

    private static string AddDropTrack(Project project, string kind, double? videoArea)
    {
        var sequence = project.ActiveSequence!;
        var id = CollisionEngine.AddTrack(sequence, kind).Id;
        if (videoArea.HasValue) sequence.VideoAreaHeight = videoArea.Value;
        return id;
    }

    private string CreateDropTrack(string kind, double? videoArea)
    {
        string? id = null;
        _controller!.UndoRedo.Commit(p => id = AddDropTrack(p, kind, videoArea), "Crear pista al soltar enlace");
        return id!;
    }

    private async Task FillPlaceholderFromDropAsync(string clipId, IDataObject data)
    {
        if (_controller == null) return;
        if (ShelfItem(data) is { } shelf)
        {
            var projectId = _controller.CurrentProject.Id;
            var sequenceId = _controller.CurrentProject.ActiveSequence?.Id;
            if (shelf.Kind == "web") await WebMediaService.ImportIntoPlaceholderAsync(_controller, shelf.Source, clipId);
            else
            {
                var shelfAsset = await LinkShelfService.PromoteAsync(_controller, shelf.Id);
                if (shelfAsset != null && _controller.CurrentProject.Id == projectId &&
                    _controller.CurrentProject.ActiveSequence?.Id == sequenceId)
                    _controller.UndoRedo.Commit(p => {
                        PlaceholderService.Replace(p, clipId, shelfAsset.Id);
                        LinkShelfService.MarkPlaced(p, shelf.Source);
                    },
                    "Conectar referencia a reserva");
            }
            return;
        }
        var draggedId = data.Get("Prometheus.AssetId") as string;
        if (draggedId != null)
        {
            var asset = _controller.CurrentProject.Assets.FirstOrDefault(a => a.Id == draggedId);
            if (asset == null) return;
            if (asset.Source.Kind == "web" && WebMediaService.IsPending(asset))
                await WebMediaService.ImportIntoPlaceholderAsync(_controller, asset.Source.Uri, clipId);
            else
                _controller.UndoRedo.Commit(p => {
                    PlaceholderService.Replace(p, clipId, draggedId);
                    LinkShelfService.MarkPlaced(p, asset.Source.Uri);
                }, "Conectar medio a reserva");
            return;
        }
        if (BrowserMediaData.TryGetUrl(data, out var url))
        {
            await WebMediaService.ImportIntoPlaceholderAsync(_controller, url.ToString(), clipId);
            return;
        }
        var files = data.GetFiles()?.Where(f => f.Path.IsFile).ToArray();
        if (files is { Length: > 1 }) { _controller.ReportStatus("Suelta un solo archivo sobre cada espacio reservado"); return; }
        var path = files is { Length: 1 } ? files[0].Path.LocalPath : BrowserMediaData.SaveImage(BrowserMediaData.GetImageData(data));
        if (path == null) { _controller.ReportStatus("No se recibió un medio compatible"); return; }
        var imported = await LocalMediaService.ProbeAsync(path);
        _controller.UndoRedo.Commit(p =>
        {
            p.Assets.Add(imported);
            PlaceholderService.Replace(p, clipId, imported.Id);
        }, "Conectar archivo a reserva");
    }

    private void PlaceAssetAt(Asset asset, string? trackId, double at, string? autoKind, double? newVideoArea)
    {
        if (_controller == null) return;
        if (asset.Source.Kind == "web" && WebMediaService.IsPending(asset))
        {
            trackId ??= CreateDropTrack(autoKind!, newVideoArea);
            _ = WebMediaService.ResolveAsync(_controller, asset.Id, trackId, at);
            return;
        }
        try { _controller.UndoRedo.Commit(p => {
            trackId ??= AddDropTrack(p, autoKind!, newVideoArea);
            ImportPlacement.PlaceAsset(p, asset.Id, trackId, at);
            LinkShelfService.MarkPlaced(p, asset.Source.Uri);
        }, "Arrastrar medio a pista"); }
        catch (Exception ex) { _controller.ReportStatus("No se pudo colocar el medio: " + ex.Message); }
    }

    private MenuItem Menu(string title, Action action)
    {
        var item = new MenuItem { Header = title };
        item.Click += (_, _) => {
            try { action(); }
            catch (InvalidOperationException ex) { _controller?.ReportStatus(ex.Message); }
        };
        return item;
    }

    private void ShowMenu(Control control, IEnumerable<MenuItem> items)
    {
        // El menú se abre solo por petición explícita; asociarlo al lienzo hacía que
        // Avalonia volviera a abrirlo en clic derecho, incluso con Zoom activo.
        control.ContextMenu = null;
        _activeTimelineMenu?.Close();
        _activeTimelineMenu = new ContextMenu { ItemsSource = items.ToArray() };
        _activeTimelineMenu.Open(control);
    }

    private static MenuItem Submenu(string title, IEnumerable<MenuItem> items) =>
        new() { Header = title, ItemsSource = items.ToArray() };

    private void AddTrack(string kind)
    {
        _controller?.UndoRedo.Commit(p => CollisionEngine.AddTrack(p.ActiveSequence!, kind), "Añadir pista " + kind);
    }

    private void RefreshTrackHeaders(Sequence sequence)
    {
        TrackHeaderPanel.Children.Clear();
        var video = sequence.Tracks.Where(t => t.Kind == "video").OrderByDescending(t => t.Order);
        var audio = sequence.Tracks.Where(t => t.Kind == "audio").OrderBy(t => t.Order);
        foreach (var track in video) AddTrackHeader(track);
        var divider = new Border { Width = TrackHeaderWidth, Height = 8, Background = new SolidColorBrush(Color.Parse("#242424")), Cursor = new Cursor(StandardCursorType.SizeNorthSouth) };
        Canvas.SetTop(divider, TimelineCanvas.DividerY);
        TrackHeaderPanel.Children.Add(divider);
        foreach (var track in audio) AddTrackHeader(track);
        UpdateTrackScrollbars();
    }

    private void AddTrackHeader(Track track)
    {
        if (track.Role == "subtitle")
        {
            AddSubtitleHeader(track);
            return;
        }
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(_compactTrackHeader ? "*" :
            track.Kind == "audio" ? "*,Auto,Auto,Auto,Auto" : "*,Auto,Auto,Auto") };
        var title = new TextBlock { Text = track.Name, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var enable = TrackHeaderButton(TrackStateIcon(track.Kind, track.Enabled));
        var solo = track.Kind == "audio" ? TrackHeaderButton(TrackHeaderLabel("F"), track.Solo) : null;
        if (solo != null) ToolTip.SetTip(solo, "Foco: escuchar únicamente esta pista de audio");
        var locked = TrackHeaderButton(LockIcon(track.Locked));
        var id = track.Id;
        var fx = TrackHeaderButton(TrackHeaderLabel("FX"), track.Effects.Any(effect => effect.Enabled));
        fx.IsEnabled = !track.Locked;
        ToolTip.SetTip(fx, "Efectos de pista");
        fx.Click += async (_, _) => await ShowTrackEffectsAsync(id);
        Grid.SetColumn(title, 0); Grid.SetColumn(enable, 1);
        Grid.SetColumn(locked, solo == null ? 3 : 4);
        grid.Children.Add(title);
        if (!_compactTrackHeader)
        {
            grid.Children.Add(enable);
            if (solo != null) { Grid.SetColumn(solo, 2); grid.Children.Add(solo); }
            Grid.SetColumn(fx, solo == null ? 2 : 3);
            grid.Children.Add(fx);
            grid.Children.Add(locked);
        }
        enable.Click += (_, _) => _controller?.UndoRedo.Commit(p => p.ActiveSequence!.Tracks.First(t => t.Id == id).Enabled = !p.ActiveSequence!.Tracks.First(t => t.Id == id).Enabled, "Activar pista");
        if (solo != null) solo.Click += (_, _) => _controller?.UndoRedo.Commit(p => p.ActiveSequence!.Tracks.First(t => t.Id == id).Solo = !p.ActiveSequence!.Tracks.First(t => t.Id == id).Solo, "Foco de audio");
        locked.Click += (_, _) => _controller?.UndoRedo.Commit(p => p.ActiveSequence!.Tracks.First(t => t.Id == id).Locked = !p.ActiveSequence!.Tracks.First(t => t.Id == id).Locked, "Bloquear pista");
        var height = TimelineCanvas.TrackHeight(track);
        var y = TimelineCanvas.TrackTop(id);
        var viewport = TimelineCanvas.TrackViewport(track.Kind);
        if (y + height <= viewport.Top || y >= viewport.Bottom) return;
        var border = new Border { Width = TrackHeaderWidth, Height = height, BorderThickness = track.Kind == "video" ? new Thickness(0, 1, 0, 0) : new Thickness(0, 0, 0, 1), BorderBrush = new SolidColorBrush(Color.Parse("#343434")), Padding = new Thickness(6, 2), Child = grid, Background = new SolidColorBrush(Color.Parse("#0C0C0C")) };
        border.Clip = new RectangleGeometry(new Rect(0, Math.Max(0, viewport.Top - y), TrackHeaderWidth, Math.Min(height, viewport.Bottom - y) - Math.Max(0, viewport.Top - y)));
        MenuItem TrackEffectsMenu()
        {
            var item = Menu("Efectos de pista…", async () => await ShowTrackEffectsAsync(id));
            item.IsEnabled = !track.Locked;
            return item;
        }
        ContextMenu TrackMenu() => new() { ItemsSource = new object[] {
            Menu("Seleccionar medios de esta pista", () => TimelineCanvas.Selection.SelectClips(_controller!.CurrentProject.ActiveSequence!.Clips.Where(c => c.TrackId == id).Select(c => c.Id))),
            Menu("Renombrar pista…", async () => await RenameTrackAsync(id)),
            TrackEffectsMenu(),
            new Separator(),
            Menu(track.Kind == "audio" ? (track.Solo ? "Desactivar foco" : "Activar foco") :
                (track.Enabled ? "Ocultar pista" : "Mostrar pista"), () => _controller?.UndoRedo.Commit(p =>
                { var lane = p.ActiveSequence!.Tracks.First(t => t.Id == id); if (lane.Kind == "audio") lane.Solo = !lane.Solo; else lane.Enabled = !lane.Enabled; }, "Cambiar foco de pista")),
            Menu(track.Locked ? "Desbloquear pista" : "Bloquear pista", () => _controller?.UndoRedo.Commit(p =>
                p.ActiveSequence!.Tracks.First(t => t.Id == id).Locked = !track.Locked, "Bloquear pista")),
            Submenu("Visualización", new[] {
                Menu(track.Compact ? "Expandir pista" : "Minimizar pista", () => SetTrackCompact(id, !track.Compact)),
                Menu(track.ShowNames ? "Ocultar nombres de medios" : "Mostrar nombres de medios", () => SetTrackNames(id, !track.ShowNames)) }),
            Submenu("Mover medios", new[] {
                Menu("A la pista superior", () => MoveTrackContents(id, -1)),
                Menu("A la pista inferior", () => MoveTrackContents(id, 1)) }),
            new Separator(),
            Menu("Eliminar pista…", async () => await DeleteTrackAsync(id))
        }};
        border.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(border).Properties.IsRightButtonPressed) return;
            TrackMenu().Open(border);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        Canvas.SetTop(border, y);
        TrackHeaderPanel.Children.Add(border);
    }

    private void AddSubtitleHeader(Track track)
    {
        var height = TimelineCanvas.TrackHeight(track);
        var y = TimelineCanvas.TrackTop(track.Id);
        var viewport = TimelineCanvas.TrackViewport(track.Kind);
        var top = Math.Max(0, viewport.Top - y);
        var bottom = Math.Min(height, viewport.Bottom - y);
        if (bottom <= top) return;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(_compactTrackHeader ? "*" : "*,Auto,Auto") };
        var add = TrackHeaderButton(TrackHeaderLabel("+"));
        ToolTip.SetTip(add, "Añadir subtítulo en el cabezal");
        add.Click += (_, _) => CreateSubtitle();
        if (!_compactTrackHeader)
        {
            grid.Children.Add(new TextBlock { Text = SubtitleTrack.Name, FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(add, 1);
            var visibility = TrackHeaderButton(TrackStateIcon(track.Kind, track.Enabled));
            ToolTip.SetTip(visibility, track.Enabled ? "Ocultar subtítulos" : "Mostrar subtítulos");
            visibility.Click += (_, _) => SetSubtitleVisibility(track.Id, !track.Enabled);
            Grid.SetColumn(visibility, 2);
            grid.Children.Add(visibility);
        }
        grid.Children.Add(add);

        var border = new Border { Width = TrackHeaderWidth, Height = height, Padding = new Thickness(6, 2),
            Background = new SolidColorBrush(Color.Parse("#292133")),
            BorderBrush = new SolidColorBrush(Color.Parse("#57445F")), BorderThickness = new Thickness(0, 1, 0, 0),
            Child = grid, Clip = new RectangleGeometry(new Rect(0, top, TrackHeaderWidth, bottom - top)) };
        border.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(border).Properties.IsRightButtonPressed) return;
            ShowMenu(border, SubtitleMenuItems(track));
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        Canvas.SetTop(border, y);
        TrackHeaderPanel.Children.Add(border);
    }

    private void SetSubtitleVisibility(string trackId, bool visible) =>
        CommitTimelineEdit(p => p.ActiveSequence!.Tracks.First(t => t.Id == trackId).Enabled = visible,
            visible ? "Mostrar subtítulos" : "Ocultar subtítulos");

    private List<MenuItem> SubtitleMenuItems(Track track, double? at = null) =>
        [Menu("Añadir subtítulo", () => CreateSubtitle(at)),
         Menu("Animación MGT…", ShowSubtitleAnimationEditor),
         Menu(track.Enabled ? "Ocultar subtítulos" : "Mostrar subtítulos", () => SetSubtitleVisibility(track.Id, !track.Enabled)),
         Menu("Replegar pista", ToggleSubtitleLane)];

    private Window? _subtitleAnimationWindow;

    private void ShowSubtitleAnimationEditor()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        if (_subtitleAnimationWindow is { } existing)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        var window = new Window { Title = "Animación de la pista de subtítulos", Width = 390, Height = 620,
            MinWidth = 340, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer { Content = new SubtitleAnimationEditor { Margin = new Thickness(16) } } };
        _subtitleAnimationWindow = window;
        window.Closed += (_, _) => _subtitleAnimationWindow = null;
        window.Show(owner);
    }

    private static Button TrackHeaderButton(Control content, bool active = false)
    {
        var button = new Button { Content = content, Width = 25, Height = 24,
            Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center };
        if (active) button.Background = new SolidColorBrush(Color.Parse("#9A5208"));
        return button;
    }

    private static TextBlock TrackHeaderLabel(string text) => new()
    {
        Text = text, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White
    };

    private void SetTrackCompact(string id, bool compact) => _controller?.UndoRedo.Commit(p =>
        p.ActiveSequence!.Tracks.First(t => t.Id == id).Compact = compact, compact ? "Minimizar pista" : "Expandir pista");

    private void SetTrackNames(string id, bool visible) => _controller?.UndoRedo.Commit(p =>
        p.ActiveSequence!.Tracks.First(t => t.Id == id).ShowNames = visible, "Mostrar nombres de medios");

    private async Task<string?> AskTrackNameAsync(string current)
    {
        if (TopLevel.GetTopLevel(this) is not Window parent) return null;
        var input = new TextBox { Text = current, Margin = new Thickness(12) };
        var save = new Button { Content = "Guardar", Width = 82 };
        var cancel = new Button { Content = "Cancelar", Width = 82 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(12) };
        row.Children.Add(save); row.Children.Add(cancel);
        var body = new StackPanel(); body.Children.Add(input); body.Children.Add(row);
        var dialog = new Window { Title = "Renombrar pista", Width = 380, Height = 145, Content = body, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        save.Click += (_, _) => dialog.Close(input.Text?.Trim());
        cancel.Click += (_, _) => dialog.Close(null);
        return await dialog.ShowDialog<string?>(parent);
    }

    private async Task RenameTrackAsync(string id)
    {
        var track = _controller?.CurrentProject.ActiveSequence?.Tracks.FirstOrDefault(t => t.Id == id);
        if (track == null) return;
        var name = await AskTrackNameAsync(track.Name);
        if (!string.IsNullOrWhiteSpace(name)) _controller!.UndoRedo.Commit(p => p.ActiveSequence!.Tracks.First(t => t.Id == id).Name = name, "Renombrar pista");
    }

    private void MoveTrackContents(string id, int direction)
    {
        var seq = _controller?.CurrentProject.ActiveSequence;
        var source = seq?.Tracks.FirstOrDefault(t => t.Id == id);
        if (source == null || source.Locked || source.Role == "subtitle") return;
        var targetOrder = source.Order + (source.Kind == "video" ? -direction : direction);
        var target = seq!.Tracks.FirstOrDefault(t => t.Kind == source.Kind && t.Order == targetOrder && !t.Locked && t.Role != "subtitle");
        if (target == null) { _controller!.ReportStatus("No hay pista compatible disponible"); return; }
        var moving = seq.Clips.Where(c => c.TrackId == id).ToArray();
        var existing = seq.Clips.Where(c => c.TrackId == target.Id).ToArray();
        if (moving.Any(a => existing.Any(b => a.TimelineStart.Seconds < b.EndSeconds && b.TimelineStart.Seconds < a.EndSeconds)))
        {
            _controller!.ReportStatus("La pista de destino contiene clips en ese tramo"); return;
        }
        _controller!.UndoRedo.Commit(p => { foreach (var clip in p.ActiveSequence!.Clips.Where(c => c.TrackId == id)) clip.TrackId = target.Id; }, "Mover recursos entre pistas");
    }

    private async Task DeleteTrackAsync(string id)
    {
        var seq = _controller?.CurrentProject.ActiveSequence;
        var track = seq?.Tracks.FirstOrDefault(t => t.Id == id);
        if (seq == null || track == null) return;
        try { TrackOperations.ValidateDelete(seq, id); }
        catch (Exception error) { _controller?.ReportStatus(error.Message); return; }
        var projectId = _controller!.CurrentProject.Id;
        var count = seq.Clips.Count(c => c.TrackId == id);
        if (count > 0)
        {
            if (TopLevel.GetTopLevel(this) is not Window parent) return;
            var text = new TextBlock { Text = $"La pista contiene {count} clip(s). Se eliminarán con la pista. Puedes deshacerlo con Ctrl+Z.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) };
            var delete = new Button { Content = "Eliminar", Width = 82 };
            var cancel = new Button { Content = "Cancelar", Width = 82 };
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(12) };
            row.Children.Add(delete); row.Children.Add(cancel);
            var body = new StackPanel(); body.Children.Add(text); body.Children.Add(row);
            var dialog = new Window { Title = "Eliminar pista", Width = 430, Height = 160, Content = body, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
            delete.Click += (_, _) => dialog.Close(true);
            cancel.Click += (_, _) => dialog.Close(false);
            if (!await dialog.ShowDialog<bool>(parent)) return;
        }
        if (_controller.CurrentProject.Id != projectId || _controller.CurrentProject.ActiveSequenceId != seq!.Id)
        { _controller.ReportStatus("El montaje cambió. Repite la eliminación en la pista deseada."); return; }
        if (!CommitTimelineEdit(p => TrackOperations.Delete(p.ActiveSequence!, id), "Eliminar pista")) return;
        TimelineCanvas.Selection.Clear();
    }

    private void ShowTimelineContext(Clip? clip, Track? track, double at, SelectedGap? gap)
    {
        if (_controller == null) return;
        var items = new List<MenuItem>();
        if (track == null)
        {
            items.Add(Menu("Añadir pista de vídeo", () => AddTrack("video")));
            items.Add(Menu("Añadir pista de audio", () => AddTrack("audio")));
            ShowMenu(TimelineCanvas, items);
            return;
        }
        if (track.Role == "subtitle")
        {
            items = SubtitleMenuItems(track, at);
            if (clip != null)
            {
                var subtitleId = clip.Id;
                if (TranscriptOperations.AudioSourceId(_controller.CurrentProject.ActiveSequence!, clip) != null)
                    items.Add(Menu("Desvincular del audio", () => DetachSubtitle(subtitleId)));
                items.Add(Menu("Eliminar subtítulo", () => CommitTimelineEdit(
                    p => p.ActiveSequence!.Clips.RemoveAll(c => c.Id == subtitleId), "Eliminar subtítulo")));
            }
            ShowMenu(TimelineCanvas, items);
            return;
        }
        if (clip != null)
        {
            var id = clip.Id;
            var editable = CanEditSelectedClips();
            if (track.Kind == "audio" && !track.Locked)
                items.Add(Submenu("Fundidos de audio", AudioTransitionMenuChoices(clip, track, at)));
            if (FindMediaCut(track, at, "video") is { } cut)
                items.Add(Submenu("Añadir transición", TransitionMenuChoices(cut.Left.Id, cut.Right.Id)));
            if (editable && clip.NestedContent != null)
            {
                items.Add(Menu("Desanidar este medio", () => UnnestSelected(id)));
            }
            if (editable && SelectedIds().Length > 0 &&
                _controller.CurrentProject.ActiveSequence!.Clips.Any(c => SelectedIds().Contains(c.Id) &&
                    _controller.CurrentProject.ActiveSequence.Tracks.Any(t => t.Id == c.TrackId && t.Kind == "video")))
                items.Add(Menu("Anidar medios seleccionados", NestSelection));
            if (editable) items.Add(Submenu("Color del clip", new[] { Menu("Predeterminado", () => SetClipLabel(id, null)) }
                .Concat(MediaLabelPalette.Options.Select(option => Menu(option.Name, () => SetClipLabel(id, option.Id))))));
            if (editable && PlaceholderService.IsPlaceholder(_controller.CurrentProject.Assets.FirstOrDefault(a => a.Id == clip.AssetId)))
            {
                items.Add(Submenu("Definir contenido", new[] {
                    Menu("Medio del proyecto…", async () => await FillPlaceholderFromLibraryAsync(id)),
                    Menu("Archivo local…", async () => await FillPlaceholderFromFileAsync(id)),
                    Menu("Enlace web…", async () => await FillPlaceholderFromWebAsync(id))
                }));
            }
            var asset = _controller.CurrentProject.Assets.FirstOrDefault(a => a.Id == clip.AssetId);
            var stream = asset?.Streams.FirstOrDefault(s => s.Id == clip.StreamId);
            if (editable && FindSilenceAudio(_controller.CurrentProject.ActiveSequence!, clip) != null)
                items.Add(Menu("Eliminar silencios…", async () => await ShowSilenceDialogAsync(id)));
            if (editable && stream?.Kind is ("video" or "audio") && SelectedIds().Length == 1)
                items.Add(new MenuItem
                {
                    Header = "Velocidad",
                    ItemsSource = new[]
                    {
                        Menu("Velocidad/Duración…", async () => await ChangeClipSpeedAsync(id)),
                        Menu("Adaptar velocidad al siguiente clip", () => FitSpeedToNextClip(id))
                    }
                });
            items.Add(Menu("Copiar", CopySelection));
            items.Add(Menu("Copiar atributos", () => CopyAttributes(id)));
            if (editable && _copiedAttributes != null) items.Add(Menu("Pegar atributos", PasteAttributes));
            if (editable)
            {
                items.Add(Menu("Restablecer atributos", ResetAttributes));
                items.Add(Menu("Cortar", () => { CopySelection(); DeleteSelection(); }));
                var destructive = new List<MenuItem> { Menu("Eliminar", DeleteSelection) };
                if (SelectedIds().Length == 1)
                {
                    destructive.Add(Menu("Eliminar y cerrar espacio", () => RippleDeleteClip(id)));
                    var playhead = TimelineCanvas.PlayheadSeconds;
                    if (playhead > clip.TimelineStart.Seconds + 1.0 / 60 && playhead < clip.EndSeconds - 1.0 / 60)
                    {
                        destructive.Add(Menu("Eliminar desde el inicio hasta el lector", () => RippleTrimToPlayhead(id, true)));
                        destructive.Add(Menu("Eliminar desde el lector hasta el final", () => RippleTrimToPlayhead(id, false)));
                    }
                }
                items.Add(Submenu("Eliminar", destructive));
            }
            if (_copiedClips.Count > 0 && _copiedProjectId == _controller.CurrentProject.Id) items.Add(Menu("Pegar en el lector", PasteSelection));
            if (editable && _controller.CurrentProject.Sequences.Count > 1)
                items.Add(Submenu("Mover a montaje", _controller.CurrentProject.Sequences
                    .Where(s => s.Id != _controller.CurrentProject.ActiveSequenceId)
                    .Select(s => Menu(s.Name, () => MoveSelectionToSequence(s.Id)))));
            var selected = SelectedIds();
            if (editable && selected.Length > 1)
            {
                items.Add(Menu("Vincular selección", LinkSelection));
                items.Add(Menu("Agrupar selección", GroupSelection));
            }
            if (editable && _controller.CurrentProject.ActiveSequence!.LinkGroups.Any(g => g.ClipIds.Any(selected.Contains))) items.Add(Menu("Separar vínculos", () => UnlinkSelection(id)));
            if (editable && _controller.CurrentProject.ActiveSequence is { } sequence &&
                TranscriptOperations.AudioSourceId(sequence, clip) is { } audioId &&
                sequence.Clips.Any(item => item.Id == audioId))
                items.Add(Menu("Desvincular del audio", () => DetachSubtitle(id)));
            if (editable && _controller.CurrentProject.ActiveSequence!.EditGroups.Any(g => g.ClipIds.Any(selected.Contains))) items.Add(Menu("Desagrupar selección", UngroupSelection));
            if (asset != null)
            {
                var info = new List<MenuItem> { Menu("Información general", () => ShowClipInfo(id)) };
                if (asset.Source.Kind == "web")
                {
                    if (editable)
                    {
                        info.Add(Menu("Descargar vista ligera", async () =>
                            await WebMediaService.ResolveAsync(_controller, asset.Id, redownload: true, quality: WebDownloadQuality.Light)));
                        info.Add(Menu("Descargar vista completa", async () =>
                            await WebMediaService.ResolveAsync(_controller, asset.Id, redownload: true, quality: WebDownloadQuality.Complete)));
                    }
                    info.Add(Menu("Copiar URL de origen", async () =>
                        await TopLevel.GetTopLevel(this)!.Clipboard!.SetTextAsync(asset.Source.Uri)));
                }
                items.Add(Submenu("Información del medio", info));
            }
        }
        else if (gap != null)
        {
            if (_copiedClips.Count > 0 && _copiedProjectId == _controller.CurrentProject.Id)
                items.Add(Menu("Pegar en el lector", PasteSelection));
            if (!track.Locked) items.Add(Menu("Eliminar hueco", () =>
            {
                if (CommitTimelineEdit(p => CollisionEngine.RippleDeleteGap(p.ActiveSequence!, gap.TrackId,
                    gap.StartSeconds, gap.EndSeconds), "Eliminar hueco")) TimelineCanvas.Selection.Clear();
            }));
            items.Add(Menu("Convertir en espacio reservado", () => CreatePlaceholderInGap(gap.TrackId, gap.StartSeconds, gap.EndSeconds)));
            items.Add(Menu("Insertar medio aquí…", async () => await InsertAssetAtAsync(track.Id, gap.StartSeconds)));
            items.Add(Menu("Importar enlace en este hueco…", async () => await ImportWebAtAsync(track.Id, gap.StartSeconds)));
        }
        else
        {
            if (_copiedClips.Count > 0 && _copiedProjectId == _controller.CurrentProject.Id)
                items.Add(Menu("Pegar en el lector", PasteSelection));
            items.Add(Menu("Insertar medio aquí…", async () => await InsertAssetAtAsync(track.Id, at)));
            items.Add(Menu("Importar enlace aquí…", async () => await ImportWebAtAsync(track.Id, at)));
            items.Add(Menu("Crear espacio reservado", () => CreatePlaceholder(track.Id, at, 5)));
        }
        ShowMenu(TimelineCanvas, items);
    }

    private string[] SelectedIds() => TimelineCanvas.Selection.SelectedClipIds.ToArray();

    private void FitSpeedToNextClip(string clipId)
    {
        if (_controller == null) return;
        try
        {
            var speed = 1d;
            _controller.UndoRedo.Commit(project => speed = CollisionEngine.FitSpeedToNextClip(project.ActiveSequence!, clipId),
                "Adaptar velocidad al siguiente clip");
            _controller.ReportStatus($"Velocidad ajustada al {speed * 100:0.##} %");
        }
        catch (InvalidOperationException error) { _controller.ReportStatus(error.Message); }
    }

    private async Task ChangeClipSpeedAsync(string clipId)
    {
        if (_controller == null || TopLevel.GetTopLevel(this) is not Window parent) return;
        var clip = _controller.CurrentProject.ActiveSequence?.Clips.FirstOrDefault(c => c.Id == clipId);
        if (clip == null) return;
        var mode = new ComboBox { ItemsSource = new[] { "Velocidad (%)", "Duración (segundos)" }, SelectedIndex = 0, Margin = new Thickness(12, 8, 12, 0) };
        var input = new TextBox { Text = (clip.Speed.Seconds * 100).ToString("0.##", CultureInfo.CurrentCulture), Margin = new Thickness(12, 4) };
        mode.SelectionChanged += (_, _) => input.Text = mode.SelectedIndex == 0
            ? (clip.Speed.Seconds * 100).ToString("0.##", CultureInfo.CurrentCulture)
            : clip.Duration.Seconds.ToString("0.###", CultureInfo.CurrentCulture);
        var hint = new TextBlock { Text = "El clip se detendrá al tocar el siguiente medio.", Margin = new Thickness(12, 2) };
        var apply = new Button { Content = "Aplicar", Width = 82 };
        var cancel = new Button { Content = "Cancelar", Width = 82 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(12) };
        row.Children.Add(apply); row.Children.Add(cancel);
        var body = new StackPanel(); body.Children.Add(mode); body.Children.Add(input); body.Children.Add(hint); body.Children.Add(row);
        var dialog = new Window { Title = "Velocidad y duración", Width = 380, Height = 210, Content = body,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        apply.Click += (_, _) => dialog.Close(input.Text);
        cancel.Click += (_, _) => dialog.Close(null);
        var answer = await dialog.ShowDialog<string?>(parent);
        if (answer == null) return;
        if (!double.TryParse(answer, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) &&
            !double.TryParse(answer, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        { _controller.ReportStatus("Introduce un número válido"); return; }
        var ratio = mode.SelectedIndex == 0 ? value / 100 : clip.Speed.Seconds * clip.Duration.Seconds / value;
        try
        {
            var limited = false;
            _controller.UndoRedo.Commit(p => limited = CollisionEngine.SetLinkedClipSpeed(p.ActiveSequence!, clipId, ratio), "Cambiar velocidad/duración");
            if (limited) _controller.ReportStatus("Duración limitada por el siguiente medio");
        }
        catch (Exception ex) { _controller.ReportStatus("No se pudo cambiar la velocidad: " + ex.Message); }
    }

    private void ShowClipInfo(string clipId)
    {
        if (_controller == null || TopLevel.GetTopLevel(this) is not Window parent) return;
        var clip = _controller.CurrentProject.ActiveSequence?.Clips.FirstOrDefault(c => c.Id == clipId);
        var asset = _controller.CurrentProject.Assets.FirstOrDefault(a => a.Id == clip?.AssetId);
        if (clip == null || asset == null) return;
        var stream = asset.Streams.FirstOrDefault(s => s.Id == clip.StreamId);
        var info = $"Nombre: {asset.Name}\nTipo: {stream?.Kind ?? "Desconocido"}\nInicio: {FormatTimecode(clip.TimelineStart.Seconds)}\n" +
            $"Duración: {clip.Duration.Seconds:0.###} s\nVelocidad: {clip.Speed.Seconds * 100:0.##} %\n" +
            $"Origen: {asset.Source.Uri}\nEstado: {(asset.Source.Kind == "web" && WebMediaService.IsPending(asset) ? WebMediaService.GetState(asset.Id)?.Message ?? "Pendiente" : "Disponible")}";
        var window = new Window { Title = "Información del medio", Width = 550, Height = 245,
            Content = new TextBlock { Text = info, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16) },
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        _ = window.ShowDialog(parent);
    }

    private bool CanEditSelectedClips()
    {
        var seq = _controller?.CurrentProject.ActiveSequence;
        if (seq == null) return false;
        var selected = SelectedIds().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return !seq.Clips.Any(c => selected.Contains(c.Id) && seq.Tracks.Any(t => t.Id == c.TrackId && t.Locked));
    }

    private void SetClipLabel(string clipId, string? labelId)
    {
        if (_controller == null || !CanEditSelectedClips()) return;
        var selected = SelectedIds();
        var ids = selected.Contains(clipId) ? selected : new[] { clipId };
        _controller.UndoRedo.Commit(p =>
        {
            foreach (var clip in p.ActiveSequence!.Clips.Where(c => ids.Contains(c.Id))) clip.LabelId = labelId;
        }, "Etiquetar clip");
    }

    private void RemoveSelectedPlaceholder(string clipId)
    {
        if (_controller == null) return;
        var seq = _controller.CurrentProject.ActiveSequence;
        var clip = seq?.Clips.FirstOrDefault(c => c.Id == clipId);
        if (clip == null || seq!.Tracks.Any(t => t.Id == clip.TrackId && t.Locked) ||
            !PlaceholderService.IsPlaceholder(_controller.CurrentProject.Assets.FirstOrDefault(a => a.Id == clip.AssetId))) return;
        _controller.UndoRedo.Commit(p => {
            var s = p.ActiveSequence!;
            s.Clips.RemoveAll(c => c.Id == clipId);
            foreach (var group in s.LinkGroups) group.ClipIds.Remove(clipId);
            s.LinkGroups.RemoveAll(g => g.ClipIds.Count < 2);
            foreach (var group in s.EditGroups) group.ClipIds.Remove(clipId);
            s.EditGroups.RemoveAll(g => g.ClipIds.Count < 2);
        }, "Quitar espacio reservado");
        TimelineCanvas.Selection.Clear();
        TimelineCanvas.InvalidateVisual();
    }

    private void DeleteSelection()
    {
        var ids = SelectedIds().ToHashSet();
        if (ids.Count == 0 || _controller == null) return;
        var seq = _controller.CurrentProject.ActiveSequence!;
        if (seq.Tracks.Any(t => t.Locked && seq.Clips.Any(c => c.TrackId == t.Id && ids.Contains(c.Id)))) { _controller.ReportStatus("Hay clips seleccionados en una pista bloqueada"); return; }
        if (!CommitTimelineEdit(p => {
            p.ActiveSequence!.Clips.RemoveAll(c => ids.Contains(c.Id));
            foreach (var group in p.ActiveSequence.LinkGroups) group.ClipIds.RemoveAll(ids.Contains);
            p.ActiveSequence.LinkGroups.RemoveAll(g => g.ClipIds.Count < 2);
            foreach (var group in p.ActiveSequence.EditGroups) group.ClipIds.RemoveAll(ids.Contains);
            p.ActiveSequence.EditGroups.RemoveAll(g => g.ClipIds.Count < 2);
        }, "Eliminar clips")) return;
        TimelineCanvas.Selection.Clear();
    }

    private void DetachSubtitle(string clipId)
    {
        if (CommitTimelineEdit(project =>
        {
            var clip = project.ActiveSequence!.Clips.FirstOrDefault(item => item.Id == clipId);
            if (clip != null)
            {
                clip.TranscriptId = null;
                clip.SubtitleAudioClipId = null;
            }
        }, "Desvincular subtítulo del audio"))
            _controller?.ReportStatus("Subtítulo desvinculado del audio");
    }

    private void RippleDeleteClip(string id)
    {
        if (_controller == null) return;
        try { _controller.UndoRedo.Commit(p => CollisionEngine.RippleDeleteClip(p.ActiveSequence!, id), "Eliminar y cerrar espacio"); }
        catch (InvalidOperationException ex) { _controller.ReportStatus(ex.Message); return; }
        TimelineCanvas.Selection.Clear();
    }

    private void RippleTrimToPlayhead(string id, bool fromStart)
    {
        if (_controller == null) return;
        var playhead = TimelineCanvas.PlayheadSeconds;
        _controller.UndoRedo.Commit(p => CollisionEngine.RippleTrimClip(p.ActiveSequence!, id, fromStart, playhead),
            fromStart ? "Eliminar hasta el lector" : "Eliminar desde el lector al final");
        TimelineCanvas.InvalidateVisual();
    }

    private void DuplicateSelection(double at)
    {
        if (_controller == null || !CanEditSelectedClips()) return;
        var ids = SelectedIds();
        if (ids.Length == 0) return;
        var seq = _controller.CurrentProject.ActiveSequence!;
        var start = seq.Clips.Where(c => ids.Contains(c.Id)).Min(c => c.TimelineStart.Seconds);
        var end = seq.Clips.Where(c => ids.Contains(c.Id)).Max(c => c.EndSeconds);
        var delta = Math.Max(end - start, at - start);
        _controller.UndoRedo.Commit(p => {
            var sequence = p.ActiveSequence!;
            var copies = CollisionEngine.DuplicateClips(sequence, ids, delta);
            CollisionEngine.Overwrite(sequence, copies.Select(c => c.Id));
        }, "Duplicar clips");
    }

    private void CopySelection()
    {
        if (_controller == null) return;
        var ids = SelectedIds();
        var seq = _controller.CurrentProject.ActiveSequence!;
        _copiedClips = seq.Clips.Where(c => ids.Contains(c.Id)).Select(CloneClip).ToList();
        _copiedLinks = seq.LinkGroups.Select(g => g.ClipIds.Where(ids.Contains).ToArray()).Where(g => g.Length > 1).ToList();
        _copiedEditGroups = seq.EditGroups.Select(g => g.ClipIds.Where(ids.Contains).ToArray()).Where(g => g.Length > 1).ToList();
        _copiedProjectId = _controller.CurrentProject.Id;
        _copiedClipboardRevision = GetClipboardSequenceNumber();
        _copiedSourceSnapshot = JsonSerializer.Deserialize<Sequence>(JsonSerializer.Serialize(seq));
        _copiedSourceIds = ids;
        _controller.ReportStatus(_copiedClips.Count + " clip(s) copiados");
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    private static Clip CloneClip(Clip source) => JsonSerializer.Deserialize<Clip>(JsonSerializer.Serialize(source))!;

    private void CopyAttributes(string clipId)
    {
        var clip = _controller?.CurrentProject.ActiveSequence?.Clips.FirstOrDefault(c => c.Id == clipId);
        if (clip != null) _copiedAttributes = CloneClip(clip);
    }

    private void PasteAttributes()
    {
        if (_controller == null || _copiedAttributes == null || !CanEditSelectedClips()) return;
        var ids = SelectedIds().ToHashSet();
        var copied = _copiedAttributes;
        _controller.UndoRedo.Commit(p => {
            foreach (var clip in p.ActiveSequence!.Clips.Where(c => ids.Contains(c.Id)))
            {
                clip.Enabled = copied.Enabled;
                clip.Speed = copied.Speed;
                clip.GainDb = copied.GainDb;
                clip.Balance = copied.Balance;
                clip.Visual = JsonSerializer.Deserialize<ClipVisualState>(JsonSerializer.Serialize(copied.Visual))!;
                clip.Effects = JsonSerializer.Deserialize<List<EffectInstance>>(JsonSerializer.Serialize(copied.Effects))!;
                clip.KeyframeTracks = JsonSerializer.Deserialize<List<KeyframeTrack>>(JsonSerializer.Serialize(copied.KeyframeTracks))!;
                clip.SamMaskTarget = copied.SamMaskTarget;
                clip.ShapeMask = copied.ShapeMask == null ? null :
                    JsonSerializer.Deserialize<ShapeMask>(JsonSerializer.Serialize(copied.ShapeMask));
                clip.SamMaskFeatherPixels = copied.SamMaskFeatherPixels;
                clip.SamMaskExpandPixels = copied.SamMaskExpandPixels;
                clip.SamMaskInverted = copied.SamMaskInverted;
                clip.MaskLayersInitialized = copied.MaskLayersInitialized;
                clip.MaskLayers = JsonSerializer.Deserialize<List<ClipMaskLayer>>(
                    JsonSerializer.Serialize(copied.MaskLayers))!;
                var effectIds = new Dictionary<string, string>();
                foreach (var effect in clip.Effects)
                {
                    var oldId = effect.Id;
                    effect.Id = Guid.NewGuid().ToString("N");
                    effectIds[oldId] = effect.Id;
                }
                KeyframeAnimation.RemapEffectInstances(clip, effectIds);
                foreach (var mask in clip.MaskLayers)
                {
                    mask.Id = Guid.NewGuid().ToString("N");
                    if (mask.Target.StartsWith(SamMaskTarget.EffectPrefix, StringComparison.Ordinal) &&
                        effectIds.TryGetValue(mask.Target[SamMaskTarget.EffectPrefix.Length..],
                            out var remapped))
                        mask.Target = SamMaskTarget.EffectPrefix + remapped;
                }
            }
        }, "Pegar atributos de clips");
    }

    private void ResetAttributes()
    {
        if (_controller == null || !CanEditSelectedClips()) return;
        var ids = SelectedIds().ToHashSet();
        _controller.UndoRedo.Commit(p => {
            foreach (var clip in p.ActiveSequence!.Clips.Where(c => ids.Contains(c.Id)))
            {
                clip.Enabled = true;
                clip.Speed = Rational.One;
                clip.GainDb = 0;
                clip.Balance = 0;
                clip.Visual = new ClipVisualState();
                clip.Effects.Clear();
                clip.KeyframeTracks.Clear();
                clip.SamMaskTarget = SamMaskTarget.Opacity;
                clip.ShapeMask = null;
                clip.SamMaskFeatherPixels = 0;
                clip.SamMaskExpandPixels = 0;
                clip.SamMaskInverted = false;
                clip.MaskLayersInitialized = true;
                clip.MaskLayers.Clear();
            }
        }, "Restablecer atributos de clips");
    }

    private void PasteSelection()
    {
        if (_controller == null || _copiedClips.Count == 0 || _copiedProjectId != _controller.CurrentProject.Id) return;
        if (_copiedSourceSnapshot != null && _copiedSourceSnapshot.Id != _controller.CurrentProject.ActiveSequenceId)
        {
            var source = _copiedSourceSnapshot;
            var ids = _copiedSourceIds;
            var atTime = TimelineCanvas.PlayheadSeconds;
            try { _controller.UndoRedo.Commit(p => SequenceTransfer.Copy(source, p.ActiveSequence!, ids, atTime), "Pegar clips entre secuencias"); }
            catch (Exception ex) { _controller.ReportStatus(ex.Message); }
            return;
        }
        var origin = _copiedClips.Min(c => c.TimelineStart.Seconds);
        var at = TimelineCanvas.PlayheadSeconds;
        _controller.UndoRedo.Commit(p => {
            var seq = p.ActiveSequence!;
            var copyMap = new Dictionary<string, string>();
            foreach (var item in _copiedClips)
            {
                var track = seq.Tracks.FirstOrDefault(t => t.Id == item.TrackId);
                if (track == null || track.Locked) continue;
                var copy = CloneClip(item);
                copy.Id = Guid.NewGuid().ToString("N");
                copy.TranscriptId = null;
                copy.SubtitleAudioClipId = null;
                copy.AudioSplitAncestors = null;
                var effectIds = new Dictionary<string, string>();
                foreach (var effect in copy.Effects)
                {
                    var oldId = effect.Id;
                    effect.Id = Guid.NewGuid().ToString("N");
                    effectIds[oldId] = effect.Id;
                }
                KeyframeAnimation.RemapEffectInstances(copy, effectIds);
                copy.TimelineStart = Rational.Time(at + item.TimelineStart.Seconds - origin);
                seq.Clips.Add(copy);
                copyMap[item.Id] = copy.Id;
            }
            foreach (var ids in _copiedLinks)
            {
                var members = ids.Where(copyMap.ContainsKey).Select(id => copyMap[id]).ToList();
                if (members.Count > 1) seq.LinkGroups.Add(new LinkGroup { ClipIds = members });
            }
            foreach (var ids in _copiedEditGroups)
            {
                var members = ids.Where(copyMap.ContainsKey).Select(id => copyMap[id]).ToList();
                if (members.Count > 1) seq.EditGroups.Add(new EditGroup { Number = seq.EditGroups.Count == 0 ? 1 : seq.EditGroups.Max(g => g.Number) + 1, ClipIds = members });
            }
            CollisionEngine.Overwrite(seq, copyMap.Values);
            if (_copiedSourceSnapshot != null)
            {
                VideoTransitionOperations.CopyBetween(_copiedSourceSnapshot, seq, copyMap);
                AudioTransitionOperations.CopyBetween(_copiedSourceSnapshot.AudioTransitions, seq.AudioTransitions, copyMap);
            }
        }, "Pegar clips");
    }

    private void MoveSelectionToSequence(string destinationId)
    {
        if (_controller == null) return;
        var selected = SelectedIds();
        if (selected.Length == 0) return;
        var sourceId = _controller.CurrentProject.ActiveSequenceId;
        try
        {
            _controller.UndoRedo.Commit(p =>
            {
                var source = p.Sequences.Single(s => s.Id == sourceId);
                var destination = p.Sequences.Single(s => s.Id == destinationId);
                if (source.Clips.Where(c => selected.Contains(c.Id))
                    .Any(c => source.Tracks.Any(t => t.Id == c.TrackId && t.Locked)))
                    throw new InvalidOperationException("Hay clips seleccionados en una pista bloqueada.");
                SequenceTransfer.Move(source, destination, selected);
            }, "Mover clips entre secuencias");
            TimelineCanvas.Selection.Clear();
        }
        catch (Exception ex) { _controller.ReportStatus(ex.Message); }
    }

    private void LinkSelection()
    {
        if (!CanEditSelectedClips()) return;
        var ids = SelectedIds();
        if (ids.Length < 2) return;
        _controller?.UndoRedo.Commit(p => p.ActiveSequence!.LinkGroups.Add(new LinkGroup { ClipIds = ids.ToList() }), "Vincular clips");
    }

    private void UnlinkSelection(string contextClipId)
    {
        if (!CanEditSelectedClips()) return;
        var ids = SelectedIds().ToHashSet();
        _controller?.UndoRedo.Commit(p => {
            foreach (var group in p.ActiveSequence!.LinkGroups) group.ClipIds.RemoveAll(ids.Contains);
            p.ActiveSequence.LinkGroups.RemoveAll(g => g.ClipIds.Count < 2);
        }, "Separar vínculos");
        TimelineCanvas.Selection.SelectClip(contextClipId);
        _controller?.SetSelectedClip(contextClipId);
    }

    private void GroupSelection()
    {
        if (!CanEditSelectedClips()) return;
        var ids = SelectedIds();
        if (ids.Length < 2) return;
        _controller?.UndoRedo.Commit(p => EditGroupOperations.Group(p.ActiveSequence!, ids), "Agrupar clips");
        if (_controller?.CurrentProject.ActiveSequence is { } sequence)
            TimelineCanvas.Selection.SelectClips(EditGroupOperations.Expand(sequence, ids));
    }

    private void UngroupSelection()
    {
        if (!CanEditSelectedClips()) return;
        var ids = SelectedIds();
        _controller?.UndoRedo.Commit(p => EditGroupOperations.Ungroup(p.ActiveSequence!, ids), "Desagrupar clips");
    }

    private async Task InsertAssetAtAsync(string trackId, double at)
    {
        if (_controller == null || TopLevel.GetTopLevel(this) is not Window parent) return;
        var seq = _controller.CurrentProject.ActiveSequence!;
        var track = seq.Tracks.First(t => t.Id == trackId);
        var assets = _controller.CurrentProject.Assets.Where(a => a.TextOverlay == null &&
            !PlaceholderService.IsPlaceholder(a) && a.Streams.Any(s => track.Kind == "video"
                ? s.Kind is "video" or "image" : s.Kind == "audio")).ToArray();
        if (assets.Length == 0) { _controller.ReportStatus("No hay medios compatibles en la bandeja"); return; }
        var picker = new ComboBox { ItemsSource = assets.Select(a => a.Name).ToArray(), SelectedIndex = 0, Margin = new Thickness(12) };
        var insert = new Button { Content = "Insertar", Width = 82 };
        var cancel = new Button { Content = "Cancelar", Width = 82 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(12) };
        row.Children.Add(insert); row.Children.Add(cancel);
        var body = new StackPanel(); body.Children.Add(picker); body.Children.Add(row);
        var dialog = new Window { Title = "Insertar medio", Width = 400, Height = 145, Content = body, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        insert.Click += (_, _) => dialog.Close(picker.SelectedIndex);
        cancel.Click += (_, _) => dialog.Close(-1);
        var index = await dialog.ShowDialog<int>(parent);
        if (index < 0 || index >= assets.Length) return;
        var asset = assets[index];
        var stream = asset.Streams.First(s => track.Kind == "video" ? s.Kind is "video" or "image" : s.Kind == "audio");
        try { _controller.UndoRedo.Commit(p => ImportPlacement.PlaceAsset(p, asset.Id, trackId, at), "Insertar medio en línea de tiempo"); }
        catch (Exception ex) { _controller.ReportStatus("No se pudo insertar el medio: " + ex.Message); }
    }
}
