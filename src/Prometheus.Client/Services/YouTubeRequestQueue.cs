using System;
using System.Threading;
using System.Threading.Tasks;

namespace Prometheus.Client.Services;

internal sealed class YouTubeRequestQueue
{
    internal static YouTubeRequestQueue Shared { get; } = new();
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private DateTimeOffset _afterRequest;
    private DateTimeOffset _limitedUntil;
    private int _rateLimits;
    private TaskCompletionSource? _verification;

    internal YouTubeRequestQueue(Func<DateTimeOffset>? now = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? Task.Delay;
    }

    internal void ResumeAfterVerification() => Interlocked.Exchange(ref _verification, null)?.TrySetResult();

    internal async Task<T> RunAsync<T>(Func<Task<T>> request, CancellationToken token,
        Action<TimeSpan, bool>? waiting = null, Action? verificationWait = null)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _verification) is { } verification)
            {
                if (verificationWait == null) throw new WebDownloadFailure(WebFailureKind.Verification);
                verificationWait();
                await verification.Task.WaitAsync(token);
                continue;
            }

            await _serial.WaitAsync(token);
            try
            {
                if (Volatile.Read(ref _verification) != null) continue;
                var due = _limitedUntil > _afterRequest ? _limitedUntil : _afterRequest;
                var wait = due - _now();
                if (wait > TimeSpan.Zero)
                {
                    waiting?.Invoke(wait, _limitedUntil > _now());
                    while (wait > TimeSpan.Zero)
                    {
                        await _delay(wait > TimeSpan.FromHours(1) ? TimeSpan.FromHours(1) : wait, token);
                        wait = due - _now();
                    }
                }
                token.ThrowIfCancellationRequested();
                try
                {
                    var result = await request();
                    _rateLimits = 0;
                    return result;
                }
                catch (WebDownloadFailure error)
                {
                    if (error.Kind == WebFailureKind.RateLimit)
                    {
                        _rateLimits = Math.Min(_rateLimits + 1, 5);
                        var next = _now().AddSeconds(Math.Min(600, 60 * (1 << (_rateLimits - 1))));
                        _limitedUntil = error.RetryAfter is { } server && server > next ? server : next;
                    }
                    if (error.Kind == WebFailureKind.Network && error.RetryAfter is { } retry && retry > _limitedUntil)
                        _limitedUntil = retry;
                    if (error.Kind == WebFailureKind.Verification)
                        Volatile.Write(ref _verification, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
                    throw;
                }
                finally { _afterRequest = _now().AddSeconds(5); }
            }
            finally { _serial.Release(); }
        }
    }
}
