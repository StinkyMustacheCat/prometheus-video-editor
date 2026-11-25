using System.Collections.Generic;
using Prometheus.Common.Docking;
using Xunit;

namespace Prometheus.UnitTests;

public class LayoutTreeTests
{
    [Fact]
    public void Ratio_ShouldBeStrictlyClampedBetween010And090()
    {
        var node = new LayoutNode();

        node.Ratio = 0.05;
        Assert.Equal(0.10, node.Ratio);

        node.Ratio = 0.95;
        Assert.Equal(0.90, node.Ratio);

        node.Ratio = 0.333;
        Assert.Equal(0.333, node.Ratio);
    }

    [Fact]
    public void RemoveLeaf_WhenRootIsSplit_ShouldPromoteSurvivingChild()
    {
        var leafA = LayoutNode.CreateLeaf("panelA");
        var leafB = LayoutNode.CreateLeaf("panelB");
        var root = LayoutNode.CreateSplit(SplitOrientation.Horizontal, leafA, leafB, 0.5);

        var newRoot = LayoutNode.Remove(root, "panelA");

        Assert.NotNull(newRoot);
        Assert.Equal(LayoutNodeType.Leaf, newRoot!.Type);
        Assert.Equal("panelB", newRoot.PanelId);
    }

    [Fact]
    public void RemoveLeaf_InNestedTree_ShouldPreserveStructure()
    {
        // Root: Split(Vertical)
        //   First: Split(Horizontal): panel1, panel2
        //   Second: panel3
        var leaf1 = LayoutNode.CreateLeaf("panel1");
        var leaf2 = LayoutNode.CreateLeaf("panel2");
        var leaf3 = LayoutNode.CreateLeaf("panel3");

        var topSplit = LayoutNode.CreateSplit(SplitOrientation.Horizontal, leaf1, leaf2, 0.5);
        var root = LayoutNode.CreateSplit(SplitOrientation.Vertical, topSplit, leaf3, 0.6);

        // Remove panel1
        var newRoot = LayoutNode.Remove(root, "panel1");

        Assert.NotNull(newRoot);
        Assert.Equal(LayoutNodeType.Split, newRoot!.Type);
        Assert.Equal(SplitOrientation.Vertical, newRoot.Orientation);

        // FirstChild should now be leaf2 directly!
        Assert.NotNull(newRoot.FirstChild);
        Assert.Equal(LayoutNodeType.Leaf, newRoot.FirstChild!.Type);
        Assert.Equal("panel2", newRoot.FirstChild.PanelId);

        // SecondChild should remain leaf3
        Assert.NotNull(newRoot.SecondChild);
        Assert.Equal("panel3", newRoot.SecondChild!.PanelId);
    }

    [Fact]
    public void SplitLeaf_ShouldTransformLeafIntoSplitNode()
    {
        var leaf = LayoutNode.CreateLeaf("existingPanel");
        bool success = leaf.SplitLeaf("existingPanel", "newPanel", SplitOrientation.Vertical, insertBefore: false, ratio: 0.333);

        Assert.True(success);
        Assert.Equal(LayoutNodeType.Split, leaf.Type);
        Assert.Equal(SplitOrientation.Vertical, leaf.Orientation);
        Assert.Equal(0.333, leaf.Ratio);
        Assert.NotNull(leaf.FirstChild);
        Assert.Equal("existingPanel", leaf.FirstChild!.PanelId);
        Assert.NotNull(leaf.SecondChild);
        Assert.Equal("newPanel", leaf.SecondChild!.PanelId);
    }

    [Fact]
    public void FactoryPresets_ShouldAllBeValidWithUniquePanelsAndValidRatios()
    {
        var presets = FactoryPresets.GetAllPresets();
        Assert.Equal(5, presets.Count);

        foreach (var preset in presets)
        {
            Assert.False(string.IsNullOrEmpty(preset.Name));
            Assert.NotNull(preset.RootNode);

            var panelIds = preset.RootNode.GetAllPanelIds();
            Assert.NotEmpty(panelIds);

            // Assert uniqueness of panel IDs
            var hashSet = new HashSet<string>(panelIds);
            Assert.Equal(panelIds.Count, hashSet.Count);

            // Assert ratios
            ValidateNodeRatios(preset.RootNode);
        }
    }

    private void ValidateNodeRatios(LayoutNode node)
    {
        if (node.Type == LayoutNodeType.Split)
        {
            Assert.InRange(node.Ratio, 0.10, 0.90);
            Assert.NotNull(node.FirstChild);
            Assert.NotNull(node.SecondChild);
            ValidateNodeRatios(node.FirstChild);
            ValidateNodeRatios(node.SecondChild);
        }
    }
}
