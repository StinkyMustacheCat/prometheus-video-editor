using System;
using System.Linq;
using Prometheus;

class InstructionRevisionTests0561 {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Main(){
  var project=Store.New();var track=project.Active.tracks.First(t=>t.kind=="video");var asset=GeneratedMedia.CreateSolid("Prueba","336699",4);project.assets.Add(asset);
  var clip=new Clip{id=Store.Id(),name="Prueba",asset_id=asset.id,stream_id=asset.streams[0].id,track_id=track.id,timeline_start=Rational.Frame(0),duration=Rational.Frame(4),source_in=Rational.Time(0)};
  project.Active.clips.Add(clip);Store.Validate(project);
  var document=Instructions.New(project);
  document.actions.Add(new InstructionAction{op="effect.apply",scope="clip",clip_id=clip.id,catalog_id=EffectContracts.BlurId});
  document.actions.Add(new InstructionAction{op="clip.move",clip_id=clip.id,track_id=track.id,at_seconds=10,placement="empty"});
  document.actions.Add(new InstructionAction{op="clip.delete",clip_id="desaparecido"});
  var editor=new Editor();editor.Reset(project);editor.Commit(p=>p.name="Proyecto actualizado");
  var plan=Instructions.Preview(editor.Project,document);
  Check(plan.ReviewedProjectId==project.id&&plan.ReviewedRevision==editor.Project.revision,"La revisión se ata al estado actual");
  Check(plan.Cards[0].status=="valid"&&plan.Cards[1].status=="partial"&&plan.Cards[1].details.Any(s=>s.Contains("revisión"))&&plan.Cards[2].status=="invalid","Coincidencia por ID, posición con atención e ID ausente");
  editor.Commit(p=>p.name="Cambio posterior");bool staleRejected=false;
  try{Instructions.Apply(editor,document,plan);}catch(Exception ex){staleRejected=ex.Message.Contains("después de la revisión");}
  Check(staleRejected,"Una revisión anterior no se aplica tras otro cambio");
  var refreshed=Instructions.Preview(editor.Project,document);Instructions.Apply(editor,document,refreshed);
  Check(editor.Project.Active.clips.Single().timeline_start.Seconds==10&&editor.Project.Active.clips.Single().effects.Count==1,"El lote revisado se aplica sobre el proyecto actualizado");
  var other=Store.New();bool foreignRejected=false;try{Instructions.Preview(other,document);}catch(Exception ex){foreignRejected=ex.Message.Contains("no coincide");}
  Check(foreignRejected,"Otro proyecto no se revisa por coincidencias superficiales");
  Console.WriteLine("InstructionRevisionTests0561 OK");
 }
}
