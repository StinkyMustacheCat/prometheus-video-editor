using System;
using System.Collections.Generic;

namespace Prometheus.Core.Model;

/// <summary>Resolution families use the short edge, independently of orientation.</summary>
public static class CanvasResolutions
{
    public const int MaximumSide = 8192;
    public const long MaximumPixels = 34_000_000;

    public static void ValidateDimensions(int width, int height, int minimumSide = 64)
    {
        if (width < minimumSide || height < minimumSide || width > MaximumSide || height > MaximumSide ||
            (long)width * height > MaximumPixels)
            throw new ArgumentOutOfRangeException(nameof(width),
                $"Lados entre {minimumSide} y {MaximumSide} píxeles; máximo 34 megapíxeles.");
    }
    public sealed record Aspect(string Label, int Width, int Height);
    public static IReadOnlyList<int> Levels { get; } = Array.AsReadOnly(new[] { 360, 480, 720, 1080, 1440, 2160 });
    public static IReadOnlyList<Aspect> Aspects { get; } = Array.AsReadOnly(new[]
    {
        new Aspect("16:9 · Horizontal", 16, 9), new Aspect("9:16 · Vertical", 9, 16),
        new Aspect("1:1 · Cuadrado", 1, 1), new Aspect("4:3", 4, 3), new Aspect("3:4", 3, 4),
        new Aspect("Ultrapanorámica · ≈21:9", 64, 27)
    });

    public static (int Width, int Height) AtShortEdge(int width, int height, int shortEdge)
    {
        if (width <= 0 || height <= 0 || shortEdge < 64 || shortEdge > 8192)
            throw new ArgumentException("La resolución no es válida.");
        var scale = shortEdge / (double)Math.Min(width, height);
        var w = Even(width * scale);
        var h = Even(height * scale);
        ValidateDimensions(w, h);
        return (w, h);
    }

    // Permit at most the rounding needed for even video dimensions.
    public static bool SameAspect(int designWidth, int designHeight, int width, int height) =>
        designWidth > 0 && designHeight > 0 && width > 0 && height > 0 &&
        Math.Abs((double)width * designHeight - (double)height * designWidth) <=
            Math.Max(designWidth, designHeight) + 0.001;

    private static int Even(double value) => checked((int)(Math.Round(value / 2, MidpointRounding.AwayFromZero) * 2));
}
