using System.Text.Json;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Prometheus.Core.Undo;

namespace Prometheus.UnitTests;

public class SubtitleStability3195Tests
{
    private static Project Fixture()
    {
        var project=ProjectStore.CreateNew();
        var sequence=project.ActiveSequence!;
        sequence.Transcriptions.Add(new TranscriptDocument { Words=[new(){Text="Uno",Start=0,End=.2},new(){Text="dos",Start=.8,End=1}] });
        TranscriptOperations.Apply(project,sequence.Transcriptions[0].Id);
        SubtitleAnimation.Set(sequence,SubtitleAnimationTests.Template);
        return project;
    }

    [Fact]
    public void TrimmingAlignedCaptionIntoSilenceDoesNotResurrectFullPhrase()
    {
        var project=Fixture();var sequence=project.ActiveSequence!;var clip=sequence.Clips.Single();
        clip.SourceIn=Rational.Time(.3);clip.Duration=Rational.Time(.3);
        var data=SubtitleAnimation.BuildTranscript(project);
        Assert.Empty(data.cues);
        Assert.Empty(SubtitleRenderPlan.At(project,.1,"video"));
        Assert.Equal("Uno dos",project.Assets.Single().TextOverlay!.Content);
        clip.SourceIn=Rational.Time(.1);SubtitleRenderPlan.Invalidate(project);
        var cue=Assert.Single(SubtitleAnimation.BuildTranscript(project).cues);
        Assert.Equal("Uno",cue.text);Assert.Equal("word",cue.timing);
    }

    [Fact]
    public void ManualAndEditedCaptionsDoNotDowngradePreciselyTimedNeighbors()
    {
        var project=Fixture();var sequence=project.ActiveSequence!;
        var manual=SubtitleTrack.Add(project,"Manual",2,1);
        var edited=SubtitleTrack.Add(project,"Editada",4,1);
        edited.CaptionWords=[new CaptionWord("old","Original",0,1)];
        var data=SubtitleAnimation.BuildTranscript(project);
        Assert.Equal("word",data.timing);
        Assert.Equal("word",data.cues[0].timing);Assert.Equal(2,data.cues[0].words!.Count);
        Assert.All(data.cues.Skip(1),cue=>{Assert.Equal("cue",cue.timing);Assert.Single(cue.words!);Assert.Equal(cue.text,cue.words![0].text);});
        Assert.Equal(manual.Duration.Seconds,data.cues[1].words![0].end-data.cues[1].words![0].start);
        var json=JsonSerializer.Serialize(data);
        Assert.Contains("\"timing\":\"word\"",json);Assert.Contains("\"timing\":\"cue\"",json);
    }

    [Fact]
    public void AlignedWordsUseExactHalfOpenClipBoundariesWithoutMicrosecondThreshold()
    {
        var project=Fixture();var clip=project.ActiveSequence!.Clips.Single();
        project.Assets.Single().TextOverlay!.Content="Micro";
        clip.CaptionWords=[new CaptionWord("micro","Micro",.5,.5000001)];
        clip.Duration=Rational.Time(1);
        Assert.Equal("Micro",Assert.Single(SubtitleAnimation.BuildTranscript(project).cues).text);
        clip.SourceIn=Rational.Time(.5);clip.Duration=Rational.Time(.25);
        var word=Assert.Single(Assert.Single(SubtitleAnimation.BuildTranscript(project).cues).words!);
        Assert.Equal(0,word.start);Assert.InRange(word.end,0.00000009,0.00000011);
        clip.CaptionWords=[new CaptionWord("edge","Micro",0,.5)];
        Assert.Empty(SubtitleAnimation.BuildTranscript(project).cues);
    }

    [Fact]
    public void InPlaceEditBoundaryRefreshesTextTimingAndDurationOnSameSequence()
    {
        var project=Fixture();var sequence=project.ActiveSequence!;var layer=SubtitleRenderPlan.At(project,.1,"video").Single();
        var before=SubtitleRenderPlan.Request(project,layer.Clip)!;
        var clip=sequence.Clips.Single();project.Assets.Single().TextOverlay!.Content="Uno tres";
        clip.CaptionWords![1]=clip.CaptionWords[1] with{Text="tres"};
        clip.TimelineStart=Rational.Time(5);clip.Speed=Rational.Time(2);clip.Duration=Rational.Time(.5);
        SubtitleRenderPlan.Invalidate(project);
        var after=SubtitleRenderPlan.Request(project,layer.Clip)!;
        Assert.NotSame(before,after);Assert.NotEqual(before.Signature,after.Signature);
        Assert.Equal("Uno tres",after.Data.cues[0].text);Assert.Equal(5.4,after.Data.cues[0].words![1].start,6);
        Assert.Equal(5.5,after.Duration,6);Assert.Same(sequence,project.ActiveSequence);
    }

    [Fact]
    public void ReplacedSourcesAndRevisionInvalidateWithoutPerFrameTranscriptRebuild()
    {
        var project=Fixture();var sequence=project.ActiveSequence!;
        var layer=SubtitleRenderPlan.At(project,.1,"video").Single();var before=SubtitleRenderPlan.Request(project,layer.Clip)!;
        for(var i=0;i<100;i++)
        {
            var current=SubtitleRenderPlan.At(project,i/100d,"video").Single();
            Assert.Same(layer.Clip,current.Clip);Assert.Same(before,SubtitleRenderPlan.Request(project,current.Clip));
        }
        var snapshot=ProjectStore.Clone(project);
        snapshot.Assets.Single().TextOverlay!.Content="Fuente nueva";
        project.Assets=snapshot.Assets;
        var replaced=SubtitleRenderPlan.Request(project,layer.Clip)!;
        Assert.NotSame(before,replaced);Assert.Equal("Fuente nueva",replaced.Data.cues[0].text);
        project.Assets.Single().TextOverlay!.Content="Nueva revisión";project.Revision++;
        Assert.Equal("Nueva revisión",SubtitleRenderPlan.Request(project,layer.Clip)!.Data.cues[0].text);
    }

    [Fact]
    public void CanvasTemplateAndFpsChangesRefreshLightweightSceneGuard()
    {
        var project=Fixture();var sequence=project.ActiveSequence!;
        var layer=SubtitleRenderPlan.At(project,.1,"video").Single();var original=SubtitleRenderPlan.Asset(project,layer.Clip)!;
        sequence.Width=1080;sequence.Height=1920;sequence.Fps=new Rational(30000,1001);
        var portrait=SubtitleRenderPlan.Asset(project,layer.Clip)!;
        Assert.NotEqual(original.Source.Uri,portrait.Source.Uri);
        Assert.True(portrait.Streams[0].Height>portrait.Streams[0].Width);
        Assert.Equal(sequence.Fps.Seconds,SubtitleRenderPlan.Request(project,layer.Clip)!.Fps);
        sequence.SubtitleAnimation!.TemplateJson=SubtitleAnimation.NormalizeTemplate(SubtitleAnimationTests.Template.Replace("Subtitle QA","Changed QA"));
        Assert.NotEqual(portrait.Source.Uri,SubtitleRenderPlan.Asset(project,layer.Clip)!.Source.Uri);
    }

    [Fact]
    public void AddedCaptionsRefreshSceneAndParameterOnlyChangesRetainTranscriptRequest()
    {
        var project=Fixture();var sequence=project.ActiveSequence!;var layer=SubtitleRenderPlan.At(project,.1,"video").Single();
        var before=SubtitleRenderPlan.Request(project,layer.Clip)!;
        SubtitleAnimation.SetParameter(sequence.SubtitleAnimation!,"size",JsonSerializer.SerializeToElement(100));
        var current=SubtitleRenderPlan.At(project,.1,"video").Single();
        Assert.Same(before,SubtitleRenderPlan.Request(project,current.Clip));
        Assert.Equal(100,current.Clip.MgtOverrides["size"].GetInt32());
        SubtitleTrack.Add(project,"Nuevo",2,1);
        var added=SubtitleRenderPlan.Request(project,layer.Clip)!;
        Assert.Equal(2,added.Data.cues.Count);Assert.Equal(3,added.Duration);
    }

    [Fact]
    public void MixedPrecisionAndEmptyAlignedCaptionSurviveUndoAndReopen()
    {
        var project=Fixture();SubtitleTrack.Add(project,"Manual",2,1);
        var id=project.ActiveSequence!.Clips[0].Id;var undo=new UndoRedoManager(project);
        undo.Commit(p=>{var clip=p.ActiveSequence!.Clips.Single(c=>c.Id==id);clip.SourceIn=Rational.Time(.3);clip.Duration=Rational.Time(.3);},"Trim pause");
        Assert.Single(SubtitleAnimation.BuildTranscript(undo.CurrentProject).cues);
        Assert.True(undo.Undo());Assert.Equal("word",SubtitleAnimation.BuildTranscript(undo.CurrentProject).timing);
        Assert.True(undo.Redo());
        var reopened=ProjectStore.Deserialize(ProjectStore.Serialize(undo.CurrentProject));
        Assert.Equal("Manual",Assert.Single(SubtitleAnimation.BuildTranscript(reopened).cues).text);
    }
}
