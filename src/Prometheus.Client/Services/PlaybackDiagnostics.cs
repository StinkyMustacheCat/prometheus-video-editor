using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Prometheus.Client.Services;

// Opt-in measurements. A completed composition means the monitor's GPU surface
// accepted the frame, not that the operating system displayed it at the next refresh.
internal sealed class PlaybackDiagnostics
{
    private readonly bool _enabled = Environment.GetEnvironmentVariable("PROMETHEUS_PLAYBACK_DIAGNOSTICS") == "1";
    private Run? _current;
    internal Run? Current => _enabled ? Volatile.Read(ref _current) : null;

    internal Run? Start(double position)
    {
        if (!_enabled) return null;
        Current?.Stop();
        var run = new Run(position);
        Volatile.Write(ref _current, run);
        return run;
    }

    internal object Describe() => Current?.Describe() ?? new { enabled = _enabled };

    internal sealed class Run
    {
        private readonly object _gate = new();
        private readonly long _started = Stopwatch.GetTimestamp();
        private readonly List<double> _frameIntervals = new();
        private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
        private double? _audio, _firstFrame, _lastFrame, _stopped;
        private long _lastFrameNumber = long.MinValue;
        private int _frames, _quality, _targetFps;
        private readonly double _start;
        private double _maxGap, _position;

        internal Run(double start)
        {
            _start = _position = start;
        }

        internal void Count(string item)
        {
            lock (_gate)
                if (_stopped == null) _counts[item] = _counts.GetValueOrDefault(item) + 1;
        }

        internal void AudioReady()
        {
            lock (_gate)
                if (_stopped == null) _audio ??= Elapsed();
        }

        internal void Present(double position, double sequenceFps, int quality, int targetFps)
        {
            lock (_gate)
            {
                if (_stopped != null) return;
                var frame = (long)Math.Floor(position * Math.Max(1, sequenceFps) + 0.00001);
                if (frame == _lastFrameNumber) return;
                _lastFrameNumber = frame;
                var now = Elapsed();
                if (_lastFrame is { } last)
                {
                    var gap = now - last;
                    _maxGap = Math.Max(_maxGap, gap);
                    if (_frameIntervals.Count < 3600) _frameIntervals.Add(gap);
                }
                _firstFrame ??= now;
                _lastFrame = now;
                _frames++;
                _quality = quality;
                _targetFps = targetFps;
                _position = position;
            }
        }

        internal void Stop()
        {
            lock (_gate) _stopped ??= Elapsed();
        }

        internal object Describe()
        {
            lock (_gate)
            {
                var gaps = _frameIntervals.OrderBy(x => x).ToArray();
                return new
                {
                    enabled = true, start_seconds = _start, last_composed_seconds = _position,
                    measured_ms = _stopped ?? Elapsed(), first_pcm_ms = _audio, first_composition_ms = _firstFrame,
                    composed_timeline_frames = _frames, quality_long_edge = _quality, requested_fps = _targetFps,
                    composition_fps = _frames > 1 && _lastFrame > _firstFrame
                        ? (_frames - 1) * 1000 / (_lastFrame - _firstFrame) : null,
                    p95_composition_gap_ms = gaps.Length == 0 ? (double?)null : gaps[(int)Math.Ceiling(gaps.Length * .95) - 1],
                    longest_composition_gap_ms = _maxGap, counts = new Dictionary<string, int>(_counts)
                };
            }
        }

        private double Elapsed() => Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
    }
}
