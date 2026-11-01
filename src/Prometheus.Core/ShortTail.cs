using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Prometheus {
 public static class ShortTail {
  public const string Namespace="prometheus.clip.short-tail";
  static Extension Find(Clip clip){return clip.extensions.FirstOrDefault(e=>e!=null&&e.@namespace==Namespace&&e.version=="1");}
  public static bool Has(Clip clip){return Find(clip)!=null;}
  public static double Limit(Clip clip){
   var ext=Find(clip);object raw;
   if(ext==null||ext.payload==null||!ext.payload.TryGetValue("source_end",out raw))return 0;
   double result;if(!Double.TryParse(Convert.ToString(raw,CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out result))return 0;
   return result;
  }
  public static void Attach(Clip clip,double sourceEnd){
   clip.extensions.RemoveAll(e=>e!=null&&e.@namespace==Namespace);
   clip.extensions.Add(new Extension{@namespace=Namespace,version="1",payload=new Dictionary<string,object>{{"source_end",sourceEnd}}});
  }
  public static bool Allows(Clip clip,StreamInfo stream){
   if(stream==null||stream.duration==null)return false;
   double limit=Limit(clip),actual=stream.duration.Seconds,end=clip.source_in.Seconds+clip.duration.Seconds*clip.speed.Seconds;
   return limit>actual+0.000001&&end<=limit+0.000001;
  }
  public static double? MissingStart(Project project,Clip clip){
   var asset=project.assets.Find(a=>a.id==clip.asset_id);
   var stream=asset==null?null:asset.streams.Find(s=>s.id==clip.stream_id);
   if(stream==null||stream.duration==null||!Allows(clip,stream))return null;
   double visible=(stream.duration.Seconds-clip.source_in.Seconds)/clip.speed.Seconds;
   if(visible>=clip.duration.Seconds-0.000001)return null;
   return clip.timeline_start.Seconds+Math.Max(0,visible);
  }
  public static void Normalize(Project project){
   foreach(var clip in project.Active.clips){
    if(!Has(clip))continue;
    var asset=project.assets.Find(a=>a.id==clip.asset_id);
    var stream=asset==null?null:asset.streams.Find(s=>s.id==clip.stream_id);
    if(stream!=null&&stream.duration!=null&&clip.source_in.Seconds+clip.duration.Seconds*clip.speed.Seconds<=stream.duration.Seconds+0.000001)
     clip.extensions.RemoveAll(e=>e!=null&&e.@namespace==Namespace);
   }
  }
 }
}
