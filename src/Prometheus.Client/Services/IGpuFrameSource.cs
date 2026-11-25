using System;

namespace Prometheus.Client.Services;

/// <summary>
/// A borrowed reference to a D3D11 frame. The source owns the handle and texture.
/// Consumers must coordinate keyed-mutex access with the source before reading pixels.
/// </summary>
public readonly record struct GpuFrameReference(
    IntPtr SharedTextureHandle, int Width, int Height, double SourceTimeSeconds,
    long Generation, bool FlipVerticalForDisplay);

public interface IGpuFrameSource
{
    bool TryGetCurrentFrame(out GpuFrameReference frame);
}
