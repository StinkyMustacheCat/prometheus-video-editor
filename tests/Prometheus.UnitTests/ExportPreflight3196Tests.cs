using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;

namespace Prometheus.UnitTests;

public class ExportPreflight3196Tests
{
    [Theory]
    [InlineData(64,64,true)]
    [InlineData(63,64,false)]
    [InlineData(8192,4150,true)]
    [InlineData(8192,4151,false)]
    [InlineData(8192,8192,false)]
    [InlineData(8193,64,false)]
    [InlineData(1921,1081,true)]
    public void CanvasAndResolutionFamiliesSharePixelBudgetWithoutForcingParity(int width,int height,bool valid)
    {
        var project=ProjectStore.CreateNew();var sequence=project.ActiveSequence!;
        if(valid)
        {
            SequenceOperations.SetCanvas(project,sequence.Id,width,height,false);
            Assert.Equal(width,sequence.Width);Assert.Equal(height,sequence.Height);
        }
        else
        {
            var before=ProjectStore.Serialize(project);
            Assert.Throws<ArgumentOutOfRangeException>(()=>SequenceOperations.SetCanvas(project,sequence.Id,width,height,true));
            Assert.Equal(before,ProjectStore.Serialize(project));
        }
    }

    [Fact]
    public void PresetFamiliesCannotProduceOverBudgetSquareCanvas()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>CanvasResolutions.AtShortEdge(1,1,8192));
        Assert.Equal((3840,2160),CanvasResolutions.AtShortEdge(16,9,2160));
        CanvasResolutions.ValidateDimensions(2,2,minimumSide:2);
        Assert.Throws<ArgumentOutOfRangeException>(()=>CanvasResolutions.ValidateDimensions(2,2));
    }

    [Theory]
    [InlineData(ExportFormat.Mp4,false)]
    [InlineData(ExportFormat.Hevc,false)]
    [InlineData(ExportFormat.ProRes422,false)]
    [InlineData(ExportFormat.ProRes4444,true)]
    [InlineData(ExportFormat.Wav,true)]
    public async Task OddCanvasIsAllowedAndOutputParityDependsOnFormat(ExportFormat format,bool ready)
    {
        var project=ProjectStore.CreateNew();var sequence=project.ActiveSequence!;
        SequenceOperations.SetCanvas(project,sequence.Id,1921,1081,false);
        sequence.Clips.Add(new Clip {TrackId=sequence.Tracks[0].Id,Duration=Rational.Time(.25)});
        var before=ProjectStore.Serialize(project);
        var inspection=await SequenceExportService.InspectAsync(project,null,null,new(false,12,192,format,AllowOffline:true));
        Assert.Equal(ready,inspection.Ready);Assert.Equal(before,ProjectStore.Serialize(project));
    }

    [Fact]
    public async Task AsyncInspectionHonorsCancellationBeforeStartingWork()
    {
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>SequenceExportService.InspectAsync(ProjectStore.CreateNew(),null,null,
            new(false,12,192),cancellation.Token));
    }

    [Fact]
    public void ExplicitProxyMappingSelectsTheMappedAudioStreamAndKeepsProjectUnchanged()
    {
        var path=Path.Combine(Path.GetTempPath(),"preflight3196-"+Guid.NewGuid()+".wav");
        try
        {
            using(var writer=new BinaryWriter(File.Create(path)))
            {writer.Write("RIFF"u8);writer.Write(96036);writer.Write("WAVEfmt "u8);writer.Write(16);writer.Write((short)1);writer.Write((short)1);
             writer.Write(48000);writer.Write(96000);writer.Write((short)2);writer.Write((short)16);writer.Write("data"u8);writer.Write(96000);writer.Write(new byte[96000]);}
            var project=ProjectStore.CreateNew();var sequence=project.ActiveSequence!;
            project.Assets.Add(new Asset {Id="web",Source=new MediaSource{Kind="web",Uri="https://example.invalid/source"},
                Streams=[new StreamInfo{Id="sound",Kind="audio",Ordinal=9,Duration=Rational.Time(1)}],
                Representations=[new MediaRepresentation{Role="preview",Locator=new Locator(path),SizeBytes=new FileInfo(path).Length,
                    ModifiedUtc=File.GetLastWriteTimeUtc(path).ToString("O"),StreamOrdinals=new(){["sound"]=0}}]});
            sequence.Clips.Add(new Clip{AssetId="web",StreamId="sound",TrackId=sequence.Tracks.First(t=>t.Kind=="audio").Id,Duration=Rational.Time(1)});
            var before=ProjectStore.Serialize(project);var plan=new ExportMediaPlan(project,null,0,1,true);
            Assert.Equal(0,Assert.Single(plan.Audio.Inputs).StreamOrdinal);
            Assert.Equal("web_preview",Assert.Single(plan.Warnings).Substitution);
            Assert.Equal(before,ProjectStore.Serialize(project));
        }
        finally {File.Delete(path);}
    }
}
