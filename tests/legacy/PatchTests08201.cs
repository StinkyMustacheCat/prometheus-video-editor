using System;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class PatchTests08201 {
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("OK "+name);}
 static Clip Clip(string id,string asset,string stream,string track,double start,double duration){return new Clip{id=id,name=id,asset_id=asset,stream_id=stream,track_id=track,timeline_start=Rational.Frame(start),duration=Rational.Frame(duration),source_in=Rational.Time(0)};}
 [STAThread]static void Main(){
  var editor=new Editor();var sequence=editor.Project.Active;
  var videos=sequence.tracks.Where(t=>t.kind=="video").OrderBy(t=>t.order).ToList();var audio=sequence.tracks.First(t=>t.kind=="audio");
  editor.Project.assets.Add(new Asset{id="fixture",name="Fixture",source=new MediaSource{kind="local",uri="fixture.mp4"},presentation_origin=Rational.Time(0),streams={new StreamInfo{id="v",kind="video",ordinal=0,codec="h264",pts_origin=Rational.Time(0),time_base=new Rational(1,60),duration=Rational.Time(5)},new StreamInfo{id="a",kind="audio",ordinal=1,codec="aac",pts_origin=Rational.Time(0),time_base=new Rational(1,48000),duration=Rational.Time(5)}}});
  sequence.clips.Add(Clip("video","fixture","v",videos[1].id,0,1));sequence.clips.Add(Clip("audio","fixture","a",audio.id,0,1));sequence.link_groups.Add(new LinkGroup{id="linked",clip_ids={"video","audio"}});audio.locked=true;Store.Validate(editor.Project);
  using(var timeline=new Timeline{Project=editor.Project,Width=1000,Height=520}){var method=typeof(Timeline).GetMethod("GroupSelection",BindingFlags.Instance|BindingFlags.NonPublic);var ids=(System.Collections.Generic.List<string>)method.Invoke(timeline,new object[]{"video"});Check(ids.Contains("video")&&ids.Contains("audio"),"selección visual incluye ambos clips vinculados");}
  editor.MoveSelection(new System.Collections.Generic.List<string>{"video"},"video",0,videos[0].id);
  Check(editor.Project.Active.clips.First(c=>c.id=="video").track_id==videos[0].id&&editor.Project.Active.clips.First(c=>c.id=="audio").track_id==audio.id,"cambio de pista conserva audio bloqueado en A1");
  Check(editor.Project.Active.link_groups.Single().clip_ids.Count==2,"el vínculo permanece");
  bool rejected=false;try{editor.MoveSelection(new System.Collections.Generic.List<string>{"video"},"video",1,videos[0].id);}catch{rejected=true;}Check(rejected&&editor.Project.Active.clips.First(c=>c.id=="audio").timeline_start.Seconds==0,"movimiento temporal vinculado respeta bloqueo");
  using(var controls=new EffectControlsPanelView()){var handle=controls.Handle;var clip=editor.Project.Active.clips.First(c=>c.id=="video");controls.ShowClip(editor.Project,clip);var first=controls.Controls[0];controls.RefreshPlayhead(editor.Project,1.0/60);controls.ShowClip(editor.Project,clip);Check(Object.ReferenceEquals(first,controls.Controls[0])&&!first.IsDisposed,"cambio de fotograma no reconstruye controles");}
  editor.Commit(p=>{p.Active.clips.Add(Clip("later","fixture","v",videos[0].id,2,1));p.Active.clips.Add(Clip("adjacent","fixture","v",videos[0].id,3,1));});
  using(var timeline=new Timeline{Project=editor.Project,Width=1000,Height=520}){
   int x=(int)Math.Round(timeline.X(1.5)),y=timeline.TrackTop(videos[0].id)+20;
   var press=typeof(Timeline).GetMethod("OnMouseDown",BindingFlags.Instance|BindingFlags.NonPublic);
   press.Invoke(timeline,new object[]{new MouseEventArgs(MouseButtons.Left,1,x,y,0)});
   Check(timeline.SelectedGapTrack==videos[0].id,"clic selecciona hueco entre medios");
   press.Invoke(timeline,new object[]{new MouseEventArgs(MouseButtons.Left,1,x,y,0)});
   Check(timeline.SelectedGapTrack==videos[0].id,"segundo clic conserva hueco seleccionado");
   int cut=(int)Math.Round(timeline.X(3)),boundary=timeline.TrackTop(videos[0].id)+timeline.TrackHeight(videos[0].id)-1;
   Check(timeline.RollPairAt(new Point(cut,boundary),videos[0])==null,"borde entre pistas no captura rizo");
   var cue=SubtitleTrack0817.Add(editor.Project,"Prueba",3,1);
   using(var strip=new SubtitleStrip0817(timeline)){strip.SetExpanded(true);strip.SelectCue(cue.id);Check(timeline.HighlightedSubtitleId==null,"selección normal no extiende cortina");timeline.ActiveTool=Timeline.ToolMode.Razor;strip.RefreshToolState();Check(timeline.HighlightedSubtitleId==cue.id,"tijera muestra cortina de intervalo");timeline.ActiveTool=Timeline.ToolMode.Select;strip.RefreshToolState();Check(timeline.HighlightedSubtitleId==null,"salir de tijera oculta cortina");}
  }
 }
}
