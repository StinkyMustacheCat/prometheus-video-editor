using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Prometheus.Core.Model;

namespace Prometheus.Client.Timeline;

public partial class TimelineCanvasControl
{
    private string? _previewHeightTrackId;
    private double? _previewTrackHeight;
    private double? _previewDividerY;
    private double _videoScroll;
    private double _audioScroll;
    private bool _subtitleLaneExpanded;
    public bool SubtitleLaneExpanded
    {
        get => _subtitleLaneExpanded;
        set
        {
            if (_subtitleLaneExpanded == value) return;
            _subtitleLaneExpanded = value;
            ClampVerticalScroll();
            TrackLayoutChanged?.Invoke();
            InvalidateVisual();
        }
    }
    private string? _inlineVideoNestedClipId;
    private string? _inlineAudioNestedClipId;
    private double _inlineVideoNestedHeight;
    private double _inlineAudioNestedHeight;
    public event Action? TrackLayoutChanged;

    private double BaseTrackHeight(Track track) => track.Role == "subtitle" && !_subtitleLaneExpanded
        ? 0 : _previewHeightTrackId == track.Id && _previewTrackHeight.HasValue
        ? _previewTrackHeight.Value : track.Compact ? 30 : Math.Clamp(track.PresentationHeight, 30, 180);

    public double TrackHeight(Track track) => BaseTrackHeight(track) +
        (_sequence?.Clips.Any(c => c.Id == (track.Kind == "video" ? _inlineVideoNestedClipId : _inlineAudioNestedClipId) && c.TrackId == track.Id) == true
            ? track.Kind == "video" ? _inlineVideoNestedHeight : _inlineAudioNestedHeight : 0);

    public double TrackBaseHeight(Track track) => BaseTrackHeight(track);

    public void SetInlineNested(string kind, string? clipId, double height)
    {
        if (kind == "video") { _inlineVideoNestedClipId = clipId; _inlineVideoNestedHeight = Math.Max(0, height); }
        else { _inlineAudioNestedClipId = clipId; _inlineAudioNestedHeight = Math.Max(0, height); }
        ClampVerticalScroll();
        TrackLayoutChanged?.Invoke();
        InvalidateVisual();
    }

    public double TrackHeight(string trackId) => _sequence?.Tracks.FirstOrDefault(t => t.Id == trackId) is { } track
        ? TrackHeight(track) : 44;

    public double DividerY
    {
        get
        {
            var natural = _sequence?.Tracks.Where(t => t.Kind == "video").Sum(TrackHeight) ?? 88;
            var wanted = _previewDividerY ?? _sequence?.VideoAreaHeight ?? natural + 32;
            return Math.Clamp(wanted, Math.Min(48, Bounds.Height / 2), Math.Max(48, Bounds.Height - 48));
        }
    }

    public double VideoScrollOffset
    {
        get => _videoScroll;
        set { _videoScroll = Math.Clamp(value, 0, VideoScrollMaximum); TrackLayoutChanged?.Invoke(); InvalidateVisual(); }
    }

    public double AudioScrollOffset
    {
        get => _audioScroll;
        set { _audioScroll = Math.Clamp(value, 0, AudioScrollMaximum); TrackLayoutChanged?.Invoke(); InvalidateVisual(); }
    }

    public double VideoScrollMaximum => Math.Max(0, (_sequence?.Tracks.Where(t => t.Kind == "video").Sum(TrackHeight) ?? 0) - DividerY);
    public double AudioScrollMaximum => Math.Max(0, (_sequence?.Tracks.Where(t => t.Kind == "audio").Sum(TrackHeight) ?? 0) - Math.Max(0, Bounds.Height - DividerY - _avDividerHeight));

    public Rect TrackViewport(string kind) => kind == "video"
        ? new Rect(0, 0, Bounds.Width, DividerY)
        : new Rect(0, DividerY + _avDividerHeight, Bounds.Width, Math.Max(0, Bounds.Height - DividerY - _avDividerHeight));

    public double TrackTop(string trackId)
    {
        if (_sequence == null) return -1;
        var video = _sequence.Tracks.Where(t => t.Kind == "video").OrderByDescending(t => t.Order).ToList();
        var audio = _sequence.Tracks.Where(t => t.Kind == "audio").OrderBy(t => t.Order).ToList();
        return GetTrackY(trackId, video, audio);
    }

    public string? NewTrackDropKind(double y)
    {
        if (_sequence == null || y < 0 || y > Bounds.Height || GetTrackAtY(y) != null) return null;
        var video = _sequence.Tracks.Where(t => t.Kind == "video").ToList();
        var audio = _sequence.Tracks.Where(t => t.Kind == "audio").ToList();
        var firstVideoTop = video.Count == 0 ? DividerY : video.Min(t => TrackTop(t.Id));
        var lastAudioBottom = audio.Count == 0 ? DividerY + _avDividerHeight : audio.Max(t => TrackTop(t.Id) + TrackHeight(t));
        if (firstVideoTop >= 18 && y <= firstVideoTop - 8) return "video";
        if (lastAudioBottom <= Bounds.Height - 18 && y >= lastAudioBottom + 8) return "audio";
        return null;
    }

    public double VideoAreaHeightForNewTrack(double y)
    {
        var total = (_sequence?.Tracks.Where(t => t.Kind == "video").Sum(TrackHeight) ?? 0) + 44;
        return Math.Clamp(Math.Max(0, y - 20) + total - _videoScroll,
            Math.Min(48, Bounds.Height / 2), Math.Max(48, Bounds.Height - 48));
    }

    public void PreviewTrackHeight(string trackId, double? height)
    {
        _previewHeightTrackId = height.HasValue ? trackId : null;
        _previewTrackHeight = height.HasValue ? Math.Clamp(height.Value, 30, 180) : null;
        ClampVerticalScroll();
        TrackLayoutChanged?.Invoke();
        InvalidateVisual();
    }

    public void PreviewDivider(double? y)
    {
        _previewDividerY = y;
        ClampVerticalScroll();
        TrackLayoutChanged?.Invoke();
        InvalidateVisual();
    }

    public void ClampVerticalScroll()
    {
        _videoScroll = Math.Clamp(_videoScroll, 0, VideoScrollMaximum);
        _audioScroll = Math.Clamp(_audioScroll, 0, AudioScrollMaximum);
    }
}
