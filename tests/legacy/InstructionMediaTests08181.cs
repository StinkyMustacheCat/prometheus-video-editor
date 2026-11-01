using System;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class InstructionMediaTests08181 {
 static void Check(bool yes,string message){if(!yes)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static void Main(string[] args){
  Tools.Root=Path.GetFullPath(args[0]);string path=Path.Combine(Tools.Root,"demo","video.mp4");
  var project=Store.New();string track=project.Active.tracks.First(t=>t.kind=="video").id;
  var document=Instructions.New(project);document.actions.Add(new InstructionAction{op="media.import_local",resource_path=path,track_id=track,at_seconds=0,placement="empty",result="clip"});
  document.actions.Add(new InstructionAction{op="clip.set_transform",clip_id="$clip",parameter_id="scale",number=1.25});
  var parsed=Instructions.Parse(Instructions.Encode(document));var media=new InstructionMediaAdapter();media.Prepare(project,parsed,CancellationToken.None);
  var plan=Instructions.Preview(project,parsed,media);Check(plan.Cards.All(c=>c.status=="valid")&&plan.Result.Active.clips.Count>0,"Archivo local y transformación se revisan juntos");
  var editor=new Editor();editor.Reset(project);Instructions.Apply(editor,parsed,plan,media);Check(editor.Project.assets.Any(a=>a.source!=null&&String.Equals(a.source.uri,path,StringComparison.OrdinalIgnoreCase)),"Archivo local añadido al proyecto");
  Check(Math.Abs(ClipFraming.Read(editor.Project.Active.clips.First(c=>c.track_id==track)).scale-1.25)<0.000001,"Referencia del clip importado resuelta");
  editor.Undo();Check(editor.Project.Active.clips.Count==0,"Importación y transformación se deshacen juntas");
  var mgt=Media.Inspect(path,CancellationToken.None);mgt.id=Store.Id();mgt.source.kind="mgt";mgt.extensions.Add(new Extension{@namespace=MgtMedia.Namespace,version="1",payload=new System.Collections.Generic.Dictionary<string,object>{{"parameters_json","[{\"label\":\"Texto\",\"parameters\":[{\"id\":\"title\",\"type\":\"text\",\"editable\":true,\"default\":\"Original\"}]}]"},{"overrides_json","{}"}}});
  var mgtProject=Store.New();mgtProject.assets.Add(mgt);string videoTrack=mgtProject.Active.tracks.First(t=>t.kind=="video").id;
  for(int i=0;i<2;i++)mgtProject.Active.clips.Add(new Clip{id=Store.Id(),name="MGT",asset_id=mgt.id,stream_id=mgt.streams.First(s=>s.kind=="video").id,track_id=videoTrack,timeline_start=Rational.Frame(i*2),duration=Rational.Frame(1),source_in=Rational.Time(0)});
  var first=mgtProject.Active.clips[0];var second=mgtProject.Active.clips[1];Store.Validate(mgtProject);
  var mgtDoc=Instructions.New(mgtProject);mgtDoc.actions.Add(new InstructionAction{op="mgt.set_parameter",clip_id=first.id,parameter_id="title",text="Nuevo"});
  var mgtParsed=Instructions.Parse(Instructions.Encode(mgtDoc));var mgtAdapter=new InstructionMediaAdapter();var mgtPlan=Instructions.Preview(mgtProject,mgtParsed,mgtAdapter);
  Check(mgtPlan.Cards[0].status=="valid"&&mgtPlan.MgtPreviewAssetIds.Count==1,"Parámetro MGT revisado y vista ligera programada");
  var mgtEditor=new Editor();mgtEditor.Reset(mgtProject);Instructions.Apply(mgtEditor,mgtParsed,mgtPlan,mgtAdapter);
  Check(mgtEditor.Project.Active.clips.Find(c=>c.id==first.id).asset_id!=mgtEditor.Project.Active.clips.Find(c=>c.id==second.id).asset_id,"Parámetro MGT crea instancia independiente");
  var changed=mgtEditor.Project.assets.Find(a=>a.id==mgtEditor.Project.Active.clips.Find(c=>c.id==first.id).asset_id);
  Check(Convert.ToString(MgtMedia.Overrides(changed)["title"])=="Nuevo","Parámetro MGT conserva nuevo valor");
  Console.WriteLine("InstructionMediaTests08181 OK");
 }
}
