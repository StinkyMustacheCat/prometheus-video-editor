using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;

namespace Prometheus.UnitTests;

public class MgtSourceRoutingTests
{
    [Theory]
    [InlineData(".mgt.json")]
    [InlineData(".mgtproj")]
    public void LegacyMoviePreviewDoesNotReplaceTheEditableDocument(string extension)
    {
        var asset = new Asset { Source = new MediaSource { Kind = "mgt", Uri = "scene" + extension },
            Representations = [new MediaRepresentation { Role = "preview", Locator = new Locator("baked.mov") }] };
        Assert.Equal("scene" + extension, asset.EffectiveLocator!.Path);
        Assert.Equal("baked.mov", asset.Representations[0].Locator.Path);
    }

    [Fact]
    public void ExplicitDocumentRepresentationIsRetainedByReferenceForRelinking()
    {
        var original = new MediaRepresentation { Role = "original", Locator = new Locator("relocated.mgt.json") };
        var asset = new Asset { Source = new MediaSource { Kind = "mgt", Uri = "old.mgt.json" },
            Representations = [new MediaRepresentation { Role = "final", Locator = new Locator("final.mov") }, original] };
        Assert.Same(original.Locator, asset.EffectiveLocator);
    }

    [Fact]
    public void OrdinaryVideoKeepsTheExistingRepresentationPriority()
    {
        var asset = new Asset { Source = new MediaSource { Kind = "local", Uri = "original.mp4" },
            Representations = [new MediaRepresentation { Role = "final", Locator = new Locator("final.mp4") }] };
        Assert.Equal("final.mp4", asset.EffectiveLocator!.Path);
    }

    [Fact]
    public void ExportUsesDocumentAndDoesNotSilentlySendMovieToCefWhenDocumentIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "mgt-route-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var document = Path.Combine(root, "scene.mgt.json");
            var movie = Path.Combine(root, "baked.mov");
            File.WriteAllText(document, "{}");
            File.WriteAllText(movie, "baked data");
            var project = ProjectStore.CreateNew();
            var asset = new Asset { Id = "mgt", ContainerFormat = "mgt.json",
                Source = new MediaSource { Kind = "mgt", Uri = document },
                Streams = [new StreamInfo { Id = "visual", Kind = "video", Duration = Rational.One }],
                Representations = [new MediaRepresentation { Role = "preview", Locator = new Locator(movie) }] };
            project.Assets.Add(asset);
            project.ActiveSequence!.Clips.Add(new Clip { AssetId = asset.Id, StreamId = "visual",
                TrackId = project.ActiveSequence.Tracks.First(t => t.Kind == "video").Id, Duration = Rational.One });
            var plan = new ExportMediaPlan(project, Path.Combine(root, "project.pthproj"), 0, 1, false);
            Assert.Equal(document, plan.VideoPath(asset));
            Assert.False(plan.UsesProxy(asset));
            Assert.Empty(plan.Warnings);
            File.Delete(document);
            var offline = new ExportMediaPlan(project, Path.Combine(root, "project.pthproj"), 0, 1, false);
            Assert.Null(offline.VideoPath(asset));
            Assert.Single(offline.Warnings);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
