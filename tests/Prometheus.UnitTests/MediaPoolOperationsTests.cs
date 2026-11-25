using Prometheus.Core.Model;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Prometheus.Core.Undo;

namespace Prometheus.UnitTests;

public class MediaPoolOperationsTests
{
    private static Project Fixture()
    {
        var project = ProjectStore.CreateNew();
        project.Assets.Add(new Asset { Id = "voice", Name = "Original", Source = new MediaSource { Uri = "voice.wav" },
            Representations = [new MediaRepresentation { Locator = new Locator("voice.wav") }],
            Streams = [new StreamInfo { Id = "voice-stream", Kind = "audio" }] });
        project.Assets.Add(new Asset { Id = "music", Name = "Music", Source = new MediaSource { Uri = "music.wav" } });
        project.ActiveSequence!.Clips.Add(new Clip { Id = "instance", Name = "Instance name", AssetId = "voice",
            StreamId = "voice-stream", TrackId = project.ActiveSequence.Tracks.First(t => t.Kind == "audio").Id });
        return project;
    }

    [Fact]
    public void RenameIsVirtualAndKeepsReferencesAndInstanceNames()
    {
        var project = Fixture();
        MediaPoolOperations.RenameAsset(project, "voice", " Narration ");
        var asset = project.Assets[0];
        Assert.Equal("Narration", asset.Name);
        Assert.Equal("voice.wav", asset.Source.Uri);
        Assert.Equal("voice.wav", asset.Representations[0].Locator.Path);
        Assert.Equal("voice-stream", asset.Streams[0].Id);
        Assert.Equal("voice", project.ActiveSequence!.Clips[0].AssetId);
        Assert.Equal("Instance name", project.ActiveSequence.Clips[0].Name);
    }

    [Fact]
    public void OrganizationSurvivesRoundTripAndOneUndoRestoresBatch()
    {
        var manager = new UndoRedoManager(Fixture());
        manager.Commit(project => {
            var folder = MediaPoolOperations.CreateFolder(project, "Audio");
            MediaPoolOperations.MoveAssets(project, ["voice", "music"], folder.Id);
            MediaPoolOperations.RenameAsset(project, "voice", "Voice over");
        }, "Organize");
        var saved = ProjectStore.Clone(manager.CurrentProject);
        Assert.Equal(1, manager.UndoCount);
        var id = Assert.Single(saved.MediaFolders).Id;
        Assert.All(saved.Assets, a => Assert.Equal(id, a.FolderId));
        Assert.Equal("Voice over", saved.Assets[0].Name);
        manager.Undo();
        Assert.Empty(manager.CurrentProject.MediaFolders);
        Assert.All(manager.CurrentProject.Assets, a => Assert.Null(a.FolderId));
        Assert.Equal("Original", manager.CurrentProject.Assets[0].Name);
        manager.Redo();
        Assert.Equal("Audio", Assert.Single(manager.CurrentProject.MediaFolders).Name);
    }

    [Fact]
    public void InvalidMoveDoesNotPartiallyChangeAssets()
    {
        var project = Fixture();
        var folder = MediaPoolOperations.CreateFolder(project, "Audio");
        Assert.Throws<ArgumentException>(() => MediaPoolOperations.MoveAssets(project, ["voice", "missing"], folder.Id));
        Assert.All(project.Assets, a => Assert.Null(a.FolderId));
        Assert.Throws<ArgumentException>(() => MediaPoolOperations.MoveAssets(project, ["voice"], "missing"));
        Assert.Null(project.Assets[0].FolderId);
    }

    [Fact]
    public void FolderRenameKeepsMembershipAndRejectsDuplicateNames()
    {
        var project = Fixture();
        var first = MediaPoolOperations.CreateFolder(project, "Audio");
        var second = MediaPoolOperations.CreateFolder(project, "B roll");
        MediaPoolOperations.MoveAssets(project, ["voice", "voice"], first.Id);
        MediaPoolOperations.RenameFolder(project, first.Id, " Voices ");
        Assert.Equal("Voices", first.Name);
        Assert.Equal(first.Id, project.Assets[0].FolderId);
        Assert.Throws<ArgumentException>(() => MediaPoolOperations.RenameFolder(project, second.Id, " voices "));
        Assert.Throws<ArgumentException>(() => MediaPoolOperations.CreateFolder(project, "Voices"));
        Assert.Equal("B roll", second.Name);
        MediaPoolOperations.MoveAssets(project, ["voice"], null);
        Assert.Null(project.Assets[0].FolderId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\nname")]
    public void InvalidNamesDoNotMutateProject(string name)
    {
        var project = Fixture();
        Assert.Throws<ArgumentException>(() => MediaPoolOperations.RenameAsset(project, "voice", name));
        Assert.Throws<ArgumentException>(() => MediaPoolOperations.CreateFolder(project, name));
        Assert.Equal("Original", project.Assets[0].Name);
        Assert.Empty(project.MediaFolders);
    }

    [Fact]
    public void PoolKeepsMgtButExcludesTextAndInternalAssets()
    {
        Assert.True(MediaPoolOperations.AppearsInPool(new Asset { Source = new MediaSource { Kind = "mgt" } }));
        Assert.False(MediaPoolOperations.AppearsInPool(new Asset { TextOverlay = new TextOverlay() }));
        Assert.False(MediaPoolOperations.AppearsInPool(new Asset { Source = new MediaSource { Uri = "nested:id" } }));
    }
}
