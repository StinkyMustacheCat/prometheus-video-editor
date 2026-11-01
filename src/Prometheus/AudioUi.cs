using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 public partial class MainForm {
  List<string> AudioTargets(string anchor){
   var project=editor.Project;var ids=new HashSet<string>{anchor};
   if(timeline.Selection.Contains(anchor))foreach(string id in timeline.Selection)ids.Add(id);
   else {
    var group=project.Active.link_groups.FirstOrDefault(g=>g.clip_ids.Contains(anchor));
    if(group!=null&&group.clip_ids.Any(timeline.Selection.Contains)){
     foreach(string selected in timeline.Selection){
      ids.Add(selected);
      foreach(var linked in project.Active.link_groups.Where(g=>g.clip_ids.Contains(selected)))foreach(string id in linked.clip_ids)ids.Add(id);
     }
    }
   }
   return project.Active.clips.Where(c=>ids.Contains(c.id)&&project.Active.tracks.Any(t=>t.id==c.track_id&&t.kind=="audio")).Select(c=>c.id).ToList();
  }
  void UpdateAudioNumber(bool onTrack,string ownerId,double value,bool balance){
   try{
    var ids=onTrack?null:AudioTargets(ownerId);
    Edit(()=>editor.Commit(p=>{
     if(onTrack){var track=p.Active.tracks.Find(t=>t.id==ownerId);if(track==null||track.kind!="audio"||track.locked)throw new Exception("Pista de audio bloqueada o ausente");if(balance)track.balance=value;else track.gain_db=value;}
     else foreach(string id in ids){var clip=p.Active.clips.Find(c=>c.id==id);if(clip==null)continue;var track=p.Active.tracks.Find(t=>t.id==clip.track_id);if(track==null||track.kind!="audio"||track.locked)throw new Exception("Clip de audio bloqueado o ausente");if(balance)clip.balance=value;else clip.gain_db=value;}
    }));
   }catch(Exception ex){Error(ex);ShowClip();}
  }
  void ApplyVoiceClean(string anchor){
   try{var ids=AudioTargets(anchor);if(ids.Count==0)throw new Exception("Selecciona un clip de voz");
    Edit(()=>editor.Commit(p=>{foreach(var id in ids){var clip=p.Active.clips.Find(c=>c.id==id);var track=p.Active.tracks.Find(t=>t.id==clip.track_id);if(track.locked)throw new Exception("La pista de voz está bloqueada");var existing=clip.effects.FirstOrDefault(e=>e.effect_id==EffectContracts.VoiceCleanId);if(existing==null)clip.effects.Add(EffectContracts.New(EffectContracts.VoiceCleanId));else existing.enabled=true;}}));
    status.Text="Limpieza de voz aplicada · ajusta la intensidad en Controles de efectos";
   }catch(Exception ex){Error(ex);ShowClip();}
  }
  void SetAutoDucking(string targetId,string voiceId){try{Edit(()=>editor.Commit(p=>{var target=p.Active.tracks.Find(t=>t.id==targetId);if(target==null||target.locked)throw new Exception("La pista de fondo está bloqueada");var rule=Ducking01016.For(p.Active,targetId)??new DuckingRule01016{target_track_id=targetId};rule.voice_track_id=voiceId;Ducking01016.Set(p.Active,rule);}));status.Text="Auto-Ducking activo · la voz atenúa la pista de fondo";}catch(Exception ex){Error(ex);ShowClip();}}
  void RemoveAutoDucking(string targetId){try{Edit(()=>editor.Commit(p=>Ducking01016.Remove(p.Active,targetId)));status.Text="Auto-Ducking retirado";}catch(Exception ex){Error(ex);ShowClip();}}
  void SetAutoDuckingParameter(string targetId,string parameter,double value){try{Edit(()=>editor.Commit(p=>{var rule=Ducking01016.For(p.Active,targetId);if(rule==null)throw new Exception("Activa primero Auto-Ducking");switch(parameter){case "amount_db":rule.amount_db=value;break;case "threshold_db":rule.threshold_db=value;break;case "attack_ms":rule.attack_ms=value;break;case "release_ms":rule.release_ms=value;break;default:throw new Exception("Parámetro de Auto-Ducking desconocido");}Ducking01016.Set(p.Active,rule);}));}catch(Exception ex){Error(ex);ShowClip();}}
 }
}
