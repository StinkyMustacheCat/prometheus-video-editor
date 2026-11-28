using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Prometheus.Client.Services;

public static class PreviewUserSettings
{
    internal const int HighQualityIndex = 2;
    internal const int DynamicQualityIndex = 3;
    internal static IReadOnlyList<string> QualityLabels { get; } = new[] { "Baja (320 px)", "Media (640 px)", "Alta (original)", "Dinámica" };
    internal static IReadOnlyList<int> RefreshRates { get; } = new[] { 15, 30, 60 };

    private sealed record Preferences(int QualityIndex = HighQualityIndex, int RefreshIndex = 2);
    private static readonly string SettingsPath = Environment.GetEnvironmentVariable("PROMETHEUS_PREVIEW_SETTINGS") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prometheus", "preview.json");
    private static Preferences _current = Load();

    public static int QualityIndex => _current.QualityIndex;
    public static int RefreshIndex => _current.RefreshIndex;
    public static event Action? Changed;

    public static void Set(int qualityIndex, int refreshIndex)
    {
        var next = Normalize(new(qualityIndex, refreshIndex));
        if (_current == next) return;
        _current = next;
        var temporary = SettingsPath + "." + Environment.ProcessId + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(SettingsPath))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(next));
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        Changed?.Invoke();
    }

    private static Preferences Load()
    {
        try { return Normalize(JsonSerializer.Deserialize<Preferences>(File.ReadAllText(SettingsPath)) ?? new()); }
        catch (IOException) { return new(); }
        catch (UnauthorizedAccessException) { return new(); }
        catch (JsonException) { return new(); }
    }

    private static Preferences Normalize(Preferences value) => new(
        value.QualityIndex >= 0 && value.QualityIndex < QualityLabels.Count ? value.QualityIndex : HighQualityIndex,
        Math.Clamp(value.RefreshIndex, 0, RefreshRates.Count - 1));
}
