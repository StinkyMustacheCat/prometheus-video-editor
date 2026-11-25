using Prometheus.Core.Model;
using Prometheus.Core.Storage;
using Prometheus.Client.Services;

namespace Prometheus.UnitTests;

public class LinkShelfPersistenceTests
{
    [Fact]
    public void ReferencesSurviveProjectSaveAndOlderProjectsOpenWithEmptyShelf()
    {
        var project = ProjectStore.CreateNew();
        project.LinkShelf.Add(new LinkShelfItem { Source = "https://example.com/video.mp4", Name = "video.mp4", Kind = "web", InSeconds = 1.5, OutSeconds = 3.5,
            DurationSeconds = 42.25, ThumbnailPath = "thumbnail.img", MetadataStatus = "ready", MediaKind = "video",
            WebMetadata = new WebMediaMetadata { OriginalTitle = "Video real", CreatorName = "Creador", PublishedAt = "2026-11-07" } });
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(project));

        Assert.Single(restored.LinkShelf);
        Assert.Equal(1.5, restored.LinkShelf[0].InSeconds);
        Assert.Equal(3.5, restored.LinkShelf[0].OutSeconds);
        Assert.Equal("https://example.com/video.mp4", restored.LinkShelf[0].Source);
        Assert.Equal(42.25, restored.LinkShelf[0].DurationSeconds);
        Assert.Equal("Creador", restored.LinkShelf[0].WebMetadata?.CreatorName);
        Assert.Equal("thumbnail.img", restored.LinkShelf[0].ThumbnailPath);
        Assert.Equal("video", restored.LinkShelf[0].MediaKind);

        var oldJson = ProjectStore.Serialize(ProjectStore.CreateNew()).Replace("\"link_shelf\": [],", "");
        Assert.Empty(ProjectStore.Deserialize(oldJson).LinkShelf);
    }

    [Fact]
    public void PlacedReferenceMovesToPersistentHistoryWithoutRemovingSource()
    {
        var project = ProjectStore.CreateNew();
        var source = "https://example.com/clip.mp4";
        project.LinkShelf.Add(new LinkShelfItem { Source = source, Name = "Clip" });

        LinkShelfService.MarkPlaced(project, source);
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(project));

        Assert.Equal(source, restored.LinkShelf.Single().Source);
        Assert.NotNull(restored.LinkShelf.Single().PlacedAt);
        Assert.True(restored.LinkShelf.Single().PlacedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void DirectWebPlacementCreatesHistoryAndMetadataUpdatesIt()
    {
        var project = ProjectStore.CreateNew();
        var source = "https://example.com/clip.mp4";
        var uri = new Uri(source);
        var asset = new Asset { Source = new MediaSource { Kind = "web", Uri = source }, Name = "Preparando medio…" };
        project.Assets.Add(asset);

        LinkShelfService.MarkPlaced(project, source);
        WebMediaMetadataUpdate.Apply(project, asset.Id, uri,
            new WebMediaMetadata { OriginalTitle = "Título real", CreatorName = "Creador" }, 42);

        var reference = Assert.Single(project.LinkShelf);
        Assert.NotNull(reference.PlacedAt);
        Assert.Equal("Título real", reference.Name);
        Assert.Equal(42, reference.DurationSeconds);
        Assert.Equal("Creador", reference.WebMetadata?.CreatorName);
    }
}
