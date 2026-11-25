using System.Runtime.InteropServices;
using System.Text.Json;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Rational = Prometheus.Core.Primitives.Rational;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using SkiaSharp;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Prometheus.UnitTests;

public class TextAndSubtitleTests
{
    [Fact]
    public void TitleBoxPersistsWrapsAndStaysAboveExistingVideo()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        sequence.Width = 320;
        sequence.Height = 180;
        var currentTop = sequence.Tracks.Single(track => track.Name == "V2");
        sequence.Clips.Add(new Clip { TrackId = currentTop.Id, Duration = Rational.Time(12) });
        var clip = TitlePlacement.Add(project, 2.004, new TitleBox(0.25, 0.65, 0.22, 0.15));
        var titleTrack = sequence.Tracks.Single(track => track.Id == clip.TrackId);
        Assert.True(titleTrack.Order > currentTop.Order);
        Assert.Equal(2, clip.TimelineStart.Seconds, 4);
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(project));
        var asset = restored.Assets.Single();
        Assert.Equal(0.25, asset.TextOverlay!.PositionX);
        Assert.Equal(0.65, asset.TextOverlay.PositionY);
        Assert.Equal(0.22, asset.TextOverlay.BoxWidth);
        using (var initial = SKBitmap.Decode(TextMediaService.Resolve(restored, asset)))
        {
            var ink = 0;
            for (var y = 0; y < initial.Height; y++)
                for (var x = 0; x < initial.Width; x++)
                    if (initial.GetPixel(x, y).Alpha > 40)
                    {
                        Assert.InRange(x, 42, 118);
                        Assert.InRange(y, 103, 131);
                        ink++;
                    }
            Assert.True(ink > 20, "El texto inicial debe caber dentro del cuadro dibujado.");
        }
        asset.TextOverlay.Content = "Uno dos tres cuatro cinco";
        asset.TextOverlay.FontSize = 108;
        asset.TextOverlay.OutlinePixels = 0;
        asset.TextOverlay.ShadowOpacity = 0;
        using var bitmap = SKBitmap.Decode(TextMediaService.Resolve(restored, asset));
        int top = bitmap.Height, bottom = 0;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y).Alpha > 40)
                {
                    Assert.InRange(x, 42, 118);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
        Assert.True(top < 110 && bottom > 125, "El texto debe ocupar más de una línea alrededor de Y.");
    }

    [Fact]
    public void TitleDrawnLateInTheFrameIsVisibleAtThePlayheadOverAnExistingClip()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        sequence.Fps = new Rational(30000, 1001);
        var highest = sequence.Tracks.Where(t => t.Kind == "video").MaxBy(t => t.Order)!;
        sequence.Clips.Add(new Clip { TrackId = highest.Id, Duration = Rational.Time(8) });
        var playhead = 175.9 / sequence.Fps.Seconds;
        var title = TitlePlacement.Add(project, playhead, new TitleBox(0.5, 0.5, 0.4, 0.25));

        Assert.Equal(new Rational(175 * 1001, 30000), title.TimelineStart);
        Assert.InRange(playhead, title.TimelineStart.Seconds, title.EndSeconds);
        Assert.True(sequence.Tracks.Single(t => t.Id == title.TrackId).Order > highest.Order);
    }

    [Theory]
    [InlineData("title", 0.88)]
    [InlineData("subtitle", 0.86)]
    public void OldTextWithoutBoxKeepsCenteredWidth(string kind, double width)
    {
        var oldText = JsonSerializer.Deserialize<TextOverlay>("{\"kind\":\"" + kind + "\",\"position_y\":0.84}")!;
        Assert.Equal(0.5, oldText.PositionX);
        Assert.Equal(0.84, oldText.PositionY);
        Assert.Null(oldText.BoxWidth);
        Assert.Equal(width, oldText.WrapWidth);
    }

    [Fact]
    public void GeneratedTextSurvivesProjectSaveAndHasTransparentBackground()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        sequence.Width = 320; sequence.Height = 180;
        var asset = GeneratedText.CreateAsset("Hola mundo", "title", 320, 180);
        project.Assets.Add(asset);
        sequence.Clips.Add(new Clip { AssetId = asset.Id, StreamId = asset.Streams[0].Id,
            TrackId = sequence.Tracks.First(track => track.Name == "V2").Id,
            Duration = Rational.Time(5) });
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(project));
        var saved = restored.Assets.Single(item => item.Id == asset.Id);
        Assert.Equal("Hola mundo", saved.TextOverlay!.Content);
        using var bitmap = SKBitmap.Decode(TextMediaService.Resolve(restored, saved));
        Assert.Equal(0, bitmap.GetPixel(2, 2).Alpha);
        var inkPixels = Enumerable.Range(60, 200).Count(x => Enumerable.Range(50, 80)
            .Any(y => bitmap.GetPixel(x, y).Alpha > 20));
        Assert.True(inkPixels > 0);
    }

    [Fact]
    public async Task CollapsedSubtitleLaneStillRendersAboveVideoAndAnimates()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Path.GetTempPath(), "prometheus-text-bg-" +
            Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using (var bitmap = new SKBitmap(320, 180))
            {
                bitmap.Erase(SKColors.Red);
                using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, encoded.ToArray());
            }
            var project = ProjectStore.CreateNew();
            var sequence = project.ActiveSequence!;
            sequence.Width = 320; sequence.Height = 180;
            var background = new Asset { Id = "background", Source = new MediaSource
                { Kind = "local", Uri = path }, Streams = [new StreamInfo
                    { Id = "picture", Kind = "image", Width = 320, Height = 180 }] };
            project.Assets.Add(background);
            sequence.Clips.Add(new Clip { AssetId = background.Id, StreamId = "picture",
                TrackId = sequence.Tracks.First(track => track.Name == "V2").Id,
                Duration = Rational.Time(3) });
            var cue = SubtitleTrack.Add(project, "HOLA", 0, 2);
            project.Assets.First(asset => asset.Id == cue.AssetId).TextOverlay!.FontSize = 150;
            Assert.Equal(-1, SubtitleTrack.Find(sequence)!.Order);
            Assert.False(sequence.SubtitleLaneExpanded);
            Assert.Equal(cue.Id, NestedRenderPlan.At(project, 1, "video").Last().Clip.Id);

            KeyframeAnimation.Set(cue, "visual.opacity", 0, 0);
            KeyframeAnimation.Set(cue, "visual.opacity", 0.4, 1);
            using var renderer = new GpuExportFrameRenderer(project, null);
            async Task<int> BrightSubtitlePixels(double at)
            {
                await renderer.RenderAsync(at);
                var bright = 0;
                renderer.WithOutputTexture((device, context, texture) =>
                {
                    using var staging = device.CreateTexture2D(new Texture2DDescription(
                        Format.B8G8R8A8_UNorm, 320, 180, 1, 1, BindFlags.None,
                        ResourceUsage.Staging, CpuAccessFlags.Read));
                    context.CopyResource(staging, texture);
                    var mapped = context.Map(staging, 0, MapMode.Read,
                        Vortice.Direct3D11.MapFlags.None);
                    try
                    {
                        for (var y = 100; y < 175; y++)
                            for (var x = 45; x < 275; x++)
                            {
                                var address = IntPtr.Add(mapped.DataPointer,
                                    y * (int)mapped.RowPitch + x * 4);
                                if (Marshal.ReadByte(address) > 150 &&
                                    Marshal.ReadByte(address, 1) > 150) bright++;
                            }
                    }
                    finally { context.Unmap(staging, 0); }
                });
                return bright;
            }
            Assert.Equal(0, await BrightSubtitlePixels(0));
            Assert.True(await BrightSubtitlePixels(1) > 40);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task VideoTrackEffectChangesOnlyItsOwnCompositionAndPreservesTransparency()
    {
        if (!OperatingSystem.IsWindows()) return;
        var token = "prometheus-track-fx-" + Guid.NewGuid().ToString("N");
        var bottom = Path.Combine(Path.GetTempPath(), token + "-bottom.png");
        var top = Path.Combine(Path.GetTempPath(), token + "-top.png");
        try
        {
            var project = ProjectStore.CreateNew();
            var sequence = project.ActiveSequence!;
            sequence.Width = 320;
            sequence.Height = 180;
            void AddImage(string path, string trackName, Action<SKCanvas> draw)
            {
                using var bitmap = new SKBitmap(320, 180);
                using var canvas = new SKCanvas(bitmap);
                canvas.Clear(SKColors.Transparent);
                draw(canvas);
                canvas.Flush();
                using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, encoded.ToArray());
                var asset = new Asset { Id = trackName, Source = new MediaSource { Kind = "local", Uri = path },
                    Streams = [new StreamInfo { Id = trackName + "-picture", Kind = "image", Width = 320, Height = 180 }] };
                project.Assets.Add(asset);
                sequence.Clips.Add(new Clip { AssetId = asset.Id, StreamId = asset.Streams[0].Id,
                    TrackId = sequence.Tracks.Single(track => track.Name == trackName).Id, Duration = Rational.Time(2) });
            }
            AddImage(bottom, "V1", canvas => canvas.Clear(SKColors.Blue));
            AddImage(top, "V2", canvas =>
            {
                using var fill = new SKPaint { Color = SKColors.Red };
                canvas.DrawRect(0, 0, 160, 180, fill);
            });
            var effects = sequence.Tracks.Single(track => track.Name == "V2").Effects;
            var monochrome = new EffectInstance { EffectId = MonochromeEffect.Id };
            effects.Add(monochrome);
            Assert.True(NestedRenderTree.NeedsGroupComposition(NestedRenderPlan.At(project, 0, "video"), sequence));
            using var renderer = new GpuExportFrameRenderer(project, null);
            async Task<(SKColor Left, SKColor Right)> ReadColors()
            {
                await renderer.RenderAsync(0);
                SKColor left = default, right = default;
                renderer.WithOutputTexture((device, context, texture) =>
                {
                    using var staging = device.CreateTexture2D(new Texture2DDescription(
                        Format.B8G8R8A8_UNorm, 320, 180, 1, 1, BindFlags.None,
                        ResourceUsage.Staging, CpuAccessFlags.Read));
                    context.CopyResource(staging, texture);
                    var mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                    try
                    {
                        SKColor Pixel(int x)
                        {
                            var offset = 90 * (int)mapped.RowPitch + x * 4;
                            return new SKColor(Marshal.ReadByte(mapped.DataPointer, offset + 2),
                                Marshal.ReadByte(mapped.DataPointer, offset + 1), Marshal.ReadByte(mapped.DataPointer, offset));
                        }
                        left = Pixel(80);
                        right = Pixel(240);
                    }
                    finally { context.Unmap(staging, 0); }
                });
                return (left, right);
            }
            var active = await ReadColors();
            Assert.InRange(active.Left.Red, 30, 140);
            Assert.InRange(Math.Abs(active.Left.Red - active.Left.Green), 0, 3);
            Assert.InRange(Math.Abs(active.Left.Red - active.Left.Blue), 0, 3);
            Assert.True(active.Right.Blue > 200 && active.Right.Red < 20);
            monochrome.Enabled = false;
            var bypassed = await ReadColors();
            Assert.True(bypassed.Left.Red > 200 && bypassed.Left.Green < 20);
        }
        finally { File.Delete(bottom); File.Delete(top); }
    }

    [Fact]
    public void VerticalSubtitleStaysInsideFrameAndTrackIsKeptSeparate()
    {
        var project = ProjectStore.CreateNew();
        var sequence = project.ActiveSequence!;
        sequence.Width = 180; sequence.Height = 320;
        var cue = SubtitleTrack.Add(project, "Primera línea\nSegunda línea", 0, 3);
        var subtitle = SubtitleTrack.Find(sequence)!;
        var normal = CollisionEngine.AddTrack(sequence, "video");
        Assert.Equal("subtitle", subtitle.Role);
        Assert.Equal("normal", normal.Role);
        Assert.Equal("V3", normal.Name);
        using var bitmap = SKBitmap.Decode(TextMediaService.Resolve(project, cue));
        Assert.Equal(180, bitmap.Width);
        Assert.Equal(320, bitmap.Height);
        Assert.Equal(0, bitmap.GetPixel(2, 2).Alpha);
        var ink = 0;
        for (var y = 200; y < 315; y++)
            for (var x = 5; x < 175; x++)
                if (bitmap.GetPixel(x, y).Alpha > 20) ink++;
        Assert.True(ink > 40);
    }
}
