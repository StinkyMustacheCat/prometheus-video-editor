using System;
using System.Diagnostics;
using Avalonia.Threading;
using Prometheus.Core.Model;

namespace Prometheus.Client.Services;

/// <summary>The single timeline clock used by the ruler, monitor, and transport controls.</summary>
public sealed class PlaybackTransport
{
    private readonly Func<Project> _project;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16.667) };
    private readonly Stopwatch _clock = new();
    private readonly AudioPreviewService _audio = new();
    private double _playStart;

    public double PositionSeconds { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool LoopEnabled { get; set; }
    public double PreviewSpeed { get; private set; } = 1;
    public StereoLevel MasterLevel => IsPlaying ? _audio.MasterLevel : default;
    public double FrameRate => Math.Max(1, _project().ActiveSequence?.Fps.Seconds ?? 60);
    public double EndSeconds => _project().ActiveSequence?.DurationSeconds ?? 0;

    public event Action<double>? PositionChanged;
    public event Action<bool>? PlayingChanged;
    public event Action<string>? AudioError;

    public PlaybackTransport(Func<Project> project)
    {
        _project = project;
        _timer.Tick += OnTick;
        _audio.Failed += message => AudioError?.Invoke(message);
    }

    public void TogglePlayback()
    {
        if (IsPlaying) { Pause(); return; }
        if (EndSeconds <= 0) return;
        if (PositionSeconds >= EndSeconds)
        {
            if (!LoopEnabled) return;
            PublishPosition(0);
        }
        IsPlaying = true;
        _playStart = PositionSeconds;
        _clock.Restart();
        _timer.Start();
        PlayingChanged?.Invoke(true);
        _audio.PlayAt(_project(), PositionSeconds, PreviewSpeed);
    }

    public void Pause()
    {
        if (!IsPlaying) return;
        _timer.Stop();
        _clock.Stop();
        IsPlaying = false;
        _audio.Stop();
        PlayingChanged?.Invoke(false);
    }

    public void Seek(double seconds)
    {
        var position = Math.Max(0, seconds);
        if (IsPlaying)
        {
            _playStart = position;
            _clock.Restart();
            _audio.PlayAt(_project(), position, PreviewSpeed);
        }
        PublishPosition(position);
    }

    public void StepFrames(int frames)
    {
        Pause();
        var fps = FrameRate;
        Seek(Math.Max(0, Math.Round(PositionSeconds * fps) + frames) / fps);
    }

    public void SetPreviewSpeed(double speed)
    {
        var next = Math.Clamp(Math.Round(speed, 2), 0.5, 3);
        if (Math.Abs(next - PreviewSpeed) < 0.001) return;
        PreviewSpeed = next;
        if (IsPlaying) Seek(PositionSeconds);
    }

    public void GoToStart() { Pause(); Seek(0); }
    public void GoToEnd() { Pause(); Seek(EndSeconds); }

    public void Reset()
    {
        Pause();
        PositionSeconds = 0;
        PositionChanged?.Invoke(0);
    }

    public void RefreshAudio()
    {
        if (IsPlaying) Seek(PositionSeconds);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!IsPlaying) return;
        var next = _audio.IsStarting ? _playStart : _audio.HasEnded ? EndSeconds :
            _audio.PlaybackSeconds ?? _playStart + _clock.Elapsed.TotalSeconds * PreviewSpeed;
        if (next >= EndSeconds)
        {
            if (LoopEnabled && EndSeconds > 0) Seek(0);
            else { PublishPosition(EndSeconds); Pause(); }
            return;
        }
        PublishPosition(next);
    }

    private void PublishPosition(double seconds)
    {
        if (Math.Abs(PositionSeconds - seconds) < 0.000001) return;
        PositionSeconds = seconds;
        PositionChanged?.Invoke(seconds);
    }

}
