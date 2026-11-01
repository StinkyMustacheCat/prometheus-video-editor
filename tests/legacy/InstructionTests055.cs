using System;
using System.Collections.Generic;
using System.Linq;
using Prometheus;

class InstructionTests055 {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static Clip Add(Project p,string track,double start,double duration){var asset=GeneratedMedia.CreateSolid("Prueba","204060",duration);p.assets.Add(asset);var clip=new Clip{id=Store.Id(),name=asset.name,asset_id=asset.id,stream_id=asset.streams[0].id,track_id=track,timeline_start=Rational.Frame(start),duration=Rational.Frame(duration),source_in=Rational.Time(0)};p.Active.clips.Add(clip);return clip;}
 static void Main(){
  var p=Store.New();string track=p.Active.tracks.First(t=>t.kind=="video").id;var first=Add(p,track,0,5);var second=Add(p,track,5,5);Store.Validate(p);
  var doc=Instructions.New(p);
  doc.actions.Add(new InstructionAction{op="clip.cut",clip_id=first.id,at_seconds=2,result="split"});
  doc.actions.Add(new InstructionAction{op="clip.delete",clip_id="$split.right",ripple=false});
  doc.actions.Add(new InstructionAction{op="gap.close",track_id=track,at_seconds=3});
  doc.actions.Add(new InstructionAction{op="effect.apply",scope="clip",clip_id=second.id,catalog_id=EffectContracts.BasicColorId,result="fx"});
  doc.actions.Add(new InstructionAction{op="effect.set_parameter",clip_id=second.id,effect_id="$fx",parameter_id="contrast",number=1.5});
  var parsed=Instructions.Parse(Instructions.Encode(doc));var plan=Instructions.Preview(p,parsed);
  Check(p.Active.clips.Count==2&&plan.Result.Active.clips.Count==2,"Revisión no altera el original y conserva dos clips");
  Check(plan.Result.Active.clips.Find(c=>c.id==second.id).timeline_start.Seconds==2,"Cierre de hueco");
  Check(Math.Abs(plan.Result.Active.clips.Find(c=>c.id==second.id).effects[0].parameters["contrast"].number.Value-1.5)<0.001,"Referencia a efecto creado");
  Check(plan.Changes.Any(x=>x.Contains("eliminados")),"Resumen estructural");
  var editor=new Editor();editor.Reset(p);Instructions.Apply(editor,parsed,plan);Check(editor.Project.Active.clips.Find(c=>c.id==second.id).timeline_start.Seconds==2,"Aplicación de lote");editor.Undo();Check(editor.Project.Active.clips.Count==2&&editor.Project.Active.clips.Find(c=>c.id==second.id).timeline_start.Seconds==5,"Un solo deshacer");
  var structure=Instructions.New(p);structure.actions.Add(new InstructionAction{op="sequence.create",name="Vertical",width=1080,height=1920,fps=60,result="seq"});structure.actions.Add(new InstructionAction{op="track.create",sequence_id="$seq",source_kind="video",name="B-roll",result="track"});structure.actions.Add(new InstructionAction{op="track.set_locked",sequence_id="$seq",track_id="$track",enabled=true});
  var structural=Instructions.Preview(p,Instructions.Parse(Instructions.Encode(structure)));Check(structural.Result.sequences.Count==2,"Secuencia creada");var newSequence=structural.Result.sequences.First(x=>x.name=="Vertical");Check(newSequence.width==1080&&newSequence.height==1920&&newSequence.fps.n==60&&newSequence.tracks.Any(x=>x.name=="B-roll"&&x.locked),"Pista creada y referenciada");
  var invalid=Instructions.New(p);invalid.actions.Add(new InstructionAction{op="clip.delete",clip_id="no-existe"});var invalidPlan=Instructions.Preview(p,invalid);Check(invalidPlan.Cards[0].status=="invalid"&&invalidPlan.ChangedCount==0,"ID inexistente se marca sin aplicación");
  bool extra=false;try{Instructions.Parse("{\"format\":\"prometheus-instructions\",\"version\":1,\"project_id\":\""+p.id+"\",\"revision\":0,\"actions\":[{\"op\":\"clip.delete\",\"clip_id\":\""+first.id+"\",\"url\":\"https://example.com\"}]}");}catch{extra=true;}Check(extra,"Campos ajenos a la acción se rechazan");
  var range=Instructions.New(p);range.actions.Add(new InstructionAction{op="range.trim",at_seconds=1,finish_seconds=3});var rangePlan=Instructions.Preview(p,range);Check(rangePlan.Result.Active.clips.Count==3&&rangePlan.Result.Active.clips.Any(c=>c.timeline_start.Seconds==3),"Recorte de rango general");
  var effects=Instructions.New(p);effects.actions.Add(new InstructionAction{op="effect.apply",scope="clip",clip_id=first.id,catalog_id=EffectContracts.BasicColorId,result="basic"});effects.actions.Add(new InstructionAction{op="effect.apply",scope="clip",clip_id=first.id,catalog_id=EffectContracts.BlurId,result="blur"});effects.actions.Add(new InstructionAction{op="effect.move",clip_id=first.id,effect_id="$blur",index=0});effects.actions.Add(new InstructionAction{op="effect.set_enabled",clip_id=first.id,effect_id="$basic",enabled=false});
  var effectsPlan=Instructions.Preview(p,effects);var effectList=effectsPlan.Result.Active.clips.Find(c=>c.id==first.id).effects;Check(effectList.Count==2&&effectList[0].effect_id==EffectContracts.BlurId&&!effectList[1].enabled,"Orden y activación de instancias");
  Console.WriteLine("InstructionTests055 OK");
 }
}
