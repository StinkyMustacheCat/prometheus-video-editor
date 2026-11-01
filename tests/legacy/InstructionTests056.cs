using System;
using System.Collections.Generic;
using System.Linq;
using Prometheus;

class InstructionTests056 {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static Clip Add(Project p,string kind,string track,double start){
  Asset asset=kind=="video"?GeneratedMedia.CreateSolid("Visual","334455",4):GeneratedMedia.CreateAudioPlaceholder("Audio","",4);
  p.assets.Add(asset);var clip=new Clip{id=Store.Id(),name=asset.name,asset_id=asset.id,stream_id=asset.streams[0].id,track_id=track,timeline_start=Rational.Frame(start),duration=Rational.Frame(4),source_in=Rational.Time(0)};p.Active.clips.Add(clip);return clip;
 }
 static void Main(){
  var p=Store.New();var vt=p.Active.tracks.First(t=>t.kind=="video").id;var at=p.Active.tracks.First(t=>t.kind=="audio").id;
  var source=Add(p,"video",vt,0);var target=Add(p,"video",vt,5);var audio=Add(p,"audio",at,0);
  ClipFraming.Set(source,"fill",1.5,0.1,-0.2);source.effects.Add(EffectContracts.New(EffectContracts.BasicColorId));source.effects[0].parameters["contrast"].number=1.4;
  Store.Validate(p);
  var snapshot=ClipAttributes.Parse(ClipAttributes.Encode(ClipAttributes.Capture(p,source.id)));
  var document=Instructions.New(p);document.actions.Add(new InstructionAction{op="clip.paste_attributes",scope="selection",clip_ids=new List<string>{target.id,audio.id},attributes=snapshot});
  document.actions.Add(new InstructionAction{op="effect.apply",scope="sequence",catalog_id=EffectContracts.BlurId});
  document.actions.Add(new InstructionAction{op="clip.delete",clip_id="missing"});
  document.actions.Add(new InstructionAction{op="effect.apply",scope="clip",clip_id=target.id,catalog_id=EffectContracts.FlipId});
  var parsed=Instructions.Parse(Instructions.Encode(document));var plan=Instructions.Preview(p,parsed);
  Check(plan.Cards.Count==4&&plan.Cards[0].status=="partial"&&plan.Cards[1].status=="valid"&&plan.Cards[1].skipped==0&&plan.Cards[2].status=="invalid"&&plan.Cards[3].status=="valid","Los clips de audio quedan fuera del alcance del efecto visual sin aviso falso");
  Check(plan.Cards[0].details.Any(s=>s.Contains("Audio"))&&plan.Cards[2].details.Any(s=>s.Contains("Clip inexistente")),"Motivos individuales disponibles");
  var result=plan.Result.Active.clips.Find(c=>c.id==target.id);Check(ClipFraming.Read(result).scale==1.5&&result.effects.Select(e=>e.effect_id).SequenceEqual(new[]{EffectContracts.BasicColorId,EffectContracts.BlurId,EffectContracts.FlipId}),"Transformación y orden de efectos");
  Check(plan.Result.Active.clips.Find(c=>c.id==audio.id).effects.Count==0,"Audio no recibe efectos visuales");
  var editor=new Editor();editor.Reset(p);Instructions.Apply(editor,parsed,plan);Check(editor.Project.Active.clips.Find(c=>c.id==target.id).effects.Count==3,"Aplicación parcial del lote");editor.Undo();Check(editor.Project.Active.clips.Find(c=>c.id==target.id).effects.Count==0,"Undo único");
  var onlyInvalid=Instructions.New(p);onlyInvalid.actions.Add(new InstructionAction{op="clip.delete",clip_id="missing"});var denied=Instructions.Preview(p,onlyInvalid);Check(denied.ChangedCount==0&&denied.Cards[0].status=="invalid","Tarjeta roja sin aplicación");
  bool rejected=false;try{Instructions.Apply(editor,onlyInvalid,denied);}catch{rejected=true;}Check(rejected,"El lote enteramente inválido no se aplica");
  var resourceProject=Store.New();var resourceTrack=resourceProject.Active.tracks.First(t=>t.kind=="video").id;var withLut=Add(resourceProject,"video",resourceTrack,0);
  var resource=new EffectResource{id=Store.Id(),kind="lut3d",uri=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","source","assets","luts","neutral.cube"))};resourceProject.effect_resources.Add(resource);withLut.effects.Add(EffectContracts.NewLut3D(resource.id));Store.Validate(resourceProject);
  var other=Store.New();var otherTrack=other.Active.tracks.First(t=>t.kind=="video").id;var otherClip=Add(other,"video",otherTrack,0);Store.Validate(other);
  var resourceDoc=Instructions.New(other);resourceDoc.actions.Add(new InstructionAction{op="clip.paste_attributes",scope="clip",clip_id=otherClip.id,attributes=ClipAttributes.Capture(resourceProject,withLut.id)});
  var resourcePlan=Instructions.Preview(other,resourceDoc);var pasted=resourcePlan.Result.Active.clips.Find(c=>c.id==otherClip.id).effects.Single();Check(resourcePlan.Cards[0].status=="valid"&&resourcePlan.Result.effect_resources.Any(r=>r.id==pasted.parameters["lut"].resource_id&&r.uri==resource.uri),"El recurso LUT se remapea al pegar atributos de otro proyecto");
  Console.WriteLine("InstructionTests056 OK");
 }
}
