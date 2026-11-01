using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 public sealed class NestedRenderGroup01034 { public Clip outer; }
 public static class NestedRenderGroups01034 {
  public const string Namespace="prometheus.nested-render-groups";
  public static List<NestedRenderGroup01034> Read(Clip clip){
   if(clip==null||clip.extensions==null)return new List<NestedRenderGroup01034>();
   var entry=clip.extensions.FirstOrDefault(e=>e.@namespace==Namespace);object json;
   return entry==null||entry.payload==null||!entry.payload.TryGetValue("groups",out json)?new List<NestedRenderGroup01034>():Store.Json().Deserialize<List<NestedRenderGroup01034>>(Convert.ToString(json));
  }
  public static void Set(Clip clip,IEnumerable<NestedRenderGroup01034> groups){
   clip.extensions.RemoveAll(e=>e.@namespace==Namespace);
   clip.extensions.Add(new Extension{@namespace=Namespace,version="1",payload=new Dictionary<string,object>{{"groups",Store.Json().Serialize(groups.ToList())}}});
  }
  public static NestedRenderGroup01034 Capture(Project project,Clip outer){
   var copy=Store.Json().Deserialize<Clip>(Store.Json().Serialize(Store.Fields(outer)));
   copy.extensions.RemoveAll(e=>e.@namespace==NestedMedia0910.Namespace||e.@namespace==Namespace);
   var track=project.Active.tracks.Find(t=>t.id==outer.track_id);
   if(track!=null&&outer.inherit_track_effects&&track.effects_enabled)
    copy.effects.InsertRange(0,track.effects.Where(e=>e.enabled).Select(e=>Store.Json().Deserialize<EffectInstance>(Store.Json().Serialize(Store.Fields(e)))));
   copy.inherit_track_effects=false;
   return new NestedRenderGroup01034{outer=copy};
  }
 }
}
