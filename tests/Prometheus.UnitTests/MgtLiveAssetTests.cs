using System.IO.Compression;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Timeline;
using Prometheus.Core.Storage;
using Prometheus.Core.Undo;
using System.Text.Json;

namespace Prometheus.UnitTests;

public class MgtLiveAssetTests
{
    [Fact]
    public void PlainPastedTextCreatesEditableMgtComposition()
    {
        var path = Path.Combine(AppContext.BaseDirectory, Guid.NewGuid() + ".mgt.json");
        try
        {
            File.WriteAllText(path, MgtPastedText.Create("Hola <mundo>\nSegunda línea", "vertical"));
            var asset = MgtLiveAsset.Inspect(path);
            Assert.Equal("mgt", asset.Source.Kind);
            Assert.Equal(1080, asset.Streams[0].Width);
            Assert.Equal(1920, asset.Streams[0].Height);
            Assert.Equal("Hola <mundo>\nSegunda línea", asset.MgtParameters.Single(p => p.Id == "title").Default.GetString());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void JsonCompositionBecomesLiveTransparentAsset()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mgt.json");
        try
        {
            File.WriteAllText(path, """
                {"version":4,"name":"Título vivo","parameters":[],"html":"<div></div>","css":"","render":"return;",
                 "recommended":{"duration":6,"aspect":"vertical"}}
                """);
            var asset = MgtLiveAsset.Inspect(path);
            Assert.Equal("mgt", asset.Source.Kind);
            Assert.Equal("mgt.json", asset.ContainerFormat);
            Assert.Equal("Título vivo", asset.Name);
            Assert.Equal(6, asset.RecommendedDuration!.Value.Seconds);
            Assert.Equal(1080, asset.Streams.Single().Width);
            Assert.Equal(1920, asset.Streams.Single().Height);
            Assert.Equal("bgra8-alpha", asset.Streams.Single().PixelFormat);
            Assert.Single(asset.Representations);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ProjectArchiveUsesDeclaredSceneDuration()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mgtproj");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("project.json").Open()))
                writer.Write("""
                    {"format":"mgt-project","version":2,
                     "snapshot":{"state":{"text":"Hola","font":"Arial","color":"#fff",
                      "background":"#000","size":48,"duration":4.5,"aspect":"square",
                      "start":{"x":50,"y":50},"end":{"x":50,"y":50}},
                      "tracks":{"overlayLanes":[]},"effects":[]}}
                    """);
            var asset = MgtLiveAsset.Inspect(path);
            Assert.Equal("mgtproj", asset.ContainerFormat);
            Assert.Equal(4.5, asset.RecommendedDuration!.Value.Seconds);
            Assert.Equal(1080, asset.Streams.Single().Width);
            Assert.Equal(1080, asset.Streams.Single().Height);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void PreviewAndExportRequestSameExactFrame()
    {
        var time = Rational.FromFrames(137, 60);
        var preview = MgtFrameRequest.At("asset", "clip", time, 60, 480, 854);
        var export = MgtFrameRequest.At("asset", "clip", time, 60, 1080, 1920);
        Assert.Equal(137, preview.Frame);
        Assert.Equal(preview.Frame, export.Frame);
        Assert.Equal(time, preview.Time);
        Assert.Equal(0, preview.Revision);
    }

    [Fact]
    public void LiveParameterEditsRemainPerClipAndUndoAsOneGesture()
    {
        var project = ProjectStore.CreateNew();
        var track = project.ActiveSequence!.Tracks.First(item => item.Kind == "video");
        var asset = new Asset { Id = "mgt-asset", Name = "MGT",
            Source = new MediaSource { Kind = "mgt", Uri = "title.mgt.json" },
            Streams = [new StreamInfo { Id = "mgt-video", Kind = "video", Width = 1920, Height = 1080,
                Duration = Rational.Time(3) }] };
        project.Assets.Add(asset);
        project.ActiveSequence.Clips.Add(new Clip { Id = "first", AssetId = asset.Id,
            StreamId = "mgt-video", TrackId = track.Id });
        project.ActiveSequence.Clips.Add(new Clip { Id = "second", AssetId = asset.Id,
            StreamId = "mgt-video", TrackId = track.Id, TimelineStart = Rational.Time(2) });
        var undo = new UndoRedoManager(project);
        foreach (var title in new[] { "A", "AB", "ABC" })
            undo.CommitCoalesced(p => p.ActiveSequence!.Clips.First(c => c.Id == "first")
                .MgtOverrides["title"] = JsonSerializer.SerializeToElement(title), "Título MGT", "mgt:first:title");

        Assert.Equal(1, undo.UndoCount);
        Assert.Empty(undo.CurrentProject.ActiveSequence!.Clips.First(c => c.Id == "second").MgtOverrides);
        Assert.Equal("ABC", undo.CurrentProject.ActiveSequence.Clips.First(c => c.Id == "first")
            .MgtOverrides["title"].GetString());
        Assert.True(undo.Undo());
        Assert.Empty(undo.CurrentProject.ActiveSequence!.Clips.First(c => c.Id == "first").MgtOverrides);
    }
}
