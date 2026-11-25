using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.UnitTests;

public class NestedRenderTreeTests
{
    [Fact]
    public void RebuildsTwoLevelsAndPreservesSiblingOrder()
    {
        var outer = new Clip { Id = "outer" };
        var inner = new Clip { Id = "inner" };
        var first = new Clip { Id = "first" };
        var second = new Clip { Id = "second" };
        var third = new Clip { Id = "third" };
        var track = new Track { Kind = "video" };
        NestedRenderLayer Layer(Clip clip, Clip[] parents, double[] times, int[] order) =>
            new(clip, track, 0, 2, parents, times, order);
        var layers = new[]
        {
            Layer(first, [outer, inner], [5, 3], [1, 1, 1]),
            Layer(second, [outer, inner], [5, 3], [1, 1, 2]),
            Layer(third, [outer], [5], [1, 2])
        };
        var root = Assert.Single(NestedRenderTree.Build(layers));
        Assert.Equal("outer", root.Clip.Id);
        Assert.Equal(5, root.LocalTimelineSeconds);
        Assert.Equal("inner", root.Children[0].Clip.Id);
        Assert.Equal(new[] { "first", "second" }, root.Children[0].Children.Select(node => node.Clip.Id));
        Assert.Equal("third", root.Children[1].Clip.Id);
        Assert.False(NestedRenderTree.NeedsGroupComposition(layers));
        outer.Visual.Opacity = 0.5;
        Assert.True(NestedRenderTree.NeedsGroupComposition(layers));
        outer.Visual.Opacity = 1;
        KeyframeAnimation.Set(outer, "visual.rotation", 0, 0);
        Assert.True(NestedRenderTree.NeedsGroupComposition(layers));
    }
}
