using System;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class MilestoneTests0914 {
 static int Main(string[] args){try{
  string source=Path.GetFullPath(args[0]),outDir=Path.GetFullPath(args[1]);Directory.CreateDirectory(outDir);
  var project=Store.New();SequenceCanvas.Set(project.Active,640,360);var asset=Media.Inspect(source,CancellationToken.None);project.assets.Add(asset);
  var video=project.Active.tracks.First(t=>t.kind=="video");Media.Insert(project,asset.id,video.id,0);
  var clip=project.Active.clips.First(c=>c.track_id==video.id);var zoom=EffectContracts.New(EffectContracts.ZoomInDynamicId);clip.effects.Add(zoom);clip.effects.Add(EffectContracts.New(EffectContracts.PanDynamicId));Store.Validate(project);
  using(var first=Rendering.FrameExact(project,.1,CancellationToken.None,640,360,false))first.Save(Path.Combine(outDir,"dynamic-first.png"));
  using(var last=Rendering.FrameExact(project,.8,CancellationToken.None,640,360,false))last.Save(Path.Combine(outDir,"dynamic-last.png"));
  string before=DynamicMotion0914.Read(project,clip).Scale(clip);clip.duration=Rational.Time(.5);string after=DynamicMotion0914.Read(project,clip).Scale(clip);if(before==after)throw new Exception("El zoom no siguió la nueva duración");Store.Validate(project);
  SequenceCanvas.Set(project.Active,640,480);if(!CanvasFit0913.Apply(project,clip,"adapt"))throw new Exception("No se adaptó el medio");var framing=ClipFraming.Read(clip);if(framing.scale<1||framing.scaleY<1)throw new Exception("Escala adaptada inválida");
  var editor=new Editor();editor.Reset(project);int beforeTracks=editor.Project.Active.tracks.Count(t=>t.kind=="video");editor.MoveSelectionAboveVideos(new System.Collections.Generic.List<string>{clip.id},clip.id,clip.timeline_start.Seconds,true);if(editor.Project.Active.tracks.Count(t=>t.kind=="video")!=beforeTracks+1)throw new Exception("No se creó la pista superior");
  Console.WriteLine("PASS 0.9.0.1/0.9.2/0.9.3/0.9.4");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
