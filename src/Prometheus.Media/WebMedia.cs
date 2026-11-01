using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Prometheus {
 public static class WebMedia {
  public const string MetadataNamespace="prometheus.web.metadata";
  static readonly ConcurrentDictionary<string,SemaphoreSlim> downloadGates=new ConcurrentDictionary<string,SemaphoreSlim>();
  static string YtDlp {get{return Path.Combine(Tools.Root,"runtime","yt-dlp.exe");}}
  public static string CookieBrowser,CookieFile;
  public static int PreviewHeight=360;
  static int PreviewCrf(int height){return height>=1080?21:height>=720?25:30;}
  static string PreviewScale(int height){return "scale="+(height*16/9)+":"+height+":force_original_aspect_ratio=decrease:force_divisible_by=2";}
  internal static string JsRuntimeArguments(){string node=Path.Combine(Tools.Root,"mgt-engine","runtime","node","node.exe");return File.Exists(node)?" --js-runtimes "+Tools.Q("node:"+node):"";}
  internal static string CookieArguments(){
   if(!String.IsNullOrWhiteSpace(CookieFile)){if(!File.Exists(CookieFile))throw new Exception("No se encontró el archivo local de cookies configurado en Medios web.");return "--cookies "+Tools.Q(CookieFile);}
   if(CookieBrowser=="chrome"||CookieBrowser=="edge"||CookieBrowser=="firefox")return "--cookies-from-browser "+CookieBrowser;
   return "";
  }
  static object Get(Dictionary<string,object> data,string key){object value;return data!=null&&data.TryGetValue(key,out value)?value:null;}
  static string AsString(object value){return value==null?"":Convert.ToString(value,CultureInfo.InvariantCulture);}
  static double Number(object value,double fallback=0){double parsed;return value!=null&&double.TryParse(AsString(value),NumberStyles.Float,CultureInfo.InvariantCulture,out parsed)?parsed:fallback;}
  static string Hash(string value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant().Substring(0,24);}
  static Extension Metadata(Asset asset){return asset.extensions.FirstOrDefault(x=>x!=null&&x.@namespace==MetadataNamespace&&x.version=="1");}
  public static string Detail(Asset asset,string key){var ext=Metadata(asset);return AsString(Get(ext==null?null:ext.payload,key));}
  public static bool IsYouTubeUrl(string text){Uri uri;if(!Uri.TryCreate(text,UriKind.Absolute,out uri)||uri.Scheme!="http"&&uri.Scheme!="https")return false;string host=uri.Host.ToLowerInvariant();return host=="youtu.be"||host=="youtube.com"||host.EndsWith(".youtube.com",StringComparison.Ordinal)||host=="youtube-nocookie.com"||host.EndsWith(".youtube-nocookie.com",StringComparison.Ordinal);}
  static bool HostIs(string host,string domain){return host==domain||host.EndsWith("."+domain,StringComparison.Ordinal);}
  public static string Platform(string text){
   Uri uri;if(!Uri.TryCreate(text,UriKind.Absolute,out uri)||uri.Scheme!="http"&&uri.Scheme!="https")return null;
   string host=uri.Host.ToLowerInvariant(),path=uri.AbsolutePath.ToLowerInvariant();
   if(IsYouTubeUrl(text))return "youtube";
   if(HostIs(host,"instagram.com")||host=="instagr.am")return path.StartsWith("/p/")||path.StartsWith("/reel/")||path.StartsWith("/reels/")||path.StartsWith("/tv/")?"instagram":null;
   if(HostIs(host,"tiktok.com"))return path.Contains("/video/")||host=="vm.tiktok.com"||host=="vt.tiktok.com"?"tiktok":null;
   if(HostIs(host,"facebook.com")||host=="fb.watch")return host=="fb.watch"||path.Contains("/videos/")||path.StartsWith("/reel/")||path.StartsWith("/watch")||path.StartsWith("/share/v/")||path.StartsWith("/share/r/")||path.EndsWith("/video.php")||path.EndsWith("/story.php")?"facebook":null;
   if(HostIs(host,"x.com")||HostIs(host,"twitter.com"))return path.Contains("/status/")?"twitter":null;
   if(host=="clips.twitch.tv")return path.Length>1?"twitch":null;
   if(HostIs(host,"twitch.tv"))return path.StartsWith("/videos/")||path.Contains("/clip/")?"twitch":null;
   return null;
  }
  public static string PlatformLabel(string platform){switch(platform){case "youtube":return "YouTube";case "instagram":return "Instagram";case "tiktok":return "TikTok";case "facebook":return "Facebook";case "twitter":return "X/Twitter";case "twitch":return "Twitch";default:return "Web";}}
  public static bool IsVideoUrl(string text){return Platform(text)!=null;}
  public static bool IsImageUrl(string text){Uri uri;if(!Uri.TryCreate(text,UriKind.Absolute,out uri)||uri.Scheme!="http"&&uri.Scheme!="https")return false;string host=uri.Host.ToLowerInvariant();return host!="localhost"&&host!="127.0.0.1"&&host!="::1";}
  public static string ProbeUrlKind(string url,CancellationToken token){
   if(Platform(url)!=null)return "video";
   if(!IsImageUrl(url))return "unsupported";
   Uri uri=new Uri(url);string extension=Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
   if(new[]{".png",".jpg",".jpeg",".gif",".webp",".bmp",".avif",".svg",".tif",".tiff"}.Contains(extension))return "image";
   HttpWebRequest request=null;
   try{request=(HttpWebRequest)WebRequest.Create(uri);request.Method="HEAD";request.Timeout=8000;request.UserAgent="Mozilla/5.0 Prometheus/0.3";using(token.Register(()=>request.Abort()))using(var response=(HttpWebResponse)request.GetResponse()){string mime=(response.ContentType??"").ToLowerInvariant();if(mime.StartsWith("image/"))return "image";if(mime.StartsWith("video/"))return "video";return "ambiguous";}}
   catch(OperationCanceledException){throw;}
   catch{token.ThrowIfCancellationRequested();return "ambiguous";}
  }
  // A direct URL is inspected from its response, not from the host or file name alone.
  // The same GET supplies the bytes used to create an image asset.
  public static Asset InspectDirectLink(string url,string pageUrl,string cacheDir,string expectedKind,CancellationToken token){
   if(!IsImageUrl(url))throw new Exception("Enlace HTTP/HTTPS no compatible");
   if(expectedKind!=null&&expectedKind!="image"&&expectedKind!="video")throw new Exception("Tipo de enlace no compatible");
   if(Platform(url)!=null){if(expectedKind=="image")throw new Exception("El enlace corresponde a una publicación de vídeo, no a una imagen directa");return InspectVideo(url,token);}
   if(File.Exists(Path.Combine(cacheDir,Hash(url)+".png"))){if(expectedKind=="video")throw new Exception("La instrucción declara vídeo, pero el enlace ya está registrado como imagen");return ImageFromBytes(url,pageUrl,cacheDir,new byte[0],token);}
   const int limit=25*1024*1024;
   var request=(HttpWebRequest)WebRequest.Create(url);request.Timeout=15000;request.ReadWriteTimeout=15000;request.UserAgent="Mozilla/5.0 Prometheus/0.3";request.Accept="image/png,image/jpeg,image/webp,image/svg+xml,*/*;q=0.5";
   try{using(token.Register(()=>request.Abort()))using(var response=(HttpWebResponse)request.GetResponse())using(var stream=response.GetResponseStream()){
    byte[] head=new byte[8192];int used=0,n;
    while(used<head.Length&&(n=stream.Read(head,used,head.Length-used))>0)used+=n;
    string mime=(response.ContentType??"").Split(';')[0].Trim().ToLowerInvariant();
    string kind=DirectKind(mime,head,used,expectedKind);
    if(kind=="video")return InspectVideo(url,token,true);
    using(var buffer=new MemoryStream()){
     buffer.Write(head,0,used);if(response.ContentLength>limit)throw new Exception("La imagen supera el límite de tamaño");
     byte[] chunk=new byte[32768];while((n=stream.Read(chunk,0,chunk.Length))>0){token.ThrowIfCancellationRequested();buffer.Write(chunk,0,n);if(buffer.Length>limit)throw new Exception("La imagen supera el límite de tamaño");}
     return ImageFromBytes(url,pageUrl,cacheDir,buffer.ToArray(),token);
    }
   }}catch(OperationCanceledException){throw;}catch(WebException ex){token.ThrowIfCancellationRequested();var response=ex.Response as HttpWebResponse;if(response!=null)throw new Exception("El servidor rechazó el enlace (HTTP "+(int)response.StatusCode+")");throw new Exception("No se pudo consultar el enlace: "+ex.Message);}
  }
  static string DirectKind(string mime,byte[] head,int length,string expectedKind){
   string signature=ImageSignature(head,length);
   bool image=mime.StartsWith("image/")||signature!=null;
   bool video=mime.StartsWith("video/")||!image&&VideoSignature(head,length);
   if(image&&video)throw new Exception("El servidor devolvió un tipo de medio contradictorio");
   if(expectedKind=="image"&&!image&&signature==null&&mime!="application/octet-stream")throw new Exception("El enlace no devuelve una imagen directa ("+(mime.Length==0?"sin tipo":mime)+")");
   if(expectedKind=="video"&&image)throw new Exception("La instrucción declara vídeo, pero el enlace devuelve una imagen");
   if(video||!image&&expectedKind=="video")return "video";
   if(!image&&expectedKind!="image")throw new Exception("No se pudo identificar el contenido del enlace; indica source_kind solo si conoces su tipo");
   return "image";
  }
  static bool VideoSignature(byte[] bytes,int length){return length>=12&&System.Text.Encoding.ASCII.GetString(bytes,4,4)=="ftyp"&&ImageSignature(bytes,length)!="avif"||length>=4&&bytes[0]==0x1A&&bytes[1]==0x45&&bytes[2]==0xDF&&bytes[3]==0xA3;}
  static string ImageSignature(byte[] bytes,int length){
   if(length>=8&&bytes[0]==137&&bytes[1]==80&&bytes[2]==78&&bytes[3]==71)return "png";
   if(length>=3&&bytes[0]==255&&bytes[1]==216&&bytes[2]==255)return "jpeg";
   if(length>=6&&(System.Text.Encoding.ASCII.GetString(bytes,0,6)=="GIF87a"||System.Text.Encoding.ASCII.GetString(bytes,0,6)=="GIF89a"))return "gif";
   if(length>=12&&System.Text.Encoding.ASCII.GetString(bytes,0,4)=="RIFF"&&System.Text.Encoding.ASCII.GetString(bytes,8,4)=="WEBP")return "webp";
   if(length>=2&&bytes[0]==66&&bytes[1]==77)return "bmp";
   if(length>=12&&System.Text.Encoding.ASCII.GetString(bytes,4,4)=="ftyp"&&System.Text.Encoding.ASCII.GetString(bytes,8,4).StartsWith("avi"))return "avif";
   if(length>=4&&(bytes[0]==73&&bytes[1]==73&&bytes[2]==42&&bytes[3]==0||bytes[0]==77&&bytes[1]==77&&bytes[2]==0&&bytes[3]==42))return "tiff";
   if(length>0){string prefix=System.Text.Encoding.UTF8.GetString(bytes,0,Math.Min(length,8192)).TrimStart('\uFEFF',' ','\t','\r','\n');if(System.Text.RegularExpressions.Regex.IsMatch(prefix,@"\A(?:<\?xml[^>]*>\s*)?(?:(?:<!--[\s\S]*?-->|<!DOCTYPE[^>]*>)\s*)*<svg(?:\s|>)",System.Text.RegularExpressions.RegexOptions.IgnoreCase))return "svg";}
   return null;
  }
  public static Asset InspectYouTube(string url,CancellationToken token){if(!IsYouTubeUrl(url))throw new Exception("Pega un enlace de vídeo de YouTube válido");return InspectVideo(url,token);}
  public static Asset InspectVideo(string url,CancellationToken token){return InspectVideo(url,token,false);}
  public static Asset InspectVideo(string url,CancellationToken token,bool allowGeneric){
   string platform=Platform(url);if(platform==null&&!allowGeneric)throw new Exception("Este enlace de vídeo aún no es compatible");
   if(platform==null)platform="web";
   if(!File.Exists(YtDlp))throw new Exception("Falta runtime/yt-dlp.exe para leer enlaces web");
   string json;try{json=Tools.Run(YtDlp,"--ignore-config "+CookieArguments()+JsRuntimeArguments()+" --no-playlist --dump-single-json --skip-download --no-warnings --socket-timeout 12 --retries 1 --extractor-retries 1 "+Tools.Q(url),token);}catch(OperationCanceledException){throw;}catch(Exception ex){string detail=ex.Message.Trim();if(detail.Length>350)detail=detail.Substring(detail.Length-350);throw new Exception("No se pudo leer el enlace de "+PlatformLabel(platform)+": "+detail);}
   return FromMetadata(url,json,allowGeneric);
  }
  public static Asset InspectAudio(string url,CancellationToken token){
   string platform=Platform(url);if(platform==null)throw new Exception("Este enlace no es compatible para extraer audio");
   if(!File.Exists(YtDlp))throw new Exception("Falta runtime/yt-dlp.exe para leer enlaces web");
   string json=Tools.Run(YtDlp,"--ignore-config "+CookieArguments()+JsRuntimeArguments()+" --no-playlist --dump-single-json --skip-download --no-warnings --socket-timeout 12 --retries 1 --extractor-retries 1 "+Tools.Q(url),token);
   return FromMetadataAudio(url,json);
  }
  public static Asset FromMetadataAudio(string url,string json){
   var asset=FromMetadata(url,json);var audio=asset.streams.FirstOrDefault(s=>s.kind=="audio");
   if(audio==null)throw new Exception("El enlace no contiene audio accesible");
   asset.streams.Clear();audio.ordinal=0;asset.streams.Add(audio);
   asset.source.resolver_type="yt-dlp-audio/"+Platform(url);
   return asset;
  }
  public static bool IsAudioOnly(Asset asset){return asset!=null&&asset.source!=null&&asset.source.resolver_type!=null&&asset.source.resolver_type.StartsWith("yt-dlp-audio/",StringComparison.Ordinal);}
  public static string AudioSelectionArguments(string template,string url){
   return "--ignore-config "+CookieArguments()+JsRuntimeArguments()+" --no-playlist --no-warnings --no-progress --socket-timeout 12 --retries 1 --extractor-retries 1 -f bestaudio -o "+Tools.Q(template)+" "+Tools.Q(url);
  }
  public static Asset FromMetadata(string url,string json){return FromMetadata(url,json,false);}
  public static Asset FromMetadata(string url,string json,bool allowGeneric){
   string platform=Platform(url);if(platform==null&&!allowGeneric)throw new Exception("Este enlace de vídeo aún no es compatible");
   if(platform==null)platform="web";
   var info=new JavaScriptSerializer{MaxJsonLength=Int32.MaxValue,RecursionLimit=100}.Deserialize<Dictionary<string,object>>(json);
   if(info==null||AsString(Get(info,"_type"))=="playlist")throw new Exception("Importa un vídeo individual, no una lista o carrusel");
   if(AsString(Get(info,"is_live")).Equals("true",StringComparison.OrdinalIgnoreCase)||AsString(Get(info,"live_status"))=="is_live")throw new Exception("Los directos aún no se importan; usa un clip o VOD terminado");
   double duration=Number(Get(info,"duration"));bool provisional=duration<=0;if(duration>48*3600)throw new Exception("El enlace supera la duración admitida");if(provisional)duration=5;
   string id=AsString(Get(info,"id")),title=AsString(Get(info,"title"));if(String.IsNullOrWhiteSpace(id))id=Hash(url);
   if(String.IsNullOrWhiteSpace(title))title=PlatformLabel(platform)+" · "+id;
   var allFormats=Get(info,"formats") as System.Collections.IEnumerable;
   var formats=allFormats==null?new List<Dictionary<string,object>>():allFormats.Cast<object>().OfType<Dictionary<string,object>>().ToList();
   if(AsString(Get(info,"vcodec"))=="none"&&!formats.Any(f=>{string codec=AsString(Get(f,"vcodec"));return codec.Length>0&&codec!="none";}))throw new Exception("Este enlace no contiene vídeo");
   double fps=Number(Get(info,"fps"));if(fps<=0)fps=formats.Select(f=>Number(Get(f,"fps"))).Where(x=>x>0&&x<=120).DefaultIfEmpty(0).Max();if(fps>120)fps=0;
   int width=(int)Number(Get(info,"width")),height=(int)Number(Get(info,"height"));
   if(width<=0)width=(int)formats.Select(f=>Number(Get(f,"width"))).DefaultIfEmpty(0).Max();
   if(height<=0)height=(int)formats.Select(f=>Number(Get(f,"height"))).DefaultIfEmpty(0).Max();
   string page=AsString(Get(info,"webpage_url"));if(platform!="web"&&Platform(page)!=platform||String.IsNullOrWhiteSpace(page))page=url;
   var asset=new Asset{id=Store.Id(),name=title,source=new MediaSource{kind="web",uri=page,page_url=page,imported_at=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture),resolver_type=platform=="youtube"?"youtube/yt-dlp":"yt-dlp/"+platform},presentation_origin=Rational.Time(0),container_format=platform};
   asset.streams.Add(new StreamInfo{id=Store.Id(),ordinal=0,kind="video",codec="virtual",pts_origin=Rational.Time(0),time_base=new Rational(1,60),duration=Rational.Frame(duration),nominal_fps=fps>0?Rational.Time(fps):null,width=width>0?(int?)width:null,height=height>0?(int?)height:null});
   string acodec=AsString(Get(info,"acodec"));bool hasAudio=acodec.Length>0&&acodec!="none"||formats.Any(f=>{string codec=AsString(Get(f,"acodec"));return codec.Length>0&&codec!="none";});
   if(hasAudio)asset.streams.Add(new StreamInfo{id=Store.Id(),ordinal=1,kind="audio",codec="virtual",pts_origin=Rational.Time(0),time_base=new Rational(1,48000),duration=Rational.Frame(duration),sample_rate=48000,channels=2});
   asset.extensions.Add(new Extension{@namespace=MetadataNamespace,version="1",payload=new Dictionary<string,object>{{"video_id",id},{"platform",platform},{"original_url",url},{"thumbnail_url",AsString(Get(info,"thumbnail"))},{"width",width},{"height",height},{"fps",fps},{"uploader",AsString(Get(info,"uploader"))},{"duration_provisional",provisional}}});
   return asset;
  }
  public static bool DurationProvisional(Asset asset){return Detail(asset,"duration_provisional").Equals("true",StringComparison.OrdinalIgnoreCase);}
  public static void EnsureProvisionalDuration(Asset asset,double seconds){if(!DurationProvisional(asset))return;if(seconds<=0||seconds>48*3600)throw new Exception("Tramo fuera de la duración admitida");foreach(var stream in asset.streams.Where(x=>x.kind=="video"||x.kind=="audio"))if(stream.duration==null||stream.duration.Seconds<seconds)stream.duration=stream.kind=="audio"?Rational.Time(seconds):Rational.Frame(seconds);}
  public static void SetMeasuredDuration(Asset asset,double seconds){
   if(asset==null||seconds<=0||seconds>48*3600)throw new Exception("La descarga no tiene una duración compatible");
   foreach(var stream in asset.streams.Where(x=>x.kind=="video"||x.kind=="audio"))stream.duration=stream.kind=="audio"?Rational.Time(seconds):Rational.Frame(seconds);
   var metadata=Metadata(asset);if(metadata!=null&&metadata.payload!=null)metadata.payload["duration_provisional"]=false;
  }
  public static double MeasureDuration(string file,CancellationToken token){
   var inspected=Media.InspectResolved(file,token);
   return inspected.streams.Where(x=>x.duration!=null).Select(x=>x.duration.Seconds).DefaultIfEmpty(0).Max();
  }
  public static void ReconcileDuration(Project project,string assetId,double seconds){
   var asset=project.assets.Find(x=>x.id==assetId);if(asset==null||!DurationProvisional(asset))return;
   double previous=asset.streams.Where(x=>x.duration!=null).Select(x=>x.duration.Seconds).DefaultIfEmpty(5).Max();
   var clips=project.Active.clips.Where(x=>x.asset_id==assetId).ToList();
   foreach(var clip in clips){if(clip.source_in.Seconds+clip.duration.Seconds*clip.speed.Seconds>seconds+0.000001)ShortTail.Attach(clip,Math.Max(previous,clip.source_in.Seconds+clip.duration.Seconds*clip.speed.Seconds));}
   if(seconds>previous+1.0/60){
    var full=clips.Where(x=>Math.Abs(x.speed.Seconds-1)<0.000001&&Math.Abs(x.source_in.Seconds+x.duration.Seconds-previous)<1.0/60+0.000001).ToList();
    foreach(var clip in full){double available=seconds-clip.source_in.Seconds;var next=project.Active.clips.Where(x=>x.track_id==clip.track_id&&x.id!=clip.id&&x.timeline_start.Seconds>=clip.End-0.000001).OrderBy(x=>x.timeline_start.Seconds).FirstOrDefault();if(next!=null)available=Math.Min(available,next.timeline_start.Seconds-clip.timeline_start.Seconds);if(available>clip.duration.Seconds+1.0/60){bool audio=project.Active.tracks.Find(x=>x.id==clip.track_id).kind=="audio";clip.duration=audio?Rational.Time(available):Rational.Frame(available);}}
   }
   SetMeasuredDuration(asset,seconds);
  }
  static byte[] ReadWeb(string url,int limit,CancellationToken token){
   if(!IsImageUrl(url))throw new Exception("Solo se admiten imágenes HTTP/HTTPS accesibles");
   var request=(HttpWebRequest)WebRequest.Create(url);request.Timeout=15000;request.ReadWriteTimeout=15000;request.UserAgent="Mozilla/5.0 Prometheus/0.3";request.Accept="image/png,image/jpeg,image/webp,image/svg+xml,*/*;q=0.5";
   try{using(token.Register(()=>request.Abort()))using(var response=(HttpWebResponse)request.GetResponse())using(var stream=response.GetResponseStream())using(var buffer=new MemoryStream()){
    if(response.ContentLength>limit)throw new Exception("La imagen supera el límite de tamaño");
    var chunk=new byte[32768];int count;while((count=stream.Read(chunk,0,chunk.Length))>0){token.ThrowIfCancellationRequested();buffer.Write(chunk,0,count);if(buffer.Length>limit)throw new Exception("La imagen supera el límite de tamaño");}
    return buffer.ToArray();
   }}catch(WebException ex){token.ThrowIfCancellationRequested();var response=ex.Response as HttpWebResponse;if(response!=null&&response.StatusCode==HttpStatusCode.Forbidden)throw new Exception("El servidor rechazó la descarga de esta imagen (403). Comprueba que el enlace temporal siga vigente.");throw new Exception("No se pudo descargar la imagen: "+ex.Message);}
  }
  static void RasterizeSvg(byte[] bytes,string target,CancellationToken token){
   string runtime=Path.Combine(Tools.Root,"mgt-engine","runtime");
   string node=Path.Combine(runtime,"node","node.exe"),chrome=Path.Combine(runtime,"chromium","chrome.exe"),playwright=Path.Combine(runtime,"playwright-core"),script=Path.Combine(Tools.Root,"runtime","svg-rasterize.js");
   if(!File.Exists(node)||!File.Exists(chrome)||!File.Exists(script))throw new Exception("Falta el conversor SVG incluido con Prometheus");
   string input=Path.Combine(Path.GetTempPath(),"prometheus-svg-"+Guid.NewGuid().ToString("N")+".svg");
   try{File.WriteAllBytes(input,bytes);Tools.Run(node,Tools.Q(script)+" "+Tools.Q(playwright)+" "+Tools.Q(chrome)+" "+Tools.Q(input)+" "+Tools.Q(target),token);if(!File.Exists(target))throw new Exception("No se generó la imagen del SVG");}
   catch(OperationCanceledException){throw;}catch(Exception ex){throw new Exception("No se pudo convertir el SVG: "+ex.Message);}
   finally{try{File.Delete(input);}catch{}}
  }
  public static void ConvertLocalSvg(string source,string target,CancellationToken token){
   var info=new FileInfo(source);if(info.Length>25*1024*1024)throw new Exception("SVG demasiado grande para convertir (máximo 25 MB)");
   SavePng(File.ReadAllBytes(source),target,token);
  }
  static void SavePng(byte[] bytes,string target,CancellationToken token){
   if(ImageSignature(bytes,Math.Min(bytes.Length,8192))=="svg"){RasterizeSvg(bytes,target,token);return;}
   try{using(var input=new MemoryStream(bytes))using(var image=System.Drawing.Image.FromStream(input,true,true))using(var bitmap=new System.Drawing.Bitmap(image))bitmap.Save(target,System.Drawing.Imaging.ImageFormat.Png);}
   catch(Exception ex){if(!(ex is ArgumentException)&&!(ex is OutOfMemoryException))throw;string raw=target+".source";try{File.WriteAllBytes(raw,bytes);Tools.Run(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -y -i "+Tools.Q(raw)+" -frames:v 1 -c:v png -f image2 "+Tools.Q(target),token);}catch{throw new Exception("El enlace no apunta directamente a una imagen compatible");}finally{if(File.Exists(raw))File.Delete(raw);}}
  }
  static Asset ImageFromBytes(string url,string pageUrl,string cacheDir,byte[] bytes,CancellationToken token,bool force=false){
   Directory.CreateDirectory(cacheDir);string target=Path.Combine(cacheDir,Hash(url)+(force?"-revision-"+Store.Id():"")+".png");
   string svgPath=Path.Combine(cacheDir,Hash(url)+".svg");bool svg=bytes.Length>0&&ImageSignature(bytes,Math.Min(bytes.Length,8192))=="svg";
   if(force||!File.Exists(target)){
    string temp=target+".partial-"+Store.Id();
    try{SavePng(bytes,temp,token);if(svg)File.WriteAllBytes(svgPath,bytes);File.Move(temp,target);}
    finally{if(File.Exists(temp))File.Delete(temp);}
   }
   var asset=Media.Inspect(target,token);asset.name=Path.GetFileName(new Uri(url).AbsolutePath);if(String.IsNullOrWhiteSpace(asset.name))asset.name="Imagen web";
   asset.source=new MediaSource{kind="web",uri=url,page_url=String.IsNullOrWhiteSpace(pageUrl)?url:pageUrl,imported_at=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture),resolver_type="image-http"};
   var metadata=new Dictionary<string,object>{{"source_url",url},{"page_url",asset.source.page_url}};
   if(File.Exists(svgPath))metadata["svg_source_path"]=svgPath;
   asset.extensions.Add(new Extension{@namespace=MetadataNamespace,version="1",payload=metadata});
   return asset;
  }
  public static Asset DownloadImage(string url,string pageUrl,string cacheDir,CancellationToken token,bool force=false){
   Directory.CreateDirectory(cacheDir);string target=Path.Combine(cacheDir,Hash(url)+".png");
   if(!force&&File.Exists(target))return ImageFromBytes(url,pageUrl,cacheDir,new byte[0],token);
   return ImageFromBytes(url,pageUrl,cacheDir,ReadWeb(url,25*1024*1024,token),token,force);
  }
  public static string DownloadThumbnail(Asset asset,string cacheDir,CancellationToken token){
   string url=Detail(asset,"thumbnail_url");if(String.IsNullOrWhiteSpace(url))return null;
   try{Directory.CreateDirectory(cacheDir);string target=Path.Combine(cacheDir,Hash(asset.source.uri)+"-thumb.png");if(File.Exists(target))return target;byte[] bytes=ReadWeb(url,5*1024*1024,token);string temp=target+".partial-"+Store.Id();try{SavePng(bytes,temp,token);if(File.Exists(target))File.Delete(temp);else File.Move(temp,target);}finally{if(File.Exists(temp))File.Delete(temp);}return target;}catch{return null;}
  }
  public static string ThumbnailPath(Asset asset,string cacheDir){return Path.Combine(cacheDir,Hash(asset.source.uri)+"-thumb.png");}
  public static MediaRepresentation ResolveYouTube(Asset asset,string cacheDir,CancellationToken token,Action<string> status,bool lightweight=false){return ResolveVideo(asset,cacheDir,token,status,lightweight);}
  public static MediaRepresentation ResolveAudio(Asset asset,string cacheDir,CancellationToken token,Action<string> status,bool force=false){
   if(!IsAudioOnly(asset))throw new Exception("Este medio no es audio web compatible");
   string key=Hash(asset.source.uri+"|audio");var gate=downloadGates.GetOrAdd(key,_=>new SemaphoreSlim(1,1));gate.Wait(token);
   try{
    Directory.CreateDirectory(cacheDir);string final=Path.Combine(cacheDir,key+"-final.mp4");
    if(!force)foreach(string cached in Directory.GetFiles(cacheDir,key+"-final*.mp4").OrderByDescending(File.GetLastWriteTimeUtc))try{return Describe(asset,cached,token,"final");}catch{}
    string staging=Path.Combine(cacheDir,key+"-work-"+Store.Id());Directory.CreateDirectory(staging);
    try{
     if(status!=null)status("Descargando audio web…");
     string template=Path.Combine(staging,"source.%(ext)s");
     Tools.Run(YtDlp,AudioSelectionArguments(template,asset.source.uri),token,true);
     string source=Directory.GetFiles(staging,"source.*").Where(x=>!x.EndsWith(".part",StringComparison.OrdinalIgnoreCase)&&!x.EndsWith(".ytdl",StringComparison.OrdinalIgnoreCase)).OrderByDescending(x=>new FileInfo(x).Length).FirstOrDefault();
     if(source==null)throw new Exception("El enlace no entregó audio descargable");
     if(status!=null)status("Preparando audio para edición…");
     string temp=Path.Combine(staging,"normalized.mp4");
     double audioDuration=DurationProvisional(asset)?0:asset.streams.Where(st=>st.duration!=null).Select(st=>st.duration.Seconds).DefaultIfEmpty(0).Max();
     Tools.RunProgress(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -y -threads 2 -filter_threads 2 -progress pipe:1 -nostats -i "+Tools.Q(source)+" -vn -map 0:a:0 -c:a aac -profile:a aac_low -b:a 192k -ar 48000 -ac 2 -movflags +faststart "+Tools.Q(temp),audioDuration,token,percent=>{if(status!=null)status("Preparando audio para edición · "+percent+" %");},true);
     var representation=Describe(asset,temp,token,"final");
     if(force)final=Path.Combine(cacheDir,key+"-final-revision-"+Store.Id()+".mp4");
     if(File.Exists(final))File.Replace(temp,final,null);else File.Move(temp,final);
     representation.locator.path=final;representation.size_bytes=new FileInfo(final).Length;representation.modified_utc=new FileInfo(final).LastWriteTimeUtc.ToString("o",CultureInfo.InvariantCulture);
     if(status!=null)status("Audio web listo");return representation;
    }finally{try{string parent=Path.GetFullPath(cacheDir).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;string child=Path.GetFullPath(staging);if(child.StartsWith(parent,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(child))Directory.Delete(child,true);}catch{}}
   }finally{gate.Release();}
  }
  public static MediaRepresentation ResolveAudioTrack(Asset asset,string cacheDir,CancellationToken token,Action<string> status,bool force=false){
   var audio=asset==null?null:asset.streams.FirstOrDefault(s=>s.kind=="audio");
   if(audio==null||asset.source==null||asset.source.kind!="web")throw new Exception("El medio web no contiene audio exportable");
   var temporary=new Asset{name=asset.name,source=new MediaSource{kind="web",uri=asset.source.uri,page_url=asset.source.page_url,resolver_type="yt-dlp-audio/"+Platform(asset.source.uri)},streams=new List<StreamInfo>{audio},extensions=asset.extensions};
   return ResolveAudio(temporary,cacheDir,token,status,force);
  }
  public static MediaRepresentation PreviewFromFull(Asset asset,string cacheDir,CancellationToken token,Action<string> status,bool force=false){
   if(asset==null||asset.source==null||asset.source.kind!="web")throw new Exception("El medio no es web");
   var original=asset.representations.FirstOrDefault(r=>(r.role=="final"||r.role=="original")&&r.locator!=null&&File.Exists(r.locator.path));
   if(original==null)throw new Exception("Descarga el archivo completo antes de regenerar su vista ligera");
   int height=PreviewHeight;Directory.CreateDirectory(cacheDir);string key=Hash(asset.source.uri);
   string destination=Path.Combine(cacheDir,key+"-preview-"+height+"-full-"+Store.Id()+".mp4");
   string temp=destination+".partial.mp4";
   try{
    if(status!=null)status("Generando vista ligera desde el archivo completo…");
    double duration=asset.streams.Where(s=>s.duration!=null).Select(s=>s.duration.Seconds).DefaultIfEmpty(0).Max();
    Tools.RunProgress(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -y -threads 2 -filter_threads 2 -progress pipe:1 -nostats -i "+Tools.Q(original.locator.path)+" -map 0:v:0 -map 0:a:0? -vf "+Tools.Q(PreviewScale(height))+" -c:v libx264 -threads 2 -preset veryfast -crf "+PreviewCrf(height)+" -pix_fmt yuv420p -r 60 -c:a aac -profile:a aac_low -b:a 128k -ar 48000 -ac 2 -movflags +faststart "+Tools.Q(temp),duration,token,null,true);
    var rep=Describe(asset,temp,token,"preview");File.Move(temp,destination);rep.locator.path=destination;rep.size_bytes=new FileInfo(destination).Length;rep.modified_utc=new FileInfo(destination).LastWriteTimeUtc.ToString("o",CultureInfo.InvariantCulture);
    if(status!=null)status("Vista ligera "+height+"p conectada al archivo completo");return rep;
   }finally{if(File.Exists(temp))File.Delete(temp);}
  }
  public static MediaRepresentation ResolveVideo(Asset asset,string cacheDir,CancellationToken token,Action<string> status,bool lightweight=false,bool force=false){
   if(asset==null||asset.source==null||asset.source.kind!="web"||(asset.source.resolver_type!="youtube/yt-dlp"&&(asset.source.resolver_type==null||!asset.source.resolver_type.StartsWith("yt-dlp/",StringComparison.Ordinal))))throw new Exception("Este medio no es un vídeo web compatible");
   string key=Hash(asset.source.uri);
   var gate=downloadGates.GetOrAdd(key,_=>new SemaphoreSlim(1,1));gate.Wait(token);
   try{
   Directory.CreateDirectory(cacheDir);int previewHeight=PreviewHeight;string final=Path.Combine(cacheDir,key+"-preview-"+previewHeight+".mp4");
   if(lightweight&&!force&&File.Exists(final))try{return Describe(asset,final,token,"preview");}catch{File.Delete(final);}
   if(lightweight&&asset.representations.Any(r=>(r.role=="final"||r.role=="original")&&r.locator!=null&&File.Exists(r.locator.path)))return PreviewFromFull(asset,cacheDir,token,status,force);
   if(!lightweight&&!force)foreach(string cached in Directory.GetFiles(cacheDir,key+"-master*").OrderByDescending(File.GetLastWriteTimeUtc)){try{return Describe(asset,cached,token,"final");}catch{}}
   string staging=Path.Combine(cacheDir,key+"-work-"+Store.Id());Directory.CreateDirectory(staging);
   try{
    if(status!=null)status(lightweight?"Descargando vista ligera…":"Descargando vídeo web…");
    string template=Path.Combine(staging,"source.%(ext)s");
    string selection=lightweight?" -S "+Tools.Q("res:"+previewHeight+",vcodec:avc,acodec:m4a"):" -f "+Tools.Q("bv*+ba/b")+" --merge-output-format mkv";
    Tools.Run(YtDlp,"--ignore-config "+CookieArguments()+JsRuntimeArguments()+" --no-playlist --no-warnings --no-progress --socket-timeout 12 --retries 1 --extractor-retries 1 --ffmpeg-location "+Tools.Q(Path.GetDirectoryName(Tools.FFmpeg))+selection+" -o "+Tools.Q(template)+" "+Tools.Q(asset.source.uri),token,true);
    var source=Directory.GetFiles(staging,"source.*").Where(x=>!x.EndsWith(".part",StringComparison.OrdinalIgnoreCase)&&!x.EndsWith(".ytdl",StringComparison.OrdinalIgnoreCase)).OrderByDescending(x=>new FileInfo(x).Length).FirstOrDefault();
    if(source==null)throw new Exception("El enlace no entregó un archivo descargable");
    if(!lightweight){var master=Describe(asset,source,token,"final");string destination=Path.Combine(cacheDir,key+"-master"+(force?"-revision-"+Store.Id():"")+Path.GetExtension(source));File.Move(source,destination);master.locator.path=destination;master.size_bytes=new FileInfo(destination).Length;master.modified_utc=new FileInfo(destination).LastWriteTimeUtc.ToString("o",CultureInfo.InvariantCulture);if(status!=null)status("Fuente web completa conservada para exportación");return master;}
    if(status!=null)status("Preparando formato de edición…");
    string temp=Path.Combine(staging,"normalized.mp4");
    double videoDuration=DurationProvisional(asset)?0:asset.streams.Where(st=>st.duration!=null).Select(st=>st.duration.Seconds).DefaultIfEmpty(0).Max();
    Tools.RunProgress(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -y -threads 2 -filter_threads 2 -progress pipe:1 -nostats -i "+Tools.Q(source)+" -map 0:v:0 -map 0:a:0? -vf "+Tools.Q(lightweight?PreviewScale(previewHeight):"scale=1920:1080:force_original_aspect_ratio=decrease:force_divisible_by=2")+" -c:v libx264 -threads 2 -preset "+(lightweight?"ultrafast":"veryfast")+" -crf "+(lightweight?PreviewCrf(previewHeight):21)+" -pix_fmt yuv420p -r 60 -c:a aac -profile:a aac_low -b:a "+(lightweight?"96k":"192k")+" -ar 48000 -ac 2 -movflags +faststart "+Tools.Q(temp),videoDuration,token,percent=>{if(status!=null)status("Preparando formato de edición · "+percent+" %");},true);
    var representation=Describe(asset,temp,token,lightweight?"preview":"final");
    if(File.Exists(final))File.Replace(temp,final,null);else File.Move(temp,final);
    representation.locator.path=final;representation.size_bytes=new FileInfo(final).Length;representation.modified_utc=new FileInfo(final).LastWriteTimeUtc.ToString("o",CultureInfo.InvariantCulture);
    if(status!=null)status("Vídeo web listo");return representation;
   }finally{try{string parent=Path.GetFullPath(cacheDir).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;string child=Path.GetFullPath(staging);if(child.StartsWith(parent,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(child))Directory.Delete(child,true);}catch{}}
   }finally{gate.Release();}
  }
  static MediaRepresentation Describe(Asset virtualAsset,string file,CancellationToken token,string role="final"){
   var downloaded=Media.InspectResolved(file,token);var map=new Dictionary<string,int>();
   foreach(var stream in virtualAsset.streams){var found=downloaded.streams.FirstOrDefault(x=>x.kind==stream.kind);if(found==null)throw new Exception("El vídeo descargado no conserva "+stream.kind);if(!DurationProvisional(virtualAsset)&&stream.duration!=null&&found.duration!=null&&Math.Abs(found.duration.Seconds-stream.duration.Seconds)>1.0)throw new Exception("La duración descargada no coincide con la del enlace");map[stream.id]=found.ordinal;}
   var info=new FileInfo(file);
   return new MediaRepresentation{role=role,locator=new Locator{path=info.FullName},size_bytes=info.Length,modified_utc=info.LastWriteTimeUtc.ToString("o",CultureInfo.InvariantCulture),stream_ordinals=map};
  }
 }
}

