using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Prometheus.Core.Model;
using SkiaSharp;

namespace Prometheus.Client.Services;

/// <summary>Rasterizes a vector shape once per source size; the resulting mask is applied on GPU.</summary>
public static class ShapeMaskRaster
{
    private static readonly ConditionalWeakTable<ShapeMask,
        Dictionary<(int Width, int Height), SamMaskKeyframe>> Cache = new();

    public static SamMaskKeyframe ForSurface(ShapeMask shape, int width, int height)
    {
        shape.Validate();
        if (width < 1 || height < 1)
            throw new ArgumentException("La resolución de la máscara no es válida.");
        if ((long)width * height > 8_000_000)
        {
            var scale = Math.Sqrt(8_000_000.0 / ((double)width * height));
            width = Math.Max(1, (int)(width * scale));
            height = Math.Max(1, (int)(height * scale));
        }
        var sizes = Cache.GetValue(shape, _ => new());
        lock (sizes)
        {
            if (sizes.TryGetValue((width, height), out var saved)) return saved;
            var result = Draw(shape, width, height);
            sizes[(width, height)] = result;
            return result;
        }
    }

    private static SamMaskKeyframe Draw(ShapeMask shape, int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height,
            SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true,
            Style = SKPaintStyle.Fill };
        if (shape.Kind == "polygon")
        {
            using var path = new SKPath();
            path.MoveTo((float)(shape.Points[0].X * width),
                (float)(shape.Points[0].Y * height));
            for (var i = 1; i < shape.Points.Count; i++)
                path.LineTo((float)(shape.Points[i].X * width),
                    (float)(shape.Points[i].Y * height));
            path.Close();
            canvas.DrawPath(path, paint);
        }
        else
        {
            var a = shape.Points[0];
            var b = shape.Points[1];
            var rect = SKRect.Create((float)(Math.Min(a.X, b.X) * width),
                (float)(Math.Min(a.Y, b.Y) * height),
                (float)(Math.Abs(a.X - b.X) * width),
                (float)(Math.Abs(a.Y - b.Y) * height));
            if (shape.Kind == "ellipse") canvas.DrawOval(rect, paint);
            else canvas.DrawRect(rect, paint);
        }
        canvas.Flush();
        var row = new byte[checked(width * 4)];
        var alpha = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            Marshal.Copy(IntPtr.Add(bitmap.GetPixels(), y * bitmap.RowBytes), row, 0, row.Length);
            for (var x = 0; x < width; x++) alpha[y * width + x] = row[x * 4 + 3];
        }
        var mask = SamMaskKeyframe.Create(0, width, height, alpha);
        mask.Id = shape.Id + ":" + width + "x" + height;
        return mask;
    }
}
