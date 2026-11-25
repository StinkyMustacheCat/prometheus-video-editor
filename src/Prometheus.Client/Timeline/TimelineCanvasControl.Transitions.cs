using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Timeline;

public partial class TimelineCanvasControl
{
    private static readonly IBrush TransitionFill = new SolidColorBrush(Color.Parse("#623F75"));
    private static readonly IBrush TransitionOutline = new SolidColorBrush(Color.Parse("#C898E8"));
    private static readonly IBrush AudioTransitionFill = new SolidColorBrush(Color.Parse("#255C5C"));
    private static readonly IBrush AudioTransitionOutline = new SolidColorBrush(Color.Parse("#83D0CB"));
    private string? _selectedTransitionId;
    private double? _dropTransitionCut;
    private string? _dropTransitionTrack;

    public event Action<string?>? TransitionSelected;
    public event Action<string>? TransitionMenuRequested;

    public string? SelectedTransitionId
    {
        get => _selectedTransitionId;
        set
        {
            if (_selectedTransitionId == value) return;
            _selectedTransitionId = value;
            InvalidateVisual();
        }
    }

    public void SetTransitionDrop(string? trackId, double? cut)
    {
        if (_dropTransitionTrack == trackId && _dropTransitionCut == cut) return;
        _dropTransitionTrack = trackId;
        _dropTransitionCut = cut;
        InvalidateVisual();
    }

    private Rect TransitionRect(string track, double start, double end, bool edge = false)
    {
        var x = TimeToPixels(start);
        var width = Math.Max(0, TimeToPixels(end) - x);
        return new Rect(x - (edge ? 0 : Math.Max(0, 12 - width) / 2), TrackTop(track) + 3,
            Math.Max(12, width), 16);
    }

    private string? TransitionAt(Point point)
    {
        if (_sequence == null || GetTrackAtY(point.Y) is not { } track) return null;
        if (track.Kind == "audio")
            return _sequence.AudioTransitions.Select(t => AudioTransitionOperations.Span(_sequence.Clips, t))
                .Where(span => span != null && span.Clip.TrackId == track.Id && AudioRect(span).Contains(point))
                .MinBy(span => Math.Abs(AudioRect(span!).Center.X - point.X))?.Transition.Id;
        return _sequence.VideoTransitions.Select(t => VideoTransitionOperations.Window(_sequence, t))
            .Where(window => window != null && window.Left.TrackId == track.Id &&
                TransitionRect(track.Id, window.Start, window.End).Contains(point))
            .MinBy(window => Math.Abs(TimeToPixels(window!.Cut) - point.X))?.Transition.Id;
    }

    private Rect AudioRect(AudioTransitionSpan span)
    {
        var rect = TransitionRect(span.Clip.TrackId, span.Start, span.End, span.Right == null);
        if (span.Transition.Kind == AudioTransitionOperations.FadeOut)
            rect = new Rect(TimeToPixels(span.End) - rect.Width, rect.Y, rect.Width, rect.Height);
        return rect;
    }

    private void DrawTransitions(DrawingContext context)
    {
        if (_sequence == null) return;
        var videoViewport = TrackViewport("video");
        using (context.PushClip(videoViewport))
            foreach (var t in _sequence.VideoTransitions)
                if (VideoTransitionOperations.Window(_sequence, t) is { } window)
                    DrawTransitionBadge(context, TransitionRect(window.Left.TrackId, window.Start, window.End),
                        videoViewport, t.Id, VideoTransitionOperations.Name(t),
                        t.Mgt != null ? "M" : t.Kind == VideoTransitionOperations.Dissolve ? "⇄" : "◆", false);

        var audioViewport = TrackViewport("audio");
        using (context.PushClip(audioViewport))
            foreach (var t in _sequence.AudioTransitions)
                if (AudioTransitionOperations.Span(_sequence.Clips, t) is { } span)
                    DrawTransitionBadge(context, AudioRect(span), audioViewport, t.Id, AudioTransitionOperations.Name(t.Kind),
                        t.Kind == AudioTransitionOperations.FadeIn ? "↗" : t.Kind == AudioTransitionOperations.FadeOut ? "↘" : "⇄", true);

        if (_dropTransitionCut is { } cut && _dropTransitionTrack is { } id &&
            _sequence.Tracks.FirstOrDefault(t => t.Id == id) is { } track)
        {
            using var scope = context.PushClip(TrackViewport(track.Kind));
            var x = TimeToPixels(cut);
            var top = TrackTop(id);
            context.DrawLine(new Pen(track.Kind == "audio" ? AudioTransitionOutline : TransitionOutline, 3),
                new Point(x, top + 2), new Point(x, top + 27));
        }
    }

    private void DrawTransitionBadge(DrawingContext context, Rect rect, Rect viewport, string id, string name,
        string symbol, bool audio)
    {
        if (!rect.Intersects(Bounds) || !rect.Intersects(viewport)) return;
        var outline = audio ? AudioTransitionOutline : TransitionOutline;
        var pen = new Pen(SelectedTransitionId == id ? SelectedClipBorderBrush : outline, 1);
        context.DrawRectangle(audio ? AudioTransitionFill : TransitionFill, pen, rect, 3);
        if (rect.Width < 16) return;
        var text = new FormattedText(rect.Width >= 128 ? name : symbol, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface("Inter, Segoe UI, sans-serif"), 10, Brushes.White);
        using var textClip = context.PushClip(rect);
        context.DrawText(text, new Point(rect.X + 4, rect.Y + 1));
    }
}
