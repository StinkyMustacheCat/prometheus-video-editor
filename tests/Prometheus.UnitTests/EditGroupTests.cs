using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Prometheus.Core.Primitives;
using Xunit;

namespace Prometheus.UnitTests;

public class EditGroupTests
{
    [Fact]
    public void GroupMergesExistingGroupsAndSelectionExpandsTransitivelyThroughLinks()
    {
        var sequence = ProjectStore.CreateNew().ActiveSequence!;
        foreach (var id in new[] { "a", "b", "c", "d" }) sequence.Clips.Add(new Clip { Id = id, TrackId = sequence.Tracks[0].Id });
        EditGroupOperations.Group(sequence, new[] { "a", "b" });
        sequence.LinkGroups.Add(new LinkGroup { ClipIds = new() { "b", "c" } });
        EditGroupOperations.Group(sequence, new[] { "b", "d" });

        Assert.Single(sequence.EditGroups);
        Assert.Equal(new[] { "a", "b", "c", "d" }, EditGroupOperations.Expand(sequence, new[] { "a" }).OrderBy(x => x));
        EditGroupOperations.Ungroup(sequence, new[] { "a" });
        Assert.Empty(sequence.EditGroups);
        Assert.Single(sequence.LinkGroups);
    }

    [Fact]
    public void AlignedTrimTargetsExcludeStaggeredMembers()
    {
        var sequence = ProjectStore.CreateNew().ActiveSequence!;
        sequence.Clips.Add(new Clip { Id = "a", TrackId = sequence.Tracks[0].Id, TimelineStart = Rational.Time(1), Duration = Rational.Time(3) });
        sequence.Clips.Add(new Clip { Id = "b", TrackId = sequence.Tracks[1].Id, TimelineStart = Rational.Time(1), Duration = Rational.Time(3) });
        sequence.Clips.Add(new Clip { Id = "c", TrackId = sequence.Tracks[2].Id, TimelineStart = Rational.Time(2), Duration = Rational.Time(2) });
        EditGroupOperations.Group(sequence, new[] { "a", "b", "c" });

        Assert.Equal(new[] { "a", "b" }, EditGroupOperations.AlignedTrimTargets(sequence, "a", true));
        Assert.Equal(new[] { "a", "b", "c" }, EditGroupOperations.AlignedTrimTargets(sequence, "a", false));
    }

    [Fact]
    public void DuplicatingWholeGroupCreatesIndependentGroup()
    {
        var sequence = ProjectStore.CreateNew().ActiveSequence!;
        sequence.Clips.Add(new Clip { Id = "a", TrackId = sequence.Tracks[0].Id, Duration = Rational.Time(1) });
        sequence.Clips.Add(new Clip { Id = "b", TrackId = sequence.Tracks[1].Id, Duration = Rational.Time(1) });
        EditGroupOperations.Group(sequence, new[] { "a", "b" });

        var copies = CollisionEngine.DuplicateClips(sequence, new[] { "a", "b" }, 2);
        Assert.Equal(2, copies.Count);
        Assert.Equal(2, sequence.EditGroups.Count);
        Assert.All(copies, copy => Assert.Contains(copy.Id, sequence.EditGroups[1].ClipIds));
        Assert.DoesNotContain("a", sequence.EditGroups[1].ClipIds);
    }
}
