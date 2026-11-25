using System;
using System.IO;
using System.Text.Json;
using SkiaSharp;

namespace Prometheus.Client.Services;

public sealed class CaptureUserSettings
{
    public string Format { get; set; } = "png";
    public bool TransparentBackground { get; set; }
    public string BackgroundColor { get; set; } = "#000000";

    private static string PathOnDisk => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Prometheus", "capture-settings.json");

    public static CaptureUserSettings Load()
    {
        try
        {
            if (File.Exists(PathOnDisk))
            {
                var saved = JsonSerializer.Deserialize<CaptureUserSettings>(File.ReadAllText(PathOnDisk));
                if (saved != null && (saved.Format == "png" || saved.Format == "jpg") &&
                    SKColor.TryParse(saved.BackgroundColor, out _)) return saved;
            }
        }
        catch { /* Prefer safe defaults when a local preference is damaged. */ }
        return new CaptureUserSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOnDisk)!);
        var temporary = PathOnDisk + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this));
        File.Move(temporary, PathOnDisk, true);
    }

    public SKColor OpaqueColor => SKColor.TryParse(BackgroundColor, out var color)
        ? new SKColor(color.Red, color.Green, color.Blue, 255) : SKColors.Black;
}
