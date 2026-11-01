using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 public sealed class ClipAttributeSnapshot {
  public string format="prometheus-clip-attributes";
  public int version=1;
  public string source_kind;
  public ClipFrame framing;
  public double gain_db,balance;
  public string blend_mode="normal";
  public List<EffectInstance> effects=new List<EffectInstance>();
  public List<EffectResource> resources=new List<EffectResource>();
 }
 public sealed class AttributeApplyResult {
  public int applied,skipped;
  public readonly List<string> reasons=new List<string>();
 }
 public static class ClipAttributes {
  public static ClipAttributeSnapshot Capture(Project project,string clipId){
   var clip=project.Active.clips.Find(c=>c.id==clipId);
   if(clip==null)throw new Exception("Selecciona un clip para copiar sus atributos");
   var track=project.Active.tracks.Find(t=>t.id==clip.track_id);
   if(track==null)throw new Exception("La pista del clip no está disponible");
   var result=new ClipAttributeSnapshot{source_kind=track.kind,framing=track.kind=="video"?ClipFraming.Read(clip):null,gain_db=clip.gain_db,balance=clip.balance,blend_mode=track.kind=="video"?BlendModes01034.Read(clip):"normal",
    effects=Store.Json().Deserialize<List<EffectInstance>>(Store.Json().Serialize(Store.Fields(clip.effects)))};
   var used=new HashSet<string>(result.effects.SelectMany(e=>e.parameters.Values).Where(v=>v.kind=="resource").Select(v=>v.resource_id));
   result.resources=Store.Json().Deserialize<List<EffectResource>>(Store.Json().Serialize(Store.Fields(project.effect_resources.Where(r=>used.Contains(r.id)).ToList())));
   return result;
  }
  public static ClipAttributeSnapshot Parse(string text){
   if(String.IsNullOrWhiteSpace(text)||text.Length>512*1024)throw new Exception("Atributos copiados no válidos");
   ClipAttributeSnapshot data;
   try{data=Store.Json().Deserialize<ClipAttributeSnapshot>(text);}catch(Exception ex){throw new Exception("Atributos copiados no válidos: "+ex.Message);}
   if(data==null||data.format!="prometheus-clip-attributes"||data.version!=1||(data.source_kind!="video"&&data.source_kind!="audio")||data.effects==null||data.resources==null)
    throw new Exception("El portapapeles no contiene atributos compatibles");
   if(data.source_kind=="video"&&data.framing==null)throw new Exception("Falta el encuadre del clip copiado");
   if(String.IsNullOrEmpty(data.blend_mode))data.blend_mode="normal";
   if(!BlendModes01034.Modes.Contains(data.blend_mode))throw new Exception("Modo de fusión copiado no válido");
   return data;
  }
  public static string Encode(ClipAttributeSnapshot data){return Store.Json().Serialize(Store.Fields(data));}
  public static AttributeApplyResult Apply(Project project,ClipAttributeSnapshot data,IEnumerable<string> targetIds){
   if(data==null||data.effects==null||data.resources==null)throw new Exception("Faltan atributos para pegar");
   var result=new AttributeApplyResult();
   var sequence=project.Active;
   foreach(string id in (targetIds??new string[0]).Distinct()){
    var target=sequence.clips.Find(c=>c.id==id);
    if(target==null){result.skipped++;result.reasons.Add("Clip inexistente: "+id);continue;}
    var track=sequence.tracks.Find(t=>t.id==target.track_id);
    if(track==null||track.locked||track.kind!=data.source_kind){result.skipped++;result.reasons.Add("«"+target.name+"»: "+(track==null?"pista inexistente":track.locked?"pista bloqueada":"tipo de medio incompatible"));continue;}
    var copied=Store.Json().Deserialize<List<EffectInstance>>(Store.Json().Serialize(Store.Fields(data.effects)));
    foreach(var effect in copied){
     effect.id=Store.Id();
     foreach(var value in effect.parameters.Values.Where(v=>v.kind=="resource")){
      var source=data.resources.Find(r=>r.id==value.resource_id);
      if(source==null)throw new Exception("Falta el archivo de un efecto copiado");
      var existing=project.effect_resources.Find(r=>r.kind==source.kind&&String.Equals(r.uri,source.uri,StringComparison.OrdinalIgnoreCase));
      if(existing==null){existing=new EffectResource{id=Store.Id(),kind=source.kind,uri=source.uri,relative=source.relative};project.effect_resources.Add(existing);}
      value.resource_id=existing.id;
     }
    }
    EffectContracts.Validate(project,copied,track.kind);
    if(track.kind=="video"){
     ClipFraming.Set(target,data.framing.mode,data.framing.scale,data.framing.x,data.framing.y);
     ClipFraming.SetOpacity(target,data.framing.opacity);
     BlendModes01034.Set(target,data.blend_mode??"normal");
    }else{target.gain_db=data.gain_db;target.balance=data.balance;}
    target.effects=copied;
    result.applied++;
   }
   return result;
  }
  public static void Reset(Project project,IEnumerable<string> targetIds){
   foreach(var id in targetIds.Distinct()){var clip=project.Active.clips.Find(c=>c.id==id);if(clip==null)throw new Exception("Clip no disponible");var track=project.Active.tracks.Find(t=>t.id==clip.track_id);if(track==null||track.locked)throw new Exception("La pista está bloqueada");
    clip.effects.Clear();clip.animation.Clear();clip.mask=null;VectorMaskStack0104.Put(clip,null);TemporalMasks0823.Put(clip,null);Sam2MaskStore090.Put(clip,null);clip.inherit_track_effects=true;
    if(track.kind=="video"){ClipFraming.Set(clip,"fit",1,0,0);ClipFraming.SetOpacity(clip,1);BlendModes01034.Set(clip,"normal");}else{clip.gain_db=0;clip.balance=0;}
   }
  }
 }
}
