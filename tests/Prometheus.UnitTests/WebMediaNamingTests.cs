using System;
using System.Text.Json;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Xunit;

namespace Prometheus.UnitTests;

public class WebMediaNamingTests
{
    [Fact]
    public void SocialPostWithoutTitle_UsesAccountAndPublicationDateAndPersistsBoth()
    {
        using var json = JsonDocument.Parse("""
            {"id":"938412","title":"938412","uploader":"Ejemplo Creativo",
             "uploader_id":"ejemplo","uploader_url":"https://example.com/ejemplo",
             "upload_date":"20261105","duration":19.2}
            """);
        var metadata = WebMediaNaming.FromYtDlp(json.RootElement, new Uri("https://example.com/video/938412"));
        Assert.Equal("@ejemplo · 2026-11-05", WebMediaNaming.DisplayName(metadata));
        var project = ProjectStore.CreateNew();
        project.Assets.Add(new Asset { Id = "web", Name = WebMediaNaming.DisplayName(metadata), WebMetadata = metadata });
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(project));
        Assert.Equal("Ejemplo Creativo", restored.Assets[0].WebMetadata?.AccountName);
        Assert.Equal("2026-11-05", restored.Assets[0].WebMetadata?.PublishedAt);
    }

    [Fact]
    public void SocialPostWithTitle_UsesTitleAndKeepsAccountSeparate()
    {
        using var json = JsonDocument.Parse("""
            {"id":"42","title":"Una escena","creator":"Director","uploader":"Cuenta"}
            """);
        var metadata = WebMediaNaming.FromYtDlp(json.RootElement, new Uri("https://example.com/42"));
        Assert.Equal("Una escena", WebMediaNaming.DisplayName(metadata));
        Assert.Equal("Director", metadata.CreatorName);
        Assert.Equal("Cuenta", metadata.AccountName);
    }

    [Fact]
    public void MissingDatesAndDuration_LeaveReadableNameForGenericDownloads()
    {
        using var json = JsonDocument.Parse("""{"id":"demo","title":"demo video","timestamp":null,"duration":null}""");
        var metadata = WebMediaNaming.FromYtDlp(json.RootElement, new Uri("https://example.com/demo"));
        Assert.Equal("demo video", WebMediaNaming.DisplayName(metadata));
        Assert.Null(metadata.PublishedAt);
    }

    [Fact]
    public void ShelfReference_UsesReadablePlaceholderUntilImported()
    {
        Assert.Equal("Instagram · enlace", WebMediaNaming.InitialName(new Uri("https://www.instagram.com/reel/xyz123/")));
        Assert.Equal("entrevista", WebMediaNaming.InitialName(new Uri("https://example.com/entrevista.mp4")));
    }

    [Fact]
    public void MetadataUpdate_ResizesProvisionalClipAndPreservesLaterManualNames()
    {
        var uri = new Uri("https://www.instagram.com/reel/abc/");
        var project = ProjectStore.CreateNew();
        var initial = WebMediaNaming.InitialName(uri);
        project.Assets.Add(new Asset { Id = "web", Name = initial, WebAutoName = initial,
            Source = new MediaSource { Kind = "web", Uri = uri.ToString() } });
        var clip = new Clip { AssetId = "web", Name = "Preparando medio…",
            TrackId = project.ActiveSequence!.Tracks[0].Id,
            TimelineStart = Rational.Time(0), Duration = Rational.Time(5), WebAutoDurationSeconds = 5 };
        project.ActiveSequence.Clips.Add(clip);
        WebMediaMetadataUpdate.Apply(project, "web", uri,
            new WebMediaMetadata { OriginalTitle = "Primera versión" }, 12);
        Assert.Equal("Primera versión", project.Assets[0].Name);
        Assert.Equal("Primera versión", clip.Name);
        Assert.Equal(12, clip.Duration.Seconds, 3);

        project.Assets[0].Name = "Mi nombre de medio";
        clip.Name = "Mi nombre de clip";
        WebMediaMetadataUpdate.Apply(project, "web", uri,
            new WebMediaMetadata { OriginalTitle = "Título actualizado" }, 14);
        Assert.Equal("Mi nombre de medio", project.Assets[0].Name);
        Assert.Equal("Mi nombre de clip", clip.Name);
        Assert.Equal("Título actualizado", project.Assets[0].WebAutoName);
    }
}
