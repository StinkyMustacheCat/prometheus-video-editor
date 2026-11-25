using System;
using System.Collections.Generic;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;
using SkiaSharp;

namespace Prometheus.Client.Services;

/// <summary>Composes a single requested PNG from GPU readbacks; never used by playback.</summary>
public static class GpuCaptureComposer
{
    public readonly record struct Layer(SKBitmap Bitmap, ClipVisualState Visual);

    public static SKBitmap Compose(int width, int height, IReadOnlyList<Layer> layers, SKColor? background = null)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var result = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(result);
        canvas.Clear(background ?? SKColors.Black);
        foreach (var layer in layers)
        {
            var rect = PreviewGeometry.PlaceLayer(width, height, layer.Bitmap.Width, layer.Bitmap.Height, layer.Visual);
            if (rect.Width <= 0 || rect.Height <= 0) continue;
            using var paint = new SKPaint
            {
                IsAntialias = true,
                FilterQuality = SKFilterQuality.High,
                Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(layer.Visual.Opacity, 0, 1) * 255))
            };
            canvas.Save();
            var anchorX = layer.Visual.FlipX ? 1 - layer.Visual.AnchorX : layer.Visual.AnchorX;
            var anchorY = layer.Visual.FlipY ? 1 - layer.Visual.AnchorY : layer.Visual.AnchorY;
            canvas.Translate((float)(rect.X + anchorX * rect.Width),
                (float)(rect.Y + anchorY * rect.Height));
            canvas.RotateDegrees((float)layer.Visual.RotationDegrees);
            canvas.Scale(layer.Visual.FlipX ? -1 : 1, layer.Visual.FlipY ? -1 : 1);
            canvas.Translate((float)(-layer.Visual.AnchorX * rect.Width),
                (float)(-layer.Visual.AnchorY * rect.Height));
            canvas.DrawBitmap(layer.Bitmap, new SKRect(0, 0, (float)rect.Width, (float)rect.Height), paint);
            canvas.Restore();
        }
        canvas.Flush();
        return result;
    }
}
