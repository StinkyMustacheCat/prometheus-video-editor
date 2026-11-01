using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;

namespace Prometheus {
 public static partial class Media {
  static bool HasVideoAlpha(Asset asset){var video=asset.streams.FirstOrDefault(x=>x.kind=="video");string pixel=video==null?"":video.pixel_format??"";return pixel.StartsWith("yuva",StringComparison.OrdinalIgnoreCase)||pixel.StartsWith("gbrap",StringComparison.OrdinalIgnoreCase)||new[]{"rgba","bgra","argb","abgr","pal8"}.Contains(pixel);}
  public static MediaRepresentation GenerateProxy(Asset asset,string cacheFolder,CancellationToken token,Action<double> progress){
   if(asset==null||asset.source==null||asset.source.kind!="local"||!asset.streams.Any(x=>x.kind=="video"))throw new Exception("El proxy inicial requiere un video local");
   Store.NormalizeAsset(asset);
   var original=asset.representations.FirstOrDefault(x=>x.role=="original"&&x.locator!=null&&File.Exists(x.locator.path));
   if(original==null)throw new Exception("No se encontró el video original para generar el proxy");
   Verify(asset,token,false);
   Directory.CreateDirectory(cacheFolder);
   string fingerprint=asset.id+"-"+new FileInfo(original.locator.path).Length+"-"+new FileInfo(original.locator.path).LastWriteTimeUtc.Ticks;
   bool alpha=HasVideoAlpha(asset);string target=Path.Combine(cacheFolder,fingerprint+(alpha?"-p1-alpha.mov":"-p1.mp4"));
   if(File.Exists(target))try{return DescribeProxy(asset,target,token);}catch{File.Delete(target);}
   string temp=Path.Combine(cacheFolder,fingerprint+"-partial-"+Store.Id()+(alpha?".mov":".mp4"));
   try{
    if(progress!=null)progress(0);
    string videoCodec=alpha?" -vf scale=640:360:force_original_aspect_ratio=decrease:force_divisible_by=2,format=argb -c:v qtrle -pix_fmt argb":" -vf scale=640:360:force_original_aspect_ratio=decrease:force_divisible_by=2 -c:v libx264 -preset ultrafast -crf 30 -pix_fmt yuv420p";
    string args="-hide_banner -loglevel error -nostdin -y -progress pipe:1 -copyts -start_at_zero -i "+Tools.Q(original.locator.path)+" -map 0:v:0 -map 0:a:0?"+videoCodec+" -fps_mode:v passthrough -c:a aac -profile:a aac_low -b:a 96k -ar 48000 -ac 2 -movflags +faststart "+Tools.Q(temp);
    using(var process=Tools.Start(Tools.FFmpeg,args,true)){
     string error="";process.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)error+=e.Data+"\n";};process.BeginErrorReadLine();
     using(token.Register(()=>{try{process.Kill();}catch{}})){
      string line;while((line=process.StandardOutput.ReadLine())!=null){token.ThrowIfCancellationRequested();if(line.StartsWith("out_time_us=")){long us;if(Int64.TryParse(line.Substring(12),out us)&&progress!=null){double duration=asset.streams.Where(x=>x.kind=="video").Max(x=>x.duration.Seconds);progress(Math.Max(0,Math.Min(99,us/1000000.0/duration*100)));}}}
      process.WaitForExit();token.ThrowIfCancellationRequested();if(process.ExitCode!=0)throw new Exception("No se pudo generar el proxy: "+(error.Length>1600?error.Substring(error.Length-1600):error));
     }
    }
    var rep=DescribeProxy(asset,temp,token);
    if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);
    rep.locator.path=target;rep.size_bytes=new FileInfo(target).Length;rep.modified_utc=new FileInfo(target).LastWriteTimeUtc.ToString("o");
    if(progress!=null)progress(100);return rep;
   }finally{if(File.Exists(temp))File.Delete(temp);}
  }
  static bool VideoFrameTimesMatch(string original,string proxy,CancellationToken token){
   Func<string,double[]> times=path=>{
    string data=Tools.Run(Tools.FFprobe,"-v error -select_streams v:0 -show_entries packet=pts_time -of csv=p=0 "+Tools.Q(path),token,true);
    var lines=data.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);var result=new double[lines.Length];
    for(int i=0;i<lines.Length;i++)if(!Double.TryParse(lines[i],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out result[i]))return new double[0];
    Array.Sort(result);return result;
   };
   var source=times(original);var converted=times(proxy);
   if(source.Length==0||source.Length!=converted.Length)return false;
   for(int i=0;i<source.Length;i++)if(Math.Abs(source[i]-converted[i])>1.0/120)return false;
   return true;
  }
  static MediaRepresentation DescribeProxy(Asset asset,string path,CancellationToken token){
   var proxy=Inspect(path,token);
   if(proxy.streams.Count!=asset.streams.Count)throw new Exception("El proxy no conserva los streams del original");
   var map=new Dictionary<string,int>();
   foreach(var original in asset.streams){
    var match=proxy.streams.FirstOrDefault(x=>x.kind==original.kind);
    if(match==null||map.ContainsValue(match.ordinal))throw new Exception("El proxy no conserva video y audio");
    if(original.duration!=null&&match.duration!=null&&Math.Abs(original.duration.Seconds-match.duration.Seconds)>0.06){
     string source=asset.representations.Where(r=>r.role=="original"&&r.locator!=null).Select(r=>r.locator.path).FirstOrDefault();
     if(original.kind!="video"||match.duration.Seconds<original.duration.Seconds-0.06||String.IsNullOrWhiteSpace(source)||!VideoFrameTimesMatch(source,path,token))throw new Exception("La duración del proxy no coincide con el original");
    }
    double originalOffset=original.pts_origin.Seconds-asset.presentation_origin.Seconds;
    double proxyOffset=match.pts_origin.Seconds-proxy.presentation_origin.Seconds;
    if(Math.Abs(originalOffset-proxyOffset)>0.06)throw new Exception("El proxy no conserva la sincronía de streams");
    map[original.id]=match.ordinal;
   }
   var file=new FileInfo(path);
   return new MediaRepresentation{role="proxy",locator=new Locator{path=file.FullName,relative=false},size_bytes=file.Length,modified_utc=file.LastWriteTimeUtc.ToString("o"),stream_ordinals=map};
  }
 }
}
