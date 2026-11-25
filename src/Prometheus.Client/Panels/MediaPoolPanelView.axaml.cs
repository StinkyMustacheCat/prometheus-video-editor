using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;

namespace Prometheus.Client.Panels;

public partial class MediaPoolPanelView : UserControl
{
    private ProjectController? _controller;
    public MediaPoolPanelView()
    {
        InitializeComponent();
        InitializeMediaFeatures();
        Loaded += (_, _) => AttachController();
    }

    private void AttachController()
    {
        if (_controller != null || ProjectController.Instance == null) return;
        _controller = ProjectController.Instance;
        _controller.StateChanged += Refresh;
        WebMediaService.StateChanged += _ => Refresh();
        Refresh();
    }

    private async System.Threading.Tasks.Task ImportAsync()
    {
        AttachController();
        if (_controller == null) return;
        var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            Title = "Importar medios o MGT", AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("Medios") {
                Patterns = new[] { "*.mp4", "*.mov", "*.mkv", "*.webm", "*.avi", "*.m4v", "*.mxf", "*.mpg", "*.mpeg", "*.ts", "*.m2ts", "*.mts",
                    "*.flv", "*.wmv", "*.asf", "*.ogv", "*.3gp", "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.wav", "*.mp3", "*.m4a", "*.aac", "*.flac", "*.ogg", "*.mgt.json", "*.mgtproj" }
            }}
        });
        foreach (var file in files)
        {
            try
            {
                var asset = await LocalMediaService.ProbeAsync(file.Path.LocalPath);
                asset.FolderId = _selectedFolder == "root" ? null : _selectedFolder;
                _controller.UndoRedo.Commit(project => project.Assets.Add(asset), "Importar " + asset.Name);
                _controller.ReportStatus("Importado: " + asset.Name);
            }
            catch (Exception ex) { _controller.ReportStatus("No se pudo importar " + file.Name + ": " + ex.Message); }
        }
    }

    private void Refresh()
    {
        RefreshMediaFeatures();
    }

    private void Insert(Asset asset)
    {
        if (_controller == null) return;
        if (asset.Source.Kind == "web" && WebMediaService.IsPending(asset))
        {
            var active = _controller.CurrentProject.ActiveSequence;
            var audioOnly = Uri.TryCreate(asset.Source.Uri, UriKind.Absolute, out var sourceUri) &&
                new[] { ".mp3", ".m4a", ".wav" }.Contains(Path.GetExtension(sourceUri.AbsolutePath).ToLowerInvariant());
            var target = active?.Tracks.Where(t => t.Kind == (audioOnly ? "audio" : "video") && !t.Locked).OrderBy(t => t.Order).FirstOrDefault();
            if (target == null) { _controller.ReportStatus("No hay una pista compatible desbloqueada"); return; }
            _ = WebMediaService.ResolveAsync(_controller, asset.Id, target.Id, active!.DurationSeconds);
            return;
        }
        var seq = _controller.CurrentProject.ActiveSequence;
        if (seq == null) return;
        var video = asset.Streams.FirstOrDefault(s => s.Kind is "video" or "image");
        var audio = asset.Streams.FirstOrDefault(s => s.Kind == "audio");
        var duration = asset.RecommendedDuration ?? Rational.Time(5);
        var start = seq.DurationSeconds;
        if (!seq.Tracks.Any(t => !t.Locked && (video != null && t.Kind == "video" || audio != null && t.Kind == "audio")))
        {
            _controller.ReportStatus("No hay una pista compatible desbloqueada");
            return;
        }
        _controller.UndoRedo.Commit(project => {
            var target = project.ActiveSequence!;
            Clip? videoClip = null;
            Clip? audioClip = null;
            if (video != null)
            {
                var track = target.Tracks.Where(t => t.Kind == "video" && !t.Locked).OrderBy(t => t.Order).FirstOrDefault();
                if (track != null) target.Clips.Add(videoClip = new Clip { Name = asset.Name, AssetId = asset.Id, StreamId = video.Id, TrackId = track.Id,
                    TimelineStart = Rational.Time(start), Duration = duration });
            }
            if (audio != null)
            {
                var track = target.Tracks.Where(t => t.Kind == "audio" && !t.Locked).OrderBy(t => t.Order).FirstOrDefault();
                if (track != null) target.Clips.Add(audioClip = new Clip { Name = asset.Name, AssetId = asset.Id, StreamId = audio.Id, TrackId = track.Id,
                    TimelineStart = Rational.Time(start), Duration = duration });
            }
            if (videoClip != null && audioClip != null)
                target.LinkGroups.Add(new LinkGroup { ClipIds = [videoClip.Id, audioClip.Id] });
        }, "Insertar " + asset.Name);
        _controller.ReportStatus("Añadido al final de la secuencia: " + asset.Name);
    }

}
