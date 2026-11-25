using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Prometheus.Core.Undo;
using Xunit;

namespace Prometheus.UnitTests;

public class AssetRemovalTests
{
    [Fact]
    public void RemovalCleansAllMontagesAndGroupsAndCanBeUndone()
    {
        var project = ProjectStore.CreateNew();
        var asset = VideoAsset("asset-video", "stream-video");
        project.Assets.Add(asset);
        var first = project.ActiveSequence!;
        first.Clips.Add(new Clip { Id = "first", AssetId = asset.Id, StreamId = asset.Streams[0].Id,
            TrackId = first.Tracks.First(track => track.Kind == "video").Id });
        first.LinkGroups.Add(new LinkGroup { ClipIds = ["first", "unrelated"] });
        var second = SequenceOperations.Create(project, "Montaje 2", first);
        second.Clips.Add(new Clip { Id = "second", AssetId = asset.Id, StreamId = asset.Streams[0].Id,
            TrackId = second.Tracks.First(track => track.Kind == "video").Id });
        var undo = new UndoRedoManager(project);

        Assert.Equal(2, NestedMediaOperations.FindAssetOccurrences(undo.CurrentProject, asset.Id).Count);
        undo.Commit(p => NestedMediaOperations.RemoveAssetEverywhere(p, asset.Id), "Eliminar medio");

        Assert.DoesNotContain(undo.CurrentProject.Assets, item => item.Id == asset.Id);
        Assert.All(undo.CurrentProject.Sequences, sequence => Assert.DoesNotContain(sequence.Clips,
            clip => clip.AssetId == asset.Id));
        Assert.Empty(undo.CurrentProject.Sequences.First().LinkGroups);
        Assert.True(undo.Undo());
        Assert.Equal(2, NestedMediaOperations.FindAssetOccurrences(undo.CurrentProject, asset.Id).Count);
    }

    [Fact]
    public void LastVisualChildBecomesReserveSoOtherNestedAudioSurvives()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        var video = VideoAsset("video-asset", "video-stream");
        var audio = new Asset { Id = "audio-asset", Name = "Audio",
            Source = new MediaSource { Kind = "local", Uri = "audio.wav" },
            Streams = [new StreamInfo { Id = "audio-stream", Kind = "audio", Duration = Rational.Time(4) }] };
        project.Assets.AddRange([video, audio]);
        sequence.Clips.Add(new Clip { Id = "picture", AssetId = video.Id, StreamId = video.Streams[0].Id,
            TrackId = sequence.Tracks.First(track => track.Kind == "video").Id,
            Duration = Rational.Time(4) });
        sequence.Clips.Add(new Clip { Id = "sound", AssetId = audio.Id, StreamId = audio.Streams[0].Id,
            TrackId = sequence.Tracks.First(track => track.Kind == "audio").Id,
            Duration = Rational.Time(4) });
        var outerId = NestedMediaOperations.Create(project, ["picture", "sound"]);

        NestedMediaOperations.RemoveAssetEverywhere(project, video.Id);

        var outer = sequence.Clips.Single(clip => clip.Id == outerId);
        Assert.Contains(outer.NestedContent!.Clips, clip => clip.Id == "sound" && clip.AssetId == audio.Id);
        Assert.Contains(outer.NestedContent.Clips, clip => clip.Id == "picture" &&
            PlaceholderService.IsPlaceholder(project.Assets.First(asset => asset.Id == clip.AssetId)));
        Assert.Empty(NestedMediaOperations.FindAssetOccurrences(project, video.Id));
        NestedMediaOperations.Validate(project, outer);
    }

    private static Asset VideoAsset(string id, string streamId) => new()
    {
        Id = id, Name = "Vídeo", Source = new MediaSource { Kind = "local", Uri = "video.mp4" },
        Streams = [new StreamInfo { Id = streamId, Kind = "video", Duration = Rational.Time(4) }]
    };
}
