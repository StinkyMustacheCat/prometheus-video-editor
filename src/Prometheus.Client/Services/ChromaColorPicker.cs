using System;
using Avalonia.Media;

namespace Prometheus.Client.Services;

public static class ChromaColorPicker
{
    private static Action<Color>? _onPicked;
    public static event Action? StateChanged;
    public static string? ClipId { get; private set; }
    public static bool SampleComposite { get; private set; }
    public static bool IsPending => _onPicked != null;

    public static void Begin(string clipId, Action<Color> onPicked)
    {
        ClipId = clipId;
        SampleComposite = false;
        _onPicked = onPicked;
        StateChanged?.Invoke();
    }

    public static void BeginComposite(Action<Color> onPicked)
    {
        ClipId = null;
        SampleComposite = true;
        _onPicked = onPicked;
        StateChanged?.Invoke();
    }

    public static void Cancel()
    {
        ClipId = null;
        SampleComposite = false;
        _onPicked = null;
        StateChanged?.Invoke();
    }

    public static void Complete(Color color)
    {
        var callback = _onPicked;
        Cancel();
        callback?.Invoke(color);
    }
}
