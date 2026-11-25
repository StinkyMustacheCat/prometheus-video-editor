using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Prometheus.Core.Model;
using SkiaSharp;

namespace Prometheus.Client.Services;

/// <summary>Creates a transparent still only when editable text or output resolution changes.</summary>
public static class TextMediaService
{
    public static string? Resolve(Project project, Clip clip) => Resolve(project,
        SubtitleRenderPlan.Asset(project, clip));

    public static string? Resolve(Project project, Asset? asset, int? outputWidth = null, int? outputHeight = null)
    {
        if (asset?.TextOverlay == null) return asset?.EffectiveLocator?.Path;
        var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay montaje activo.");
        var width = outputWidth ?? sequence.Width;
        var height = outputHeight ?? sequence.Height;
        var text = asset.TextOverlay;
        text.Validate();
        var identity = JsonSerializer.Serialize(text) + "|" + width + "x" + height;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var folder = Path.Combine(AppContext.BaseDirectory, "Cache", "Text");
        var path = Path.Combine(folder, hash + ".png");
        if (File.Exists(path)) return path;
        Directory.CreateDirectory(folder);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using var bitmap = Render(text, width, height);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using (var stream = File.Create(temporary)) data.SaveTo(stream);
            try { File.Move(temporary, path); }
            catch (IOException) when (File.Exists(path)) { }
            return path;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static SKBitmap Render(TextOverlay text, int width, int height)
    {
        if (width < 1 || height < 1 || (long)width * height > 34_000_000)
            throw new ArgumentException("La resolución del texto no es válida.");
        var bitmap = new SKBitmap(new SKImageInfo(width, height,
            SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var scale = Math.Min(width, height) / 1080f;
        using var face = SKTypeface.FromFamilyName(text.FontFamily,
            SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ??
            SKTypeface.Default;
        using var fill = new SKPaint { IsAntialias = true, Typeface = face,
            TextSize = (float)(text.FontSize * scale),
            Color = SKColor.Parse(text.Color), Style = SKPaintStyle.Fill };
        var maxWidth = (float)(width * text.WrapWidth);
        var centerX = (float)(text.PositionX * width);
        var left = centerX - maxWidth / 2;
        var lines = Wrap(text.Content, fill, maxWidth);
        var lineHeight = fill.FontSpacing * 1.12f;
        var centerY = (float)(text.PositionY * height);
        var firstBaseline = centerY - (lines.Count - 1) * lineHeight / 2 -
            (fill.FontMetrics.Ascent + fill.FontMetrics.Descent) / 2;
        using var outline = new SKPaint { IsAntialias = true, Typeface = face,
            TextSize = fill.TextSize, Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)(text.OutlinePixels * scale),
            StrokeJoin = SKStrokeJoin.Round, Color = SKColor.Parse(text.OutlineColor) };
        using var shadow = new SKPaint { IsAntialias = true, Typeface = face,
            TextSize = fill.TextSize, Color = SKColors.Black.WithAlpha(
            (byte)Math.Round(text.ShadowOpacity * 255)) };
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var measured = fill.MeasureText(line);
            var x = text.Alignment switch
            {
                "left" => left,
                "right" => left + maxWidth - measured,
                _ => centerX - measured / 2
            };
            var y = firstBaseline + index * lineHeight;
            if (text.ShadowOpacity > 0)
                canvas.DrawText(line, x + 3 * scale,
                    y + 4 * scale, shadow);
            if (text.OutlinePixels > 0) canvas.DrawText(line, x, y, outline);
            canvas.DrawText(line, x, y, fill);
        }
        canvas.Flush();
        return bitmap;
    }

    private static List<string> Wrap(string content, SKPaint paint, float maxWidth)
    {
        var result = new List<string>();
        foreach (var paragraph in content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) { result.Add(" "); continue; }
            var line = "";
            foreach (var word in words)
            {
                if (paint.MeasureText(word) > maxWidth)
                {
                    if (line.Length > 0) { result.Add(line); line = ""; }
                    var elements = StringInfo.GetTextElementEnumerator(word);
                    while (elements.MoveNext())
                    {
                        var element = elements.GetTextElement();
                        var expanded = line + element;
                        if (line.Length > 0 && paint.MeasureText(expanded) > maxWidth)
                        { result.Add(line); line = element; }
                        else line = expanded;
                    }
                    continue;
                }
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && paint.MeasureText(candidate) > maxWidth)
                { result.Add(line); line = word; }
                else line = candidate;
            }
            if (line.Length > 0) result.Add(line);
        }
        return result.Count == 0 ? [" "] : result;
    }
}
