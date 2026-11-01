using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace Prometheus {
 public static class MgtMedia {
  public const string Namespace="prometheus.mgt.asset";
  static readonly object RenderGate=new object();
  public static bool IsMgt(Asset asset){return asset!=null&&asset.source!=null&&asset.source.kind=="mgt";}
  public static bool IsJsonFile(string path){return path!=null&&Regex.IsMatch(Path.GetFileName(path),@"\.mgt(?: \([1-9][0-9]*\))?\.json$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);}
  public static bool IsFile(string path){return IsJsonFile(path)||path!=null&&path.EndsWith(".mgtproj",StringComparison.OrdinalIgnoreCase);}
  public static bool LooksLikeDocument(string text){
   if(String.IsNullOrWhiteSpace(text)||text.TrimStart()[0]!='{')return false;
   try{
    var root=Store.Json().DeserializeObject(text) as Dictionary<string,object>;
    return root!=null&&root.ContainsKey("version")&&(root.ContainsKey("render")||root.ContainsKey("html"));
   }catch{return false;}
  }
  public static Extension Metadata(Asset asset){return asset.extensions.FirstOrDefault(x=>x!=null&&x.@namespace==Namespace&&x.version=="1");}
  public static bool IsInstance(Asset asset){var ext=Metadata(asset);object flag;return ext!=null&&ext.payload!=null&&ext.payload.TryGetValue("instance",out flag)&&Convert.ToBoolean(flag);}
  public static void InvalidateSam2(Project project,string assetId){foreach(var clip in project.sequences.SelectMany(sequence=>sequence.clips).Where(c=>c.asset_id==assetId)){var mask=Sam2MaskStore090.Get(clip);if(mask==null)continue;mask.stale=true;mask.enabled=false;Sam2MaskStore090.Put(clip,mask);}}
  public static Dictionary<string,object> Overrides(Asset asset){var ext=Metadata(asset);object raw;if(ext==null||ext.payload==null||!ext.payload.TryGetValue("overrides_json",out raw)||raw==null)return new Dictionary<string,object>();return Store.Json().Deserialize<Dictionary<string,object>>(Convert.ToString(raw));}
  public static object[] Parameters(Asset asset){var ext=Metadata(asset);object raw;if(ext==null||ext.payload==null||!ext.payload.TryGetValue("parameters_json",out raw)||raw==null)return new object[0];var values=Store.Json().DeserializeObject(Convert.ToString(raw)) as System.Collections.IEnumerable;return values==null?new object[0]:values.Cast<object>().ToArray();}
  public static void SetOverride(Asset asset,string key,object value){var ext=Metadata(asset);if(ext==null)throw new Exception("Faltan parámetros MGT.");var values=Overrides(asset);values[key]=value;ext.payload["overrides_json"]=Store.Json().Serialize(values);ext.payload.Remove("preview_fingerprint");ext.payload.Remove("final_fingerprint");asset.representations.RemoveAll(x=>x.role=="preview"||x.role=="final");asset.locator=null;asset.sha256=null;Store.NormalizeAsset(asset);}
  public static void Relink(Asset original,Asset replacement){
   if(!IsMgt(original)||!IsMgt(replacement))throw new Exception("Selecciona un archivo MGT.");
   if(original.streams.Count!=replacement.streams.Count||original.streams.Any(stream=>!replacement.streams.Any(next=>next.kind==stream.kind)))throw new Exception("El nuevo MGT no tiene la misma combinación de vídeo y audio.");
   foreach(var stream in original.streams){var next=replacement.streams.First(s=>s.kind==stream.kind);stream.duration=next.duration;stream.width=next.width;stream.height=next.height;stream.nominal_fps=next.nominal_fps;}
   original.source=replacement.source;original.container_format=replacement.container_format;original.recommended_duration=replacement.recommended_duration;original.representations.Clear();original.locator=null;original.sha256=null;
   original.extensions.RemoveAll(x=>x.@namespace==Namespace);original.extensions.Add(Metadata(replacement));Store.NormalizeAsset(original);
  }
  public static void ReconcileClipDurations(Project project,Asset asset){
   foreach(var clip in project.sequences.SelectMany(sequence=>sequence.clips).Where(c=>c.asset_id==asset.id)){
    var stream=asset.streams.FirstOrDefault(s=>s.id==clip.stream_id);
    if(stream==null||stream.duration==null)continue;
    double end=clip.source_in.Seconds+clip.duration.Seconds*clip.speed.Seconds;
    if(end>stream.duration.Seconds+0.000001){
     if(!ShortTail.Has(clip)||ShortTail.Limit(clip)<end-0.000001)ShortTail.Attach(clip,end);
    }else clip.extensions.RemoveAll(e=>e!=null&&e.@namespace==ShortTail.Namespace);
   }
  }
  public static void CopyDuration(Asset target,Asset rendered){
   if(!IsMgt(target)||!IsMgt(rendered))return;
   target.recommended_duration=rendered.recommended_duration;
   foreach(var stream in target.streams){var next=rendered.streams.FirstOrDefault(s=>s.id==stream.id);if(next!=null)stream.duration=next.duration;}
  }
  public static Asset Inspect(string file,CancellationToken cancel){
   if(!IsFile(file))throw new Exception("Formato MGT no compatible.");
   using(var engine=new MgtEngine(cancel)){
    var imported=engine.Import(file,cancel);string session=MgtEngine.String(imported,"assetId");
    try{
     var contract=engine.Preflight(session,"preview",60,new Dictionary<string,object>(),cancel);
     double duration=MgtEngine.Number(contract,"encodedDuration");if(duration<=0)duration=MgtEngine.Number(contract,"duration");if(duration<=0)throw new Exception("MGT no informó una duración utilizable.");
     var source=new MediaSource{kind="mgt",uri=Path.GetFullPath(file),imported_at=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture),resolver_type="mgt-asset-v2"};
     var asset=new Asset{id=Store.Id(),name=MgtEngine.String(contract,"name"),source=source,container_format=MgtEngine.String(contract,"sourceFormat"),presentation_origin=Rational.Time(0),recommended_duration=Rational.Frame(duration)};
     if(String.IsNullOrWhiteSpace(asset.name))asset.name=Path.GetFileName(file);
     int width=(int)MgtEngine.Number(contract,"width"),height=(int)MgtEngine.Number(contract,"height");
     asset.streams.Add(new StreamInfo{id=Store.Id(),ordinal=0,kind="video",codec="mgt-prores",pts_origin=Rational.Time(0),time_base=new Rational(1,60),duration=Rational.Frame(duration),nominal_fps=new Rational(60,1),width=width,height=height,pixel_format="yuva444p12le",color_space="bt709",color_range="tv"});
     if(MgtEngine.Bool(contract,"hasAudio"))asset.streams.Add(new StreamInfo{id=Store.Id(),ordinal=1,kind="audio",codec="aac",pts_origin=Rational.Time(0),time_base=new Rational(1,48000),duration=Rational.Time(duration),sample_rate=48000,channels=2,channel_layout="stereo"});
     object schema;contract.TryGetValue("parameters",out schema);object overrides;contract.TryGetValue("overrides",out overrides);
     asset.extensions.Add(new Extension{@namespace=Namespace,version="1",payload=new Dictionary<string,object>{{"parameters_json",Store.Json().Serialize(schema??new object[0])},{"overrides_json",Store.Json().Serialize(overrides??new Dictionary<string,object>())},{"engine_version",MgtEngine.String(contract,"engineVersion")},{"source_format",MgtEngine.String(contract,"sourceFormat")},{"source_version",MgtEngine.Number(contract,"sourceVersion")}}});
     return asset;
    }finally{engine.Release(session);}
   }
  }
  public static void Resolve(Asset asset,bool preview,CancellationToken cancel,Action<int> progress){lock(RenderGate)ResolveCore(asset,preview,cancel,progress);}
  static void ResolveCore(Asset asset,bool preview,CancellationToken cancel,Action<int> progress){
   if(!IsMgt(asset))throw new Exception("El medio no es MGT.");
   string profile=preview?"preview":"final",role=preview?"preview":"final";
   using(var engine=new MgtEngine(cancel)){
    var imported=engine.Import(asset.source.uri,cancel);string session=MgtEngine.String(imported,"assetId");
    try{
     var overrides=Overrides(asset);var contract=engine.Preflight(session,profile,60,overrides,cancel);
     double duration=MgtEngine.Number(contract,"encodedDuration");if(duration<=0)duration=MgtEngine.Number(contract,"duration");
     if(duration<=0)throw new Exception("MGT no informó una duración utilizable.");
     asset.recommended_duration=Rational.Frame(duration);
     foreach(var stream in asset.streams)stream.duration=stream.kind=="audio"?Rational.Time(duration):Rational.Frame(duration);
     string fingerprint=MgtEngine.String(contract,"fingerprint"),key=role+"_fingerprint";
     var ext=Metadata(asset);if(ext==null)throw new Exception("Falta el contrato MGT del medio.");
     var existing=asset.representations.FirstOrDefault(r=>r.role==role&&r.locator!=null&&File.Exists(r.locator.path));
     object saved;if(existing!=null&&ext.payload.TryGetValue(key,out saved)&&Convert.ToString(saved)==fingerprint)return;
     string directory=Environment.GetEnvironmentVariable("PROMETHEUS_MGT_CACHE_ROOT");
     if(String.IsNullOrWhiteSpace(directory))directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","MGT-cache");
     Directory.CreateDirectory(directory);
     string file=Path.Combine(directory,asset.id+"-"+role+"-"+fingerprint.Substring(0,16)+".mov");
     if(!File.Exists(file))engine.Render(session,profile,60,overrides,file,cancel,progress);
     var info=new FileInfo(file);if(info.Length==0)throw new Exception("El render MGT quedó vacío.");
     var representation=new MediaRepresentation{role=role,locator=new Locator{path=info.FullName,relative=false},size_bytes=info.Length,modified_utc=info.LastWriteTimeUtc.ToString("o",CultureInfo.InvariantCulture),sha256=fingerprint};
     foreach(var stream in asset.streams)representation.stream_ordinals[stream.id]=stream.ordinal;
     asset.representations.RemoveAll(r=>r.role==role);asset.representations.Add(representation);ext.payload[key]=fingerprint;Store.NormalizeAsset(asset);
    }finally{engine.Release(session);}
   }
  }
 }
}
