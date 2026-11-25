using System;
using System.IO;
using Prometheus.Common.Docking;
using Prometheus.Common.Persistence;
using Xunit;

namespace Prometheus.UnitTests;

public class PersistenceTests : IDisposable
{
    private readonly string _testFilePath;

    public PersistenceTests()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"prometheus_test_layout_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_testFilePath))
        {
            File.Delete(_testFilePath);
        }
    }

    [Fact]
    public void SaveAndLoad_ShouldPersistCompleteLayoutHierarchy()
    {
        var service = new LayoutPersistenceService(_testFilePath);
        var layout = FactoryPresets.CreateEquilibrado();
        layout.MinimizedPanels.Add("color");
        layout.FloatingPanels.Add(new FloatingWindowDescriptor("audio_mixer", 150, 200, 350, 450));

        bool saved = service.SaveLayout(layout);
        Assert.True(saved);
        Assert.True(File.Exists(_testFilePath));

        var loaded = service.LoadLayout();
        Assert.NotNull(loaded);
        Assert.Equal(FactoryPresets.PresetEquilibrado, loaded.Name);
        Assert.Contains("color", loaded.MinimizedPanels);
        Assert.Single(loaded.FloatingPanels);
        Assert.Equal("audio_mixer", loaded.FloatingPanels[0].PanelId);
        Assert.Equal(150, loaded.FloatingPanels[0].X);

        var loadedPanelIds = loaded.RootNode!.GetAllPanelIds();
        Assert.Contains("media_pool", loadedPanelIds);
        Assert.Contains("timeline", loadedPanelIds);
        Assert.Contains("monitor", loadedPanelIds);
    }

    [Fact]
    public void Load_WhenFileDoesNotExist_ShouldFallbackToEquilibrado()
    {
        var service = new LayoutPersistenceService(_testFilePath);
        var loaded = service.LoadLayout();

        Assert.NotNull(loaded);
        Assert.Equal(FactoryPresets.PresetEquilibrado, loaded.Name);
    }

    [Fact]
    public void Load_WhenLegacyFileHasNoRootNode_ShouldFallbackToEquilibrado()
    {
        File.WriteAllText(_testFilePath, "{\"Preset\":\"personalizado\",\"Root\":{\"Id\":null}}");
        var service = new LayoutPersistenceService(_testFilePath);
        var loaded = service.LoadLayout();

        Assert.NotNull(loaded);
        Assert.Equal(FactoryPresets.PresetEquilibrado, loaded.Name);
        Assert.NotNull(loaded.RootNode);
    }

    [Fact]
    public void Load_MigratesOldEffectsPanelToMediaControls()
    {
        var service = new LayoutPersistenceService(_testFilePath);
        var layout = new WorkspaceLayout {
            RootNode = LayoutNode.CreateSplit(SplitOrientation.Horizontal,
                LayoutNode.CreateLeaf("effect_controls"), LayoutNode.CreateLeaf("timeline")),
            ClosedPanels = new() { "properties" }
        };
        Assert.True(service.SaveLayout(layout));
        var loaded = service.LoadLayout();
        Assert.NotNull(loaded.RootNode?.FindNode("properties"));
        Assert.Null(loaded.RootNode?.FindNode("effect_controls"));
        Assert.DoesNotContain("properties", loaded.ClosedPanels);
    }

    [Fact]
    public void Load_RemovesDuplicateEffectsPanelWhenInspectorAlreadyExists()
    {
        var service = new LayoutPersistenceService(_testFilePath);
        var layout = new WorkspaceLayout {
            RootNode = LayoutNode.CreateSplit(SplitOrientation.Horizontal,
                LayoutNode.CreateLeaf("effect_controls"), LayoutNode.CreateLeaf("properties"))
        };
        Assert.True(service.SaveLayout(layout));
        var loaded = service.LoadLayout();
        Assert.Equal("properties", loaded.RootNode?.PanelId);
    }
}
