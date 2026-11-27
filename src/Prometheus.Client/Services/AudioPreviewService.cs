using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Prometheus.Core.Model;

namespace Prometheus.Client.Services;

/// <summary>Streams mixed timeline PCM directly to the Windows audio device.</summary>
public sealed class AudioPreviewService
{
    private readonly object _gate = new();
    private Session? _session;
    public event Action<string>? Failed;

    private sealed class Session
    {
        public required double Start;
        public required double PreviewSpeed;
        public readonly CancellationTokenSource Cancellation = new();
        public Process? Decoder;
        public AudioMixCommand? Command;
        public WaveOutEvent? Output;
        public Task? Startup;
        public Task? Pump;
        public Task<string>? Diagnostics;
        public bool Ready;
        public bool Failed;
        public StereoLevel MasterLevel;
    }

    internal sealed class PcmPipeProvider : IWaveProvider
    {
        private readonly BufferedWaveProvider _buffered;
        private readonly Action<StereoLevel> _report;
        private volatile bool _completed;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public PcmPipeProvider(Action<StereoLevel> report)
        {
            _report = report;
            _buffered = new BufferedWaveProvider(WaveFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(2),
                ReadFully = true
            };
        }
        public int BufferedBytes => _buffered.BufferedBytes;
        public int FreeBytes => _buffered.BufferLength - _buffered.BufferedBytes;
        public void Add(byte[] buffer, int count) => _buffered.AddSamples(buffer, 0, count);
        public void Complete() => _completed = true;
        public int Read(byte[] buffer, int offset, int count)
        {
            var available = _buffered.BufferedBytes;
            if (_completed && available == 0) return 0;
            var read = _buffered.Read(buffer, offset, count);
            double left = 0, right = 0;
            for (var i = offset; i + 7 < offset + Math.Min(read, available); i += 8)
            {
                left = Math.Max(left, Math.Abs(BitConverter.ToSingle(buffer, i)));
                right = Math.Max(right, Math.Abs(BitConverter.ToSingle(buffer, i + 4)));
            }
            _report(new StereoLevel(left, right));
            return read;
        }
    }

    public bool IsStarting
    {
        get { lock (_gate) return _session is { Ready: false, Failed: false }; }
    }

    public bool HasEnded
    {
        get { lock (_gate) return _session is { Ready: true, Output.PlaybackState: PlaybackState.Stopped }; }
    }

    public double? PlaybackSeconds
    {
        get
        {
            lock (_gate)
            {
                var session = _session;
                if (session is not { Ready: true } || session.Output == null) return null;
                try { return session.Start + session.Output.GetPosition() / 384000.0 * session.PreviewSpeed; }
                catch { return null; }
            }
        }
    }

    public StereoLevel MasterLevel
    {
        get { lock (_gate) return _session?.MasterLevel ?? default; }
    }

    public void Stop()
    {
        Session? old;
        lock (_gate) { old = _session; _session = null; }
        if (old == null) return;
        old.Cancellation.Cancel();
        try { if (old.Decoder is { HasExited: false }) old.Decoder.Kill(true); } catch { }
        try { old.Output?.Stop(); } catch { }
        _ = Task.Run(async () => {
            if (old.Startup != null) try { await old.Startup; } catch { }
            if (old.Pump != null) try { await old.Pump; } catch { }
            DisposeSession(old);
        });
    }

    public void PlayAt(Project project, double time, double previewSpeed = 1)
    {
        Stop();
        var plan = AudioMixPlan.Create(project, time);
        if (plan.Inputs.Count == 0 || plan.DurationSeconds <= 0) return;
        var session = new Session { Start = time, PreviewSpeed = previewSpeed };
        lock (_gate)
        {
            _session = session;
            session.Startup = Task.Run(() => Start(session, plan));
        }
    }

    private void Start(Session session, AudioMixPlan plan)
    {
        try
        {
            session.Command = plan.PrepareProcess(session.PreviewSpeed, floatingPoint: true);
            if (session.Cancellation.IsCancellationRequested) return;
            var decoder = Process.Start(session.Command.StartInfo) ?? throw new InvalidOperationException("FFmpeg no abrió el audio");
            session.Decoder = decoder;
            session.Diagnostics = decoder.StandardError.ReadToEndAsync();
            if (session.Cancellation.IsCancellationRequested) return;
            var output = new WaveOutEvent { DesiredLatency = 120, NumberOfBuffers = 3 };
            session.Output = output;
            var provider = new PcmPipeProvider(level => {
                lock (_gate) if (ReferenceEquals(_session, session)) session.MasterLevel = level;
            });
            session.Pump = PumpAsync(session, provider);
            // The device callback must never wait for a short FFmpeg pipe read.
            var warmup = Stopwatch.StartNew();
            while (provider.BufferedBytes < 96_000 && !session.Pump.IsCompleted && warmup.ElapsedMilliseconds < 2000)
            {
                if (session.Cancellation.IsCancellationRequested) return;
                Thread.Sleep(10);
            }
            output.Init(provider);
            lock (_gate)
            {
                if (!ReferenceEquals(_session, session) || session.Cancellation.IsCancellationRequested || session.Failed) return;
                output.Play();
                session.Ready = true;
            }
        }
        catch (Exception ex)
        {
            if (session.Decoder != null) ExternalProcess.Kill(session.Decoder);
            if (session.Pump == null) ReleaseCommand(session);
            ReportFailure(session, ex);
        }
        finally
        {
            if (session.Cancellation.IsCancellationRequested)
            {
                if (session.Decoder != null) ExternalProcess.Kill(session.Decoder);
                if (session.Pump == null) ReleaseCommand(session);
            }
        }
    }

    private async Task PumpAsync(Session session, PcmPipeProvider provider)
    {
        var cancellation = session.Cancellation.Token;
        var decoder = session.Decoder!;
        var stream = decoder.StandardOutput.BaseStream;
        var chunk = new byte[16_384];
        var carry = 0;
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                if (provider.FreeBytes < chunk.Length)
                {
                    await Task.Delay(10, cancellation);
                    continue;
                }
                var read = await stream.ReadAsync(chunk.AsMemory(carry), cancellation);
                if (read == 0) break;
                var aligned = (read + carry) & ~7;
                if (aligned > 0) provider.Add(chunk, aligned);
                carry = read + carry - aligned;
                if (carry > 0) Array.Copy(chunk, aligned, chunk, 0, carry);
            }
            await decoder.WaitForExitAsync(cancellation);
            var diagnostics = session.Diagnostics == null ? "" : await session.Diagnostics;
            if (decoder.ExitCode != 0 && !cancellation.IsCancellationRequested)
                throw new IOException("No se pudo reproducir el audio: " + diagnostics.Trim());
        }
        catch (OperationCanceledException) { }
        catch (IOException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { ReportFailure(session, error); }
        finally
        {
            ExternalProcess.Kill(decoder);
            try { await decoder.WaitForExitAsync(); } catch (InvalidOperationException) { }
            provider.Complete();
            session.Command?.Dispose();
        }
    }

    private void ReportFailure(Session session, Exception error)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_session, session) || session.Cancellation.IsCancellationRequested || session.Failed) return;
            session.Failed = true;
        }
        try { session.Output?.Stop(); } catch { }
        Failed?.Invoke(error.Message);
    }

    private static void DisposeSession(Session session)
    {
        try { session.Output?.Dispose(); } catch { }
        try { if (session.Decoder is { HasExited: false }) session.Decoder.Kill(true); } catch { }
        try { session.Decoder?.Dispose(); } catch { }
        session.Command?.Dispose();
        session.Cancellation.Dispose();
    }

    private static void ReleaseCommand(Session session)
    {
        try { session.Decoder?.WaitForExit(2000); } catch (InvalidOperationException) { }
        session.Command?.Dispose();
    }
}
