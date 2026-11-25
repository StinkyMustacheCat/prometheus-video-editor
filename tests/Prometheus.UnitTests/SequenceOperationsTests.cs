using Prometheus.Core.Model;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;

namespace Prometheus.UnitTests;

public class SequenceOperationsTests
{
    [Fact]
    public void DuplicateRemapsTracksClipsAndGroups()
    {
        var project = ProjectStore.CreateNew();
        var original = project.ActiveSequence!;
        var first = new Clip { TrackId = original.Tracks[0].Id, AssetId = "asset-1" };
        var second = new Clip { TrackId = original.Tracks[1].Id, AssetId = "asset-1" };
        original.Clips.AddRange(new[] { first, second });
        original.LinkGroups.Add(new LinkGroup { ClipIds = new() { first.Id, second.Id } });
        var duplicate = SequenceOperations.Duplicate(project, original.Id);

        Assert.NotEqual(original.Id, duplicate.Id);
        Assert.All(duplicate.Tracks, t => Assert.DoesNotContain(original.Tracks, old => old.Id == t.Id));
        Assert.All(duplicate.Clips, c => Assert.DoesNotContain(original.Clips, old => old.Id == c.Id));
        Assert.All(duplicate.Clips, c => Assert.Contains(duplicate.Tracks, t => t.Id == c.TrackId));
        Assert.All(duplicate.LinkGroups.SelectMany(g => g.ClipIds), id => Assert.Contains(duplicate.Clips, c => c.Id == id));
        Assert.Equal(2, project.Sequences.Count);
    }

    [Fact]
    public void CannotDeleteLastSequence()
    {
        var project = ProjectStore.CreateNew();
        Assert.Throws<InvalidOperationException>(() => SequenceOperations.Delete(project, project.ActiveSequenceId));
    }

    [Fact]
    public void SequenceViewPersistsPerSequence()
    {
        var project = ProjectStore.CreateNew();
        var first = project.ActiveSequence!;
        first.ViewPlayheadSeconds = 18;
        first.ViewOffsetSeconds = 12;
        first.ViewZoomPixelsPerSecond = 22;
        var second = SequenceOperations.Create(project, "Segunda", first);
        second.ViewPlayheadSeconds = 4;
        var loaded = ProjectStore.Deserialize(ProjectStore.Serialize(project));
        Assert.Equal(18, loaded.Sequences[0].ViewPlayheadSeconds);
        Assert.Equal(12, loaded.Sequences[0].ViewOffsetSeconds);
        Assert.Equal(22, loaded.Sequences[0].ViewZoomPixelsPerSecond);
        Assert.Equal(4, loaded.Sequences[1].ViewPlayheadSeconds);
    }
}
