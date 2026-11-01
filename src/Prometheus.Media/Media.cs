using System;using System.IO;using System.Diagnostics;using System.Collections.Generic;using System.Globalization;using System.Linq;using System.Threading;using System.Text;using System.Security.Cryptography;
namespace Prometheus {
 public static class Tools {
  public static string Root=AppDomain.CurrentDomain.BaseDirectory;
  public static string FFmpeg{get{return Path.Combine(Root,"runtime","ffmpeg.exe");}}
  public static string FFprobe{get{return Path.Combine(Root,"runtime","ffprobe.exe");}}
  public static string Q(string s){return "\""+s.Replace("\"","\\\"")+"\"";}
  public static string N(double n){return n.ToString("0.#########",CultureInfo.InvariantCulture);}
   private static bool? _nvencAvailable;
   public static bool CanUseNvenc(){
    if(!_nvencAvailable.HasValue){
     try{
      var psi=new ProcessStartInfo(FFmpeg,"-f lavfi -i color=black:s=256x256:d=0.1 -c:v h264_nvenc -f null -"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
      using(var proc=Process.Start(psi)){proc.WaitForExit();_nvencAvailable=(proc.ExitCode==0);}
     }catch{_nvencAvailable=false;}
    }
    return _nvencAvailable.Value;
   }
  public static Process Start(string exe,string args,bool background=false){
   string filterScript=null;
   if(Path.GetFileName(exe).Equals("ffmpeg.exe",StringComparison.OrdinalIgnoreCase)&&args.Length>24000){
    const string option="-filter_complex ";int marker=args.IndexOf(option,StringComparison.Ordinal);
    if(marker>=0){
     int opening=marker+option.Length;
     if(opening<args.Length&&args[opening]=='\"'){
      int closing=opening+1;
      while(closing<args.Length){
       if(args[closing]=='\"'){
        int escapes=0;for(int j=closing-1;j>opening&&args[j]=='\\';j--)escapes++;
        if(escapes%2==0)break;
       }
       closing++;
      }
      if(closing<args.Length){
       string graph=args.Substring(opening+1,closing-opening-1).Replace("\\\"","\"");
       filterScript=Path.Combine(Path.GetTempPath(),"prometheus-filter-"+Guid.NewGuid().ToString("N")+".txt");
       File.WriteAllText(filterScript,graph,new UTF8Encoding(false));
       args=args.Substring(0,marker)+"-/filter_complex "+Q(filterScript)+args.Substring(closing+1);
      }
     }
    }
   }
   var p=new Process();p.StartInfo=new ProcessStartInfo(exe,args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
   if(filterScript!=null){p.EnableRaisingEvents=true;p.Exited+=(s,e)=>{try{File.Delete(filterScript);}catch{}};}
   try{p.Start();if(background)try{p.PriorityClass=ProcessPriorityClass.BelowNormal;}catch{}return p;}catch{if(filterScript!=null)try{File.Delete(filterScript);}catch{}p.Dispose();throw;}
  }
  public static string Run(string exe,string args,CancellationToken token,bool background=false){using(var p=Start(exe,args,background)){string err="";p.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)err+=e.Data+"\n";};p.BeginErrorReadLine();using(token.Register(()=>{try{p.Kill();}catch{}})){string data=p.StandardOutput.ReadToEnd();p.WaitForExit();token.ThrowIfCancellationRequested();if(p.ExitCode!=0)throw new Exception(err.Length>1800?err.Substring(err.Length-1800):err);return data;}}}
  public static void RunProgress(string exe,string args,double duration,CancellationToken token,Action<int> progress,bool background=false){using(var p=Start(exe,args,background)){string err="";p.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)err+=e.Data+"\n";};p.BeginErrorReadLine();using(token.Register(()=>{try{p.Kill();}catch{}})){string line;int last=-1;while((line=p.StandardOutput.ReadLine())!=null){token.ThrowIfCancellationRequested();if(line.StartsWith("out_time_us=")&&duration>0){long micros;if(long.TryParse(line.Substring(12),out micros)){int percent=Math.Min(99,Math.Max(0,(int)(micros/1000000.0/duration*100)));if(percent>last){last=percent;if(progress!=null)progress(percent);}}}}p.WaitForExit();token.ThrowIfCancellationRequested();if(p.ExitCode!=0)throw new Exception(err.Length>1800?err.Substring(err.Length-1800):err);}}}
 }
 public class AudioImportBatch {public List<Asset> assets=new List<Asset>();public List<string> errors=new List<string>();}
 public class RelinkPlan {public Dictionary<string,Asset> matches=new Dictionary<string,Asset>();public List<string> missing=new List<string>();public List<string> ambiguous=new List<string>();public int scanned_files;}
 public static partial class Media {
  public const string ProxyPreferenceNamespace="prometheus.ui.proxy-preference";
  public static bool PrefersProxy(Asset asset){var ext=asset.extensions.FirstOrDefault(x=>x!=null&&x.@namespace==ProxyPreferenceNamespace&&x.version=="1");object value;return ext==null||ext.payload==null||!ext.payload.TryGetValue("enabled",out value)||Convert.ToBoolean(value,CultureInfo.InvariantCulture);}
  public static void SetProxyPreference(Asset asset,bool enabled){asset.extensions.RemoveAll(x=>x!=null&&x.@namespace==ProxyPreferenceNamespace);asset.extensions.Add(new Extension{@namespace=ProxyPreferenceNamespace,version="1",payload=new Dictionary<string,object>{{"enabled",enabled}}});}
  public static AudioImportBatch ReadTimelineBatch(string[] files,string kind,CancellationToken token){var batch=new AudioImportBatch();foreach(string file in files){token.ThrowIfCancellationRequested();try{if(Directory.Exists(file))throw new Exception("Arrastra archivos, no carpetas");var asset=Inspect(file,token);bool audio=asset.streams.All(st=>st.kind=="audio");if((kind=="audio"&&!audio)||(kind=="video"&&audio))throw new Exception(kind=="audio"?"Suelta videos e imágenes sobre una pista de video":"Suelta el audio sobre una pista de audio");batch.assets.Add(asset);}catch(OperationCanceledException){throw;}catch(Exception ex){batch.errors.Add(Path.GetFileName(file)+": "+ex.Message);}}return batch;}
  public static void InsertTimelineBatch(Project p,List<Asset> assets,string trackId,double at){var track=p.Active.tracks.Find(t=>t.id==trackId);if(track==null||track.locked)throw new Exception("Pista ausente o bloqueada");if(track.kind=="audio"){InsertAudioBatch(p,assets,trackId,at);return;}double cursor=Rational.Frame(at).Seconds;foreach(var asset in assets){if(!asset.streams.Any(st=>st.kind!="audio"))throw new Exception("Medio incompatible con la pista de video");p.assets.Add(asset);Insert(p,asset.id,trackId,cursor);cursor=Math.Ceiling((p.Active.clips.Where(c=>c.asset_id==asset.id).Max(c=>c.End)-0.000001)*60)/60;}}

  public static AudioImportBatch ReadAudioBatch(string[] files,CancellationToken token){var batch=new AudioImportBatch();foreach(string file in files){token.ThrowIfCancellationRequested();try{var asset=Inspect(file,token);if(asset.streams.Any(s=>s.kind!="audio")||asset.streams.Count!=1)throw new Exception("Arrastra un archivo de audio a esta pista; los videos se importan desde Medios.");batch.assets.Add(asset);}catch(OperationCanceledException){throw;}catch(Exception ex){batch.errors.Add(Path.GetFileName(file)+": "+ex.Message);}}return batch;}
  public static void InsertAudioBatch(Project p,List<Asset> assets,string trackId,double at){var target=p.Active.tracks.Find(t=>t.id==trackId);if(target==null||target.kind!="audio")throw new Exception("Selecciona una pista de audio");if(target.locked)throw new Exception("Pista bloqueada");var insertedIds=new List<string>();var begin=Rational.Time(Math.Max(0,at));foreach(var asset in assets){if(asset.streams.Count!=1||asset.streams[0].kind!="audio")throw new Exception("Medio incompatible con una pista de audio");var stream=asset.streams[0];var duration=new Rational((long)Math.Floor(stream.duration.Seconds*48000+0.000001),48000);if(duration.n<=0)throw new Exception("Audio demasiado corto");p.assets.Add(asset);p.Active.clips.Add(new Clip{id=Store.Id(),name=asset.name,asset_id=asset.id,stream_id=stream.id,track_id=target.id,timeline_start=begin,source_in=new Rational(0,1),duration=duration});insertedIds.Add(p.Active.clips.Last().id);begin=Rational.Time(begin.Seconds+duration.Seconds);}Editor.Overwrite(p,insertedIds);}
  public static MediaRepresentation SelectRepresentation(Asset asset,bool preview){
   Store.NormalizeAsset(asset);
   string[] roles=preview?(MgtMedia.IsMgt(asset)||PrefersProxy(asset)?new[]{"preview","proxy","final","original"}:new[]{"original","final","preview","proxy"}):new[]{"final","original"};
   foreach(string role in roles){var rep=asset.representations.Find(r=>r.role==role&&r.locator!=null&&File.Exists(r.locator.path));if(rep!=null)return rep;}
   return null;
  }
  public static string RequirePath(Asset asset,bool preview){
   var rep=SelectRepresentation(asset,preview);
   if(rep==null)throw new Exception((preview?"Vista previa":"Exportación")+": medio sin representación local disponible: "+asset.name);
   return rep.locator.path;
  }
  public static void Verify(Asset asset,CancellationToken token,bool preview){
   token.ThrowIfCancellationRequested();var rep=SelectRepresentation(asset,preview);
   if(rep==null)throw new Exception("Medio sin archivo disponible: "+asset.name);
   var file=new FileInfo(rep.locator.path);
   if(rep.size_bytes.HasValue&&rep.size_bytes.Value!=file.Length)throw new Exception("El medio cambió desde la importación: "+asset.name);
   if(!String.IsNullOrEmpty(rep.modified_utc)){
    DateTime recorded;if(!DateTime.TryParse(rep.modified_utc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out recorded)||Math.Abs((file.LastWriteTimeUtc-recorded.ToUniversalTime()).TotalSeconds)>2)
     throw new Exception("El medio cambió desde la importación: "+asset.name);
   }
  }
  public static void Verify(Asset asset,CancellationToken token){Verify(asset,token,false);}
  public static void VerifySources(Project p,CancellationToken token,bool preview){foreach(var a in p.assets.Where(x=>p.Active.clips.Any(c=>c.asset_id==x.id&&c.enabled&&p.Active.tracks.Find(t=>t.id==c.track_id).enabled))){if(a.source!=null&&a.source.kind=="generated")continue;if(a.source!=null&&a.source.kind=="web"&&SelectRepresentation(a,preview)==null){if(RangeMedia.CoversUsed(p,a)){RangeMedia.VerifyUsed(p,a,token);continue;}if(preview)continue;}if(preview&&MgtMedia.IsMgt(a)&&SelectRepresentation(a,true)==null)continue;Verify(a,token,preview);}}
  public static void VerifySources(Project p,CancellationToken token){VerifySources(p,token,false);}
  public static bool CompatibleLocal(Asset asset,Asset candidate){
   Store.NormalizeAsset(asset);Store.NormalizeAsset(candidate);
   if(asset.source.kind!="local"||candidate.source.kind!="local")return false;
   var before=asset.streams.OrderBy(s=>s.ordinal).ToList();var after=candidate.streams.OrderBy(s=>s.ordinal).ToList();
   if(before.Count!=after.Count)return false;
   for(int i=0;i<before.Count;i++){
    var a=before[i];var b=after[i];double tolerance=a.kind=="audio"?0.001:1.0/60;
    bool fpsDifferent=a.nominal_fps!=null&&b.nominal_fps!=null&&Math.Abs(a.nominal_fps.Seconds-b.nominal_fps.Seconds)>0.000001;
    bool offsetDifferent=Math.Abs((a.pts_origin.Seconds-asset.presentation_origin.Seconds)-(b.pts_origin.Seconds-candidate.presentation_origin.Seconds))>tolerance;
    if(a.kind!=b.kind||a.codec!=b.codec||a.width!=b.width||a.height!=b.height||a.sample_rate!=b.sample_rate||a.channels!=b.channels||
     fpsDifferent||offsetDifferent||(a.duration==null)!=(b.duration==null)||(a.duration!=null&&Math.Abs(a.duration.Seconds-b.duration.Seconds)>tolerance))return false;
   }
   return true;
  }
  public static void RelinkLocal(Asset asset,Asset candidate){
   if(!CompatibleLocal(asset,candidate))throw new Exception("El archivo no coincide con las características del medio original");
   var before=asset.streams.OrderBy(s=>s.ordinal).ToList();var after=candidate.streams.OrderBy(s=>s.ordinal).ToList();
   var original=candidate.representations.Find(r=>r.role=="original");
   var replacement=new MediaRepresentation{role="original",locator=original.locator,size_bytes=original.size_bytes,modified_utc=original.modified_utc,sha256=original.sha256};
   for(int i=0;i<before.Count;i++)replacement.stream_ordinals[before[i].id]=after[i].ordinal;
   asset.source.uri=candidate.source.uri;asset.source.relative=false;
   asset.representations.Clear();asset.representations.Add(replacement);
   asset.container_format=candidate.container_format;asset.bitrate=candidate.bitrate;
   Store.NormalizeAsset(asset);
  }
  public static RelinkPlan FindRelinks(Project project,string folder,CancellationToken token){
   if(!Directory.Exists(folder))throw new DirectoryNotFoundException("La carpeta para reconectar no existe: "+folder);
   var plan=new RelinkPlan();
   var offline=project.assets.Where(a=>{Store.NormalizeAsset(a);return a.source.kind=="local"&&SelectRepresentation(a,false)==null;}).ToList();
   if(offline.Count==0)return plan;
   var wanted=new HashSet<string>(offline.Select(a=>Path.GetFileName(a.source.uri)),StringComparer.OrdinalIgnoreCase);
   var found=new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);
   var queue=new Queue<string>();queue.Enqueue(Path.GetFullPath(folder));var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   while(queue.Count>0){
    token.ThrowIfCancellationRequested();string directory=queue.Dequeue();if(!visited.Add(directory))continue;
    try{
     foreach(string file in Directory.EnumerateFiles(directory)){token.ThrowIfCancellationRequested();plan.scanned_files++;string name=Path.GetFileName(file);if(!wanted.Contains(name))continue;List<string> paths;if(!found.TryGetValue(name,out paths)){paths=new List<string>();found[name]=paths;}paths.Add(file);}
     foreach(string child in Directory.EnumerateDirectories(directory)){token.ThrowIfCancellationRequested();try{if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0)queue.Enqueue(child);}catch(UnauthorizedAccessException){}catch(IOException){}}
    }catch(UnauthorizedAccessException){}catch(IOException){}
   }
   var inspected=new Dictionary<string,Asset>(StringComparer.OrdinalIgnoreCase);
   foreach(var asset in offline){
    token.ThrowIfCancellationRequested();string name=Path.GetFileName(asset.source.uri);List<string> paths;
    if(!found.TryGetValue(name,out paths)){plan.missing.Add(asset.name);continue;}
    var original=asset.representations.FirstOrDefault(r=>r.role=="original");long? size=original==null?null:original.size_bytes;
    var compatible=new List<Asset>();
    foreach(string path in paths){
     token.ThrowIfCancellationRequested();
     try{
      if(size.HasValue&&new FileInfo(path).Length!=size.Value)continue;
      Asset candidate;if(!inspected.TryGetValue(path,out candidate)){candidate=Inspect(path,token);inspected[path]=candidate;}
      if(CompatibleLocal(asset,candidate))compatible.Add(candidate);
     }catch(OperationCanceledException){throw;}catch(Exception){}
    }
    if(compatible.Count==1)plan.matches[asset.id]=compatible[0];
    else if(compatible.Count>1)plan.ambiguous.Add(asset.name);
    else plan.missing.Add(asset.name);
   }
   return plan;
  }
  static double Num(object o,double fallback){double r;return o!=null&&double.TryParse(Convert.ToString(o),NumberStyles.Float,CultureInfo.InvariantCulture,out r)?r:fallback;}
  static object Get(Dictionary<string,object>d,string k){object v;return d.TryGetValue(k,out v)?v:null;}
  static Rational Rate(object o){string[] p=Convert.ToString(o).Split('/');long n,d;if(p.Length==2&&long.TryParse(p[0],out n)&&long.TryParse(p[1],out d)&&d>0&&n>0)return new Rational(n,d);return null;}
  static Rational ExactTime(object ticks,Rational timebase,double fallback){long value;if(timebase!=null&&ticks!=null&&long.TryParse(Convert.ToString(ticks),out value)){try{return new Rational(checked(value*timebase.n),timebase.d);}catch(OverflowException){}}return Rational.Time(fallback);}
  static int? Rotation(object sideData){var entries=sideData as System.Collections.IEnumerable;if(entries==null)return null;foreach(var entry in entries){var values=entry as Dictionary<string,object>;object raw;if(values!=null&&values.TryGetValue("rotation",out raw)){double degrees=Num(raw,0);if(Math.Abs(degrees-Math.Round(degrees))<0.001)return (int)Math.Round(degrees);}}return null;}
  public static Asset Inspect(string file,CancellationToken token){return Inspect(file,token,false);}
  internal static Asset InspectResolved(string file,CancellationToken token){return Inspect(file,token,true);}
  public static bool IsAudioFileExtension(string file){return new[]{".wav",".mp3",".m4a",".flac",".ogg",".opus",".aac",".aif",".aiff"}.Contains(Path.GetExtension(file).ToLowerInvariant());}
  public static bool IsConvertibleStillExtension(string file){return new[]{".webp",".bmp",".tif",".tiff",".avif",".svg",".gif"}.Contains(Path.GetExtension(file).ToLowerInvariant());}
  public static string ConvertedStillCacheRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","converted-stills");
  public static string PreparedMediaCacheRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","PreparedMedia");
  public static readonly string[] SupportedVideoContainers=new[] {".mp4",".mov",".mkv",".webm",".avi",".m4v",".ts",".mts",".m2ts",".mxf"};
  public static string EnsurePreparedMedia(string file,string profile,CancellationToken token){
   var info=new FileInfo(file);if(!info.Exists)throw new FileNotFoundException("Archivo no disponible",file);
   string signature=info.FullName+"|"+info.Length+"|"+info.LastWriteTimeUtc.Ticks+"|"+profile;
   string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(signature))).Replace("-","").ToLowerInvariant();
   string dir=PreparedMediaCacheRoot;Directory.CreateDirectory(dir);
   string target=Path.Combine(dir,"prep-"+hash+".mp4");if(File.Exists(target))return target;
   string temp=target+".partial-"+Store.Id()+".mp4";
   try{
    var vfilters=new List<string>();
    if(profile.Contains("interlaced"))vfilters.Add("bwdif=mode=send_frame:parity=auto:deint=all");
    if(profile.Contains("hdr")){vfilters.Add("format=yuv420p10le");vfilters.Add("tonemap=tonemap=hable:desat=0");vfilters.Add("format=yuv420p");}
    else vfilters.Add("format=yuv420p");
    vfilters.Add("fps=60:round=near");
    string vf=String.Join(",",vfilters);
    string enc=Tools.CanUseNvenc()?"-c:v h264_nvenc -preset p5 -cq 18":"-c:v libx264 -preset fast -crf 18";
    string args="-hide_banner -loglevel error -nostdin -y -i "+Tools.Q(file)+" -vf "+Tools.Q(vf)+" "+enc+" -c:a aac -b:a 256k -ar 48000 -ac 2 -movflags +faststart "+Tools.Q(temp);
    Tools.Run(Tools.FFmpeg,args,token);
    token.ThrowIfCancellationRequested();
    if(!File.Exists(temp)||new FileInfo(temp).Length==0)throw new Exception("Error al preparar medio: "+file);
    if(File.Exists(target))File.Delete(temp);else File.Move(temp,target);
    return target;
   }finally{if(File.Exists(temp))try{File.Delete(temp);}catch{}}
  }
  static Asset InspectConvertedStill(string file,CancellationToken token){
   var original=new FileInfo(file);if(!original.Exists)throw new FileNotFoundException("Imagen no disponible",file);
   string signature=original.FullName+"|"+original.Length+"|"+original.LastWriteTimeUtc.Ticks;
   string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(signature))).Replace("-","").ToLowerInvariant();
   string folder=ConvertedStillCacheRoot;
   Directory.CreateDirectory(folder);string target=Path.Combine(folder,hash+".png");
   if(!File.Exists(target)){
    string temporary=target+".partial-"+Store.Id();
    try{
     if(Path.GetExtension(file).Equals(".svg",StringComparison.OrdinalIgnoreCase))WebMedia.ConvertLocalSvg(file,temporary,token);
     else Tools.Run(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -y -i "+Tools.Q(file)+" -frames:v 1 -pix_fmt rgba -c:v png -f image2 "+Tools.Q(temporary),token);
     token.ThrowIfCancellationRequested();if(!File.Exists(temporary))throw new Exception("No se pudo convertir la imagen");
     if(!File.Exists(target))File.Move(temporary,target);
    }finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch{}}
   }
   var asset=Inspect(target,token,false);asset.name=original.Name;asset.source.uri=original.FullName;
   asset.extensions.Add(new Extension{@namespace="prometheus.import.static-image",version="1",payload=new Dictionary<string,object>{{"source_extension",original.Extension.ToLowerInvariant()},{"first_frame_only",original.Extension.Equals(".gif",StringComparison.OrdinalIgnoreCase)}}});
   return asset;
  }
  static Asset Inspect(string file,CancellationToken token,bool resolvedVideo){
   if(MgtMedia.IsFile(file))return MgtMedia.Inspect(file,token);
   if(!resolvedVideo&&IsConvertibleStillExtension(file))return InspectConvertedStill(file,token);
   string ext=Path.GetExtension(file).ToLowerInvariant();bool audioFile=IsAudioFileExtension(file);bool image=ext==".png"||ext==".jpg"||ext==".jpeg";
   if(!resolvedVideo&&!audioFile&&!image&&!SupportedVideoContainers.Contains(ext))throw new Exception("Formato no compatible. Usa MP4, MOV, MKV, WebM, AVI, TS, MXF, audio común, PNG o JPEG.");
   var doc=Store.Json().Deserialize<Dictionary<string,object>>(Tools.Run(Tools.FFprobe,"-v error -show_streams -show_format -of json "+Tools.Q(file),token));var fmt=(Dictionary<string,object>)doc["format"];double length=Num(Get(fmt,"duration"),0);bool movProres=ext==".mp4"&&((System.Collections.IEnumerable)doc["streams"]).Cast<object>().OfType<Dictionary<string,object>>().Any(s=>Convert.ToString(Get(s,"codec_type"))=="video"&&Convert.ToString(Get(s,"codec_name"))=="prores");
   var info=new FileInfo(file);var locator=new Locator{path=Path.GetFullPath(file),relative=false};

   if(!resolvedVideo&&!audioFile&&!image){
    bool needsPrep=false;string profile=null;
    foreach(var raw in (System.Collections.IEnumerable)doc["streams"]){
     var s=(Dictionary<string,object>)raw;string kind=Convert.ToString(Get(s,"codec_type"));if(kind!="video")continue;
     string codec=Convert.ToString(Get(s,"codec_name"));string pixFmt=Convert.ToString(Get(s,"pix_fmt"));string field=Convert.ToString(Get(s,"field_order"));string trc=Convert.ToString(Get(s,"color_transfer"));string primaries=Convert.ToString(Get(s,"color_primaries"));
     bool isInterlaced=(field!=""&&field!="unknown"&&field!="progressive");
     string cspace=Convert.ToString(Get(s,"color_space"));bool isHdr=(trc=="smpte2084"||trc=="arib-std-b67"||primaries=="bt2020"||cspace=="bt2020nc"||cspace=="bt2020c");
     bool isNonStandardContainer=(ext!=".mp4"&&ext!=".mov")||(!movProres&&!(codec=="h264"&&pixFmt=="yuv420p"||codec=="hevc"&&pixFmt=="yuv420p"));
     Rational rRate=Rate(Get(s,"r_frame_rate")),avgRate=Rate(Get(s,"avg_frame_rate"));
     bool isVfr=rRate!=null&&avgRate!=null&&Math.Abs(rRate.Seconds-avgRate.Seconds)>0.05;
     if(isInterlaced||isHdr||isNonStandardContainer||isVfr){
      needsPrep=true;
      profile=(isHdr&&isInterlaced)?"hdr_interlaced_normalized":isHdr?"hdr_tonemapped_sdr":isInterlaced?"interlaced_deinterlaced":"conform_cfr_bt709";
      break;
     }
    }
    if(needsPrep&&profile!=null){
     string prepPath=EnsurePreparedMedia(file,profile,token);
     var prepAsset=InspectResolved(prepPath,token);
     prepAsset.name=Path.GetFileName(file);
     prepAsset.source=new MediaSource{kind="local",uri=locator.path,imported_at=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture)};
     prepAsset.locator=locator;
     prepAsset.representations.Clear();
     var origRep=new MediaRepresentation{role="original",locator=locator,size_bytes=info.Length,modified_utc=info.LastWriteTimeUtc.ToString("o",CultureInfo.InvariantCulture)};
     var finalRep=new MediaRepresentation{role="final",locator=new Locator{path=prepPath,relative=false},size_bytes=new FileInfo(prepPath).Length,modified_utc=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture)};
     var prevRep=new MediaRepresentation{role="preview",locator=new Locator{path=prepPath,relative=false},size_bytes=new FileInfo(prepPath).Length,modified_utc=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture)};
     foreach(var stream in prepAsset.streams){
      origRep.stream_ordinals[stream.id]=stream.ordinal;
      finalRep.stream_ordinals[stream.id]=stream.ordinal;
      prevRep.stream_ordinals[stream.id]=stream.ordinal;
     }
     prepAsset.representations.Add(origRep);
     prepAsset.representations.Add(finalRep);
     prepAsset.representations.Add(prevRep);
     prepAsset.extensions.Add(new Extension{@namespace="prometheus.media.prep-profile",version="1",payload=new Dictionary<string,object>{{"profile",profile},{"original_path",locator.path},{"prepared_path",prepPath}}});
     prepAsset.extensions.Add(new Extension{@namespace="prometheus.media.index",version="1",payload=new Dictionary<string,object>{{"container",ext},{"profile",profile},{"is_interlaced",profile.Contains("interlaced")},{"is_hdr",profile.Contains("hdr")},{"compatible",true},{"stages",new List<string>{"playback","frame_seek","proxy","export"}}}});
     return prepAsset;
    }
   }

   var a=new Asset{id=Store.Id(),name=Path.GetFileName(file),source=new MediaSource{kind="local",uri=locator.path,imported_at=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture)},representations=new List<MediaRepresentation>{new MediaRepresentation{role="original",locator=locator,size_bytes=info.Length,modified_utc=info.LastWriteTimeUtc.ToString("o",CultureInfo.InvariantCulture)}},locator=locator,presentation_origin=Rational.Time(Num(Get(fmt,"start_time"),0)),container_format=Convert.ToString(Get(fmt,"format_name"))};
   double bitrate=Num(Get(fmt,"bit_rate"),0);if(bitrate>0)a.bitrate=(long)bitrate;
   foreach(var raw in (System.Collections.IEnumerable)doc["streams"]){var s=(Dictionary<string,object>)raw;string kind=Convert.ToString(Get(s,"codec_type"));if(kind!="video"&&kind!="audio")continue;string codec=Convert.ToString(Get(s,"codec_name"));
    if(!resolvedVideo&&ext==".mp4"&&!movProres&&((kind=="video"&&!(codec=="h264"&&Convert.ToString(Get(s,"pix_fmt"))=="yuv420p"||codec=="hevc"&&Convert.ToString(Get(s,"pix_fmt"))=="yuv420p"))||(kind=="audio"&&(codec!="aac"||Convert.ToString(Get(s,"profile"))!="LC"))))throw new Exception("MP4 compatible: H.264/HEVC SDR de 8 bits yuv420p y audio AAC-LC.");
    if(kind=="video"&&!image){string field=Convert.ToString(Get(s,"field_order"));string trc=Convert.ToString(Get(s,"color_transfer"));if((field!=""&&field!="unknown"&&field!="progressive")||trc=="smpte2084"||trc=="arib-std-b67")throw new Exception("Video entrelazado/HDR fuera del perfil inicial");}
    int channels=(int)Num(Get(s,"channels"),2),sample=(int)Num(Get(s,"sample_rate"),48000);if(kind=="audio"&&(channels>2||channels<1||sample<8000||sample>192000))throw new Exception("Audio compatible: mono o estéreo entre 8 y 192 kHz");
    if(audioFile&&kind!="audio")throw new Exception("El archivo de audio contiene una pista visual no compatible");
    if(ext==".wav"&&!new[]{"pcm_u8","pcm_s16le","pcm_s24le","pcm_s32le","pcm_f32le","pcm_f64le"}.Contains(codec))throw new Exception("WAV compatible: PCM entero o flotante");
    double duration=Num(Get(s,"duration"),length);if(!image&&duration<=0)throw new Exception("No se pudo determinar duración");int? displayRotation=kind=="video"?Rotation(Get(s,"side_data_list")):null;if(displayRotation.HasValue&&displayRotation.Value%90!=0)throw new Exception("Rotación de vídeo no compatible: usa múltiplos de 90 grados");
    a.streams.Add(new StreamInfo{id=Store.Id(),ordinal=(int)Num(Get(s,"index"),0),kind=image?"image":kind,codec=codec,pts_origin=ExactTime(Get(s,"start_pts"),Rate(Get(s,"time_base")),Num(Get(s,"start_time"),a.presentation_origin.Seconds)),time_base=Rate(Get(s,"time_base"))??new Rational(1,48000),duration=image?null:ExactTime(Get(s,"duration_ts"),Rate(Get(s,"time_base")),duration),nominal_fps=kind=="video"&&!image?Rate(Get(s,"avg_frame_rate"))??new Rational(60,1):null,sample_rate=kind=="audio"?(int?)sample:null,channels=kind=="audio"?(int?)channels:null,width=kind=="video"?(int?)Num(Get(s,"width"),0):null,height=kind=="video"?(int?)Num(Get(s,"height"),0):null,display_rotation=displayRotation,pixel_format=kind=="video"?Convert.ToString(Get(s,"pix_fmt")):null,channel_layout=kind=="audio"?Convert.ToString(Get(s,"channel_layout")):null,sample_aspect_ratio=kind=="video"?Convert.ToString(Get(s,"sample_aspect_ratio")):null,color_primaries=kind=="video"?Convert.ToString(Get(s,"color_primaries")):null,color_transfer=kind=="video"?Convert.ToString(Get(s,"color_transfer")):null,color_space=kind=="video"?Convert.ToString(Get(s,"color_space")):null,color_range=kind=="video"?Convert.ToString(Get(s,"color_range")):null});
   }
   if(a.streams.Count==0||a.streams.Count(x=>x.kind=="video")>1||a.streams.Count(x=>x.kind=="audio")>1)throw new Exception("Medio sin stream compatible o con varias pistas internas");
   if((ext==".mov"||movProres||ext==".mp4"&&a.streams.Any(x=>x.kind=="video"&&x.codec=="hevc"))&&a.streams.Any(x=>x.kind=="video"))Tools.Run(Tools.FFmpeg,"-hide_banner -v error -nostdin -i "+Tools.Q(file)+" -t 0.1 -map 0:v:0 -frames:v 1 -f null NUL",token);
   foreach(var stream in a.streams)a.representations[0].stream_ordinals[stream.id]=stream.ordinal;
   if(a.streams.Any(x=>x.kind=="image"))a.recommended_duration=new Rational(5,1);
   a.extensions.Add(new Extension{@namespace="prometheus.media.index",version="1",payload=new Dictionary<string,object>{{"container",ext},{"codecs",a.streams.Select(st=>st.codec).Distinct().ToList()},{"is_interlaced",false},{"is_hdr",false},{"compatible",true},{"stages",new List<string>{"playback","frame_seek","proxy","export"}}}});
   return a;
  }
  public static void Insert(Project p,string assetId,string trackId,double at){var a=p.assets.Find(x=>x.id==assetId);var target=p.Active.tracks.Find(t=>t.id==trackId);if(a==null||target==null)throw new Exception("Selecciona un medio y una pista");if(target.locked)throw new Exception("Pista bloqueada");var selected=a.streams.Where(s=>target.kind=="video"?s.kind!="audio":s.kind=="audio").ToList();if(selected.Count==0)throw new Exception("El medio no corresponde a la pista seleccionada");var st=selected[0];var ids=new List<string>();double start=Rational.Frame(at).Seconds;Action<StreamInfo,Track,double> add=(stream,t,offset)=>{if(t.locked)throw new Exception("Pista bloqueada");double begin=(t.kind=="audio"?Rational.Time(start+Math.Max(0,offset)):Rational.Frame(start+Math.Max(0,offset))).Seconds;var c=new Clip{id=Store.Id(),name=a.name,asset_id=a.id,stream_id=stream.id,track_id=t.id,timeline_start=Rational.Time(begin),source_in=Rational.Time(0),duration=stream.duration==null?(a.recommended_duration??new Rational(5,1)):(t.kind=="video"?new Rational((long)Math.Floor(stream.duration.Seconds*60),60):new Rational((long)Math.Floor(stream.duration.Seconds*48000+0.000001),48000))};p.Active.clips.Add(c);ids.Add(c.id);};
   double streamOffset=st.pts_origin.Seconds-a.presentation_origin.Seconds;add(st,target,streamOffset);
   if(st.kind=="video"){var audio=a.streams.Find(s=>s.kind=="audio");if(audio!=null){double offset=audio.pts_origin.Seconds-a.presentation_origin.Seconds;double begin=Rational.Time(start+Math.Max(0,offset)).Seconds;double duration=audio.duration==null?(a.recommended_duration??new Rational(5,1)).Seconds:Math.Floor(audio.duration.Seconds*48000+0.000001)/48000.0;var t=p.Active.tracks.Where(x=>x.kind=="audio"&&!x.locked).OrderBy(x=>x.order).FirstOrDefault(x=>!p.Active.clips.Any(c=>c.track_id==x.id&&c.timeline_start.Seconds<begin+duration-0.000001&&c.End>begin+0.000001));if(t==null)t=Store.AddTrack(p.Active,"audio");add(audio,t,offset);}}
   Editor.Overwrite(p,ids);if(ids.Count>1)p.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=ids});
  }
 }
}



