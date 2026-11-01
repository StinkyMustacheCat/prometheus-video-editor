using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class EditorActionUiTests058 {
 static void Check(bool okay,string message){if(!okay)throw new Exception(message);}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();var project=Store.New();var track=project.Active.tracks.First(t=>t.kind=="video");var asset=GeneratedMedia.CreateSolid("Prueba","225588",10);project.assets.Add(asset);
  var clip=new Clip{id=Store.Id(),name="Prueba",asset_id=asset.id,stream_id=asset.streams[0].id,track_id=track.id,timeline_start=Rational.Frame(2),duration=Rational.Frame(4),source_in=Rational.Time(0)};project.Active.clips.Add(clip);Store.Validate(project);
  using(var form=new MainForm()){
   var flags=BindingFlags.Instance|BindingFlags.NonPublic;var editor=(Editor)typeof(MainForm).GetField("editor",flags).GetValue(form);editor.Reset(project);var timeline=(Timeline)typeof(MainForm).GetField("timeline",flags).GetValue(form);timeline.Project=editor.Project;int revision=editor.Project.revision;
   form.ExecuteEditorAction(new EditorActionRequest{id="playhead.seek",seconds=2,project_id=project.id});
   Check(Math.Abs(timeline.Cursor-2)<0.001,"Ubicar lector");
   form.ExecuteEditorAction(new EditorActionRequest{id="selection.clip.at_playhead"});Check(timeline.Selected==clip.id,"Seleccionar clip bajo lector");
   form.ExecuteEditorAction(new EditorActionRequest{id="playhead.step_frames",frames=5});Check(Math.Abs(timeline.Cursor-(2+5.0/60))<0.001,"Avanzar cinco fotogramas");
   form.ExecuteEditorAction(new EditorActionRequest{id="playhead.boundary.next"});Check(Math.Abs(timeline.Cursor-6)<0.001,"Siguiente límite de clip");
   form.ExecuteEditorAction(new EditorActionRequest{id="selection.clear"});Check(timeline.Selected==null&&timeline.Selection.Count==0,"Limpiar selección");
   Check(editor.Project.revision==revision,"Transporte y selección no cambian revisión");
  }
  Console.WriteLine("EditorActionUiTests058 OK");
 }
}
