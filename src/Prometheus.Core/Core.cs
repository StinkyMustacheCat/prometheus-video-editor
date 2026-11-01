using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Web.Script.Serialization;

namespace Prometheus {
 public class Rational { public long n; public long d; public Rational(){d=1;} public Rational(long a,long b){if(b<=0)throw new Exception("Denominador inválido");long g=Gcd(Math.Abs(a),b);n=a/g;d=b/g;} static long Gcd(long a,long b){while(b!=0){long c=a%b;a=b;b=c;}return a==0?1:a;} public double Seconds {get{return (double)n/d;}} public static Rational Time(double s){return new Rational((long)Math.Round(s*48000),48000);} public static Rational Frame(double s){return new Rational((long)Math.Round(s*60),60);} }
 public class Extension {public string @namespace; public string version; public Dictionary<string,object> payload;}
 public class Locator {public string path; public bool relative;}
 public class MediaSource {public string kind,uri;public bool relative;public string page_url,imported_at,resolver_type;}
 public class MediaRepresentation {public string role;public Locator locator;public long? size_bytes;public string modified_utc,sha256;public Dictionary<string,int> stream_ordinals=new Dictionary<string,int>();}
 public class StreamInfo {public string id;public int ordinal;public string kind;public string codec;public Rational pts_origin,time_base,duration,nominal_fps;public int? sample_rate,channels,width,height,display_rotation;public string pixel_format,channel_layout,sample_aspect_ratio,color_primaries,color_transfer,color_space,color_range;}
 public class Asset {public string id,name;public MediaSource source;public List<MediaRepresentation> representations=new List<MediaRepresentation>();public string container_format;public long? bitrate;public Rational recommended_duration,presentation_origin;public List<StreamInfo> streams=new List<StreamInfo>();public List<Extension> extensions=new List<Extension>();[NonSerialized]public Locator locator;[NonSerialized]public string sha256;}
 public class EffectResource {public string id,kind,uri;public bool relative;}
 public class EffectValue {public string kind;public double? number;public string text,resource_id;public bool? boolean;}
 public class EffectInstance {public string id,effect_id,contract_version;public bool enabled=true;public Dictionary<string,EffectValue> parameters=new Dictionary<string,EffectValue>();}
 public class Track {public string id,name,kind;public int order;public bool enabled=true,locked,effects_enabled=true;public double gain_db,balance;public List<EffectInstance> effects=new List<EffectInstance>();}
 public class Clip {public string id,name,track_id,asset_id,stream_id;public Rational timeline_start,duration,source_in,speed=new Rational(1,1);public bool enabled=true,inherit_track_effects=true;public double gain_db,balance;public List<EffectInstance> effects=new List<EffectInstance>();public List<KeyframeCurve> animation=new List<KeyframeCurve>();public ClipMask mask;public List<Extension> extensions=new List<Extension>();public double End {get{return timeline_start.Seconds+duration.Seconds;}}}
 public class LinkGroup {public string id;public List<string> clip_ids=new List<string>();}
 public class EditGroup {public string id;public int number;public List<string> clip_ids=new List<string>();}
 public class Sequence {public string id,name;public int width=1920,height=1080;public Rational fps=new Rational(60,1);public string color_space="rec709-sdr";public int mix_sample_rate=48000,mix_channels=2;public List<Track> tracks=new List<Track>();public List<Clip> clips=new List<Clip>();public List<LinkGroup> link_groups=new List<LinkGroup>();public List<EditGroup> edit_groups=new List<EditGroup>();public List<TransitionInstance> transitions=new List<TransitionInstance>();public List<Extension> extensions=new List<Extension>();public double Length {get{return clips.Count==0?0:clips.Max(c=>c.End);}}}
 public class Project {public string format="prometheus-project",schema_version="0.7.6",id,name="Sin título";public int revision;public string active_sequence_id;public List<Asset> assets=new List<Asset>();public List<EffectResource> effect_resources=new List<EffectResource>();public List<MgtTransitionResource> mgt_transitions=new List<MgtTransitionResource>();public List<Sequence> sequences=new List<Sequence>();public List<Extension> extensions=new List<Extension>();public Sequence Active {get{return sequences.Find(s=>s.id==active_sequence_id);}} }
 public static class Store {
  public static string Id(){return Guid.NewGuid().ToString("N");}
  public static JavaScriptSerializer Json(){return new JavaScriptSerializer{MaxJsonLength=16*1024*1024,RecursionLimit=80};}
  // Serialize fields only: convenience properties Seconds/End/Length/Active must never leak into the contract.
  public static object Fields(object x){if(x==null)return null;Type t=x.GetType();if(t.IsPrimitive||x is string||x is decimal)return x;var list=x as System.Collections.IEnumerable;if(list!=null&&!(x is System.Collections.IDictionary)){var a=new List<object>();foreach(var v in list)a.Add(Fields(v));return a;}var dic=x as System.Collections.IDictionary;if(dic!=null){var m=new Dictionary<string,object>();foreach(System.Collections.DictionaryEntry e in dic)m[Convert.ToString(e.Key)]=Fields(e.Value);return m;}var o=new Dictionary<string,object>();foreach(var f in t.GetFields().Where(f=>!f.IsNotSerialized))o[f.Name]=Fields(f.GetValue(x));return o;}
  public static string Encode(Project p){NormalizeAssets(p);return Json().Serialize(Fields(p));}
  static void Shape(object node,Type type){if(node==null)return;var optional=Nullable.GetUnderlyingType(type);if(optional!=null)type=optional;if(type.IsPrimitive||type==typeof(string)||type==typeof(decimal))return;if(type.IsGenericType&&type.GetGenericTypeDefinition()==typeof(Dictionary<,>))return;if(type.IsGenericType&&type.GetGenericTypeDefinition()==typeof(List<>)){var values=node as System.Collections.IEnumerable;if(values==null)throw new Exception("Lista inválida");foreach(var value in values)Shape(value,type.GetGenericArguments()[0]);return;}if(type==typeof(object))return;var dic=node as Dictionary<string,object>;if(dic==null)throw new Exception("Objeto inválido");var fields=type.GetFields().Where(f=>!f.IsNotSerialized).ToArray();if(dic.Count!=fields.Length||fields.Any(f=>!dic.ContainsKey(f.Name)))throw new Exception("Campos ausentes o desconocidos en "+type.Name);foreach(var f in fields)Shape(dic[f.Name],f.FieldType);}
  public static Project Decode(string s){
   var raw=Json().DeserializeObject(s) as Dictionary<string,object>;
   if(raw==null)throw new Exception("Proyecto inválido");
   object version;
   if(raw.TryGetValue("schema_version",out version)&&Convert.ToString(version)=="0.2.0"){
    raw["schema_version"]="0.3.0";
    raw["effect_resources"]=new object[0];
   }
   if(Convert.ToString(raw["schema_version"])=="0.3.0"){
    raw["schema_version"]="0.4.0";
    foreach(var sequence in (object[])raw["sequences"]){var seq=(Dictionary<string,object>)sequence;
     foreach(var track in (object[])seq["tracks"]){var item=(Dictionary<string,object>)track;item["effects_enabled"]=true;item["effects"]=new object[0];}
     foreach(var clip in (object[])seq["clips"]){((Dictionary<string,object>)clip)["inherit_track_effects"]=true;}
    }
   }
   if(Convert.ToString(raw["schema_version"])=="0.4.0"){
    raw["schema_version"]="0.5.0";
    foreach(var sequence in (object[])raw["sequences"])((Dictionary<string,object>)sequence)["transitions"]=new object[0];
   }
   if(Convert.ToString(raw["schema_version"])=="0.5.0"){
    raw["schema_version"]="0.6.0";raw["mgt_transitions"]=new object[0];
    foreach(var sequence in (object[])raw["sequences"]){var seq=(Dictionary<string,object>)sequence;
     foreach(var transition in (object[])seq["transitions"])((Dictionary<string,object>)transition)["mgt_resource_id"]=null;
    }
   }
   if(Convert.ToString(raw["schema_version"])=="0.6.0"){
    raw["schema_version"]="0.7.0";
    foreach(var sequence in (object[])raw["sequences"]){var seq=(Dictionary<string,object>)sequence;
     foreach(var clip in (object[])seq["clips"])((Dictionary<string,object>)clip)["animation"]=new object[0];
    }
   }
   if(Convert.ToString(raw["schema_version"])=="0.7.0"){
    raw["schema_version"]="0.7.1";
    foreach(var sequence in (object[])raw["sequences"]){var seq=(Dictionary<string,object>)sequence;
     foreach(var clip in (object[])seq["clips"])((Dictionary<string,object>)clip)["mask"]=null;
    }
   }
   if(Convert.ToString(raw["schema_version"])=="0.7.1"){raw["schema_version"]="0.7.2";foreach(var item in (object[])raw["mgt_transitions"])((Dictionary<string,object>)item)["catalog_hidden"]=false;}
   if(Convert.ToString(raw["schema_version"])=="0.7.2"){
    raw["schema_version"]="0.7.3";
    foreach(var sequence in (object[])raw["sequences"]){var seq=(Dictionary<string,object>)sequence;foreach(var clip in (object[])seq["clips"]){var item=(Dictionary<string,object>)clip;
     foreach(var animation in (object[])item["animation"]){var curve=(Dictionary<string,object>)animation;foreach(var vertex in (object[])curve["points"]){var point=(Dictionary<string,object>)vertex;point["in_tangent"]=1.0;point["out_tangent"]=1.0;}}
    }}
   }
   if(Convert.ToString(raw["schema_version"])=="0.7.3"){
    raw["schema_version"]="0.7.4";
    foreach(var sequence in (object[])raw["sequences"]){var seq=(Dictionary<string,object>)sequence;foreach(var clip in (object[])seq["clips"]){var item=(Dictionary<string,object>)clip;
     foreach(var animation in (object[])item["animation"])((Dictionary<string,object>)animation)["enabled"]=true;
    }}
   }
   if(Convert.ToString(raw["schema_version"])=="0.7.4"){
    raw["schema_version"]="0.7.5";
    foreach(var sequence in (object[])raw["sequences"])((Dictionary<string,object>)sequence)["edit_groups"]=new object[0];
   }
   if(Convert.ToString(raw["schema_version"])=="0.7.5"){
    raw["schema_version"]="0.7.6";
    foreach(var asset in (object[])raw["assets"]){var media=(Dictionary<string,object>)asset;foreach(var stream in (object[])media["streams"])((Dictionary<string,object>)stream)["display_rotation"]=null;}
   }
   s=Json().Serialize(raw);
   Shape(Json().DeserializeObject(s),typeof(Project));var p=Json().Deserialize<Project>(s);Validate(p);return p;
  }
  public static Project Copy(Project p){return Decode(Encode(p));}
  public static Project New(){var p=new Project{id=Id()};var s=new Sequence{id=Id(),name="Secuencia 01"};p.sequences.Add(s);p.active_sequence_id=s.id;AddTrack(s,"video");AddTrack(s,"video");AddTrack(s,"audio");AddTrack(s,"audio");return p;}
  public static Track AddTrack(Sequence s,string kind){int i=s.tracks.Where(z=>z.kind==kind).Select(z=>z.order+1).DefaultIfEmpty(0).Max();var t=new Track{id=Id(),name=(kind=="video"?"V":"A")+(i+1),kind=kind,order=i};s.tracks.Add(t);return t;}
  public static string PathOf(Locator locator,string projectDir){return locator.relative?Path.GetFullPath(Path.Combine(projectDir,locator.path)):locator.path;}
  public static string PathOf(Asset a,string projectDir){NormalizeAsset(a);return a.locator==null?null:PathOf(a.locator,projectDir);}
  public static void NormalizeAssets(Project p){if(p!=null&&p.assets!=null)foreach(var asset in p.assets)NormalizeAsset(asset);}
  public static void NormalizeAsset(Asset asset){
   if(asset==null)return;
   if(asset.source==null&&asset.locator!=null)asset.source=new MediaSource{kind="local",uri=asset.locator.path,relative=asset.locator.relative};
   if(asset.representations==null)asset.representations=new List<MediaRepresentation>();
   if(asset.representations.Count==0&&asset.locator!=null)asset.representations.Add(new MediaRepresentation{role="original",locator=asset.locator,sha256=asset.sha256});
   var preferred=asset.representations.FirstOrDefault(r=>r!=null&&r.role=="final")??asset.representations.FirstOrDefault(r=>r!=null&&r.role=="original")??asset.representations.FirstOrDefault(r=>r!=null&&r.role=="proxy")??asset.representations.FirstOrDefault(r=>r!=null&&r.role=="preview");
   asset.locator=preferred==null?null:preferred.locator;asset.sha256=preferred==null?null:preferred.sha256;
  }
  static void RationalOk(Rational r){if(r==null||r.d<=0||Math.Abs((double)r.n)>9007199254740991d||r.d>9007199254740991L)throw new Exception("Tiempo inválido");}
  public static void Validate(Project p){
   if(p==null||p.format!="prometheus-project"||p.schema_version!="0.7.6")throw new Exception("Versión de proyecto incompatible");
   NormalizeAssets(p);
   var ids=new HashSet<string>();Action<string> id=x=>{if(String.IsNullOrWhiteSpace(x)||!ids.Add(x))throw new Exception("ID ausente o repetido");};id(p.id);
   if(p.Active==null)throw new Exception("Falta secuencia activa");
   if(p.effect_resources==null)throw new Exception("Faltan recursos de efectos");
   if(p.mgt_transitions==null)throw new Exception("Faltan transiciones MGT");
   foreach(var resource in p.mgt_transitions){if(resource==null)throw new Exception("Transición MGT inválida");id(resource.id);MgtTransitionResource.Validate(resource);}
   foreach(var resource in p.effect_resources){if(resource==null)throw new Exception("Recurso de efecto inválido");id(resource.id);if(resource.kind!="lut3d"||String.IsNullOrWhiteSpace(resource.uri))throw new Exception("Recurso de efecto inválido");if(resource.relative&&Path.IsPathRooted(resource.uri))throw new Exception("Ruta relativa de efecto inválida");}
   foreach(var a in p.assets){id(a.id);if(a.source==null||String.IsNullOrWhiteSpace(a.source.uri)||!(new[]{"local","web","mgt","generated"}).Contains(a.source.kind))throw new Exception("Origen de medio inválido");if(a.source.relative&&(a.source.kind=="web"||Path.IsPathRooted(a.source.uri)))throw new Exception("Origen relativo inválido");if(a.source.kind=="generated"&&(GeneratedMedia.Kind(a)!="solid"&&GeneratedMedia.Kind(a)!="placeholder"&&GeneratedMedia.Kind(a)!="text"&&GeneratedMedia.Kind(a)!="nested"||!GeneratedMedia.ValidStreams(a)))throw new Exception("Medio generado inválido");RationalOk(a.presentation_origin);if(a.recommended_duration!=null){RationalOk(a.recommended_duration);if(a.recommended_duration.Seconds<=0)throw new Exception("Duración recomendada inválida");}var roles=new HashSet<string>();foreach(var rep in a.representations){if(rep==null||!(new[]{"original","preview","proxy","final"}).Contains(rep.role)||!roles.Add(rep.role)||rep.locator==null||String.IsNullOrWhiteSpace(rep.locator.path))throw new Exception("Representación inválida");if(rep.locator.relative&&Path.IsPathRooted(rep.locator.path))throw new Exception("Ruta relativa inválida");if(rep.size_bytes<0)throw new Exception("Tamaño de medio inválido");if(rep.stream_ordinals!=null)foreach(var map in rep.stream_ordinals)if(map.Value<0||!a.streams.Any(st=>st.id==map.Key))throw new Exception("Mapa de stream inválido");}var ord=new HashSet<int>();foreach(var st in a.streams){id(st.id);if(!ord.Add(st.ordinal)||st.ordinal<0)throw new Exception("Stream inválido");RationalOk(st.pts_origin);RationalOk(st.time_base);if(st.time_base.Seconds<=0)throw new Exception("Base temporal inválida");if(st.kind!="image"){if(st.duration==null&&GeneratedMedia.IsUntimedPlaceholder(a))continue;RationalOk(st.duration);if(st.duration.Seconds<=0)throw new Exception("Duración de medio inválida");}}}
   foreach(var asset in p.assets)GeneratedMedia.ValidateText(asset);
   var sequenceNames=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var s in p.sequences){id(s.id);if(String.IsNullOrWhiteSpace(s.name)||s.name.Length>80||!sequenceNames.Add(s.name.Trim()))throw new Exception("Nombre de secuencia vacío o repetido");SequenceCanvas.Validate(s.width,s.height);if(s.fps.n!=60||s.fps.d!=1||s.mix_sample_rate!=48000||s.mix_channels!=2||s.color_space!="rec709-sdr")throw new Exception("El prototipo admite secuencias SDR a 60 FPS y audio estéreo 48 kHz");var orders=new HashSet<string>();foreach(var t in s.tracks){id(t.id);if((t.kind!="video"&&t.kind!="audio")||t.order<0||!orders.Add(t.kind+":"+t.order))throw new Exception("Pista inválida");if(t.gain_db< -60||t.gain_db>12)throw new Exception("Volumen inválido");if(Double.IsNaN(t.balance)||t.balance< -1||t.balance>1)throw new Exception("Balance de pista inválido");EffectContracts.Validate(p,t.effects,t.kind);}
    foreach(var c in s.clips){id(c.id);var t=s.tracks.Find(z=>z.id==c.track_id);var a=p.assets.Find(z=>z.id==c.asset_id);var st=a==null?null:a.streams.Find(z=>z.id==c.stream_id);if(t==null||st==null||t.kind!=(st.kind=="audio"?"audio":"video"))throw new Exception("Referencia de clip inválida");foreach(var r in new[]{c.timeline_start,c.duration,c.source_in,c.speed})RationalOk(r);if(c.timeline_start.Seconds<0||c.duration.Seconds<=0||c.source_in.Seconds<0||c.speed.Seconds<=0)throw new Exception("Rango de clip inválido");if(st.duration!=null&&!GeneratedMedia.IsUntimedPlaceholder(a)&&c.source_in.Seconds+c.duration.Seconds*c.speed.Seconds>st.duration.Seconds+0.000001&&!ShortTail.Allows(c,st))throw new Exception("El recorte excede el medio");double grid=t.kind=="video"?60:48000;if(Math.Abs(c.timeline_start.Seconds*grid-Math.Round(c.timeline_start.Seconds*grid))>0.00001||Math.Abs(c.duration.Seconds*grid-Math.Round(c.duration.Seconds*grid))>0.00001)throw new Exception("Clip fuera de cuadrícula temporal");if(c.gain_db< -60||c.gain_db>12)throw new Exception("Volumen inválido");if(Double.IsNaN(c.balance)||c.balance< -1||c.balance>1)throw new Exception("Balance de clip inválido");EffectContracts.Validate(p,c,t.kind);ClipFraming.Validate(c);ClipKeyframes.Validate(c);ClipMasks.Validate(c.mask);VectorMaskStack0104.Validate(c);TemporalMasks0823.Validate(c);Sam2MaskStore090.Validate(c);NestedMedia0910.Validate(p,c);}
    if(SubtitleTrack0817.TrackId(s)!=null&&(SubtitleTrack0817.Track(s)==null||SubtitleTrack0817.Track(s).kind!="video"))throw new Exception("Falta la pista de subtítulos");
    foreach(var cue in s.clips.Where(c=>c.track_id==SubtitleTrack0817.TrackId(s)))if(!GeneratedMedia.IsSubtitle(p.assets.Find(a=>a.id==cue.asset_id)))throw new Exception("La pista de subtítulos solo admite texto de subtítulos");
    foreach(var t in s.tracks){var cs=s.clips.Where(c=>c.track_id==t.id).OrderBy(c=>c.timeline_start.Seconds).ToList();for(int i=1;i<cs.Count;i++)if(cs[i].timeline_start.Seconds<cs[i-1].End-0.000001)throw new Exception("Hay una colisión en "+t.name+". Usa otra pista o deja un hueco.");}
    var members=new HashSet<string>();foreach(var g in s.link_groups){id(g.id);if(g.clip_ids.Count<2)throw new Exception("Vínculo incompleto");foreach(var cid in g.clip_ids)if(!s.clips.Any(c=>c.id==cid)||!members.Add(cid))throw new Exception("Vínculo inválido");}
    var grouped=new HashSet<string>();var numbers=new HashSet<int>();foreach(var g in s.edit_groups){id(g.id);if(g.number<1||!numbers.Add(g.number)||g.clip_ids==null||g.clip_ids.Count<2)throw new Exception("Grupo de edición inválido");foreach(var cid in g.clip_ids)if(!s.clips.Any(c=>c.id==cid)||!grouped.Add(cid))throw new Exception("Clip repetido en grupos de edición");}
    Ducking01016.Validate(s);
    TransitionContracts.Validate(p,s,id);
   }
  }
  static void RewriteRangePaths(Asset asset,Func<string,string> rewrite){
   var extension=asset.extensions.FirstOrDefault(x=>x!=null&&x.@namespace=="prometheus.web.ranges"&&x.version=="1"&&x.payload!=null);
   object raw;if(extension==null||!extension.payload.TryGetValue("segments_json",out raw)||raw==null)return;
   var ranges=Json().Deserialize<List<Dictionary<string,object>>>(Convert.ToString(raw));
   if(ranges==null)return;
   foreach(var range in ranges){object value;if(range!=null&&range.TryGetValue("path",out value)&&value!=null)range["path"]=rewrite(Convert.ToString(value));}
   extension.payload["segments_json"]=Json().Serialize(ranges);
  }
  public static Project Load(string file){var p=Decode(File.ReadAllText(file));string dir=Path.GetDirectoryName(Path.GetFullPath(file));foreach(var a in p.assets){if((a.source.kind=="local"||a.source.kind=="mgt")&&a.source.relative){a.source.uri=Path.GetFullPath(Path.Combine(dir,a.source.uri));a.source.relative=false;}foreach(var rep in a.representations)if(rep.locator.relative){rep.locator.path=PathOf(rep.locator,dir);rep.locator.relative=false;}RewriteRangePaths(a,path=>Path.IsPathRooted(path)?path:Path.GetFullPath(Path.Combine(dir,path)));NormalizeAsset(a);}foreach(var resource in p.effect_resources)if(resource.relative){resource.uri=Path.GetFullPath(Path.Combine(dir,resource.uri));resource.relative=false;}return p;}
  static string RelativeInside(string path,string directory,out bool relative){relative=false;if(String.IsNullOrWhiteSpace(path))return path;if(!Path.IsPathRooted(path)){relative=true;return path;}string full=Path.GetFullPath(path),root=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase))return path;relative=true;return full.Substring(root.Length);}
  public static void Save(Project p,string file){
   Validate(p);string fullFile=Path.GetFullPath(file),directory=Path.GetDirectoryName(fullFile),tmp=fullFile+".tmp-"+Id();
   var snapshot=Copy(p);
   foreach(var asset in snapshot.assets){
    bool relative;
    if(asset.source.kind=="local"||asset.source.kind=="mgt"){asset.source.uri=RelativeInside(asset.source.uri,directory,out relative);asset.source.relative=relative;}
    foreach(var representation in asset.representations){representation.locator.path=RelativeInside(representation.locator.path,directory,out relative);representation.locator.relative=relative;}
    RewriteRangePaths(asset,path=>{bool inside;return RelativeInside(path,directory,out inside);});
   }
   foreach(var resource in snapshot.effect_resources){bool relative;resource.uri=RelativeInside(resource.uri,directory,out relative);resource.relative=relative;}
   try{File.WriteAllText(tmp,Encode(snapshot),new System.Text.UTF8Encoding(false));Decode(File.ReadAllText(tmp));if(File.Exists(fullFile))File.Replace(tmp,fullFile,fullFile+".bak");else File.Move(tmp,fullFile);}finally{if(File.Exists(tmp))File.Delete(tmp);}
  }
 }
 public partial class Editor {
  public static void Overwrite(Project p,IEnumerable<string> incomingIds){var incoming=new HashSet<string>(incomingIds);var masks=p.Active.clips.Where(c=>incoming.Contains(c.id)).ToList();foreach(var clip in masks){var track=p.Active.tracks.Find(t=>t.id==clip.track_id);if(track==null||track.locked)throw new Exception("Pista de destino ausente o bloqueada");}var replacements=new Dictionary<string,List<Clip>>();foreach(var old in p.Active.clips.Where(c=>!incoming.Contains(c.id)).ToList()){var covers=masks.Where(c=>c.track_id==old.track_id&&c.timeline_start.Seconds<old.End-0.000001&&c.End>old.timeline_start.Seconds+0.000001).OrderBy(c=>c.timeline_start.Seconds).ToList();if(covers.Count==0)continue;var track=p.Active.tracks.Find(t=>t.id==old.track_id);if(track.locked)throw new Exception("Pista bloqueada");long begin=(long)Math.Round(old.timeline_start.Seconds*48000),end=(long)Math.Round(old.End*48000),cursor=begin;var pieces=new List<Clip>();Action<long,long> retain=(start,finish)=>{if(finish<=start)return;var piece=Store.Json().Deserialize<Clip>(Store.Json().Serialize(Store.Fields(old)));piece.id=pieces.Count==0?old.id:Store.Id();piece.timeline_start=new Rational(start,48000);piece.duration=new Rational(finish-start,48000);piece.source_in=Rational.Time(old.source_in.Seconds+(start-begin)/48000.0*old.speed.Seconds);pieces.Add(piece);};foreach(var cover in covers){long start=Math.Max(begin,(long)Math.Round(cover.timeline_start.Seconds*48000)),finish=Math.Min(end,(long)Math.Round(cover.End*48000));retain(cursor,Math.Min(start,end));cursor=Math.Max(cursor,finish);}retain(cursor,end);p.Active.clips.Remove(old);p.Active.clips.AddRange(pieces);replacements[old.id]=pieces;}foreach(var group in p.Active.link_groups.ToList()){if(!group.clip_ids.Any(replacements.ContainsKey))continue;p.Active.link_groups.Remove(group);var unchanged=group.clip_ids.Where(id=>!replacements.ContainsKey(id)&&p.Active.clips.Any(c=>c.id==id)).ToList();if(unchanged.Count>1)p.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=unchanged});var fragments=group.clip_ids.Where(replacements.ContainsKey).SelectMany(id=>replacements[id]).ToList();foreach(var matching in fragments.GroupBy(c=>Math.Round(c.timeline_start.Seconds*48000)+":"+Math.Round(c.End*48000))){var ids=matching.Select(c=>c.id).ToList();if(ids.Count>1)p.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=ids});}}}
  public Project Project;readonly Stack<string> undo=new Stack<string>(),redo=new Stack<string>();
  readonly List<Tuple<Project,Project>> materializedPaths=new List<Tuple<Project,Project>>();
  sealed class DerivedUpdate {public string assetId,sourceUri;public Action<Project> apply;}
  readonly List<DerivedUpdate> derivedUpdates=new List<DerivedUpdate>();
  public Editor(){Project=Store.New();}
  public void Commit(Action<Project> operation){var p=Store.Copy(Project);operation(p);foreach(var sequence in p.sequences){var live=new HashSet<string>(sequence.clips.Select(c=>c.id));foreach(var group in sequence.edit_groups)group.clip_ids=group.clip_ids.Where(live.Contains).Distinct().ToList();sequence.edit_groups.RemoveAll(g=>g.clip_ids.Count<2);}ShortTail.Normalize(p);TransitionContracts.Normalize(p);p.revision=Project.revision+1;Store.Validate(p);undo.Push(Store.Encode(Project));redo.Clear();Project=p;}
  // Prepared media is derived state. Replay its small patches on demand when traversing
  // history instead of decoding and rewriting every undo/redo snapshot on download completion.
  public bool UpdateDerivedAsset(string assetId,string sourceUri,Action<Project> operation){
   if(!DerivedMatches(Project,assetId,sourceUri))return false;
   var next=Store.Copy(Project);operation(next);ShortTail.Normalize(next);Store.Validate(next);
   Project=next;derivedUpdates.Add(new DerivedUpdate{assetId=assetId,sourceUri=sourceUri,apply=operation});
   return true;
  }
  static bool DerivedMatches(Project p,string assetId,string sourceUri){var asset=p.assets.Find(a=>a.id==assetId);return asset!=null&&asset.source!=null&&asset.source.uri==sourceUri;}
  static object RebasePayload(object historical,object before,object after){
   var oldMap=before as Dictionary<string,object>;var newMap=after as Dictionary<string,object>;var currentMap=historical as Dictionary<string,object>;
   if(oldMap!=null&&newMap!=null&&currentMap!=null){foreach(string key in oldMap.Keys.Where(k=>newMap.ContainsKey(k)&&currentMap.ContainsKey(k)).ToList())currentMap[key]=RebasePayload(currentMap[key],oldMap[key],newMap[key]);return currentMap;}
   var oldList=before as object[];var newList=after as object[];var currentList=historical as object[];
   if(oldList!=null&&newList!=null&&currentList!=null){for(int i=0;i<Math.Min(currentList.Length,Math.Min(oldList.Length,newList.Length));i++)currentList[i]=RebasePayload(currentList[i],oldList[i],newList[i]);return currentList;}
   var oldText=before as string;var newText=after as string;var currentText=historical as string;
   return oldText!=null&&newText!=null&&currentText==oldText?newText:historical;
  }
  static void RebasePaths(Project history,Project before,Project after){
   foreach(var asset in history.assets){var old=before.assets.Find(a=>a.id==asset.id);var updated=after.assets.Find(a=>a.id==asset.id);if(old==null||updated==null)continue;
    if(asset.source.uri==old.source.uri){asset.source.uri=updated.source.uri;asset.source.relative=updated.source.relative;}
    foreach(var rep in asset.representations){var prior=old.representations.Find(r=>r.role==rep.role);var replacement=updated.representations.Find(r=>r.role==rep.role);if(prior!=null&&replacement!=null&&rep.locator.path==prior.locator.path){rep.locator.path=replacement.locator.path;rep.locator.relative=replacement.locator.relative;}}
    foreach(var extension in asset.extensions){var prior=old.extensions.FirstOrDefault(e=>e.@namespace==extension.@namespace);var replacement=updated.extensions.FirstOrDefault(e=>e.@namespace==extension.@namespace);if(prior!=null&&replacement!=null)RebasePayload(extension.payload,prior.payload,replacement.payload);}
    Store.NormalizeAsset(asset);
   }
   foreach(var resource in history.effect_resources){var old=before.effect_resources.Find(r=>r.id==resource.id);var updated=after.effect_resources.Find(r=>r.id==resource.id);if(old!=null&&updated!=null&&resource.uri==old.uri){resource.uri=updated.uri;resource.relative=updated.relative;}}
   foreach(var sequence in history.sequences)foreach(var clip in sequence.clips){var oldSequence=before.sequences.Find(s=>s.id==sequence.id);var newSequence=after.sequences.Find(s=>s.id==sequence.id);var old=oldSequence==null?null:oldSequence.clips.Find(c=>c.id==clip.id);var updated=newSequence==null?null:newSequence.clips.Find(c=>c.id==clip.id);if(old==null||updated==null)continue;var oldMask=Sam2MaskStore090.Get(old);var newMask=Sam2MaskStore090.Get(updated);var currentMask=Sam2MaskStore090.Get(clip);if(oldMask!=null&&newMask!=null&&currentMask!=null&&currentMask.directory==oldMask.directory){currentMask.directory=newMask.directory;Sam2MaskStore090.Put(clip,currentMask);}}
  }
  static void RebaseStack(Stack<string> stack,Project before,Project after){var entries=stack.Reverse().ToList();stack.Clear();foreach(string entry in entries){var history=Store.Decode(entry);RebasePaths(history,before,after);Store.Validate(history);stack.Push(Store.Encode(history));}}
  static bool MaterializedAssetChanged(Asset old,Asset updated){
   if(old.source.uri!=updated.source.uri||old.source.relative!=updated.source.relative)return true;
   foreach(var rep in old.representations){var next=updated.representations.Find(r=>r.role==rep.role);if(next!=null&&(rep.locator.path!=next.locator.path||rep.locator.relative!=next.locator.relative))return true;}
   foreach(var extension in old.extensions){var next=updated.extensions.FirstOrDefault(e=>e.@namespace==extension.@namespace);if(next!=null&&Store.Json().Serialize(Store.Fields(extension.payload))!=Store.Json().Serialize(Store.Fields(next.payload)))return true;}
   return false;
  }
  static void KeepChangedMaterializedPaths(Project source,Project target){
   source.assets.RemoveAll(a=>{var next=target.assets.Find(b=>b.id==a.id);return next==null||!MaterializedAssetChanged(a,next);});
   var assetIds=new HashSet<string>(source.assets.Select(a=>a.id));target.assets.RemoveAll(a=>!assetIds.Contains(a.id));
   source.effect_resources.RemoveAll(r=>{var next=target.effect_resources.Find(x=>x.id==r.id);return next==null||(r.uri==next.uri&&r.relative==next.relative);});
   var resourceIds=new HashSet<string>(source.effect_resources.Select(r=>r.id));target.effect_resources.RemoveAll(r=>!resourceIds.Contains(r.id));
   foreach(var seq in source.sequences){var next=target.sequences.Find(s=>s.id==seq.id);seq.clips.RemoveAll(c=>{var newer=next==null?null:next.clips.Find(x=>x.id==c.id);var oldMask=Sam2MaskStore090.Get(c);var newMask=newer==null?null:Sam2MaskStore090.Get(newer);return oldMask==null||newMask==null||oldMask.directory==newMask.directory;});seq.tracks.Clear();seq.link_groups.Clear();seq.edit_groups.Clear();seq.transitions.Clear();seq.extensions.Clear();}
   source.sequences.RemoveAll(s=>s.clips.Count==0);
   var sequenceIds=new HashSet<string>(source.sequences.Select(s=>s.id));target.sequences.RemoveAll(s=>!sequenceIds.Contains(s.id));
   foreach(var seq in target.sequences){var prior=source.sequences.Find(s=>s.id==seq.id);var clipIds=new HashSet<string>(prior.clips.Select(c=>c.id));seq.clips.RemoveAll(c=>!clipIds.Contains(c.id));seq.tracks.Clear();seq.link_groups.Clear();seq.edit_groups.Clear();seq.transitions.Clear();seq.extensions.Clear();}
   source.mgt_transitions.Clear();target.mgt_transitions.Clear();source.extensions.Clear();target.extensions.Clear();
  }
  public void SyncMaterializedPaths(Project before,Project after){Store.Validate(after);var source=Store.Copy(before);var target=Store.Copy(after);RebaseStack(undo,source,target);RebaseStack(redo,source,target);foreach(var patch in derivedUpdates){var old=source.assets.Find(a=>a.id==patch.assetId);var updated=target.assets.Find(a=>a.id==patch.assetId);if(old!=null&&updated!=null&&patch.sourceUri==old.source.uri)patch.sourceUri=updated.source.uri;}KeepChangedMaterializedPaths(source,target);if(source.assets.Count>0||source.effect_resources.Count>0||source.sequences.Count>0)materializedPaths.Add(Tuple.Create(source,target));Project=after;}
  Project RestoreDerived(Project p){foreach(var patch in derivedUpdates)if(DerivedMatches(p,patch.assetId,patch.sourceUri)){patch.apply(p);var asset=p.assets.Find(a=>a.id==patch.assetId);asset.representations=asset.representations.GroupBy(r=>r.role).Select(g=>g.Last()).ToList();}foreach(var mapping in materializedPaths)RebasePaths(p,mapping.Item1,mapping.Item2);ShortTail.Normalize(p);Store.Validate(p);return p;}
  public void Reset(Project p){Store.Validate(p);Project=p;undo.Clear();redo.Clear();derivedUpdates.Clear();materializedPaths.Clear();}
  public void Undo(){if(undo.Count==0)return;var previous=RestoreDerived(Store.Decode(undo.Peek()));redo.Push(Store.Encode(Project));undo.Pop();Project=previous;}
  public void Redo(){if(redo.Count==0)return;var next=RestoreDerived(Store.Decode(redo.Peek()));undo.Push(Store.Encode(Project));redo.Pop();Project=next;}
  public List<string> Related(string cid){var g=Project.Active.link_groups.Find(x=>x.clip_ids.Contains(cid));return g==null?new List<string>{cid}:g.clip_ids.ToList();}
  public void Move(string cid,double start,string targetTrack){MoveSelection(new List<string>{cid},cid,start,targetTrack);} public void MoveIndependent(string cid,double start,string targetTrack){var clip=Project.Active.clips.Find(c=>c.id==cid);if(clip==null)throw new Exception("Selecciona un clip");double delta=Rational.Frame(start-clip.timeline_start.Seconds).Seconds;Commit(p=>{var c=p.Active.clips.Find(x=>x.id==cid);if(p.Active.tracks.Find(t=>t.id==c.track_id).locked)throw new Exception("Pista bloqueada");if(targetTrack!=null){var t=p.Active.tracks.Find(x=>x.id==targetTrack);if(t==null||t.locked)throw new Exception("Pista de destino bloqueada");var asset=Project.assets.Find(a=>a.id==c.asset_id);var stream=asset.streams.Find(x=>x.id==c.stream_id);if(t.kind!=(stream.kind=="audio"?"audio":"video"))throw new Exception("El tipo de pista no es compatible con este medio");c.track_id=targetTrack;}c.timeline_start=Rational.Time(Math.Max(0,c.timeline_start.Seconds+delta));Overwrite(p,new[]{cid});});}
  public void Cut(string cid,double at){var related=Related(cid);at=Rational.Frame(at).Seconds;Commit(p=>{var rights=new List<string>();var lefts=new List<string>();bool changed=false;foreach(var c in p.Active.clips.Where(c=>related.Contains(c.id)).ToList()){if(p.Active.tracks.Find(t=>t.id==c.track_id).locked)throw new Exception("Pista bloqueada");double offset=at-c.timeline_start.Seconds;if(offset<=0.000001){rights.Add(c.id);continue;}if(offset>=c.duration.Seconds-0.000001){lefts.Add(c.id);continue;}var right=Store.Json().Deserialize<Clip>(Store.Json().Serialize(Store.Fields(c)));right.id=Store.Id();right.timeline_start=Rational.Time(at);right.duration=Rational.Time(c.duration.Seconds-offset);right.source_in=Rational.Time(c.source_in.Seconds+offset*c.speed.Seconds);c.duration=Rational.Time(offset);p.Active.clips.Add(right);rights.Add(right.id);lefts.Add(c.id);changed=true;}if(!changed)throw new Exception("Coloca el cursor dentro del recorte");p.Active.link_groups.RemoveAll(g=>g.clip_ids.Any(related.Contains));if(lefts.Count>1)p.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=lefts});if(rights.Count>1)p.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=rights});});}
  public void Delete(string cid){DeleteSelection(new List<string>{cid});}
  public void DeleteSelection(IEnumerable<string> selection){
   var requested=new HashSet<string>((selection??Enumerable.Empty<string>()).Where(id=>Project.Active.clips.Any(c=>c.id==id)));
   if(requested.Count==0)return;
   var related=new HashSet<string>(requested.SelectMany(Related));
   Commit(p=>{
    foreach(var c in p.Active.clips.Where(c=>related.Contains(c.id)))if(p.Active.tracks.Find(t=>t.id==c.track_id).locked)throw new Exception("Una pista de la selección está bloqueada");
    p.Active.clips.RemoveAll(c=>related.Contains(c.id));
    foreach(var g in p.Active.link_groups)g.clip_ids.RemoveAll(related.Contains);
    p.Active.link_groups.RemoveAll(g=>g.clip_ids.Count<2);
   });
  }
  static bool Still(Project p,Clip clip){
   var asset=p.assets.Find(a=>a.id==clip.asset_id);
   var stream=asset==null?null:asset.streams.Find(st=>st.id==clip.stream_id);
   return stream!=null&&(stream.kind=="image"||GeneratedMedia.IsUntimedPlaceholder(asset));
  }
  public double TrimEdge(string cid,bool left,double edge){
   var related=Related(cid);var original=Project.Active.clips.Find(c=>c.id==cid);
   double low=Double.NegativeInfinity,high=Double.PositiveInfinity;
   bool video=related.Any(id=>Project.Active.tracks.Find(t=>t.id==Project.Active.clips.Find(c=>c.id==id).track_id).kind=="video");
   foreach(var c in Project.Active.clips.Where(c=>related.Contains(c.id))){
    var st=Project.assets.Find(a=>a.id==c.asset_id).streams.Find(x=>x.id==c.stream_id);
    bool still=st.kind=="image"||GeneratedMedia.IsUntimedPlaceholder(Project.assets.Find(a=>a.id==c.asset_id));double minimum=st.kind=="audio"?1.0/48000:1.0/60;
    if(left){
     low=Math.Max(low,still?-c.timeline_start.Seconds:Math.Max(-c.timeline_start.Seconds,-c.source_in.Seconds/c.speed.Seconds));
     high=Math.Min(high,c.duration.Seconds-minimum);
     var previous=Project.Active.clips.Where(x=>x.track_id==c.track_id&&!related.Contains(x.id)&&x.End<=c.timeline_start.Seconds+0.000001).OrderByDescending(x=>x.End).FirstOrDefault();if(previous!=null)low=Math.Max(low,previous.End-c.timeline_start.Seconds);
    }else{
     low=Math.Max(low,minimum-c.duration.Seconds);
     var next=Project.Active.clips.Where(x=>x.track_id==c.track_id&&!related.Contains(x.id)&&x.timeline_start.Seconds>=c.End-0.000001).OrderBy(x=>x.timeline_start.Seconds).FirstOrDefault();if(next!=null)high=Math.Min(high,next.timeline_start.Seconds-c.End);
     if(!still&&st.duration!=null){
      if(ShortTail.Has(c)&&c.source_in.Seconds+c.duration.Seconds*c.speed.Seconds>st.duration.Seconds+0.000001)high=Math.Min(high,0);
      else high=Math.Min(high,(st.duration.Seconds-c.source_in.Seconds)/c.speed.Seconds-c.duration.Seconds);
     }
    }
   }
   double grid=video?60:48000;low=Math.Ceiling(low*grid-0.000001)/grid;high=Math.Floor(high*grid+0.000001)/grid;
   double anchor=left?original.timeline_start.Seconds:original.End;
   double delta=Math.Round((edge-anchor)*grid)/grid;
   return anchor+Math.Max(low,Math.Min(high,delta));
  }
  public void Trim(string cid,bool left,double edge){var related=Related(cid);var original=Project.Active.clips.Find(c=>c.id==cid);double delta=TrimEdge(cid,left,edge)-(left?original.timeline_start.Seconds:original.End);if(Math.Abs(delta)<0.000001)return;Commit(p=>{foreach(var c in p.Active.clips.Where(c=>related.Contains(c.id))){if(p.Active.tracks.Find(t=>t.id==c.track_id).locked)throw new Exception("Pista bloqueada");if(left){c.timeline_start=Rational.Time(c.timeline_start.Seconds+delta);if(!Still(p,c))c.source_in=Rational.Time(c.source_in.Seconds+delta*c.speed.Seconds);}c.duration=Rational.Time(c.duration.Seconds+(left?-delta:delta));}});}
  public void MoveSelection(List<string> selection,string anchorId,double start,string targetTrack){MoveSelectedGroup(selection,anchorId,start,targetTrack,true);}
  public void MoveSelectionAboveVideos(List<string> selection,string anchorId,double start,bool includeLinked){
   if(selection==null||selection.Count==0)throw new Exception("Selecciona clips para mover");
   var ids=new HashSet<string>(includeLinked?selection.SelectMany(Related):selection);ids.Add(anchorId);
   var anchor=Project.Active.clips.Find(c=>c.id==anchorId);if(anchor==null)throw new Exception("Clip de origen ausente");
   var anchorTrack=Project.Active.tracks.Find(t=>t.id==anchor.track_id);if(anchorTrack==null||anchorTrack.kind!="video"||anchorTrack.locked)throw new Exception("Arrastra un clip de vídeo para crear una pista superior");
   double delta=Rational.Frame(start-anchor.timeline_start.Seconds).Seconds;
   var locked=Project.Active.clips.Where(c=>ids.Contains(c.id)&&Project.Active.tracks.Any(t=>t.id==c.track_id&&t.locked)).ToList();
   if(locked.Count>0){var link=Project.Active.link_groups.FirstOrDefault(g=>g.clip_ids.Contains(anchorId));if(Math.Abs(delta)>0.000001||link==null||locked.Any(c=>!link.clip_ids.Contains(c.id)||Project.Active.tracks.Find(t=>t.id==c.track_id).kind!="audio"))throw new Exception("El conjunto incluye una pista bloqueada");foreach(var c in locked)ids.Remove(c.id);}
   var selected=Project.Active.clips.Where(c=>ids.Contains(c.id)).ToList();double earliest=selected.Min(c=>c.timeline_start.Seconds);if(delta< -earliest)delta=Math.Ceiling(-earliest*60)/60;
   int highest=Project.Active.tracks.Where(t=>t.kind=="video").Max(t=>t.order);int shift=highest+1-anchorTrack.order;
   Commit(p=>{
    int needed=selected.Where(c=>p.Active.tracks.Any(t=>t.id==c.track_id&&t.kind=="video")).Max(c=>p.Active.tracks.Find(t=>t.id==c.track_id).order+shift);
    while(p.Active.tracks.Where(t=>t.kind=="video").Max(t=>t.order)<needed)Store.AddTrack(p.Active,"video");
    foreach(var clip in p.Active.clips.Where(c=>ids.Contains(c.id))){var source=p.Active.tracks.Find(t=>t.id==clip.track_id);if(source==null||source.locked)throw new Exception("Pista bloqueada");if(source.kind=="video")clip.track_id=p.Active.tracks.First(t=>t.kind=="video"&&t.order==source.order+shift).id;clip.timeline_start=Rational.Time(clip.timeline_start.Seconds+delta);}
    Overwrite(p,ids);
   });
  }
  public void MoveSelectionBelowAudios(List<string> selection,string anchorId,double start,bool includeLinked){
   if(selection==null||selection.Count==0)throw new Exception("Selecciona clips para mover");
   var ids=new HashSet<string>(includeLinked?selection.SelectMany(Related):selection);ids.Add(anchorId);
   var anchor=Project.Active.clips.Find(c=>c.id==anchorId);if(anchor==null)throw new Exception("Clip de origen ausente");
   var anchorTrack=Project.Active.tracks.Find(t=>t.id==anchor.track_id);if(anchorTrack==null||anchorTrack.kind!="audio"||anchorTrack.locked)throw new Exception("Arrastra un clip de audio para crear una pista inferior");
   double delta=Rational.Frame(start-anchor.timeline_start.Seconds).Seconds;
   var locked=Project.Active.clips.Where(c=>ids.Contains(c.id)&&Project.Active.tracks.Any(t=>t.id==c.track_id&&t.locked)).ToList();
   if(locked.Count>0){var link=Project.Active.link_groups.FirstOrDefault(g=>g.clip_ids.Contains(anchorId));if(Math.Abs(delta)>0.000001||link==null||locked.Any(c=>!link.clip_ids.Contains(c.id)||Project.Active.tracks.Find(t=>t.id==c.track_id).kind!="video"))throw new Exception("El conjunto incluye una pista bloqueada");foreach(var c in locked)ids.Remove(c.id);}
   var selected=Project.Active.clips.Where(c=>ids.Contains(c.id)).ToList();double earliest=selected.Min(c=>c.timeline_start.Seconds);if(delta< -earliest)delta=Math.Ceiling(-earliest*60)/60;
   int lowest=Project.Active.tracks.Where(t=>t.kind=="audio").Max(t=>t.order);int shift=lowest+1-anchorTrack.order;
   Commit(p=>{
    int needed=selected.Where(c=>p.Active.tracks.Any(t=>t.id==c.track_id&&t.kind=="audio")).Max(c=>p.Active.tracks.Find(t=>t.id==c.track_id).order+shift);
    while(p.Active.tracks.Where(t=>t.kind=="audio").Max(t=>t.order)<needed)Store.AddTrack(p.Active,"audio");
    foreach(var clip in p.Active.clips.Where(c=>ids.Contains(c.id))){var source=p.Active.tracks.Find(t=>t.id==clip.track_id);if(source==null||source.locked)throw new Exception("Pista bloqueada");if(source.kind=="audio")clip.track_id=p.Active.tracks.First(t=>t.kind=="audio"&&t.order==source.order+shift).id;clip.timeline_start=Rational.Time(clip.timeline_start.Seconds+delta);}
    Overwrite(p,ids);
   });
  }
  public void MoveSelectedGroup(List<string> selection,string anchorId,double start,string targetTrack,bool includeLinked){
   if(selection==null||selection.Count==0)throw new Exception("Selecciona clips para mover");
   var ids=new HashSet<string>(includeLinked?selection.SelectMany(Related):selection);
   if(!ids.Contains(anchorId))ids.Add(anchorId);
   var anchor=Project.Active.clips.Find(c=>c.id==anchorId);if(anchor==null)throw new Exception("Clip de origen ausente");
   var sourceTrack=Project.Active.tracks.Find(t=>t.id==anchor.track_id);
   var destination=Project.Active.tracks.Find(t=>t.id==(targetTrack??anchor.track_id));
   if(destination==null||destination.kind!=sourceTrack.kind||sourceTrack.locked||destination.locked)throw new Exception("Pista de origen o destino incompatible o bloqueada");
   double delta=Rational.Frame(start-anchor.timeline_start.Seconds).Seconds;
   var lockedMembers=Project.Active.clips.Where(c=>ids.Contains(c.id)&&Project.Active.tracks.Any(t=>t.id==c.track_id&&t.locked)).ToList();
   if(lockedMembers.Count>0){var link=Project.Active.link_groups.FirstOrDefault(g=>g.clip_ids.Contains(anchorId));bool verticalOnly=Math.Abs(delta)<0.000001&&destination.id!=sourceTrack.id&&sourceTrack.kind=="video";if(!verticalOnly||link==null||lockedMembers.Any(c=>!link.clip_ids.Contains(c.id)||Project.Active.tracks.Find(t=>t.id==c.track_id).kind!="audio"))throw new Exception("El conjunto incluye una pista bloqueada");foreach(var c in lockedMembers)ids.Remove(c.id);}
   int orderShift=destination.order-sourceTrack.order;
   var selected=Project.Active.clips.Where(c=>ids.Contains(c.id)).ToList();
   var map=new Dictionary<string,string>();
   foreach(string sourceId in selected.Select(c=>c.track_id).Distinct()){
    var source=Project.Active.tracks.Find(t=>t.id==sourceId);
    var target=Project.Active.tracks.Find(t=>t.kind==source.kind&&t.order==source.order+orderShift);
    if(target==null&&source.kind!=sourceTrack.kind)target=source; if(target==null||target.locked)throw new Exception("Falta una pista de destino compatible o está bloqueada");
    map[sourceId]=target.id;
   }
   double earliest=selected.Min(c=>c.timeline_start.Seconds);
   if(delta< -earliest)delta=Math.Ceiling(-earliest*60)/60;
   if(Math.Abs(delta)<0.000001&&map.All(pair=>pair.Key==pair.Value))return;
   Commit(p=>{foreach(var c in p.Active.clips.Where(c=>ids.Contains(c.id))){
    var track=p.Active.tracks.Find(t=>t.id==c.track_id);if(track==null||track.locked)throw new Exception("Una pista de origen está bloqueada");
    c.timeline_start=Rational.Time(c.timeline_start.Seconds+delta);c.track_id=map[c.track_id];
   }Overwrite(p,ids);});
  }
  public double SlipSource(string cid,double proposed){var original=Project.Active.clips.Find(c=>c.id==cid);if(original==null)throw new Exception("Selecciona un clip");var related=Related(cid);double low=Double.NegativeInfinity,high=Double.PositiveInfinity;foreach(var c in Project.Active.clips.Where(x=>related.Contains(x.id))){var track=Project.Active.tracks.Find(t=>t.id==c.track_id);if(track==null||track.locked)throw new Exception("Pista bloqueada");var asset=Project.assets.Find(a=>a.id==c.asset_id);var st=asset.streams.Find(x=>x.id==c.stream_id);if(st.kind=="image")return original.source_in.Seconds;low=Math.Max(low,-c.source_in.Seconds/c.speed.Seconds);if(st.duration!=null)high=Math.Min(high,(st.duration.Seconds-c.duration.Seconds*c.speed.Seconds-c.source_in.Seconds)/c.speed.Seconds);}double delta=Rational.Frame((proposed-original.source_in.Seconds)/original.speed.Seconds).Seconds;if(high<low-0.000001)throw new Exception("El medio no tiene margen para mover su contenido");delta=Math.Max(low,Math.Min(high,delta));return Rational.Time(original.source_in.Seconds+delta*original.speed.Seconds).Seconds;}
  public void Slip(string cid,double sourceSeconds){var original=Project.Active.clips.Find(c=>c.id==cid);if(original==null)throw new Exception("Selecciona un clip");double effective=SlipSource(cid,sourceSeconds),delta=(effective-original.source_in.Seconds)/original.speed.Seconds;if(Math.Abs(delta)<0.000001)return;var related=Related(cid);Commit(p=>{foreach(var c in p.Active.clips.Where(x=>related.Contains(x.id)))c.source_in=Rational.Time(c.source_in.Seconds+delta*c.speed.Seconds);});}
  public void InsertSelection(List<string> selection,string anchorId,double start,string targetTrack){var ids=selection.SelectMany(Related).Distinct().ToList();if(!ids.Contains(anchorId))ids.Add(anchorId);var selected=Project.Active.clips.Where(c=>ids.Contains(c.id)).ToList();if(selected.Count==0)throw new Exception("Selecciona un clip");double destination=Rational.Frame(start).Seconds,origin=selected.Min(c=>c.timeline_start.Seconds),end=selected.Max(c=>c.End),span=end-origin;var anchor=selected.FirstOrDefault(c=>c.id==anchorId)??selected[0];double requestedGroupStart=destination-(anchor.timeline_start.Seconds-origin);var destinationTrack=targetTrack??anchor.track_id;if(Math.Abs(requestedGroupStart-origin)<1.0/60&&destinationTrack==anchor.track_id)return;var affected=new List<string>();Commit(p=>{foreach(var c in p.Active.clips.Where(c=>ids.Contains(c.id)))if(p.Active.tracks.Find(t=>t.id==c.track_id).locked)throw new Exception("El conjunto incluye una pista bloqueada");var dt=p.Active.tracks.Find(t=>t.id==destinationTrack);var anchorTrack=p.Active.tracks.Find(t=>t.id==anchor.track_id);if(dt==null||dt.locked)throw new Exception("Pista de destino ausente o bloqueada");if(anchorTrack==null||anchorTrack.kind!=dt.kind)throw new Exception("El tipo de pista no es compatible");var trackMap=new Dictionary<string,string>();foreach(string sourceId in selected.Select(c=>c.track_id).Distinct()){var sourceTrack=p.Active.tracks.Find(t=>t.id==sourceId);int targetOrder=sourceTrack.order+(dt.order-anchorTrack.order);var mapped=selected.Count==1?dt:p.Active.tracks.FirstOrDefault(t=>t.kind==sourceTrack.kind&&t.order==targetOrder);if(mapped==null&&sourceTrack.kind!=anchorTrack.kind)mapped=p.Active.tracks.FirstOrDefault(t=>t.id==sourceTrack.id);if(mapped==null||mapped.locked)throw new Exception("Falta una pista de destino compatible o está bloqueada");trackMap[sourceId]=mapped.id;}affected=trackMap.Values.Distinct().ToList();var sourceIntervals=new Dictionary<string,List<double[]>>();foreach(string trackId in selected.Select(c=>c.track_id).Distinct()){var ranges=selected.Where(c=>c.track_id==trackId).OrderBy(c=>c.timeline_start.Seconds).ToList();var merged=new List<double[]>();foreach(var c in ranges){if(merged.Count==0||c.timeline_start.Seconds>merged[merged.Count-1][1]+0.000001)merged.Add(new[]{c.timeline_start.Seconds,c.End});else merged[merged.Count-1][1]=Math.Max(merged[merged.Count-1][1],c.End);}sourceIntervals[trackId]=merged;}foreach(var pair in sourceIntervals){foreach(var c in p.Active.clips.Where(c=>!ids.Contains(c.id)&&c.track_id==pair.Key)){if(pair.Value.Any(r=>c.timeline_start.Seconds<r[1]-0.000001&&c.End>r[0]+0.000001))throw new Exception("No se puede reordenar: otro clip se superpone al origen");double removed=pair.Value.Where(r=>r[1]<=c.timeline_start.Seconds+0.000001).Sum(r=>r[1]-r[0]);if(removed>0)c.timeline_start=Rational.Time(c.timeline_start.Seconds-removed);}}double removedBefore=destinationTrack==anchor.track_id&&sourceIntervals.ContainsKey(anchor.track_id)?sourceIntervals[anchor.track_id].Where(r=>r[1]<=requestedGroupStart+0.000001).Sum(r=>r[1]-r[0]):0;double insertAt=Math.Max(0,requestedGroupStart-removedBefore);foreach(string trackId in affected){var track=p.Active.tracks.Find(t=>t.id==trackId);if(track==null||track.locked)throw new Exception("Pista de destino ausente o bloqueada");foreach(var c in p.Active.clips.Where(c=>!ids.Contains(c.id)&&c.track_id==trackId&&c.timeline_start.Seconds<insertAt-0.000001&&c.End>insertAt+0.000001).ToList()){var right=Store.Json().Deserialize<Clip>(Store.Json().Serialize(Store.Fields(c)));double offset=insertAt-c.timeline_start.Seconds;right.id=Store.Id();right.timeline_start=Rational.Time(insertAt);right.duration=Rational.Time(c.End-insertAt);right.source_in=Rational.Time(c.source_in.Seconds+offset*c.speed.Seconds);c.duration=Rational.Time(offset);p.Active.clips.Add(right);}foreach(var c in p.Active.clips.Where(c=>!ids.Contains(c.id)&&c.track_id==trackId&&c.timeline_start.Seconds>=insertAt-0.000001).ToList())c.timeline_start=Rational.Time(c.timeline_start.Seconds+span);}double delta=insertAt-origin;foreach(var c in p.Active.clips.Where(c=>ids.Contains(c.id))){c.timeline_start=Rational.Time(c.timeline_start.Seconds+delta);c.track_id=trackMap[c.track_id];}});}
  public bool RangeTrim(string cid,double cut,double finish,bool left){var clip=Project.Active.clips.Find(c=>c.id==cid);if(clip==null)return false;double a=Math.Min(cut,finish),b=Math.Max(cut,finish);if(Math.Abs(b-a)<1.0/60000)return false;var related=Related(cid);Commit(p=>{var rightMap=new Dictionary<string,string>();foreach(var c in p.Active.clips.Where(x=>related.Contains(x.id)).ToList()){if(p.Active.tracks.Find(t=>t.id==c.track_id).locked)throw new Exception("Pista bloqueada");double start=c.timeline_start.Seconds,end=c.End,ra=Math.Max(start,a),rb=Math.Min(end,b);if(rb<=ra+0.000001)continue;bool keepLeft=ra>start+0.000001,keepRight=rb<end-0.000001;if(!keepLeft&&!keepRight){p.Active.clips.Remove(c);continue;}if(!keepLeft){double offset=rb-start;c.timeline_start=Rational.Time(rb);c.source_in=Rational.Time(c.source_in.Seconds+offset*c.speed.Seconds);c.duration=Rational.Time(end-rb);continue;}if(!keepRight){c.duration=Rational.Time(ra-start);continue;}var right=Store.Json().Deserialize<Clip>(Store.Json().Serialize(Store.Fields(c)));double rightOffset=rb-start;right.id=Store.Id();right.timeline_start=Rational.Time(rb);right.source_in=Rational.Time(c.source_in.Seconds+rightOffset*c.speed.Seconds);right.duration=Rational.Time(end-rb);c.duration=Rational.Time(ra-start);p.Active.clips.Add(right);rightMap[c.id]=right.id;}foreach(var group in p.Active.link_groups.ToList()){group.clip_ids.RemoveAll(id=>!p.Active.clips.Any(c=>c.id==id));var suffix=group.clip_ids.Where(id=>rightMap.ContainsKey(id)).Select(id=>rightMap[id]).ToList();if(group.clip_ids.Count<2)p.Active.link_groups.Remove(group);if(suffix.Count>1)p.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=suffix});}});return true;}
  public bool RangeTrimSelection(List<string> targets,double cut,double finish){double a=Rational.Frame(Math.Min(cut,finish)).Seconds,b=Rational.Frame(Math.Max(cut,finish)).Seconds;if(b-a<1.0/60)return false;var requested=new HashSet<string>(targets??new List<string>());var ids=Project.Active.clips.Where(c=>(requested.Count==0||requested.Contains(c.id))&&c.timeline_start.Seconds<b-0.000001&&c.End>a+0.000001).Select(c=>c.id).ToList();if(ids.Count==0)return false;Commit(p=>{var rightMap=new Dictionary<string,string>();foreach(var c in p.Active.clips.Where(x=>ids.Contains(x.id)).ToList()){var track=p.Active.tracks.Find(t=>t.id==c.track_id);if(track==null||track.locked)throw new Exception("Una pista afectada está bloqueada");double start=c.timeline_start.Seconds,end=c.End,ra=Math.Max(start,a),rb=Math.Min(end,b);if(rb<=ra+0.000001)continue;bool keepLeft=ra>start+0.000001,keepRight=rb<end-0.000001;if(!keepLeft&&!keepRight){p.Active.clips.Remove(c);continue;}if(!keepLeft){double offset=rb-start;c.timeline_start=Rational.Time(rb);c.source_in=Rational.Time(c.source_in.Seconds+offset*c.speed.Seconds);c.duration=Rational.Time(end-rb);continue;}if(!keepRight){c.duration=Rational.Time(ra-start);continue;}var right=Store.Json().Deserialize<Clip>(Store.Json().Serialize(Store.Fields(c)));double rightOffset=rb-start;right.id=Store.Id();right.timeline_start=Rational.Time(rb);right.source_in=Rational.Time(c.source_in.Seconds+rightOffset*c.speed.Seconds);right.duration=Rational.Time(end-rb);c.duration=Rational.Time(ra-start);p.Active.clips.Add(right);rightMap[c.id]=right.id;}foreach(var group in p.Active.link_groups.ToList()){if(!group.clip_ids.Any(id=>ids.Contains(id)))continue;var originalIds=group.clip_ids.ToList();var rightIds=new List<string>();foreach(string id in originalIds){string right;if(rightMap.TryGetValue(id,out right))rightIds.Add(right);else if(p.Active.clips.Any(c=>c.id==id&&c.timeline_start.Seconds>=b-0.000001))rightIds.Add(id);}group.clip_ids.RemoveAll(id=>!p.Active.clips.Any(c=>c.id==id)||p.Active.clips.Find(c=>c.id==id).timeline_start.Seconds>=b-0.000001);if(group.clip_ids.Count<2)p.Active.link_groups.Remove(group);if(rightIds.Count>1)p.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=rightIds});}});return true;}
  public void RippleDelete(string cid){var related=Related(cid);var target=Project.Active.clips.Find(c=>c.id==cid);if(target==null)throw new Exception("Selecciona un clip");double start=target.timeline_start.Seconds,end=target.End;Commit(p=>{foreach(var c in p.Active.clips.Where(x=>!related.Contains(x.id)&&x.timeline_start.Seconds>=end-0.000001).ToList()){if(p.Active.tracks.Find(t=>t.id==c.track_id).locked)throw new Exception("El contenido posterior está bloqueado");c.timeline_start=Rational.Time(c.timeline_start.Seconds-(end-start));}foreach(var c in p.Active.clips.Where(x=>related.Contains(x.id)))if(p.Active.tracks.Find(t=>t.id==c.track_id).locked)throw new Exception("Pista bloqueada");p.Active.clips.RemoveAll(x=>related.Contains(x.id));p.Active.link_groups.RemoveAll(g=>{g.clip_ids.RemoveAll(related.Contains);return g.clip_ids.Count<2;});});}
  public void RippleDeleteGap(string trackId,double at){var track=Project.Active.tracks.Find(t=>t.id==trackId);if(track==null||track.locked)throw new Exception("Pista ausente o bloqueada");var clips=Project.Active.clips.Where(c=>c.track_id==trackId).OrderBy(c=>c.timeline_start.Seconds).ToList();var left=clips.LastOrDefault(c=>c.End<=at+0.000001);var right=clips.FirstOrDefault(c=>c.timeline_start.Seconds>=at-0.000001);if(left==null||right==null||right.timeline_start.Seconds<=left.End+0.000001)throw new Exception("No hay un espacio vacío en este punto");double gap=right.timeline_start.Seconds-left.End;Commit(p=>{foreach(var c in p.Active.clips.Where(c=>c.timeline_start.Seconds>=right.timeline_start.Seconds-0.000001).ToList()){if(p.Active.tracks.Find(t=>t.id==c.track_id).locked)throw new Exception("El contenido posterior está bloqueado");c.timeline_start=Rational.Time(c.timeline_start.Seconds-gap);}});}
  public void RollEdit(string leftId,string rightId,double cut){var left=Project.Active.clips.Find(c=>c.id==leftId);var right=Project.Active.clips.Find(c=>c.id==rightId);if(left==null||right==null||left.track_id!=right.track_id||Math.Abs(left.End-right.timeline_start.Seconds)>0.000001)throw new Exception("Selecciona una unión válida");var track=Project.Active.tracks.Find(t=>t.id==left.track_id);if(track==null||track.locked)throw new Exception("Pista ausente o bloqueada");var leftStream=Project.assets.Find(a=>a.id==left.asset_id).streams.Find(s=>s.id==left.stream_id);var rightStream=Project.assets.Find(a=>a.id==right.asset_id).streams.Find(s=>s.id==right.stream_id);double grid=track.kind=="audio"?48000:60,min=1/grid,low=Math.Max(min-left.duration.Seconds,rightStream.kind=="image"?Double.NegativeInfinity:-right.source_in.Seconds/right.speed.Seconds),high=right.duration.Seconds-min;if(leftStream.duration!=null)high=Math.Min(high,(leftStream.duration.Seconds-left.source_in.Seconds-left.duration.Seconds*left.speed.Seconds)/left.speed.Seconds);low=Math.Ceiling(low*grid-0.000001)/grid;high=Math.Floor(high*grid+0.000001)/grid;if(high<low)throw new Exception("No hay fotogramas disponibles para mover este punto de corte");double delta=Math.Round((cut-left.End)*grid)/grid;delta=Math.Max(low,Math.Min(high,delta));Commit(p=>{var l=p.Active.clips.Find(c=>c.id==leftId);var r=p.Active.clips.Find(c=>c.id==rightId);if(p.Active.tracks.Find(t=>t.id==l.track_id).locked)throw new Exception("Pista bloqueada");double next=l.End+delta;l.duration=Rational.Time(l.duration.Seconds+delta);r.timeline_start=Rational.Time(next);if(!Still(p,r))r.source_in=Rational.Time(r.source_in.Seconds+delta*r.speed.Seconds);r.duration=Rational.Time(r.duration.Seconds-delta);});}public void RippleTrim(string cid,bool left,double edge){var original=Project.Active.clips.Find(x=>x.id==cid);if(original==null)throw new Exception("Selecciona un clip");var related=Related(cid);var bounds=related.Select(id=>{var c=Project.Active.clips.Find(x=>x.id==id);return new{track=c.track_id,end=c.End};}).ToList();double effective=TrimEdge(cid,left,edge),anchor=left?original.timeline_start.Seconds:original.End,delta=effective-anchor;if(Math.Abs(delta)<0.000001)return;Commit(p=>{foreach(var c in p.Active.clips.Where(x=>related.Contains(x.id))){if(p.Active.tracks.Find(t=>t.id==c.track_id).locked)throw new Exception("Pista bloqueada");if(left){c.timeline_start=Rational.Time(c.timeline_start.Seconds+delta);if(!Still(p,c))c.source_in=Rational.Time(c.source_in.Seconds+delta*c.speed.Seconds);}c.duration=Rational.Time(c.duration.Seconds+(left?-delta:delta));}if(!left){foreach(var bound in bounds)foreach(var later in p.Active.clips.Where(x=>!related.Contains(x.id)&&x.track_id==bound.track&&x.timeline_start.Seconds>=bound.end-0.000001).ToList()){if(p.Active.tracks.Find(t=>t.id==bound.track).locked)throw new Exception("Contenido posterior bloqueado");later.timeline_start=Rational.Time(later.timeline_start.Seconds+delta);}}});}public bool CutSelection(List<string> selection,double at){return CutSelection(selection,at,true);}public bool CutSelection(List<string> selection,double at,bool includeLinked){at=Rational.Frame(at).Seconds;var ids=new HashSet<string>(selection);if(includeLinked&&ids.Count>0)foreach(var group in Project.Active.link_groups.Where(g=>g.clip_ids.Any(ids.Contains)))foreach(string linkedId in group.clip_ids)ids.Add(linkedId);var lockedLinked=new HashSet<string>(Project.Active.link_groups.Where(g=>g.clip_ids.Any(id=>{var linked=Project.Active.clips.Find(c=>c.id==id);return linked==null||Project.Active.tracks.Find(t=>t.id==linked.track_id).locked;})).SelectMany(g=>g.clip_ids));var candidates=Project.Active.clips.Where(c=>(ids.Count==0||ids.Contains(c.id))&&(!includeLinked||!lockedLinked.Contains(c.id))&&!Project.Active.tracks.Find(t=>t.id==c.track_id).locked&&at>c.timeline_start.Seconds+0.000001&&at<c.End-0.000001).Select(c=>c.id).ToList();if(candidates.Count==0)return false;Commit(p=>{var rightIds=new Dictionary<string,string>();foreach(var c in p.Active.clips.Where(c=>candidates.Contains(c.id)).ToList()){double offset=at-c.timeline_start.Seconds;var right=Store.Json().Deserialize<Clip>(Store.Json().Serialize(Store.Fields(c)));right.id=Store.Id();right.timeline_start=Rational.Time(at);right.duration=Rational.Time(c.duration.Seconds-offset);right.source_in=Rational.Time(c.source_in.Seconds+offset*c.speed.Seconds);c.duration=Rational.Time(offset);p.Active.clips.Add(right);rightIds[c.id]=right.id;}foreach(var group in p.Active.link_groups.ToList()){if(!group.clip_ids.Any(rightIds.ContainsKey))continue;var lefts=new List<string>();var rights=new List<string>();foreach(string id in group.clip_ids){var clip=p.Active.clips.Find(c=>c.id==id);string rightId;if(rightIds.TryGetValue(id,out rightId)){lefts.Add(id);rights.Add(rightId);}else if(clip.timeline_start.Seconds>=at)rights.Add(id);else if(clip.End<=at)lefts.Add(id);}p.Active.link_groups.Remove(group);if(lefts.Count>1)p.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=lefts});if(rights.Count>1)p.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=rights});}foreach(var group in p.Active.edit_groups)foreach(var pair in rightIds)if(group.clip_ids.Contains(pair.Key)&&!group.clip_ids.Contains(pair.Value))group.clip_ids.Add(pair.Value);});return true;}
  public void Unlink(string cid){UnlinkMany(new[]{cid});}
  public void UnlinkMany(IEnumerable<string> clipIds){var ids=new HashSet<string>(clipIds??Enumerable.Empty<string>());if(ids.Count==0)return;Commit(p=>p.Active.link_groups.RemoveAll(g=>g.clip_ids.Any(ids.Contains)));}
  public void Link(List<string> clips){Commit(p=>{if(clips.Count<2)throw new Exception("Selecciona dos o más clips con Ctrl+clic");p.Active.link_groups.RemoveAll(g=>g.clip_ids.Any(clips.Contains));p.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=clips.ToList()});});}
  public void Group(IEnumerable<string> clipIds){var requested=new HashSet<string>(clipIds??Enumerable.Empty<string>());var ids=new HashSet<string>(requested.SelectMany(Related));foreach(var group in Project.Active.edit_groups.Where(g=>g.clip_ids.Any(ids.Contains)))foreach(string id in group.clip_ids)ids.Add(id);if(ids.Count<2)throw new Exception("Selecciona dos o más medios para agrupar");Commit(p=>{foreach(string id in ids){var clip=p.Active.clips.Find(c=>c.id==id);if(clip==null||p.Active.tracks.Find(t=>t.id==clip.track_id).locked)throw new Exception("El grupo incluye una pista bloqueada");}p.Active.edit_groups.RemoveAll(g=>g.clip_ids.Any(ids.Contains));int next=p.Active.edit_groups.Select(g=>g.number).DefaultIfEmpty(0).Max()+1;p.Active.edit_groups.Add(new EditGroup{id=Store.Id(),number=next,clip_ids=ids.ToList()});});}
  public void UngroupMany(IEnumerable<string> clipIds){var ids=new HashSet<string>(clipIds??Enumerable.Empty<string>());if(ids.Count==0)return;Commit(p=>p.Active.edit_groups.RemoveAll(g=>g.clip_ids.Any(ids.Contains)));}
 }
}




















