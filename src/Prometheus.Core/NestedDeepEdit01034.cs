using System;
using System.Linq;

namespace Prometheus {
 public sealed class NestedChildInfo01034 { public Clip child; public NestedContent0910 content; }
 public static partial class NestedMedia0910 {
  public static NestedChildInfo01034 FindDeepChild(Project project,string rootId,string childId){
   var root=project.Active.clips.Find(c=>c.id==rootId);return IsNested(project,root)?FindChildIn(project,root,childId,0):null;
  }
  static NestedChildInfo01034 FindChildIn(Project project,Clip parent,string childId,int depth){
   if(depth>16)throw new Exception("Demasiados niveles de medios anidados");
   var content=Read(parent);var direct=content.clips.Find(c=>c.id==childId);
   if(direct!=null)return new NestedChildInfo01034{child=direct,content=content};
   foreach(var nested in content.clips.Where(c=>IsNested(project,c))){var found=FindChildIn(project,nested,childId,depth+1);if(found!=null)return found;}
   return null;
  }
  public static void UpdateDeepChild(Project project,string rootId,string childId,double start,double duration,string trackId){
   var root=project.Active.clips.Find(c=>c.id==rootId);if(!IsNested(project,root))throw new Exception("Medio anidado ausente");
   var video=Visual(project,root);if(!UpdateChildIn(project,video,childId,start,duration,trackId,0))throw new Exception("Componente interno ausente");
   SyncCompanion(project,video);
  }
  static bool UpdateChildIn(Project project,Clip parent,string childId,double start,double duration,string trackId,int depth){
   if(depth>16)throw new Exception("Demasiados niveles de medios anidados");
   var content=Read(parent);var child=content.clips.Find(c=>c.id==childId);
   if(child==null){foreach(var nested in content.clips.Where(c=>IsNested(project,c))){if(!UpdateChildIn(project,nested,childId,start,duration,trackId,depth+1))continue;Write(parent,content);return true;}return false;}
   if(start<0||duration<1.0/60||start+duration>48*3600)throw new Exception("Rango interno inválido");
   bool audio=content.audio_orders.ContainsKey(child.track_id);
   if(trackId!=null&&(audio?!content.audio_orders.ContainsKey(trackId):!content.video_orders.ContainsKey(trackId)))throw new Exception("Pista interna incompatible");
   if(trackId!=null)child.track_id=trackId;
   double previousEnd=child.End;
   bool leftTrim=Math.Abs(start-child.timeline_start.Seconds)>0.000001&&Math.Abs(duration-child.duration.Seconds)>0.000001&&Math.Abs(start+duration-previousEnd)<0.000001;
   double sourceIn=leftTrim?child.source_in.Seconds+(start-child.timeline_start.Seconds)*child.speed.Seconds:child.source_in.Seconds;
   var asset=project.assets.Find(a=>a.id==child.asset_id);var stream=asset==null?null:asset.streams.Find(s=>s.id==child.stream_id);
   if(sourceIn< -0.000001||stream!=null&&stream.duration!=null&&sourceIn+duration*child.speed.Seconds>stream.duration.Seconds+0.000001)throw new Exception("El recorte interno excede la fuente");
   if(content.clips.Any(c=>c.id!=child.id&&c.track_id==child.track_id&&c.timeline_start.Seconds<start+duration-0.000001&&c.End>start+0.000001))throw new Exception("El componente se superpone con otro de la pista");
   child.source_in=Rational.Time(Math.Max(0,sourceIn));child.timeline_start=Rational.Frame(start);child.duration=Rational.Frame(duration);
   double contentEnd=content.clips.Max(c=>c.End),required=Math.Max(1.0/60,(contentEnd-parent.source_in.Seconds)/parent.speed.Seconds);
   var parentAsset=project.assets.Find(a=>a.id==parent.asset_id);if(parentAsset!=null)parentAsset.recommended_duration=Rational.Frame(contentEnd);
   if(required<parent.duration.Seconds-0.000001)parent.duration=Rational.Frame(required);
   Write(parent,content);return true;
  }
  public static void RemoveDeepChild(Project project,string rootId,string childId){
   var root=project.Active.clips.Find(c=>c.id==rootId);var video=Visual(project,root);
   if(video==null||!RemoveChildIn(project,video,childId,0))throw new Exception("Componente interno ausente");SyncCompanion(project,video);
  }
  static bool RemoveChildIn(Project project,Clip parent,string childId,int depth){
   if(depth>16)throw new Exception("Demasiados niveles de medios anidados");
   var content=Read(parent);var child=content.clips.Find(c=>c.id==childId);
   if(child==null){foreach(var nested in content.clips.Where(c=>IsNested(project,c))){if(!RemoveChildIn(project,nested,childId,depth+1))continue;Write(parent,content);return true;}return false;}
   if(content.clips.Count<=1||content.clips.Count(c=>content.video_orders.ContainsKey(c.track_id)&&c.id!=childId)==0)throw new Exception("El medio anidado necesita un componente visual");
   content.clips.Remove(child);Write(parent,content);return true;
  }
  public static void UpdateDeepChildProperties(Project project,string rootId,string childId,double sx,double sy,double? blur,double gain){
   var root=project.Active.clips.Find(c=>c.id==rootId);var video=Visual(project,root);
   if(video==null||!UpdateChildPropertiesIn(project,video,childId,sx,sy,blur,gain,0))throw new Exception("Componente interno ausente");SyncCompanion(project,video);
  }
  static bool UpdateChildPropertiesIn(Project project,Clip parent,string childId,double sx,double sy,double? blur,double gain,int depth){
   if(depth>16)throw new Exception("Demasiados niveles de medios anidados");var content=Read(parent);var child=content.clips.Find(c=>c.id==childId);
   if(child==null){foreach(var nested in content.clips.Where(c=>IsNested(project,c))){if(!UpdateChildPropertiesIn(project,nested,childId,sx,sy,blur,gain,depth+1))continue;Write(parent,content);return true;}return false;}
   if(content.audio_orders.ContainsKey(child.track_id)){if(gain< -60||gain>12)throw new Exception("Volumen fuera de rango");child.gain_db=gain;}
   else {if(sx<.25||sx>4||sy<.25||sy>4)throw new Exception("Escala fuera de rango");var frame=ClipFraming.Read(child);if(Math.Abs(sx-sy)>.000001&&frame.scaleLinked)ClipFraming.SetScaleLinked(child,false);ClipFraming.Set(child,frame.mode,sx,frame.x,frame.y);if(!ClipFraming.Read(child).scaleLinked)ClipFraming.SetScaleY(child,sy);var effect=child.effects.FirstOrDefault(e=>e.effect_id==EffectContracts.BlurId);if(effect!=null&&blur.HasValue)effect.parameters["sigma"].number=blur.Value;}
   Write(parent,content);return true;
  }
 }
}
