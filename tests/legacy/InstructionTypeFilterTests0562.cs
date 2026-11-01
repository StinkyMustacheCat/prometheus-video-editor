using System;
using System.Collections.Generic;
using System.Linq;
using Prometheus;

class InstructionTypeFilterTests0562 {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static Clip Add(Project p,string kind,string track,double start){var asset=kind=="video"?GeneratedMedia.CreateSolid("Visual","334455",4):GeneratedMedia.CreateAudioPlaceholder("Audio","",4);p.assets.Add(asset);var clip=new Clip{id=Store.Id(),name=kind,asset_id=asset.id,stream_id=asset.streams[0].id,track_id=track,timeline_start=Rational.Frame(start),duration=Rational.Frame(4),source_in=Rational.Time(0)};p.Active.clips.Add(clip);return clip;}
 static InstructionPlan Review(Project p,InstructionAction action){var doc=Instructions.New(p);doc.actions.Add(action);return Instructions.Preview(p,doc);}
 static void Main(){
  var p=Store.New();var videoTrack=p.Active.tracks.First(t=>t.kind=="video");var audioTrack=p.Active.tracks.First(t=>t.kind=="audio");
  var video=Add(p,"video",videoTrack.id,0);var audio=Add(p,"audio",audioTrack.id,0);Store.Validate(p);
  var sharpen=Review(p,new InstructionAction{op="effect.apply",scope="sequence",catalog_id=EffectContracts.SharpenId});
  Check(sharpen.Cards[0].status=="valid"&&sharpen.Cards[0].changed==1&&sharpen.Cards[0].skipped==0,"Nitidez en secuencia solo cuenta vídeo");
  Check(sharpen.Result.Active.clips.Find(c=>c.id==video.id).effects.Count==1&&sharpen.Result.Active.clips.Find(c=>c.id==audio.id).effects.Count==0,"Nitidez no toca audio");
  var highPass=Review(p,new InstructionAction{op="effect.apply",scope="sequence",catalog_id=EffectContracts.HighPassId});
  Check(highPass.Cards[0].status=="valid"&&highPass.Cards[0].changed==1&&highPass.Cards[0].skipped==0,"Efecto de audio en secuencia solo cuenta audio");
  var selected=Review(p,new InstructionAction{op="effect.apply",scope="selection",clip_ids=new List<string>{video.id,audio.id},catalog_id=EffectContracts.SharpenId});
  Check(selected.Cards[0].status=="valid"&&selected.Cards[0].changed==1&&selected.Cards[0].skipped==0,"Selección mixta no genera omisiones por tipo");
  var removal=Review(sharpen.Result,new InstructionAction{op="effect.remove",scope="sequence",catalog_id=EffectContracts.SharpenId});
  Check(removal.Cards[0].status=="valid"&&removal.Cards[0].changed==1&&removal.Cards[0].skipped==0,"Quitar efecto tampoco cuenta audio");
  var wrongTrack=Review(p,new InstructionAction{op="effect.apply",scope="track",track_id=audioTrack.id,catalog_id=EffectContracts.SharpenId});
  Check(wrongTrack.Cards[0].status=="invalid"&&wrongTrack.Cards[0].summary.Contains("no es compatible"),"Pista incompatible queda roja");
  var noTarget=Review(p,new InstructionAction{op="effect.apply",scope="clip",clip_id=audio.id,catalog_id=EffectContracts.SharpenId});
  Check(noTarget.Cards[0].status=="invalid"&&noTarget.Cards[0].summary.Contains("No hay clips de vídeo"),"Destino explícito incompatible queda rojo");
  var blocked=Store.Copy(p);blocked.Active.tracks.First(t=>t.id==videoTrack.id).locked=true;Store.Validate(blocked);
  var blockedPlan=Review(blocked,new InstructionAction{op="effect.apply",scope="sequence",catalog_id=EffectContracts.SharpenId});
  Check(blockedPlan.Cards[0].status=="invalid"&&blockedPlan.Cards[0].summary.Contains("bloqueadas"),"Bloqueo real se informa");
  var secondVideoTrack=blocked.Active.tracks.First(t=>t.kind=="video"&&t.id!=videoTrack.id);Add(blocked,"video",secondVideoTrack.id,5);Store.Validate(blocked);
  var partial=Review(blocked,new InstructionAction{op="effect.apply",scope="sequence",catalog_id=EffectContracts.SharpenId});
  Check(partial.Cards[0].status=="partial"&&partial.Cards[0].changed==1&&partial.Cards[0].skipped==1,"Solo el bloqueo real convierte la tarjeta en amarilla");
  Console.WriteLine("InstructionTypeFilterTests0562 OK");
 }
}
