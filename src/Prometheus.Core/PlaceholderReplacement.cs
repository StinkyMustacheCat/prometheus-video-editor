using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 public static class PlaceholderReplacement {
  public const string IntentNamespace="prometheus.placeholder.intent";
  public static string ExpectedType(Clip clip){var ext=clip.extensions.FirstOrDefault(x=>x!=null&&x.@namespace==IntentNamespace&&x.version=="1");object value;return ext!=null&&ext.payload!=null&&ext.payload.TryGetValue("type",out value)?Convert.ToString(value):"any";}
  public static bool Accepts(Project project,Clip clip,Asset asset){
   if(project==null||clip==null||asset==null||GeneratedMedia.IsPlaceholder(asset))return false;
   var original=project.assets.Find(a=>a.id==clip.asset_id);
   var track=project.Active.tracks.Find(t=>t.id==clip.track_id);
   if(!GeneratedMedia.IsPlaceholder(original)||track==null||track.locked)return false;
   if(track.kind=="audio")return asset.streams.Any(s=>s.kind=="audio");
   var stream=asset.streams.FirstOrDefault(s=>s.kind=="video")??asset.streams.FirstOrDefault(s=>s.kind=="image");
   if(stream==null)return false;
   string expected=ExpectedType(clip);
   return expected=="any"||expected=="video"&&stream.kind=="video"||expected=="image"&&stream.kind=="image"||expected=="web"&&asset.source!=null&&asset.source.kind=="web";
  }
  public static void SetExpectedType(Project project,string clipId,string type){
   if(!(new[]{"any","video","image","web"}).Contains(type))throw new Exception("Tipo de espacio reservado inválido");
   var clip=project.Active.clips.FirstOrDefault(c=>c.id==clipId);if(clip==null||!GeneratedMedia.IsPlaceholder(project.assets.Find(a=>a.id==clip.asset_id)))throw new Exception("Selecciona un espacio reservado");
   clip.extensions.RemoveAll(x=>x!=null&&x.@namespace==IntentNamespace);
   if(type!="any")clip.extensions.Add(new Extension{@namespace=IntentNamespace,version="1",payload=new Dictionary<string,object>{{"type",type}}});
  }
  public static void Replace(Project project,string clipId,string assetId,double sourceStart){
   var clip=project.Active.clips.FirstOrDefault(c=>c.id==clipId);
   if(clip==null||!GeneratedMedia.IsPlaceholder(project.assets.Find(a=>a.id==clip.asset_id)))throw new Exception("Este clip ya no es un espacio reservado");
   var track=project.Active.tracks.FirstOrDefault(t=>t.id==clip.track_id);if(track==null||track.locked)throw new Exception("Desbloquea la pista para conectar el medio");
   var asset=project.assets.FirstOrDefault(a=>a.id==assetId);if(asset==null||GeneratedMedia.IsPlaceholder(asset))throw new Exception("Elige un medio compatible disponible");
   var stream=track.kind=="audio"?asset.streams.FirstOrDefault(s=>s.kind=="audio"):
    asset.streams.FirstOrDefault(s=>s.kind=="video")??asset.streams.FirstOrDefault(s=>s.kind=="image");
   if(stream==null)throw new Exception("El medio elegido no es compatible con esta pista");
   string expected=ExpectedType(clip);
   if(track.kind=="video"&&(expected=="video"&&stream.kind!="video"||expected=="image"&&stream.kind!="image"||expected=="web"&&(asset.source==null||asset.source.kind!="web")))throw new Exception("El medio no coincide con el tipo definido para este espacio");
   var start=Rational.Frame(sourceStart);if(start.Seconds<0)throw new Exception("El inicio de la fuente no puede ser negativo");
   if(stream.kind=="image"&&start.n!=0)throw new Exception("Una imagen no tiene un punto de inicio temporal");
   if(stream.duration!=null&&start.Seconds>=stream.duration.Seconds-0.000001)throw new Exception("El inicio elegido está fuera del medio");
   double sourceEnd=start.Seconds+clip.duration.Seconds*clip.speed.Seconds;
   clip.asset_id=asset.id;clip.stream_id=stream.id;clip.source_in=start;clip.name=asset.name;
   clip.extensions.RemoveAll(x=>x!=null&&x.@namespace==IntentNamespace);
   if(stream.duration!=null&&sourceEnd>stream.duration.Seconds+0.000001)ShortTail.Attach(clip,sourceEnd);
  }
 }
}
