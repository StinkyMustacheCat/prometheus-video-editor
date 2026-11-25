using Prometheus.Client.Services;

namespace Prometheus.UnitTests;

public class EffectLibrarySettingsTests
{
    [Fact]
    public void CustomTabsFavoritesAndOrderSurviveRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), "prometheus-effects-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var settings = new EffectLibrarySettings();
            var custom = new EffectLibraryCustomTab { Name = "Mis colores", Families = new() { "video:Color" } };
            settings.CustomTabs.Add(custom);
            settings.TabOrder.Insert(0, custom.Id);
            settings.Favorites.Add(EffectCatalog.Available[0].Id);
            settings.Personalized.Add(EffectCatalog.Available[1].Id);
            settings.Save(path);

            var restored = EffectLibrarySettings.Load(path);
            Assert.Equal(custom.Id, restored.TabOrder[0]);
            Assert.Equal("Mis colores", Assert.Single(restored.CustomTabs).Name);
            Assert.Contains(EffectCatalog.Available[0].Id, restored.Favorites);
            Assert.Contains(EffectCatalog.Available[1].Id, restored.Personalized);
            Assert.Contains("personalized", restored.TabOrder);
            Assert.Contains("audio", restored.TabOrder);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
