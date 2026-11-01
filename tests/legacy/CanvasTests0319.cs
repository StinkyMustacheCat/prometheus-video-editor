using System;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class CanvasTests0319 {
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Main(string[] args){
  Tools.Root=Path.GetFullPath(args[0]);
  var project=Store.New();SequenceCanvas.Set(project.Active,360,640);
  var graphic=Media.Inspect(Path.Combine(Tools.Root,"demo","grafico.png"),CancellationToken.None);project.assets.Add(graphic);
  Media.Insert(project,graphic.id,project.Active.tracks.First(t=>t.kind=="video").id,0);
  var clip=project.Active.clips.First(c=>c.asset_id==graphic.id&&project.Active.tracks.Any(t=>t.id==c.track_id&&t.kind=="video"));
  Store.Validate(project);
  Check(project.Active.width==360&&project.Active.height==640,"lienzo vertical guardado en proyecto");
  var size=SequenceCanvas.PreviewSize(project.Active,320);Check(size[0]==180&&size[1]==320,"vista previa vertical conserva proporción");
  ClipFraming.Set(clip,"fill",1.25,0.1,-0.1);Store.Validate(project);
  var loaded=Store.Decode(Store.Encode(project));
  var frame=ClipFraming.Read(loaded.Active.clips.First());
  Check(frame.mode=="fill"&&Math.Abs(frame.scale-1.25)<0.00001&&Math.Abs(frame.x-0.1)<0.00001,"encuadre conserva modo, escala y posición al guardar");
  string command=Rendering.Build(loaded,true,false,180,320,30,"raw",0,true);
  Check(command.Contains("force_original_aspect_ratio=increase")&&command.Contains("overlay=x='"),"grafo de vídeo aplica relleno y posición");
  using(var bitmap=Rendering.Frame(loaded,0,CancellationToken.None,180,320))Check(bitmap.Width==180&&bitmap.Height==320,"fotograma real tiene proporción vertical");
  loaded.Active.clips.First().duration=Rational.Frame(1);
  string output=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"prometheus-canvas-"+Guid.NewGuid().ToString("N")+".mp4");
  try{Rendering.Export(loaded,output,CancellationToken.None);var exported=Media.Inspect(output,CancellationToken.None);var video=exported.streams.First(s=>s.kind=="video");Check(video.width==360&&video.height==640,"exportación MP4 respeta lienzo vertical");}
  finally{if(File.Exists(output))File.Delete(output);}
  bool invalid=false;try{SequenceCanvas.Set(project.Active,361,640);}catch{invalid=true;}
  Check(invalid,"dimensiones impares rechazadas");
 }
}
