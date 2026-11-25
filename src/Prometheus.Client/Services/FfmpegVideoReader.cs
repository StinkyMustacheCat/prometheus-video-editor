using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;

namespace Prometheus.Client.Services;

/// <summary>Decodes software video into bounded BGRA frames; the compositor still runs on D3D11.</summary>
internal sealed class FfmpegVideoReader : IDisposable
{
    private readonly string _path;
    private readonly int _width;
    private readonly int _height;
    private readonly double _fps;
    private readonly bool _alpha;
    private readonly bool _timed;
    private readonly string? _decoder;
    private Channel<double>? _timestamps;
    private double? _pendingTime;
    private bool _endOfStream;
    private readonly object _readLock = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _closing;
    private byte[] _frame;
    private byte[]? _nextFrame;
    private Process? _process;
    private double _nextTime;
    private double _frameTime = double.NaN;
    private int _disposed;

    public byte[] Frame => _frame;
    public long FrameVersion { get; private set; }

    public FfmpegVideoReader(string path, int width, int height, double fps, bool alpha,
        bool timed, string? decoder = null)
    {
        _path = path;
        _width = width;
        _height = height;
        _fps = fps > 0 ? fps : 30;
        _alpha = alpha;
        _timed = timed;
        _decoder = decoder;
        _frame = GC.AllocateUninitializedArray<byte>(checked(width * height * 4));
        _closing = _lifetime.Token;
    }

    public Task<bool> ReadPreviewAtAsync(double seconds, CancellationToken cancellation) =>
        Task.Run(() => ReadAt(seconds, cancellation, preview: true), cancellation);

    public bool ReadAt(double seconds, CancellationToken cancellation = default, bool preview = false)
    {
        lock (_readLock)
        {
            cancellation.ThrowIfCancellationRequested();
            if (_closing.IsCancellationRequested) return false;
            seconds = Math.Max(0, seconds);
            if (!_timed && !double.IsNaN(_frameTime) && Math.Abs(_frameTime - seconds) < 0.45 / _fps)
                return true;

            try
            {
                var seek = _timed
                    ? seconds < _frameTime - 0.0001 || seconds > Math.Max(_pendingTime ?? _frameTime, _nextTime) + 1
                    : seconds < _nextTime - 1.5 / _fps || seconds > _nextTime + 0.5;
                if (_process == null || seek) Restart(seconds, preview);

                var process = _process!;
                using var cancelRead = cancellation.UnsafeRegister(static state => Kill((Process)state!), process);
                using var closeRead = _closing.UnsafeRegister(static state => Kill((Process)state!), process);

                if (_timed) ReadTimedFrames(process, seconds, cancellation);
                else
                {
                    while (_nextTime <= seconds + 0.45 / _fps && ReadFrame(process, cancellation))
                    {
                        PublishFrame(_nextTime);
                        _nextTime += 1 / _fps;
                    }
                }
                cancellation.ThrowIfCancellationRequested();
                return !_closing.IsCancellationRequested && !double.IsNaN(_frameTime);
            }
            finally
            {
                if (_closing.IsCancellationRequested || cancellation.IsCancellationRequested) Stop();
            }
        }
    }

    private void ReadTimedFrames(Process process, double seconds, CancellationToken cancellation)
    {
        // The timestamp belongs to the complete raw frame in the other pipe.
        while (true)
        {
            if (!_pendingTime.HasValue)
            {
                if (!ReadFrame(process, cancellation)) break;
                try
                {
                    _pendingTime = _timestamps!.Reader.ReadAsync(cancellation).AsTask().GetAwaiter().GetResult();
                }
                catch (ChannelClosedException) when (_closing.IsCancellationRequested) { return; }
                catch (ChannelClosedException ex)
                {
                    throw new InvalidDataException("FFmpeg no entregó el tiempo del fotograma.", ex);
                }
            }

            var time = double.IsNaN(_pendingTime.Value) ? _nextTime : _pendingTime.Value;
            _pendingTime = time;
            _nextTime = time + 1 / _fps;
            if (!double.IsNaN(_frameTime) && time > seconds + 0.0001) break;
            PublishFrame(time);
            _pendingTime = null;
            if (time >= seconds - 0.0001) break;
        }
    }

    private bool ReadFrame(Process process, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (_endOfStream || _closing.IsCancellationRequested) return false;
        _nextFrame ??= GC.AllocateUninitializedArray<byte>(_frame.Length);
        try { process.StandardOutput.BaseStream.ReadExactly(_nextFrame); }
        catch (EndOfStreamException)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!_closing.IsCancellationRequested && process.WaitForExit(100) && process.ExitCode != 0)
                throw new InvalidDataException("FFmpeg no pudo decodificar el vídeo.");
            _endOfStream = true;
            return false;
        }
        return true;
    }

    private void PublishFrame(double seconds)
    {
        (_frame, _nextFrame) = (_nextFrame!, _frame);
        _frameTime = seconds;
        FrameVersion++;
    }

    private void Restart(double seconds, bool preview)
    {
        Stop();
        var info = new ProcessStartInfo(LocalMediaService.Tool("ffmpeg"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        void Add(params string[] args) { foreach (var arg in args) info.ArgumentList.Add(arg); }
        Add("-hide_banner", "-loglevel", _timed ? "info" : "error", "-nostats", "-noautorotate");
        if (_timed) Add("-copyts", "-start_at_zero", "-noaccurate_seek");
        if (_decoder != null) Add("-c:v", _decoder);
        if (preview) Add("-filter_threads", Math.Clamp(Environment.ProcessorCount, 1, 4).ToString(CultureInfo.InvariantCulture));
        Add("-ss", seconds.ToString("0.########", CultureInfo.InvariantCulture), "-i", _path,
            "-map", "0:V:0", "-an", "-sn", "-dn", "-vf",
            $"scale={_width}:{_height}:flags=fast_bilinear" +
            (_alpha ? ",format=rgba,premultiply=inplace=1,format=bgra" : ",format=bgra") +
            (_timed ? ",settb=1/1000000,showinfo=checksum=0" : ""),
            "-fps_mode", "passthrough", "-f", "rawvideo", "-pix_fmt", "bgra");
        if (preview) Add("-threads:v", "1");
        Add("pipe:1");
        _process = Process.Start(info) ?? throw new InvalidOperationException("No se pudo abrir el vídeo con alfa.");
        _timestamps = _timed ? Channel.CreateUnbounded<double>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }) : null;
        _ = DrainDiagnosticsAsync(_process.StandardError, _timestamps?.Writer);
        _pendingTime = null;
        _endOfStream = false;
        _frameTime = double.NaN;
        _nextTime = seconds;
    }

    private static async Task DrainDiagnosticsAsync(StreamReader errors, ChannelWriter<double>? timestamps)
    {
        try
        {
            while (await errors.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (timestamps != null && TryReadFrameTimestamp(line, out var time))
                    timestamps.TryWrite(time);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
        finally { timestamps?.TryComplete(); }
    }

    internal static bool TryReadFrameTimestamp(string line, out double seconds)
    {
        seconds = double.NaN;
        if (!line.StartsWith("[Parsed_showinfo_", StringComparison.Ordinal) ||
            !line.Contains(" n:", StringComparison.Ordinal)) return false;
        const string key = " pts:";
        var start = line.IndexOf(key, StringComparison.Ordinal);
        if (start >= 0)
        {
            var value = line[(start + key.Length)..].TrimStart();
            var end = value.IndexOf(' ');
            if (long.TryParse(end < 0 ? value : value[..end], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var microseconds))
                seconds = microseconds / 1_000_000d;
        }
        // Preserve one timestamp entry per raw frame even if the source has no PTS.
        return true;
    }

    private static void Kill(Process process)
    {
        // FFmpeg is launched directly and owns the redirected pipe.
        try { if (!process.HasExited) process.Kill(); }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception) { }
    }

    private void Stop()
    {
        if (_process == null) return;
        Kill(_process);
        _process.Dispose();
        _process = null;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        lock (_readLock) Stop();
        _lifetime.Dispose();
    }
}
