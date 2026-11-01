using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 public static partial class NestedMedia0910 {
  public static string Component(Clip clip){var data=Data(clip);object value;return data!=null&&data.payload!=null&&data.payload.TryGetValue("component",out value)?Convert.ToString(value):"both";}
  static void SetComponent(Clip clip,string component){var data=Data(clip);if(data==null)throw new Exception("Medio anidado sin datos");data.payload["component"]=component;}
  public static Clip Companion(Project project,Clip clip){if(project==null||clip==null)return null;var group=project.Active.link_groups.FirstOrDefault(x=>x.clip_ids.Contains(clip.id));if(group==null)return null;return project.Active.clips.FirstOrDefault(x=>x.id!=clip.id&&group.clip_ids.Contains(x.id)&&x.asset_id==clip.asset_id&&IsNested(project,x));}
  static Clip Visual(Project project,Clip clip){if(clip==null)return null;return project.Active.tracks.Any(t=>t.id==clip.track_id&&t.kind=="video")?clip:Companion(project,clip);}
  static Track FreeAudioTrack(Project project,Clip outer,Track preferred){
   var sequence=project.Active;
   if(preferred!=null&&!preferred.locked&&!sequence.clips.Any(c=>c.track_id==preferred.id&&c.id!=outer.id&&c.timeline_start.Seconds<outer.End-0.000001&&c.End>outer.timeline_start.Seconds+0.000001))return preferred;
   var available=sequence.tracks.Where(t=>t.kind=="audio"&&!t.locked).OrderBy(t=>t.order).FirstOrDefault(t=>!sequence.clips.Any(c=>c.track_id==t.id&&c.id!=outer.id&&c.timeline_start.Seconds<outer.End-0.000001&&c.End>outer.timeline_start.Seconds+0.000001));
   return available??Store.AddTrack(sequence,"audio");
  }
  public static Clip EnsureAudioCompanion(Project project,string outerId){
   var outer=project.Active.clips.Find(c=>c.id==outerId);if(!IsNested(project,outer))return null;
   var video=Visual(project,outer);if(video==null)return null;
   var content=Read(video);if(content==null||content.audio_orders.Count==0)return null;
   var existing=Companion(project,video);if(existing!=null)return existing;
   var asset=project.assets.Find(a=>a.id==video.asset_id);
   var stream=asset.streams.FirstOrDefault(s=>s.kind=="audio");
   if(stream==null){stream=new StreamInfo{id=Store.Id(),ordinal=asset.streams.Max(s=>s.ordinal)+1,kind="audio",codec="nested",pts_origin=Rational.Time(0),time_base=new Rational(1,48000),duration=Rational.Frame(content.clips.Max(c=>c.End)),sample_rate=48000,channels=2,channel_layout="stereo"};asset.streams.Add(stream);}
   var preferred=content.audio_orders.OrderBy(x=>x.Value).Select(x=>project.Active.tracks.Find(t=>t.id==x.Key)).FirstOrDefault(t=>t!=null&&t.kind=="audio");
   var audio=new Clip{id=Store.Id(),name=video.name+" · audio",asset_id=asset.id,stream_id=stream.id,track_id=FreeAudioTrack(project,video,preferred).id,timeline_start=video.timeline_start,duration=video.duration,source_in=video.source_in,speed=video.speed};
   Write(audio,content);SetComponent(audio,"audio");SetComponent(video,"video");
   project.Active.clips.Add(audio);project.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=new List<string>{video.id,audio.id}});
   return audio;
  }
  public static Clip Create(Project project,IEnumerable<string> ids,string name){var video=CreateLegacy(project,ids,name);EnsureAudioCompanion(project,video.id);return video;}
  static void SyncCompanion(Project project,Clip video){
   var audio=Companion(project,video);if(audio==null)return;
   var content=Read(video);
   if(content.audio_orders.Count==0){project.Active.clips.Remove(audio);project.Active.link_groups.RemoveAll(group=>group.clip_ids.Contains(audio.id));return;}
   Write(audio,content);SetComponent(audio,"audio");SetComponent(video,"video");audio.timeline_start=video.timeline_start;audio.duration=video.duration;audio.source_in=video.source_in;audio.speed=video.speed;
   var stream=project.assets.Find(a=>a.id==audio.asset_id).streams.Find(s=>s.id==audio.stream_id);double required=Math.Max(audio.source_in.Seconds+audio.duration.Seconds*audio.speed.Seconds,content.clips.Max(c=>c.End));if(stream.duration==null||stream.duration.Seconds<required)stream.duration=Rational.Frame(required);
   audio.track_id=FreeAudioTrack(project,audio,project.Active.tracks.Find(t=>t.id==audio.track_id)).id;
  }
  public static void UpdateChild(Project project,string outerId,string childId,double start,double duration,string targetTrackId){var outer=project.Active.clips.Find(c=>c.id==outerId);var video=Visual(project,outer);if(video==null)throw new Exception("Medio anidado visual ausente");UpdateChildLegacy(project,video.id,childId,start,duration,targetTrackId);SyncCompanion(project,video);}
  public static void UpdateChildProperties(Project project,string outerId,string childId,double scaleX,double scaleY,double? blur,double gain){var outer=project.Active.clips.Find(c=>c.id==outerId);var video=Visual(project,outer);if(video==null)throw new Exception("Medio anidado visual ausente");UpdateChildPropertiesLegacy(project,video.id,childId,scaleX,scaleY,blur,gain);SyncCompanion(project,video);}
  public static void RemoveChild(Project project,string outerId,string childId){var outer=project.Active.clips.Find(c=>c.id==outerId);var video=Visual(project,outer);if(video==null)throw new Exception("Medio anidado visual ausente");RemoveChildLegacy(project,video.id,childId);SyncCompanion(project,video);}
  public static Project Flatten(Project original){return Flatten(original,null);}
  public static Project Flatten(Project original,IEnumerable<string> selectedIds){
   if(!original.Active.clips.Any(c=>IsNested(original,c)))return original;
   var project=Store.Copy(original);var sequence=project.Active;
   var selected=selectedIds==null?null:new HashSet<string>(selectedIds);
   var orderPaths=sequence.tracks.ToDictionary(t=>t.id,t=>new List<int>{t.order});
   int audioOrder=sequence.tracks.Where(t=>t.kind=="audio").Select(t=>t.order).DefaultIfEmpty(0).Max()+1;
   while(true){var outer=sequence.clips.FirstOrDefault(c=>IsNested(project,c)&&(selected==null||selected.Contains(c.id)));if(outer==null)break;
    var content=Read(outer);var parentTrack=sequence.tracks.Find(t=>t.id==outer.track_id);string component=Component(outer);
    bool includeVideo=component=="both"||component=="video",includeAudio=component=="both"||component=="audio";
    var mapping=new Dictionary<string,string>();var group=NestedRenderGroups01034.Capture(project,outer);var ancestors=NestedRenderGroups01034.Read(outer);
    if(ancestors.Any(x=>x.outer.id==outer.id)||ancestors.Count>=16)throw new Exception("El anidado contiene una referencia circular o demasiados niveles");
    if(includeVideo)foreach(var sourceId in content.video_orders.OrderBy(pair=>pair.Value).Select(pair=>pair.Key)){var target=new Track{id=Store.Id(),name="Interna V",kind="video",order=parentTrack.order+1+content.video_orders[sourceId],enabled=parentTrack.enabled};sequence.tracks.Add(target);mapping[sourceId]=target.id;orderPaths[target.id]=orderPaths[parentTrack.id].Concat(new[]{content.video_orders[sourceId]+1}).ToList();ConfigureInternalTrack(sequence,sourceId,target);}
    if(includeAudio)foreach(var sourceId in content.audio_orders.OrderBy(pair=>pair.Value).Select(pair=>pair.Key)){var target=new Track{id=Store.Id(),name="Interna A",kind="audio",order=audioOrder++,enabled=parentTrack.enabled};sequence.tracks.Add(target);mapping[sourceId]=target.id;orderPaths[target.id]=orderPaths[parentTrack.id].Concat(new[]{content.audio_orders[sourceId]+1}).ToList();ConfigureInternalTrack(sequence,sourceId,target);}
    double parentStart=outer.timeline_start.Seconds,parentIn=outer.source_in.Seconds,parentSpeed=outer.speed.Seconds;
    foreach(var child in content.clips.Where(c=>mapping.ContainsKey(c.track_id))){
     double localStart=(child.timeline_start.Seconds-parentIn)/parentSpeed,localEnd=(child.End-parentIn)/parentSpeed,visibleStart=Math.Max(0,localStart),visibleEnd=Math.Min(outer.duration.Seconds,localEnd);if(visibleEnd<=visibleStart+0.000001)continue;
     var copy=Store.Json().Deserialize<Clip>(Store.Json().Serialize(Store.Fields(child)));copy.id=Store.Id();copy.track_id=mapping[child.track_id];copy.timeline_start=Rational.Time(parentStart+visibleStart);copy.duration=Rational.Time(visibleEnd-visibleStart);copy.source_in=Rational.Time(child.source_in.Seconds+(parentIn+visibleStart*parentSpeed-child.timeline_start.Seconds)*child.speed.Seconds);copy.speed=Rational.Time(child.speed.Seconds*parentSpeed);copy.enabled=copy.enabled&&outer.enabled;
     if(copy.effects.Any(e=>e.effect_id==EffectContracts.ZoomInDynamicId||e.effect_id==EffectContracts.ZoomOutDynamicId||e.effect_id==EffectContracts.PanDynamicId)){
      copy.extensions.RemoveAll(e=>e.@namespace=="prometheus.motion.preview-window");
      copy.extensions.Add(new Extension{@namespace="prometheus.motion.preview-window",version="1",payload=new Dictionary<string,object>{{"start",parentStart+localStart},{"duration",child.duration.Seconds/parentSpeed}}});
     }
     NestedRenderGroups01034.Set(copy,ancestors.Concat(new[]{group}));
     sequence.clips.Add(copy);
    }
    sequence.clips.Remove(outer);
   }
   foreach(string kind in new[]{"video","audio"}){int order=0;foreach(var track in sequence.tracks.Where(t=>t.kind==kind).OrderBy(t=>String.Join("/",orderPaths[t.id].Select(n=>n.ToString("D8")))))track.order=order++;}
   return project;
  }
  public static List<string> Unnest(Project project,string clipId){
   var clip=project.Active.clips.Find(c=>c.id==clipId);if(!IsNested(project,clip))throw new Exception("Selecciona un medio anidado");
   var video=Visual(project,clip);if(video==null)throw new Exception("Falta el componente visual");var audio=Companion(project,video);
   var ids=new HashSet<string>{video.id};if(audio!=null)ids.Add(audio.id);
   if(project.Active.transitions.Any(t=>ids.Contains(t.left_clip_id)||ids.Contains(t.right_clip_id)))throw new Exception("Quita las transiciones del medio anidado antes de desanidarlo");
   foreach(var id in ids){var outer=project.Active.clips.Find(c=>c.id==id);if(project.Active.tracks.Find(t=>t.id==outer.track_id).locked)throw new Exception("Desbloquea las pistas del medio anidado");}
   var oldIds=new HashSet<string>(project.Active.clips.Select(c=>c.id));
   var originalTracks=project.Active.tracks.ToDictionary(t=>t.id,t=>t.order);
   var flat=Flatten(project,ids);
   var revealed=flat.Active.clips.Where(c=>!oldIds.Contains(c.id)).ToList();
   foreach(var child in revealed){
    foreach(var group in NestedRenderGroups01034.Read(child).Where(g=>ids.Contains(g.outer.id))){
     bool isAudio=flat.Active.tracks.Any(t=>t.id==child.track_id&&t.kind=="audio");
     ApplyOuter(group.outer,child,isAudio,flat.Active.tracks.First(t=>t.id==group.outer.track_id));
     if(isAudio){child.gain_db=Math.Max(-60,Math.Min(12,child.gain_db+group.outer.gain_db));foreach(var effect in group.outer.effects.Where(e=>e.enabled)){var copy=Store.Json().Deserialize<EffectInstance>(Store.Json().Serialize(Store.Fields(effect)));copy.id=Store.Id();child.effects.Add(copy);}}
    }
    child.extensions.RemoveAll(e=>e.@namespace==NestedRenderGroups01034.Namespace);
   }
   var internalTracks=flat.Active.tracks.Where(t=>!originalTracks.ContainsKey(t.id)).OrderBy(t=>t.kind).ThenBy(t=>t.order).ToList();
   foreach(var track in flat.Active.tracks.Where(t=>originalTracks.ContainsKey(t.id)))track.order=originalTracks[track.id];
   foreach(string kind in new[]{"video","audio"}){
    int lastOrder=-1;
    var created=new HashSet<string>();
    foreach(var source in internalTracks.Where(t=>t.kind==kind)){
     var children=revealed.Where(c=>c.track_id==source.id).ToList();
     if(children.Count==0){flat.Active.tracks.Remove(source);continue;}
     bool neutral=source.enabled&&!source.locked&&source.effects.Count==0&&Math.Abs(source.gain_db)<0.000001&&Math.Abs(source.balance)<0.000001;
     var available=neutral?flat.Active.tracks.Where(t=>originalTracks.ContainsKey(t.id)&&t.kind==kind&&t.order>lastOrder&&!t.locked&&t.enabled&&t.effects.Count==0&&Math.Abs(t.gain_db)<0.000001&&Math.Abs(t.balance)<0.000001).OrderBy(t=>t.order).FirstOrDefault(t=>children.All(child=>!flat.Active.clips.Any(other=>other.track_id==t.id&&other.timeline_start.Seconds<child.End-0.000001&&other.End>child.timeline_start.Seconds+0.000001))):null;
     if(available!=null){foreach(var child in children)child.track_id=available.id;flat.Active.tracks.Remove(source);lastOrder=available.order;}
     else{int highest=flat.Active.tracks.Where(t=>t.kind==kind&&(originalTracks.ContainsKey(t.id)||created.Contains(t.id))).Select(t=>t.order).DefaultIfEmpty(-1).Max();source.order=Math.Max(highest,lastOrder)+1;lastOrder=source.order;created.Add(source.id);}
    }
   }
   project.Active.clips=flat.Active.clips;project.Active.tracks=flat.Active.tracks;
   project.Active.link_groups.RemoveAll(g=>g.clip_ids.Any(ids.Contains));project.Active.edit_groups.RemoveAll(g=>g.clip_ids.Any(ids.Contains));
   if(!project.sequences.SelectMany(s=>s.clips).Any(c=>c.asset_id==video.asset_id))project.assets.RemoveAll(a=>a.id==video.asset_id);
   return project.Active.clips.Where(c=>!oldIds.Contains(c.id)).Select(c=>c.id).ToList();
  }
 }
}
