using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Prometheus.Client.Services;

public sealed class EffectLibraryCustomTab
{
    public string Id { get; set; } = "custom:" + Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Nueva pestaña";
    public List<string> Families { get; set; } = new();
}

public sealed class EffectLibrarySettings
{
    private static readonly string[] BaseTabs = { "favorites", "personalized", "video", "transitions", "audio" };
    public List<string> TabOrder { get; set; } = new(BaseTabs);
    public List<string> Favorites { get; set; } = new();
    public List<string> Personalized { get; set; } = new();
    public List<EffectLibraryCustomTab> CustomTabs { get; set; } = new();

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Prometheus", "effect-library.json");

    public static EffectLibrarySettings Load(string? path = null)
    {
        try
        {
            var target = path ?? DefaultPath;
            if (!File.Exists(target)) return new EffectLibrarySettings();
            var result = JsonSerializer.Deserialize<EffectLibrarySettings>(File.ReadAllText(target))
                ?? new EffectLibrarySettings();
            result.Normalize();
            return result;
        }
        catch { return new EffectLibrarySettings(); }
    }

    public void Save(string? path = null)
    {
        Normalize();
        var target = path ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(target)) File.Replace(temp, target, null);
        else File.Move(temp, target);
    }

    public void Normalize()
    {
        CustomTabs ??= new();
        Favorites ??= new();
        Personalized ??= new();
        TabOrder ??= new();
        CustomTabs = CustomTabs.Where(t => !string.IsNullOrWhiteSpace(t.Id) &&
            !string.IsNullOrWhiteSpace(t.Name)).GroupBy(t => t.Id).Select(g => g.First()).ToList();
        foreach (var tab in CustomTabs) tab.Families ??= new();
        var valid = BaseTabs.Concat(CustomTabs.Select(t => t.Id)).ToHashSet();
        TabOrder = TabOrder.Where(valid.Contains).Distinct().ToList();
        foreach (var tab in BaseTabs.Concat(CustomTabs.Select(t => t.Id)))
            if (!TabOrder.Contains(tab)) TabOrder.Add(tab);
        Favorites = Favorites.Where(id => EffectCatalog.Available.Any(e => e.Id == id)).Distinct().ToList();
        Personalized = Personalized.Where(id => EffectCatalog.Available.Any(e => e.Id == id)).Distinct().ToList();
    }
}
