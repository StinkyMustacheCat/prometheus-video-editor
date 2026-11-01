using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 public static partial class Instructions {
  static Dictionary<string,object> ContextMap(object value){return value as Dictionary<string,object>;}
  static object[] ContextRows(object value){return value as object[]??new object[0];}
  static bool ContextEmpty(object value){return value==null||value is object[]&&((object[])value).Length==0;}
  static void ContextRemove(Dictionary<string,object> row,string key,Func<object,bool> predicate){object value;if(row.TryGetValue(key,out value)&&predicate(value))row.Remove(key);}
  static double ContextTime(object value){return Math.Round(Convert.ToDouble(value),6,MidpointRounding.AwayFromZero);}
  static void ContextRound(Dictionary<string,object> row,params string[] keys){foreach(var key in keys){object value;if(row.TryGetValue(key,out value)&&value!=null)row[key]=ContextTime(value);}}
  static bool ContextDefaultFrame(object value){var frame=ContextMap(value);if(frame==null)return true;object v;return (!frame.TryGetValue("mode",out v)||Convert.ToString(v)=="fit")&&(!frame.TryGetValue("scale",out v)||Math.Abs(Convert.ToDouble(v)-1)<0.000001)&&(!frame.TryGetValue("scaleY",out v)||Math.Abs(Convert.ToDouble(v)-1)<0.000001)&&(!frame.TryGetValue("x",out v)||Math.Abs(Convert.ToDouble(v))<0.000001)&&(!frame.TryGetValue("y",out v)||Math.Abs(Convert.ToDouble(v))<0.000001)&&(!frame.TryGetValue("opacity",out v)||Math.Abs(Convert.ToDouble(v)-1)<0.000001)&&(!frame.TryGetValue("scaleLinked",out v)||Convert.ToBoolean(v));}
  public static string CopyCompactState(Project project,string scope,IEnumerable<string> selectedIds){
   var state=ContextMap(Store.Json().DeserializeObject(CopyState(project,scope,selectedIds)));
   if(state==null)throw new Exception("No se pudo preparar el contexto compacto");
   state["format"]="prometheus-ai-context";state["version"]=2;
   state["time_unit"]="seconds";state["time_precision"]="1/48000 s; vídeo en cuadrícula de 1/60 s; FPS de secuencia independiente";
   state["web_sources"]=new Dictionary<string,object>{{"video",new[]{"YouTube","Instagram","TikTok","Facebook","X/Twitter","Twitch"}},{"image","URL HTTP/HTTPS directa y estable"}};
   state["media_guidance"]="Prioriza medios existentes y enlaces estables; usa generadores de imágenes solo si el usuario lo pide.";
   state["omitted_defaults"]=new Dictionary<string,object>{{"clip.speed",1},{"clip.enabled",true},{"clip.inherit_track_effects",true},{"track.enabled",true},{"track.effects_enabled",true},{"optional_array","vacío si se omite"},{"optional_value","no aplicado si se omite"}};
   var assets=ContextRows(state["assets"]).Select(ContextMap).Where(a=>a!=null).ToList();
   var byAsset=assets.Where(a=>a.ContainsKey("id")).ToDictionary(a=>Convert.ToString(a["id"]),a=>a);
   var sequences=ContextRows(state["sequences"]).Select(ContextMap).Where(s=>s!=null).ToList();
   var allClips=sequences.SelectMany(s=>ContextRows(s["clips"]).Select(ContextMap).Where(c=>c!=null)).ToList();
   var assetUses=allClips.Where(c=>c.ContainsKey("asset_id")).GroupBy(c=>Convert.ToString(c["asset_id"])).ToDictionary(g=>g.Key,g=>g.Count());
   var inlineSubtitleAssets=new HashSet<string>();
   foreach(var sequence in sequences){
    var model=project.sequences.FirstOrDefault(s=>s.id==Convert.ToString(sequence["id"]));
    var subtitleTrack=model==null?null:SubtitleTrack0817.TrackId(model);
    foreach(var track in ContextRows(sequence["tracks"]).Select(ContextMap).Where(t=>t!=null)){
     ContextRemove(track,"enabled",v=>v!=null&&Convert.ToBoolean(v));ContextRemove(track,"effects_enabled",v=>v!=null&&Convert.ToBoolean(v));
     ContextRemove(track,"locked",v=>v!=null&&!Convert.ToBoolean(v));ContextRemove(track,"effects",ContextEmpty);
    }
    foreach(var clip in ContextRows(sequence["clips"]).Select(ContextMap).Where(c=>c!=null)){
     string assetId=Convert.ToString(clip["asset_id"]);Dictionary<string,object> asset;byAsset.TryGetValue(assetId,out asset);
     if(model!=null){var original=model.clips.FirstOrDefault(c=>c.id==Convert.ToString(clip["id"]));if(NestedMedia0910.IsNested(project,original)){var nested=NestedMedia0910.Read(original);clip["internal_clips"]=nested.clips.Select(c=>new{id=c.id,asset_id=c.asset_id,track_id=c.track_id,start_seconds=ContextTime(c.timeline_start.Seconds),duration_seconds=ContextTime(c.duration.Seconds)}).ToArray();clip["internal_tracks"]=nested.video_orders.Select(x=>new{id=x.Key,kind="video",order=x.Value}).Concat(nested.audio_orders.Select(x=>new{id=x.Key,kind="audio",order=x.Value})).ToArray();}}
     if(asset!=null&&clip.ContainsKey("name")&&String.Equals(Convert.ToString(clip["name"]),Convert.ToString(asset["name"]),StringComparison.Ordinal))clip.Remove("name");
     if(subtitleTrack!=null&&Convert.ToString(clip["track_id"])==subtitleTrack&&asset!=null&&assetUses.ContainsKey(assetId)&&assetUses[assetId]==1&&asset.ContainsKey("text")){
      clip["text"]=asset["text"];clip.Remove("asset_id");inlineSubtitleAssets.Add(assetId);
     }
     ContextRound(clip,"start_seconds","duration_seconds","source_in_seconds","speed");
     ContextRemove(clip,"source_in_seconds",v=>v!=null&&Math.Abs(Convert.ToDouble(v))<0.000001);ContextRemove(clip,"speed",v=>v!=null&&Math.Abs(Convert.ToDouble(v)-1)<0.000001);
     ContextRemove(clip,"enabled",v=>v!=null&&Convert.ToBoolean(v));ContextRemove(clip,"inherit_track_effects",v=>v!=null&&Convert.ToBoolean(v));
     ContextRemove(clip,"linked_group",v=>v==null);ContextRemove(clip,"edit_group",v=>v==null);ContextRemove(clip,"editable_in_context",v=>v!=null&&Convert.ToBoolean(v));
     ContextRemove(clip,"framing",ContextDefaultFrame);ContextRemove(clip,"keyframes",ContextEmpty);ContextRemove(clip,"effects",ContextEmpty);
    }
    foreach(var marker in ContextRows(sequence["markers"]).Select(ContextMap).Where(m=>m!=null))ContextRound(marker,"at_seconds");
    ContextRemove(sequence,"transitions",ContextEmpty);ContextRemove(sequence,"markers",ContextEmpty);ContextRemove(sequence,"audio_ducking",ContextEmpty);
   }
   var used=new HashSet<string>(allClips.Where(c=>c.ContainsKey("asset_id")).Select(c=>Convert.ToString(c["asset_id"])));
   var fullAssets=new List<object>();var library=new List<object>();
   foreach(var asset in assets){
    string id=Convert.ToString(asset["id"]);if(inlineSubtitleAssets.Contains(id))continue;
    if(scope!="project"&&!used.Contains(id)){library.Add(new Dictionary<string,object>{{"id",id},{"name",asset["name"]},{"kind",asset["kind"]}});continue;}
    ContextRound(asset,"recommended_duration_seconds");ContextRemove(asset,"generated_kind",v=>v==null);ContextRemove(asset,"is_placeholder",v=>v!=null&&!Convert.ToBoolean(v));
    foreach(var key in new[]{"text","text_size","text_color","mgt_contract","recommended_duration_seconds"})ContextRemove(asset,key,v=>v==null);
    ContextRemove(asset,"streams",ContextEmpty);fullAssets.Add(asset);
   }
   state["assets"]=fullAssets.ToArray();if(library.Count>0)state["library_assets"]=library.ToArray();ContextRemove(state,"shelf_items",ContextEmpty);
   var actions=new List<object>();foreach(var operation in AvailableActions()){
    var required=RequiredFields(operation).Split(new[]{','},StringSplitOptions.RemoveEmptyEntries);
    var optional=AcceptedFields(operation).Where(field=>!required.Contains(field)).ToArray();
    var row=new Dictionary<string,object>{{"op",operation},{"required",required}};if(optional.Length>0)row["optional"]=optional;actions.Add(row);
   }
   state["supported_actions"]=actions.ToArray();
   var exampleAction=new Dictionary<string,object>{{"op","clip.inherit_track_effects"},{"clip_id",allClips.Count==0?"ID_DEL_CLIP":Convert.ToString(allClips[0]["id"])},{"value",true}};
   var example=new Dictionary<string,object>{{"format","prometheus-instructions"},{"version",1},{"project_id",project.id},{"revision",project.revision},{"actions",new[]{exampleAction}}};
   if(scope=="io"){var context=ContextMap(state["context"]);example["context_sequence_id"]=context["sequence_id"];example["context_in_frame"]=context["in_frame"];example["context_out_frame"]=context["out_frame"];}
   if(scope=="tracks"){var context=ContextMap(state["context"]);example["context_sequence_id"]=context["sequence_id"];example["context_track_ids"]=context["track_ids"];}
   state["instruction_example"]=example;
   return Store.Json().Serialize(state);
  }
 }
}
