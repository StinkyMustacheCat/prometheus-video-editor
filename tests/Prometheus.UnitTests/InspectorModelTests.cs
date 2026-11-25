using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Undo;
using Xunit;

namespace Prometheus.UnitTests;

public class InspectorModelTests
{
    [Fact]
    public void VisualAndBasicColor_AreSavedAndUndoable()
    {
        var project = ProjectStore.CreateNew();
        var asset = new Asset { Id = "image", Name = "Imagen", Source = new MediaSource { Kind = "local", Uri = "image.png" } };
        asset.Streams.Add(new StreamInfo { Id = "stream", Kind = "video", Duration = Rational.Time(5) });
        project.Assets.Add(asset);
        project.ActiveSequence!.Clips.Add(new Clip { Id = "clip", Name = "Imagen", AssetId = asset.Id,
            StreamId = "stream", TrackId = project.ActiveSequence.Tracks.First(t => t.Kind == "video").Id });
        var history = new UndoRedoManager(project);
        history.Commit(p =>
        {
            var clip = p.ActiveSequence!.Clips.Single();
            clip.Visual.PositionX = 0.25;
            clip.Visual.ScaleY = 1.5;
            clip.Visual.RotationDegrees = 37;
            clip.Visual.Opacity = 0.6;
            BasicColorEffect.Set(clip, true, new BasicColorSettings(0.2, 1.3, 0.7));
        }, "Controles");
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(history.CurrentProject));
        var edited = restored.ActiveSequence!.Clips.Single();
        Assert.Equal(0.25, edited.Visual.PositionX);
        Assert.Equal(1.5, edited.Visual.ScaleY);
        Assert.Equal(37, edited.Visual.RotationDegrees);
        Assert.Equal(0.6, edited.Visual.Opacity);
        Assert.Equal(new BasicColorSettings(0.2, 1.3, 0.7), BasicColorEffect.Read(edited));
        Assert.True(history.Undo());
        Assert.Equal(1, history.CurrentProject.ActiveSequence!.Clips.Single().Visual.Opacity);
        Assert.Equal(0, history.CurrentProject.ActiveSequence.Clips.Single().Visual.RotationDegrees);
        Assert.Null(BasicColorEffect.Find(history.CurrentProject.ActiveSequence.Clips.Single()));
    }

    [Fact]
    public void DisabledColor_PreservesParametersButRendersNeutral()
    {
        var clip = new Clip();
        var values = new BasicColorSettings(-0.3, 1.7, 2);
        BasicColorEffect.Set(clip, true, values);
        BasicColorEffect.Set(clip, false, values);
        Assert.Equal(values, BasicColorEffect.ReadStored(clip));
        Assert.Equal(BasicColorSettings.Neutral, BasicColorEffect.Read(clip));
    }
}
