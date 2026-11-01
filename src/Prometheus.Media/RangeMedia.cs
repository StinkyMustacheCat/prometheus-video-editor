using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Prometheus {
 public sealed class MediaRange {
  public double start,end;
  public string path,modified_utc;
  public string quality_profile;
  public long size_bytes;
  public Dictionary<string,int> stream_ordinals=new Dictionary<string,int>();
 }
 public static class RangeMedia {
  public const string Namespace="prometheus.web.ranges";
  public const double HandleSeconds=2.0;
  public const double MergeGapSeconds=3.0;
  public static bool CanResolveSelective(Asset asset){return asset!=null&&asset.source!=null&&asset.source.kind=="web"&&!WebMedia.IsAudioOnly(asset)&&!WebMedia.DurationProvisional(asset)&&(asset.source.resolver_type=="youtube/yt-dlp"||asset.source.resolver_type!=null&&asset.source.resolver_type.StartsWith("yt-dlp/",StringComparison.Ordinal));}
  static readonly ConcurrentDictionary<string,SemaphoreSlim> gates=new ConcurrentDictionary<string,SemaphoreSlim>();
  static Extension Find(Asset asset){return asset.extensions.FirstOrDefault(x=>x!=null&&x.@namespace==Namespace&&x.version=="1");}
  public static List<MediaRange> Read(Asset asset){
   var ext=Find(asset);object raw;
   if(ext==null||ext.payload==null||!ext.payload.TryGetValue("segments_json",out raw)||raw==null)return new List<MediaRange>();
   try{return Store.Json().Deserialize<List<MediaRange>>(Convert.ToString(raw)).Where(x=>x!=null&&x.end>x.start&&x.path!=null).OrderBy(x=>x.start).ToList();}
   catch{return new List<MediaRange>();}
  }
  public static void Write(Asset asset,IList<MediaRange> segments){
   var ext=Find(asset);if(ext==null){ext=new Extension{@namespace=Namespace,version="1",payload=new Dictionary<string,object>()};asset.extensions.Add(ext);}
   ext.payload["segments_json"]=Store.Json().Serialize(Store.Fields(segments.ToList()));
  }
  public static void Merge(Asset target,Asset source){
   var have=Read(target);foreach(var segment in Read(source)){have.RemoveAll(x=>Math.Abs(x.start-segment.start)<0.0001&&Math.Abs(x.end-segment.end)<0.0001);have.Add(segment);}
   Write(target,have.OrderBy(x=>x.start).ToList());
  }
  public static List<Clip> UsedClips(Project project,Asset asset){return project.Active.clips.Where(c=>c.asset_id==asset.id&&c.enabled&&project.Active.tracks.Any(t=>t.id==c.track_id&&t.enabled)).ToList();}
  static double SourceEnd(Clip clip){return clip.source_in.Seconds+clip.duration.Seconds*clip.speed.Seconds;}
  public static double RequiredPreroll(Project project,Clip clip){var transition=project.Active.transitions.FirstOrDefault(t=>t.enabled&&t.right_clip_id==clip.id);return transition==null||transition.preview_expanded||transition.transition_id!=TransitionContracts.Dissolve?0:transition.duration_frames/60.0;}
  static double AssetLength(Asset asset){return asset.streams.Where(s=>s.duration!=null).Select(s=>s.duration.Seconds).DefaultIfEmpty(0).Max();}
  public static List<MediaRange> Plan(Project project,Asset asset){
   double limit=AssetLength(asset);if(limit<=0)throw new Exception("Duración web no disponible");
   var proposed=UsedClips(project,asset).Select(c=>new MediaRange{start=Math.Max(0,Math.Floor((c.source_in.Seconds-RequiredPreroll(project,c)*c.speed.Seconds-HandleSeconds)*60)/60),end=Math.Min(limit,Math.Ceiling((SourceEnd(c)+HandleSeconds)*60)/60)}).Where(x=>x.end>x.start).OrderBy(x=>x.start).ToList();
   var merged=new List<MediaRange>();foreach(var range in proposed){if(merged.Count>0&&range.start<=merged.Last().end+MergeGapSeconds)merged.Last().end=Math.Max(merged.Last().end,range.end);else merged.Add(range);}return merged;
  }
  public static MediaRange FindCovering(Asset asset,Clip clip){
   return FindCovering(asset,clip,0);
  }
  public static MediaRange FindCovering(Project project,Asset asset,Clip clip){return FindCovering(asset,clip,RequiredPreroll(project,clip));}
  static MediaRange FindCovering(Asset asset,Clip clip,double preroll){
   double start=clip.source_in.Seconds-preroll*clip.speed.Seconds,end=SourceEnd(clip);
   return Read(asset).FirstOrDefault(x=>x.start<=start+0.0001&&x.end>=end-0.0001&&Available(x));
  }
  static bool Available(MediaRange range){try{if(range.quality_profile!="source-v2")return false;var file=new FileInfo(range.path);DateTime modified;if(!file.Exists||file.Length!=range.size_bytes||!DateTime.TryParse(range.modified_utc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out modified))return false;return Math.Abs((file.LastWriteTimeUtc-modified.ToUniversalTime()).TotalSeconds)<=2;}catch{return false;}}
  public static bool CoversUsed(Project project,Asset asset){var clips=UsedClips(project,asset);return clips.Count>0&&clips.All(c=>FindCovering(project,asset,c)!=null);}
  public static void VerifyUsed(Project project,Asset asset,CancellationToken token){foreach(var range in UsedClips(project,asset).Select(c=>FindCovering(project,asset,c)).Distinct()){token.ThrowIfCancellationRequested();if(range==null)throw new Exception("Falta un tramo descargado de "+asset.name);var file=new FileInfo(range.path);if(file.Length!=range.size_bytes||Math.Abs((file.LastWriteTimeUtc-DateTime.Parse(range.modified_utc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind).ToUniversalTime()).TotalSeconds)>2)throw new Exception("Un tramo en caché cambió: "+asset.name);}}
  static string Hash(string value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant().Substring(0,24);}
  static string Section(double start,double end){return "*"+start.ToString("0.000",CultureInfo.InvariantCulture)+"-"+end.ToString("0.000",CultureInfo.InvariantCulture);}
  static MediaRange Describe(Asset asset,string file,double start,double end,CancellationToken token){
   var inspected=Media.Inspect(file,token);var map=new Dictionary<string,int>();
   foreach(var stream in asset.streams){var found=inspected.streams.FirstOrDefault(x=>x.kind==stream.kind);if(found==null)throw new Exception("El tramo descargado no conserva "+stream.kind);map[stream.id]=found.ordinal;}
   double actual=inspected.streams.Where(x=>x.duration!=null).Select(x=>x.duration.Seconds).DefaultIfEmpty(0).Max();
   if(actual<end-start-0.25)throw new Exception("El tramo descargado terminó antes de lo esperado");
   var info=new FileInfo(file);return new MediaRange{start=start,end=end,path=info.FullName,size_bytes=info.Length,modified_utc=info.LastWriteTimeUtc.ToString("o",CultureInfo.InvariantCulture),quality_profile="source-v2",stream_ordinals=map};
  }
  public static MediaRange Download(Asset asset,double start,double end,string cacheDir,CancellationToken token,Action<string> status){
   string key=Hash(asset.source.uri+"|range-v2|"+Section(start,end));var gate=gates.GetOrAdd(key,_=>new SemaphoreSlim(1,1));gate.Wait(token);
   try{
    Directory.CreateDirectory(cacheDir);string final=Path.Combine(cacheDir,key+"-range.mp4");
    if(File.Exists(final))try{return Describe(asset,final,start,end,token);}catch{File.Delete(final);}
    string staging=Path.Combine(cacheDir,key+"-work-"+Store.Id());Directory.CreateDirectory(staging);
    try{
     if(status!=null)status("Descargando tramo "+Section(start,end)+" de "+asset.name+"…");
     string template=Path.Combine(staging,"source.%(ext)s");string ytdlp=Path.Combine(Tools.Root,"runtime","yt-dlp.exe");
     Tools.Run(ytdlp,"--ignore-config "+WebMedia.CookieArguments()+WebMedia.JsRuntimeArguments()+" --no-playlist --no-warnings --no-progress --socket-timeout 12 --retries 1 --extractor-retries 1 --ffmpeg-location "+Tools.Q(Path.GetDirectoryName(Tools.FFmpeg))+" --download-sections "+Tools.Q(Section(start,end))+" --force-keyframes-at-cuts -f "+Tools.Q("bv*+ba/b")+" --merge-output-format mkv -o "+Tools.Q(template)+" "+Tools.Q(asset.source.uri),token);
     string source=Directory.GetFiles(staging,"source.*").Where(x=>!x.EndsWith(".part",StringComparison.OrdinalIgnoreCase)&&!x.EndsWith(".ytdl",StringComparison.OrdinalIgnoreCase)).OrderByDescending(x=>new FileInfo(x).Length).FirstOrDefault();
     if(source==null)throw new Exception("El enlace no entregó el tramo pedido");
     if(status!=null)status("Preparando tramo para edición…");
     string normalized=Path.Combine(staging,"normalized.mp4");
     Tools.RunProgress(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -y -progress pipe:1 -nostats -i "+Tools.Q(source)+" -t "+Tools.N(end-start)+" -map 0:v:0 -map 0:a:0? -c:v libx264 -preset medium -crf 17 -pix_fmt yuv420p -c:a aac -profile:a aac_low -b:a 256k -ar 48000 -ac 2 -movflags +faststart "+Tools.Q(normalized),end-start,token,percent=>{if(status!=null)status("Preparando tramo "+Section(start,end)+" · "+percent+" %");});
     var result=Describe(asset,normalized,start,end,token);
     if(File.Exists(final))File.Replace(normalized,final,null);else File.Move(normalized,final);
     result.path=final;result.size_bytes=new FileInfo(final).Length;result.modified_utc=new FileInfo(final).LastWriteTimeUtc.ToString("o",CultureInfo.InvariantCulture);
     return result;
    }finally{try{string parent=Path.GetFullPath(cacheDir).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;string child=Path.GetFullPath(staging);if(child.StartsWith(parent,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(child))Directory.Delete(child,true);}catch{}}
   }finally{gate.Release();}
  }
  public static int ResolveUsed(Project project,Asset asset,string cacheDir,CancellationToken token,Action<string> status){
   var segments=Read(asset);int downloaded=0;
   foreach(var planned in Plan(project,asset)){
    token.ThrowIfCancellationRequested();if(segments.Any(x=>x.start<=planned.start+0.0001&&x.end>=planned.end-0.0001&&Available(x)))continue;
    var segment=Download(asset,planned.start,planned.end,cacheDir,token,status);
    segments.Add(segment);Write(asset,segments);downloaded++;
   }
   VerifyUsed(project,asset,token);return downloaded;
  }
 }
}
