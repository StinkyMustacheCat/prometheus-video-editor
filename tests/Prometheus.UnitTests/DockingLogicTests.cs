using System.Collections.Generic;
using Prometheus.Common.Docking;
using Xunit;

namespace Prometheus.UnitTests;

public class DockingLogicTests
{
    [Fact]
    public void EdgeGutter_LeftDock_ShouldCreateRootSplitWith025Ratio()
    {
        var originalRoot = LayoutNode.CreateLeaf("monitor");
        var draggedPanelId = "media_pool";

        // Emulate Left Gutter Dock (25% left, 75% right)
        var newRoot = LayoutNode.CreateSplit(
            SplitOrientation.Horizontal,
            LayoutNode.CreateLeaf(draggedPanelId),
            originalRoot,
            0.25);

        Assert.Equal(LayoutNodeType.Split, newRoot.Type);
        Assert.Equal(SplitOrientation.Horizontal, newRoot.Orientation);
        Assert.Equal(0.25, newRoot.Ratio);
        Assert.Equal("media_pool", newRoot.FirstChild!.PanelId);
        Assert.Equal("monitor", newRoot.SecondChild!.PanelId);
    }

    [Fact]
    public void EdgeGutter_RightDock_ShouldCreateRootSplitWith075Ratio()
    {
        var originalRoot = LayoutNode.CreateLeaf("monitor");
        var draggedPanelId = "properties";

        // Emulate Right Gutter Dock (75% left, 25% right)
        var newRoot = LayoutNode.CreateSplit(
            SplitOrientation.Horizontal,
            originalRoot,
            LayoutNode.CreateLeaf(draggedPanelId),
            0.75);

        Assert.Equal(LayoutNodeType.Split, newRoot.Type);
        Assert.Equal(SplitOrientation.Horizontal, newRoot.Orientation);
        Assert.Equal(0.75, newRoot.Ratio);
        Assert.Equal("monitor", newRoot.FirstChild!.PanelId);
        Assert.Equal("properties", newRoot.SecondChild!.PanelId);
    }

    [Fact]
    public void CenterSubstitution_ShouldDisplaceTargetPanel()
    {
        var root = LayoutNode.CreateSplit(
            SplitOrientation.Horizontal,
            LayoutNode.CreateLeaf("targetPanel"),
            LayoutNode.CreateLeaf("otherPanel"),
            0.50);

        var draggedPanelId = "incomingPanel";
        var minimizedPanels = new List<string>();

        // Center drop on targetPanel:
        var targetNode = root.FindNode("targetPanel");
        Assert.NotNull(targetNode);

        // Displace previous panel to minimized
        minimizedPanels.Add(targetNode!.PanelId!);
        targetNode.PanelId = draggedPanelId;

        Assert.Contains("targetPanel", minimizedPanels);
        Assert.Equal("incomingPanel", root.FirstChild!.PanelId);
        Assert.Equal("otherPanel", root.SecondChild!.PanelId);
    }

    [Fact]
    public void FloatingWindowDescriptor_ShouldEnforceMinimumSize100x100()
    {
        var descriptor = new FloatingWindowDescriptor("test_panel", 50, 50, 40, 60);

        Assert.Equal(100, descriptor.Width);
        Assert.Equal(100, descriptor.Height);
    }

    [Theory]
    [InlineData(FactoryPresets.PresetEquilibrado)]
    [InlineData(FactoryPresets.PresetMontaje)]
    [InlineData(FactoryPresets.PresetBRoll)]
    [InlineData(FactoryPresets.PresetRevision)]
    [InlineData(FactoryPresets.PresetEdicionCompleta)]
    public void Presets_ShouldContainMasterEssentialPanels(string presetName)
    {
        var preset = FactoryPresets.GetPresetByName(presetName);
        var panelIds = preset.RootNode!.GetAllPanelIds();

        // Every factory preset must at least feature the timeline and monitor
        Assert.Contains("timeline", panelIds);
        Assert.Contains("monitor", panelIds);
    }
}
