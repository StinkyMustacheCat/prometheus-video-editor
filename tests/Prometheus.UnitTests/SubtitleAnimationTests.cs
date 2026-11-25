using System.Text.Json;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Prometheus.Core.Undo;

namespace Prometheus.UnitTests;

public class SubtitleAnimationTests
{
    public static string Template => JsonSerializer.Serialize(new { version=3,name="Subtitle QA",parameters=new object[] {
        new {id="color",label="Color",type="color",@default="#FFFFFFFF"},
        new {id="size",label="Size",type="number",@default=80,min=20,max=200}},
        subtitles=new{version=1,kind="template",timing="cue",tail=.25,cues=Array.Empty<object>()},
        html="<canvas></canvas>",css="",render="ctx.root.querySelector('canvas').width=ctx.width;" });

    private static (Project P, TranscriptDocument Doc) Fixture(bool linked=false)
    {
        var p=ProjectStore.CreateNew();var s=p.ActiveSequence!;
        var doc=new TranscriptDocument {TimelineStart=2,Words=[new(){Text="Uno",Start=0,End=.5},new(){Text="dos",Start=.5,End=1}]};
        if(linked)
        {
            var asset=new Asset {Id="audio-asset",Source=new MediaSource{Kind="file"},Streams=[new(){Id="audio-stream",Kind="audio",Duration=Rational.Time(10)}]};p.Assets.Add(asset);
            var audio=new Clip {Id="audio",TrackId=s.Tracks.First(t=>t.Kind=="audio").Id,AssetId=asset.Id,StreamId="audio-stream",TimelineStart=Rational.Time(2),Duration=Rational.Time(1)};
            s.Clips.Add(audio);doc.SourceClipId=audio.Id;doc.SourceTimeOrigin=0;
        }
        s.Transcriptions.Add(doc);TranscriptOperations.Apply(p,doc.Id);SubtitleAnimation.Set(s,Template);return(p,doc);
    }
    [Fact] public void TemplateIsEmbeddedAndUndoableWithoutAddingPoolAssets()
    {
        var (p,doc)=Fixture();var before=p.Assets.Count;
        var reloaded=ProjectStore.Deserialize(ProjectStore.Serialize(p));
        Assert.NotNull(reloaded.ActiveSequence!.SubtitleAnimation);
        Assert.Equal(before,reloaded.Assets.Count);
        Assert.Equal(2,reloaded.ActiveSequence.Clips.Single().CaptionWords!.Count);
        Assert.Equal(doc.Words[0].Id,reloaded.ActiveSequence.Clips.Single().CaptionWords![0].WordId);
        var undo=new UndoRedoManager(p);undo.Commit(x=>SubtitleAnimation.Set(x.ActiveSequence!,null),"Remove");
        Assert.Null(undo.CurrentProject.ActiveSequence!.SubtitleAnimation);undo.Undo();
        Assert.NotNull(undo.CurrentProject.ActiveSequence!.SubtitleAnimation);
    }
    [Fact] public void PhrasePreservesPreciseWordTimesAndSourceInAfterSplit()
    {
        var(p,_)=Fixture();var s=p.ActiveSequence!;
        var cue=s.Clips.Single();Assert.Equal(2,cue.CaptionWords!.Count);
        var split=CollisionEngine.SplitClip(s,cue.Id,2.5);
        var data=SubtitleAnimation.BuildTranscript(p);
        Assert.Equal(2,data.cues.Count);Assert.Equal("Uno",data.cues[0].text);Assert.Equal("dos",data.cues[1].text);
        Assert.Equal(2.5,data.cues[1].words![0].start,6);Assert.Equal(3,data.cues[1].words![0].end,6);
    }
    [Fact] public void AudioMoveSpeedAndTrimKeepWordAnimationAligned()
    {
        var(p,_)=Fixture(true);var before=ProjectStore.Clone(p);var s=p.ActiveSequence!;
        var audio=s.Clips.Single(c=>c.Id=="audio");audio.TimelineStart=Rational.Time(5);audio.Speed=Rational.Time(2);audio.Duration=Rational.Time(.5);
        TranscriptOperations.FollowAudioEdits(before,p);
        var data=SubtitleAnimation.BuildTranscript(p);Assert.Equal(5,data.cues[0].words![0].start,6);
        Assert.Equal(5.25,data.cues[0].words![1].start,6);Assert.Equal(5.5,data.cues[0].words![1].end,6);
        before=ProjectStore.Clone(p);audio.SourceIn=Rational.Time(.5);audio.Duration=Rational.Time(.25);
        TranscriptOperations.FollowAudioEdits(before,p);data=SubtitleAnimation.BuildTranscript(p);
        Assert.Equal("dos",data.cues[0].text);Assert.Equal(5,data.cues[0].words![0].start,6);
    }
    [Fact] public void EditedTextUsesPhraseTimingWithoutFabricatedWordTimes()
    {
        var(p,_)=Fixture();p.Assets.Single().TextOverlay!.Content="Frase completamente nueva";
        var data=SubtitleAnimation.BuildTranscript(p);Assert.Equal("cue",data.timing);
        Assert.Single(data.cues[0].words!);Assert.Equal("Frase completamente nueva",data.cues[0].words![0].text);
    }
    [Fact] public void ManualLeftTrimAndExtensionKeepTheCaptionWordClock()
    {
        var(p,_)=Fixture();var cue=p.ActiveSequence!.Clips.Single();
        ClipTrimOperations.ApplyEdge(p,cue.Id,true,2.5);
        var data=SubtitleAnimation.BuildTranscript(p);Assert.Equal("dos",data.cues[0].text);
        Assert.Equal(2.5,data.cues[0].words![0].start,6);
        ClipTrimOperations.ApplyEdge(p,cue.Id,true,1.5);
        data=SubtitleAnimation.BuildTranscript(p);Assert.Equal(2,data.cues[0].words![0].start,6);
        Assert.Equal(2.5,data.cues[0].words![1].start,6);
    }
    [Fact] public void PhraseOnlyImportDoesNotPretendToHaveWordAlignment()
    {
        var p=ProjectStore.CreateNew();var doc=TranscriptOperations.Parse("1\n00:00:00,000 --> 00:00:01,000\nToda la frase\n","SRT",0);
        p.ActiveSequence!.Transcriptions.Add(doc);TranscriptOperations.Apply(p,doc.Id);SubtitleAnimation.Set(p.ActiveSequence,Template);
        Assert.Null(p.ActiveSequence.Clips.Single().CaptionWords);
        var data=SubtitleAnimation.BuildTranscript(p);Assert.Equal("cue",data.timing);Assert.Single(data.cues[0].words!);
        Assert.Equal("Toda la frase",data.cues[0].words![0].text);
    }
    [Fact] public void ExistingCaptionsCanResolveTheirDocumentWithoutRegeneration()
    {
        var(p,_)=Fixture();p.ActiveSequence!.Clips.Single().CaptionWords=null;
        var data=SubtitleAnimation.BuildTranscript(p);Assert.Equal("word",data.timing);Assert.Equal(2,data.cues[0].words!.Count);
    }
    [Fact] public void ParametersValidateAndSurviveStorage()
    {
        var(p,_)=Fixture();var settings=p.ActiveSequence!.SubtitleAnimation!;
        SubtitleAnimation.SetParameter(settings,"color",JsonSerializer.SerializeToElement("#FF000080"));
        Assert.Throws<ArgumentException>(()=>SubtitleAnimation.SetParameter(settings,"size",JsonSerializer.SerializeToElement(999)));
        Assert.Throws<ArgumentException>(()=>SubtitleAnimation.SetParameter(settings,"missing",JsonSerializer.SerializeToElement(1)));
        Assert.Throws<ArgumentException>(()=>SubtitleAnimation.SetParameter(settings,"color",JsonSerializer.SerializeToElement("red")));
        Assert.Equal("#FF000080",ProjectStore.Clone(p).ActiveSequence!.SubtitleAnimation!.Parameters["color"].GetString());
    }
    [Fact] public void LoadedTranscriptCannotBeMistakenForTemplate()
    {
        Assert.Throws<ArgumentException>(()=>SubtitleAnimation.NormalizeTemplate(Template.Replace("\"template\"","\"composition\"")));
    }
    [Theory]
    [InlineData("\"wrong\"")]
    [InlineData("-1")]
    [InlineData("6")]
    public void RejectsInvalidExitTimingBeforeLoading(string tail)
    {
        Assert.Throws<ArgumentException>(()=>SubtitleAnimation.NormalizeTemplate(Template.Replace("\"tail\":0.25", "\"tail\":"+tail)));
    }
    [Fact] public void LivePlanHasOnePersistentLayerAcrossWordsAndNoDuplicateStaticText()
    {
        var(p,_)=Fixture();var count=p.Assets.Count;
        var first=SubtitleRenderPlan.At(p,2.1,"video");var second=SubtitleRenderPlan.At(p,2.6,"video");
        Assert.Single(first);Assert.Single(second);Assert.True(SubtitleRenderPlan.IsScene(first[0].Clip));
        Assert.Equal(first[0].Clip.Id,second[0].Clip.Id);Assert.Equal(count,p.Assets.Count);
        p=ProjectStore.Clone(p);p.ActiveSequence!.SubtitleAnimation!.Enabled=false;
        Assert.False(SubtitleRenderPlan.IsScene(SubtitleRenderPlan.At(p,2.1,"video")[0].Clip));
        p.ActiveSequence.Tracks.Single(t=>t.Role=="subtitle").Enabled=false;
        Assert.Empty(SubtitleRenderPlan.At(p,2.1,"video"));
    }
    [Fact] public void LiveSceneDoesNotPrewarmASeparateSourceForEverySubtitleWord()
    {
        var(p,_)=Fixture();
        Assert.Contains(2d,NestedRenderPlan.UpcomingStarts(p,0,3,"video"));
        Assert.Empty(NestedRenderPlan.UpcomingStarts(p,0,3,"video",includeSubtitles:false));
    }
}
