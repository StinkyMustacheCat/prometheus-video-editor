using System;

namespace Prometheus.Client.Services;

internal sealed class DynamicPreviewBudget
{
    private static readonly TimeSpan WindowLength = TimeSpan.FromSeconds(.65);
    private static readonly TimeSpan RecoveryLength = TimeSpan.FromSeconds(2.6);
    private static readonly TimeSpan SampleGap = TimeSpan.FromSeconds(.6);

    private TimeSpan? _windowStart, _lastSample, _fastSince, _lastRecovery;
    private TimeSpan _recoverAfter;
    private int _samples, _slow, _fast;

    public int Level { get; private set; }

    // The source textures stay open. Only the dimensions of the monitor compositor change.
    public int OutputLongEdge(int canvasEdge) => Level switch
    {
        1 => Math.Max(1, (int)Math.Round(canvasEdge * 2.0 / 3)),
        2 => Math.Max(1, (int)Math.Round(canvasEdge * .5)),
        _ => 0
    };

    public void Reset()
    {
        Level = 0;
        _lastRecovery = null;
        _recoverAfter = TimeSpan.Zero;
        ResetEvidence();
    }

    // Gaps, seeks and source startup should not count as rendering pressure.
    public void ResetEvidence()
    {
        _lastSample = _fastSince = null;
        ClearWindow();
    }

    public void Observe(TimeSpan now, TimeSpan renderTime, TimeSpan compositionTime, double targetFps)
    {
        if (_lastSample is { } last && now - last > SampleGap) ResetEvidence();
        _lastSample = now;
        _windowStart ??= now;
        _samples++;
        var frameBudget = 1 / Math.Max(1, targetFps);
        var composeSeconds = compositionTime.TotalSeconds;
        // A late decoder or CEF frame alone is not a reason to shrink the compositor.
        if (renderTime.TotalSeconds > frameBudget * 1.12 && composeSeconds > frameBudget * .16)
            _slow++;
        var recoveryArea = Level == 2 ? 16.0 / 9 : 2.25;
        if (renderTime.TotalSeconds < frameBudget * .84 || composeSeconds * recoveryArea < frameBudget * .12)
            _fast++;
        if (now - _windowStart.Value < WindowLength || _samples < 5) return;

        if (_slow * 3 >= _samples * 2)
        {
            _fastSince = null;
            if (Level < 2)
            {
                Level++;
                // A recent recovery that immediately fell behind needs a longer wait.
                _recoverAfter = now + (_lastRecovery is { } recovery && now - recovery < RecoveryLength
                    ? RecoveryLength * 2 : RecoveryLength);
                _lastRecovery = null;
            }
        }
        else if (Level > 0 && _fast * 10 >= _samples * 9)
        {
            _fastSince ??= _windowStart;
            if (now >= _recoverAfter && now - _fastSince.Value >= RecoveryLength)
            {
                Level--;
                _lastRecovery = now;
                _fastSince = null;
            }
        }
        else _fastSince = null;

        ClearWindow();
    }

    private void ClearWindow()
    {
        _windowStart = null;
        _samples = _slow = _fast = 0;
    }
}
