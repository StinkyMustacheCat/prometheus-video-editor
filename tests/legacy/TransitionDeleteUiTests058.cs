using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class TransitionDeleteUiTests058 {
 static void Check(bool okay,string message){if(!okay)throw new Exception(message);}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();var project=Store.New();var track=project.Active.tracks.First(t=>t.kind=="video");var asset=GeneratedMedia.CreateSolid("Corte","445566",12);project.assets.Add(asset);
  var left=new Clip{id=Store.Id(),name="A",asset_id=asset.id,stream_id=asset.streams[0].id,track_id=track.id,timeline_start=Rational.Frame(0),duration=Rational.Frame(4),source_in=Rational.Time(1)};
  var right=new Clip{id=Store.Id(),name="B",asset_id=asset.id,stream_id=asset.streams[0].id,track_id=track.id,timeline_start=Rational.Frame(4),duration=Rational.Frame(4),source_in=Rational.Time(5)};
  project.Active.clips.Add(left);project.Active.clips.Add(right);Store.Validate(project);
  var editor=new Editor();editor.Reset(project);editor.SetTransition(left.id,right.id,TransitionContracts.DipBlack,30);Check(editor.Project.Active.transitions.Count>0,"Fixture con transición");
  using(var form=new MainForm()){
   var flags=BindingFlags.Instance|BindingFlags.NonPublic;
   var live=(Editor)typeof(MainForm).GetField("editor",flags).GetValue(form);live.Reset(editor.Project);
   var timeline=(Timeline)typeof(MainForm).GetField("timeline",flags).GetValue(form);timeline.Project=live.Project;
   var id=live.Project.Active.transitions[0].id;typeof(MainForm).GetField("selectedTransitionId",flags).SetValue(form,id);timeline.SelectedTransitionId=id;
   bool deleted=(bool)typeof(MainForm).GetMethod("DeleteSelectedTransition",flags).Invoke(form,new object[0]);
   Check(deleted&&live.Project.Active.transitions.Count==0,"Suprimir transición seleccionada");
   live.Undo();Check(live.Project.Active.transitions.Count>0,"Deshacer recupera la transición");
  }
  Console.WriteLine("TransitionDeleteUiTests058 OK");
 }
}
