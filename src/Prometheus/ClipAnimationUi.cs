using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 public partial class MainForm {
  static void CheckAnimationPosition(Clip clip,double time){
   if(clip==null||time<clip.timeline_start.Seconds-0.00001||time>=clip.End-0.00001)
    throw new Exception("Sitúa el lector dentro del clip para editar sus fotogramas clave.");
  }
  static double BaseAnimationValue(Clip clip,string target){
   var frame=ClipFraming.Read(clip);
   if(target=="transform.scale")return frame.scale;if(target=="transform.scale_y")return frame.scaleY;if(target=="transform.x")return frame.x;if(target=="transform.y")return frame.y;if(target=="transform.opacity")return frame.opacity;
   var parts=target.Split(':');if(parts.Length==3&&parts[0]=="effect"){var effect=clip.effects.FirstOrDefault(e=>e.id==parts[1]);EffectValue value;if(effect!=null&&effect.parameters.TryGetValue(parts[2],out value)&&value.number.HasValue)return value.number.Value;}
   throw new Exception("Parámetro animable no disponible");
  }
  static void SetAnimatedValue(Clip clip,string target,double time,double value){
   var curve=ClipKeyframes.Find(clip,target);if(curve==null||!curve.enabled)throw new Exception("Activa primero la animación de este parámetro.");
   if(curve.points.Count==0&&time>clip.timeline_start.Seconds+1.0/120)ClipKeyframes.Set(clip,target,clip.timeline_start.Seconds,BaseAnimationValue(clip,target));
   ClipKeyframes.Set(clip,target,time,value);
  }
  void ToggleAnimationKeyframe(string clipId,string target,double value){
   try{
    Edit(()=>editor.Commit(p=>{
     var clip=p.Active.clips.Find(c=>c.id==clipId);if(clip==null)throw new Exception("Clip no disponible");
     if(p.Active.tracks.Find(t=>t.id==clip.track_id).locked)throw new Exception("La pista está bloqueada");
     var curve=ClipKeyframes.Find(clip,target);if(curve==null)ClipKeyframes.Arm(clip,target);else curve.enabled=!curve.enabled;
    }));
    status.Text="Animación del parámetro actualizada";
   }catch(Exception ex){Error(ex);ShowClip();}
  }
  void AddAnimationKeyframe(string clipId,string target,double time){
   try{Edit(()=>editor.Commit(p=>{var clip=p.Active.clips.Find(c=>c.id==clipId);CheckAnimationPosition(clip,time);if(p.Active.tracks.Find(t=>t.id==clip.track_id).locked)throw new Exception("La pista está bloqueada");
    if(!ClipKeyframes.Active(clip,target))ClipKeyframes.Arm(clip,target);ClipKeyframes.Set(clip,target,Rational.Frame(time).Seconds,ClipKeyframes.Value(clip,target,time,BaseAnimationValue(clip,target)));
   }));status.Text="Fotograma clave añadido";}catch(Exception ex){Error(ex);ShowClip();}
  }
  void ChangeAnimationValue(string clipId,string target,double value){
   try{double time=timeline.Cursor;Edit(()=>editor.Commit(p=>{
    var clip=p.Active.clips.Find(c=>c.id==clipId);CheckAnimationPosition(clip,time);
    if(p.Active.tracks.Find(t=>t.id==clip.track_id).locked)throw new Exception("La pista está bloqueada");
    SetAnimatedValue(clip,target,time,value);
   }));status.Text="Fotograma clave actualizado";}catch(Exception ex){Error(ex);ShowClip();}
  }
  void PreviewAnimationValue(string clipId,string target,double value){
   double time=timeline.Cursor;
   PreviewProject(p=>{var clip=p.Active.clips.Find(c=>c.id==clipId);CheckAnimationPosition(clip,time);SetAnimatedValue(clip,target,time,value);});
  }
  void ResetAnimationParameter(string clipId,string target,double value){
   try{Edit(()=>editor.Commit(p=>{
    var clip=p.Active.clips.Find(c=>c.id==clipId);if(clip==null||p.Active.tracks.Find(t=>t.id==clip.track_id).locked)throw new Exception("Clip no disponible o pista bloqueada");
    if(ClipKeyframes.Active(clip,target)){CheckAnimationPosition(clip,timeline.Cursor);SetAnimatedValue(clip,target,timeline.Cursor,value);return;}
    var frame=ClipFraming.Read(clip);
    if(target=="transform.opacity")ClipFraming.SetOpacity(clip,value);
    else if(target=="transform.scale_y")ClipFraming.SetScaleY(clip,value);
    else ClipFraming.Set(clip,frame.mode,target=="transform.scale"?value:frame.scale,target=="transform.x"?value:frame.x,target=="transform.y"?value:frame.y);
   }));status.Text="Parámetro restablecido en el lector";}catch(Exception ex){Error(ex);ShowClip();}
  }
  void ClearAnimationParameter(string clipId,string target){
   try{Edit(()=>editor.Commit(p=>{var clip=p.Active.clips.Find(c=>c.id==clipId);if(clip==null||p.Active.tracks.Find(t=>t.id==clip.track_id).locked)throw new Exception("Clip no disponible o pista bloqueada");clip.animation.RemoveAll(curve=>curve.target==target);}));status.Text="Fotogramas clave del parámetro eliminados";}catch(Exception ex){Error(ex);ShowClip();}
  }
  void NavigateAnimationParameter(string clipId,string target,int direction){
   var clip=editor.Project.Active.clips.Find(c=>c.id==clipId);var curve=clip==null?null:ClipKeyframes.Find(clip,target);if(curve==null||curve.points.Count==0)return;
   var times=curve.points.Select(point=>clip.timeline_start.Seconds+(point.source_seconds-clip.source_in.Seconds)/clip.speed.Seconds).Where(time=>time>=clip.timeline_start.Seconds&&time<clip.End);
   double? next=direction<0?times.Where(time=>time<timeline.Cursor-1.0/120).OrderByDescending(time=>time).Cast<double?>().FirstOrDefault():times.Where(time=>time>timeline.Cursor+1.0/120).OrderBy(time=>time).Cast<double?>().FirstOrDefault();
   if(next.HasValue)Seek(next.Value);
  }
  void MoveAnimationKeyframe(string clipId,string target,double oldSource,double newTimelineTime){
   try{Edit(()=>editor.Commit(p=>{
    var clip=p.Active.clips.Find(c=>c.id==clipId);if(clip==null)throw new Exception("Clip no disponible");
    var track=p.Active.tracks.Find(t=>t.id==clip.track_id);if(track==null||track.locked)throw new Exception("La pista está bloqueada");
    var curve=ClipKeyframes.Find(clip,target);if(curve==null)throw new Exception("Fotograma clave no disponible");
    var point=curve.points.FirstOrDefault(x=>Math.Abs(x.source_seconds-oldSource)<1.0/48000);if(point==null)throw new Exception("El fotograma clave cambió");
    double limited=Math.Max(clip.timeline_start.Seconds,Math.Min(clip.End-1.0/60,newTimelineTime));
    double source=Math.Round(ClipKeyframes.SourceAt(clip,Rational.Frame(limited).Seconds)*48000)/48000.0;
    if(curve.points.Any(x=>x!=point&&Math.Abs(x.source_seconds-source)<1.0/48000))throw new Exception("Ya hay un fotograma clave en ese punto");
    point.source_seconds=source;curve.points=curve.points.OrderBy(x=>x.source_seconds).ToList();
   }));status.Text="Fotograma clave movido";}catch(Exception ex){Error(ex);ShowClip();}
  }
  void DeleteAnimationKeyframes(List<KeyframeSelection> keys){
   try{string id=timeline.Selected;Edit(()=>editor.Commit(p=>{
    var clip=p.Active.clips.Find(c=>c.id==id);if(clip==null||p.Active.tracks.Find(t=>t.id==clip.track_id).locked)throw new Exception("Clip no disponible o pista bloqueada");
    foreach(var key in keys){var curve=ClipKeyframes.Find(clip,key.target);if(curve==null)continue;curve.points.RemoveAll(point=>Math.Abs(point.source_seconds-key.source_seconds)<1.0/48000);}
   }));status.Text="Fotogramas clave eliminados";}catch(Exception ex){Error(ex);ShowClip();}
  }
  void PasteAnimationKeyframes(List<KeyframeTransfer> keys,double at){
   try{string id=timeline.Selected;Edit(()=>editor.Commit(p=>{
    var clip=p.Active.clips.Find(c=>c.id==id);if(clip==null||p.Active.tracks.Find(t=>t.id==clip.track_id).locked)throw new Exception("Clip no disponible o pista bloqueada");
    foreach(var key in keys){double time=Rational.Frame(at+key.offset_seconds).Seconds;CheckAnimationPosition(clip,time);string destination=key.target;
     if(key.contract_id!=null){var same=clip.effects.Where(effect=>effect.effect_id==key.contract_id).ToList();if(key.effect_ordinal<0||key.effect_ordinal>=same.Count)throw new Exception("El destino no contiene un efecto compatible para pegar estos fotogramas clave.");destination="effect:"+same[key.effect_ordinal].id+":"+key.parameter;}
     else if(destination.StartsWith("effect:",StringComparison.Ordinal)&&!clip.effects.Any(effect=>destination.StartsWith("effect:"+effect.id+":",StringComparison.Ordinal)))throw new Exception("El destino no contiene el mismo efecto para pegar estos fotogramas clave.");
     ClipKeyframes.Set(clip,destination,time,key.value);var curve=ClipKeyframes.Find(clip,destination);if(curve.points.Count==1)curve.interpolation=key.interpolation??"linear";var point=curve.points.First(item=>Math.Abs(item.source_seconds-ClipKeyframes.SourceAt(clip,time))<1.0/48000);point.in_tangent=key.in_tangent;point.out_tangent=key.out_tangent;
    }
   }));status.Text="Fotogramas clave pegados";}catch(Exception ex){Error(ex);ShowClip();}
  }
  void ChangeAnimationInterpolation(string id,string target,string mode){
   try{Edit(()=>editor.Commit(p=>{var clip=p.Active.clips.Find(c=>c.id==id);if(clip==null||p.Active.tracks.Find(t=>t.id==clip.track_id).locked)throw new Exception("Clip no disponible o pista bloqueada");var curve=ClipKeyframes.Find(clip,target);if(curve==null)throw new Exception("No hay fotogramas clave en este parámetro");curve.interpolation=mode;}));status.Text="Interpolación: "+mode;}catch(Exception ex){Error(ex);ShowClip();}
  }
  void ChangeAnimationTangent(string id,string target,double source,bool incoming,double tangent){
   try{Edit(()=>editor.Commit(p=>{var clip=p.Active.clips.Find(c=>c.id==id);if(clip==null||p.Active.tracks.Find(t=>t.id==clip.track_id).locked)throw new Exception("Clip no disponible o pista bloqueada");var curve=ClipKeyframes.Find(clip,target);var point=curve==null?null:curve.points.FirstOrDefault(x=>Math.Abs(x.source_seconds-source)<1.0/48000);if(point==null||curve.interpolation!="bezier")throw new Exception("La curva Bézier cambió");if(incoming)point.in_tangent=tangent;else point.out_tangent=tangent;}));status.Text="Curva Bézier actualizada";}catch(Exception ex){Error(ex);ShowClip();}
  }
 }
}
