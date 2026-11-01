using System;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class MediaPanelTests038 {
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
 static void Main(){
  string bundle=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","..","dev-036","full-smoke-final","Prometheus-0.3.0-win-x64"));
  Tools.Root=bundle;MediaThumbnail.CacheRoot=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"thumbnail-test-038");
  string video=Path.Combine(bundle,"demo","video.mp4"),audio=Path.Combine(bundle,"demo","voz.wav"),image=Path.Combine(bundle,"demo","grafico.png");
  Check(MediaDropCompatibility.CanPlace(new[]{video},"video"),"video local puede ir a pista visual");
  Check(MediaDropCompatibility.CanPlace(new[]{audio},"audio"),"WAV puede ir a pista de audio");
  Check(MediaDropCompatibility.CanPlace(new[]{image},"video"),"PNG puede ir a pista visual");
  Check(!MediaDropCompatibility.CanPlace(new[]{audio},"video")&&!MediaDropCompatibility.CanPlace(new[]{image},"audio"),"pistas incompatibles rechazan el arrastre");
  var asset=Media.Inspect(video,CancellationToken.None);string key=MediaThumbnail.Key(asset);
  using(var thumb=MediaThumbnail.Create(asset,key))Check(thumb.Width==120&&thumb.Height==68,"miniatura de vídeo se genera al tamaño esperado");
  Check(Directory.GetFiles(MediaThumbnail.CacheRoot,"*.png").Length>0,"miniatura se guarda en caché");
  using(var cached=MediaThumbnail.Create(asset,key))Check(cached.Width==120,"miniatura se reutiliza desde caché");
 }
}
