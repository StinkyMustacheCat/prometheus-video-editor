using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Prometheus;

class InstructionTests053 {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static Clip Add(Project p,string name,string track,double start,double duration){
  var asset=GeneratedMedia.CreateSolid(name,"336699",duration);p.assets.Add(asset);
  var clip=new Clip{id=Store.Id(),name=name,asset_id=asset.id,stream_id=asset.streams[0].id,track_id=track,timeline_start=Rational.Frame(start),duration=Rational.Frame(duration),source_in=Rational.Time(0)};
  p.Active.clips.Add(clip);return clip;
 }
 static InstructionAction Effect(string op,string id,string scope,string track=null){return new InstructionAction{op=op,catalog_id=id,scope=scope,track_id=track};}
 static void Main(){
  var p=Store.New();var video=p.Active.tracks.First(t=>t.kind=="video");var audio=p.Active.tracks.First(t=>t.kind=="audio");
  var first=Add(p,"igual.mp4",video.id,0,5);var second=Add(p,"igual.mp4",video.id,5,5);Store.Validate(p);
  string state=Instructions.CopyState(p,false);Check(state.Contains(first.id)&&state.Contains(second.id)&&state.Contains("prometheus-state"),"Estado incompleto");
  var doc=Instructions.New(p);doc.actions.Add(Effect("effect.apply",EffectContracts.BasicColorId,"sequence"));
  var parsed=Instructions.Parse(Instructions.Encode(doc));var plan=Instructions.Preview(p,parsed);Check(plan.Result.Active.clips.All(c=>c.effects.Count==1)&&p.Active.clips.All(c=>c.effects.Count==0),"La revisión debe ser una copia");
  var editor=new Editor();editor.Reset(p);Instructions.Apply(editor,parsed,plan);Check(editor.Project.Active.clips.All(c=>c.effects.Count==1),"Aplicación de efecto");editor.Undo();Check(editor.Project.Active.clips.All(c=>c.effects.Count==0),"Deshacer lote");editor.Redo();Check(editor.Project.Active.clips.All(c=>c.effects.Count==1),"Rehacer lote");
  var stale=Instructions.Preview(editor.Project,parsed);Check(stale.ReviewedRevision==editor.Project.revision&&stale.ReviewedProjectId==editor.Project.id,"Una instrucción anterior se revisa en el estado actual del mismo proyecto");
  var trackDoc=Instructions.New(editor.Project);trackDoc.actions.Add(Effect("effect.apply",EffectContracts.BlurId,"track",video.id));trackDoc.actions.Add(new InstructionAction{op="clip.inherit_track_effects",clip_id=first.id,value=false});
  var trackPlan=Instructions.Preview(editor.Project,trackDoc);Check(trackPlan.Result.Active.tracks.First(t=>t.id==video.id).effects.Count==1&&trackPlan.Result.Active.clips.First(c=>c.id==first.id).inherit_track_effects==false,"Efecto de pista y exclusión");
  var transitions=Instructions.New(editor.Project);transitions.actions.Add(new InstructionAction{op="transition.apply",scope="sequence",catalog_id=TransitionContracts.DipBlack,duration_frames=20});
  var transPlan=Instructions.Preview(editor.Project,transitions);Check(transPlan.Result.Active.transitions.Count==1&&transPlan.Result.Active.transitions[0].left_clip_id==first.id,"Transición por corte");
  var remove=Instructions.New(transPlan.Result);remove.actions.Add(new InstructionAction{op="transition.remove",scope="sequence",catalog_id=TransitionContracts.DipBlack});Check(Instructions.Preview(transPlan.Result,remove).Result.Active.transitions.Count==0,"Quitar transición");
  var change=Instructions.New(editor.Project);change.actions.Add(new InstructionAction{op="clip.trim",clip_id=first.id,edge="right",duration_seconds=4.0,ripple=false});change.actions.Add(new InstructionAction{op="clip.move",clip_id=second.id,track_id=video.id,at_seconds=12.0,placement="empty"});change.actions.Add(new InstructionAction{op="clip.duplicate",clip_id=first.id,track_id=video.id,at_seconds=20.0,placement="empty"});
  var changed=Instructions.Preview(editor.Project,change);Check(changed.Result.Active.clips.Count==3&&Math.Abs(changed.Result.Active.clips.First(c=>c.id==first.id).duration.Seconds-4)<0.001&&Math.Abs(changed.Result.Active.clips.First(c=>c.id==second.id).timeline_start.Seconds-12)<0.001,"Trim, mover y duplicar");
  var bad=Instructions.New(editor.Project);bad.actions.Add(Effect("effect.apply",EffectContracts.BlurId,"track",video.id));bad.actions.Add(new InstructionAction{op="clip.move",clip_id="no-existe",at_seconds=5,placement="empty"});var mixed=Instructions.Preview(editor.Project,bad);Check(mixed.Cards[0].status=="valid"&&mixed.Cards[1].status=="invalid"&&editor.Project.Active.tracks.First(t=>t.id==video.id).effects.Count==0,"La revisión parcial no modifica el proyecto");
  bool unknown=false;try{Instructions.Parse(Instructions.Encode(doc).Replace("effect.apply","shell.execute"));}catch{unknown=true;}Check(unknown,"Operaciones desconocidas deben rechazarse");
  string lut=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","source","assets","luts","neutral.cube"));
  var lutDoc=Instructions.New(editor.Project);lutDoc.actions.Add(new InstructionAction{op="effect.apply",scope="clip",clip_id=first.id,catalog_id=EffectContracts.Lut3DId,resource_path=lut});
  var lutPlan=Instructions.Preview(editor.Project,lutDoc);Check(lutPlan.Result.effect_resources.Count==1&&lutPlan.Result.Active.clips.First(c=>c.id==first.id).effects.Any(e=>e.effect_id==EffectContracts.Lut3DId),"LUT por instrucción");
  var linked=Store.New();var vt=linked.Active.tracks.First(t=>t.kind=="video");var at=linked.Active.tracks.First(t=>t.kind=="audio");var lv=Add(linked,"video 1",vt.id,0,5);var rv=Add(linked,"video 2",vt.id,5,5);
  var la=GeneratedMedia.CreateAudioPlaceholder("audio 1","",5);var ra=GeneratedMedia.CreateAudioPlaceholder("audio 2","",5);linked.assets.Add(la);linked.assets.Add(ra);
  var lac=new Clip{id=Store.Id(),name=la.name,asset_id=la.id,stream_id=la.streams[0].id,track_id=at.id,timeline_start=Rational.Time(0),duration=Rational.Time(5),source_in=Rational.Time(0)};
  var rac=new Clip{id=Store.Id(),name=ra.name,asset_id=ra.id,stream_id=ra.streams[0].id,track_id=at.id,timeline_start=Rational.Time(5),duration=Rational.Time(5),source_in=Rational.Time(0)};linked.Active.clips.Add(lac);linked.Active.clips.Add(rac);
  linked.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=new List<string>{lv.id,lac.id}});linked.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=new List<string>{rv.id,rac.id}});Store.Validate(linked);
  var linkedDoc=Instructions.New(linked);linkedDoc.actions.Add(new InstructionAction{op="transition.apply",scope="sequence",catalog_id=TransitionContracts.DipBlack,duration_frames=20});
  var linkedPlan=Instructions.Preview(linked,linkedDoc);Check(linkedPlan.Result.Active.transitions.Count==2&&linkedPlan.Result.Active.transitions[0].group_id==linkedPlan.Result.Active.transitions[1].group_id,"Transición audiovisual vinculada");
  var linkedTrim=Instructions.New(linked);linkedTrim.actions.Add(new InstructionAction{op="clip.trim",clip_id=lv.id,edge="right",duration_seconds=4,ripple=false});
  var linkedTrimPlan=Instructions.Preview(linked,linkedTrim);Check(Math.Abs(linkedTrimPlan.Result.Active.clips.First(c=>c.id==lac.id).duration.Seconds-4)<0.001,"Recorte de vinculados");
  Console.WriteLine("InstructionTests053 OK");
 }
}
