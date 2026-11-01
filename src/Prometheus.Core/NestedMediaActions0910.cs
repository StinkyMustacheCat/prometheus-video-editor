using System;
using System.Linq;

namespace Prometheus {
 public static partial class NestedMedia0910 {
  static Track FreeVideoTrack01024(Project project,System.Collections.Generic.List<Clip> selected,double start,double end){
   var ids=new System.Collections.Generic.HashSet<string>(selected.Select(c=>c.id));
   var sequence=project.Active;
   var available=sequence.tracks.Where(t=>t.kind=="video"&&!t.locked).OrderBy(t=>t.order)
    .FirstOrDefault(t=>!sequence.clips.Any(c=>!ids.Contains(c.id)&&c.track_id==t.id&&c.timeline_start.Seconds<end-0.000001&&c.End>start+0.000001));
   return available??Store.AddTrack(sequence,"video");
  }
  static void ConfigureInternalTrack(Sequence sequence,string originalId,Track target){var source=sequence.tracks.Find(t=>t.id==originalId);if(source==null)return;target.enabled=target.enabled&&source.enabled;target.gain_db=source.gain_db;target.balance=source.balance;target.effects_enabled=source.effects_enabled;target.effects=Store.Json().Deserialize<System.Collections.Generic.List<EffectInstance>>(Store.Json().Serialize(source.effects));}
  static void ApplyOuter(Clip outer,Clip child,bool audio,Track parentTrack){
   if(audio)return;
   var parent=ClipFraming.Read(outer);var own=ClipFraming.Read(child);
   if(Math.Abs(parent.scale-1)>0.000001||Math.Abs(parent.scaleY-1)>0.000001||Math.Abs(parent.x)>0.000001||Math.Abs(parent.y)>0.000001||Math.Abs(parent.opacity-1)>0.000001){
    bool linked=parent.scaleLinked&&own.scaleLinked;
    if(!linked&&own.scaleLinked)ClipFraming.SetScaleLinked(child,false);
    ClipFraming.Set(child,own.mode,Math.Max(.25,Math.Min(4,own.scale*parent.scale)),Math.Max(-1,Math.Min(1,own.x+parent.x)),Math.Max(-1,Math.Min(1,own.y+parent.y)));
    if(!linked)ClipFraming.SetScaleY(child,Math.Max(.25,Math.Min(4,own.scaleY*parent.scaleY)));
    ClipFraming.SetOpacity(child,own.opacity*parent.opacity);
   }
   foreach(var effect in outer.effects.Where(e=>e.enabled)){
    var copy=Store.Json().Deserialize<EffectInstance>(Store.Json().Serialize(Store.Fields(effect)));copy.id=Store.Id();child.effects.Add(copy);
   }
   if(outer.inherit_track_effects&&parentTrack.effects_enabled)foreach(var effect in parentTrack.effects.Where(e=>e.enabled)){
    var copy=Store.Json().Deserialize<EffectInstance>(Store.Json().Serialize(Store.Fields(effect)));copy.id=Store.Id();child.effects.Add(copy);
   }
   if(outer.effects.Any(e=>e.enabled&&(e.effect_id==EffectContracts.ZoomInDynamicId||e.effect_id==EffectContracts.ZoomOutDynamicId||e.effect_id==EffectContracts.PanDynamicId))||outer.inherit_track_effects&&parentTrack.effects_enabled&&parentTrack.effects.Any(e=>e.enabled&&(e.effect_id==EffectContracts.ZoomInDynamicId||e.effect_id==EffectContracts.ZoomOutDynamicId||e.effect_id==EffectContracts.PanDynamicId))){
    double start=outer.timeline_start.Seconds,duration=outer.duration.Seconds;var range=outer.extensions.FirstOrDefault(e=>e.@namespace=="prometheus.motion.preview-window");
    if(range!=null&&range.payload!=null){object value;if(range.payload.TryGetValue("start",out value))start=Convert.ToDouble(value);if(range.payload.TryGetValue("duration",out value))duration=Convert.ToDouble(value);}
    child.extensions.RemoveAll(e=>e.@namespace=="prometheus.motion.preview-window");
    child.extensions.Add(new Extension{@namespace="prometheus.motion.preview-window",version="1",payload=new System.Collections.Generic.Dictionary<string,object>{{"start",start},{"duration",duration}}});
   }
  }
  public static void UpdateChildLegacy(Project p,string outerId,string childId,double start,double duration,string targetTrackId){
   var outer=p.Active.clips.Find(c=>c.id==outerId);if(!IsNested(p,outer))throw new Exception("Medio anidado no encontrado");
   var content=Read(outer);var child=content.clips.Find(c=>c.id==childId);if(child==null)throw new Exception("Componente interno no encontrado");
   if(start<0||duration<1.0/60||start+duration>48*3600)throw new Exception("Rango interno inválido");
   bool audio=content.audio_orders.ContainsKey(child.track_id);
   if(targetTrackId!=null){if(audio?!content.audio_orders.ContainsKey(targetTrackId):!content.video_orders.ContainsKey(targetTrackId))throw new Exception("La pista interna no admite este medio");child.track_id=targetTrackId;}
   double previousEnd=child.End;
   bool leftTrim=Math.Abs(start-child.timeline_start.Seconds)>0.000001&&Math.Abs(duration-child.duration.Seconds)>0.000001&&Math.Abs(start+duration-previousEnd)<0.000001;
   double sourceIn=leftTrim?child.source_in.Seconds+(start-child.timeline_start.Seconds)*child.speed.Seconds:child.source_in.Seconds;
   var asset=p.assets.Find(a=>a.id==child.asset_id);var stream=asset==null?null:asset.streams.Find(s=>s.id==child.stream_id);
   if(sourceIn< -0.000001||stream!=null&&stream.duration!=null&&sourceIn+duration*child.speed.Seconds>stream.duration.Seconds+0.000001)throw new Exception("El recorte interno excede el medio original");
   if(content.clips.Any(c=>c.id!=child.id&&c.track_id==child.track_id&&c.timeline_start.Seconds<start+duration-0.000001&&c.End>start+0.000001))throw new Exception("El recorte interno se superpone con otro medio de la misma pista");
   child.source_in=Rational.Time(Math.Max(0,sourceIn));child.timeline_start=Rational.Frame(start);child.duration=Rational.Frame(duration);
   double sourceEnd=content.clips.Max(c=>c.End),required=Math.Max(1.0/60,(sourceEnd-outer.source_in.Seconds)/outer.speed.Seconds);
   p.assets.Find(a=>a.id==outer.asset_id).recommended_duration=Rational.Frame(sourceEnd);
   if(required<outer.duration.Seconds-0.000001)outer.duration=Rational.Frame(required);
   Write(outer,content);
  }
  public static void UpdateChildPropertiesLegacy(Project p,string outerId,string childId,double scaleX,double scaleY,double? blur,double gain){var outer=p.Active.clips.Find(c=>c.id==outerId);if(!IsNested(p,outer))throw new Exception("Medio anidado no encontrado");var content=Read(outer);var child=content.clips.Find(c=>c.id==childId);if(child==null)throw new Exception("Componente interno no encontrado");if(content.audio_orders.ContainsKey(child.track_id)){if(gain< -60||gain>12)throw new Exception("Volumen fuera de rango");child.gain_db=gain;}else{if(scaleX<.25||scaleX>4||scaleY<.25||scaleY>4)throw new Exception("Escala fuera de rango");var frame=ClipFraming.Read(child);if(Math.Abs(scaleX-scaleY)>.000001&&frame.scaleLinked)ClipFraming.SetScaleLinked(child,false);ClipFraming.Set(child,frame.mode,scaleX,frame.x,frame.y);if(!ClipFraming.Read(child).scaleLinked)ClipFraming.SetScaleY(child,scaleY);var effect=child.effects.FirstOrDefault(e=>e.effect_id==EffectContracts.BlurId);if(effect!=null&&blur.HasValue){if(blur.Value<.1||blur.Value>20)throw new Exception("Desenfoque fuera de rango");effect.parameters["sigma"].number=blur.Value;}}Write(outer,content);}
  public static void RemoveChildLegacy(Project p,string outerId,string childId){var outer=p.Active.clips.Find(c=>c.id==outerId);if(!IsNested(p,outer))throw new Exception("Medio anidado no encontrado");var content=Read(outer);if(content.clips.Count<=1||content.clips.Count(c=>content.video_orders.ContainsKey(c.track_id)&&c.id!=childId)==0)throw new Exception("El medio anidado necesita un componente visual");if(content.clips.RemoveAll(c=>c.id==childId)!=1)throw new Exception("Componente interno no encontrado");Write(outer,content);}
  public static Clip CreateBlurredBackground(Project p,string clipId){
   var upper=p.Active.clips.Find(c=>c.id==clipId);if(upper==null)throw new Exception("Selecciona un medio visual");
   var asset=p.assets.Find(a=>a.id==upper.asset_id);var stream=asset==null?null:asset.streams.Find(s=>s.id==upper.stream_id);var track=p.Active.tracks.Find(t=>t.id==upper.track_id);
   if(stream==null||stream.kind=="audio"||track==null||track.locked||GeneratedMedia.Kind(asset)!=null)throw new Exception("Selecciona un vídeo o imagen editable");
   Track lower=p.Active.tracks.Where(t=>t.kind=="video"&&t.order<track.order&&!t.locked).OrderByDescending(t=>t.order).FirstOrDefault(t=>!p.Active.clips.Any(c=>c.track_id==t.id&&c.timeline_start.Seconds<upper.End-0.000001&&c.End>upper.timeline_start.Seconds+0.000001));
   bool temporaryTrack=lower==null;
   if(temporaryTrack){int insertedAt=track.order;foreach(var t in p.Active.tracks.Where(t=>t.kind=="video"&&t.order>=insertedAt))t.order++;lower=new Track{id=Store.Id(),name="Fondo",kind="video",order=insertedAt};p.Active.tracks.Add(lower);}
   var background=Store.Json().Deserialize<Clip>(Store.Json().Serialize(Store.Fields(upper)));background.id=Store.Id();background.name="Fondo desenfocado · "+upper.name;background.track_id=lower.id;
   background.effects.Add(EffectContracts.New(EffectContracts.BlurId));background.effects.Last().parameters["sigma"].number=10;
   ClipFraming.Set(background,"fill",1,0,0);CanvasFit0913.Apply(p,upper,"fit");p.Active.clips.Add(background);
   var withAudio=p.Active.link_groups.Where(g=>g.clip_ids.Contains(upper.id)).SelectMany(g=>g.clip_ids).Where(id=>p.Active.clips.Any(c=>c.id==id&&p.Active.tracks.Any(t=>t.id==c.track_id&&t.kind=="audio"))).ToList();
   var result=Create(p,new[]{upper.id,background.id}.Concat(withAudio),"Fondo desenfocado · "+upper.name);
   result.track_id=track.id;
   if(temporaryTrack&&!p.Active.clips.Any(c=>c.track_id==lower.id)){
    int removedOrder=lower.order;p.Active.tracks.Remove(lower);
    foreach(var t in p.Active.tracks.Where(t=>t.kind=="video"&&t.order>removedOrder))t.order--;
   }
   return result;
  }
 }
}




