using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Prometheus;

class AudioWebTests0314 {
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static string Digest(string value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant().Substring(0,24);}
 static void Main(string[] args){
  Tools.Root=Path.GetFullPath(args[0]);string url="https://www.youtube.com/watch?v=abc123";
  string json="{\"id\":\"abc123\",\"title\":\"Audio de prueba\",\"duration\":2,\"vcodec\":\"h264\",\"acodec\":\"aac\",\"width\":320,\"height\":180}";
  var asset=WebMedia.FromMetadataAudio(url,json);
  Check(WebMedia.IsAudioOnly(asset)&&asset.streams.Count==1&&asset.streams[0].kind=="audio","el enlace crea un medio virtual solo de audio");
  string command=WebMedia.AudioSelectionArguments("source.%(ext)s",url);
  Check(command.Contains("-f bestaudio")&&!command.Contains("bestvideo"),"yt-dlp selecciona bestaudio sin descargar vídeo");
  var p=Store.New();p.assets.Add(asset);var track=p.Active.tracks.First(t=>t.kind=="audio");
  var placed=Media.PlaceInFreeTrack(p,asset.id,track.id,0);Store.Validate(p);
  Check(placed.ClipIds.Count==1&&p.Active.clips.Single().track_id==track.id,"audio virtual se inserta en pista compatible antes de descargarse");
  string cache=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"audio-web-cache-0314");Directory.CreateDirectory(cache);
  string final=Path.Combine(cache,Digest(url+"|audio")+"-final.mp4");
  Tools.Run(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -y -i "+Tools.Q(Path.Combine(Tools.Root,"demo","voz.wav"))+" -vn -c:a aac -b:a 192k -ar 48000 -ac 2 "+Tools.Q(final),CancellationToken.None);
  var rep=WebMedia.ResolveAudio(asset,cache,CancellationToken.None,null);
  Check(rep.role=="final"&&rep.locator.path==final&&rep.stream_ordinals[asset.streams[0].id]==0,"el audio final se recupera de caché y mapea su pista");
  var copy=WebMedia.FromMetadataAudio(url,json);var reused=WebMedia.ResolveAudio(copy,cache,CancellationToken.None,null);
  Check(reused.locator.path==final,"otro asset del mismo enlace reutiliza el archivo local");
  asset.representations.Add(rep);string exported=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"audio-web-0314.wav");
  Rendering.Export(p,exported,CancellationToken.None);
  Check(File.Exists(exported)&&new FileInfo(exported).Length>1000,"medio web de audio resuelto se exporta correctamente");
 }
}
