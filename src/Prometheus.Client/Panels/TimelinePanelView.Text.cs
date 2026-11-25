using System;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public partial class TimelinePanelView
{
    private void ToggleSubtitleLane()
    {
        if (_controller?.CurrentProject.ActiveSequence == null) return;
        var expanded = !_controller.CurrentProject.ActiveSequence.SubtitleLaneExpanded;
        _controller.UndoRedo.Commit(project =>
        {
            var sequence = project.ActiveSequence!;
            if (expanded) SubtitleTrack.Ensure(sequence);
            sequence.SubtitleLaneExpanded = expanded;
        }, expanded ? "Desplegar pista de subtítulos" : "Replegar pista de subtítulos");
        if (expanded) TimelineCanvas.VideoScrollOffset = TimelineCanvas.VideoScrollMaximum;
    }

    private void CreateSubtitle(double? at = null)
    {
        if (_controller?.CurrentProject.ActiveSequence is not { } current) return;
        var fps = Math.Max(1, current.Fps.Seconds);
        var start = Math.Max(0, Math.Floor((at ?? _transport?.PositionSeconds ?? 0) * fps + 1e-8) / fps);
        string? createdId = null;
        try
        {
            _controller.UndoRedo.Commit(project =>
            {
                createdId = SubtitleTrack.Add(project, "Nuevo subtítulo", start, 3).Id;
                project.ActiveSequence!.SubtitleLaneExpanded = true;
            }, "Crear subtítulo");
            if (createdId == null) return;
            TimelineCanvas.Selection.SelectClip(createdId);
            _controller.SetSelectedClip(createdId);
            _controller.ReportStatus("Subtítulo creado · edita su contenido en Controles de medio");
        }
        catch (Exception ex) { _controller.ReportStatus("Texto: " + ex.Message); }
    }
}
