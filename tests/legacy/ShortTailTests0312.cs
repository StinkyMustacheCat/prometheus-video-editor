using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class ShortTailTests0312 {
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Main(string[] args){
  Tools.Root=Path.GetFullPath(args[0]);
  var p=Store.New();var reserved=GeneratedMedia.CreatePlaceholder("Intro","",5);
  var shortVideo=Media.Inspect(Path.Combine(Tools.Root,"demo","video.mp4"),CancellationToken.None);
  p.assets.Add(reserved);p.assets.Add(shortVideo);
  string track=p.Active.tracks.First(t=>t.kind=="video"&&t.order==0).id;
  Media.Insert(p,reserved.id,track,0);
  var clip=p.Active.clips.Single();
  PlaceholderReplacement.Replace(p,clip.id,shortVideo.id,0);
  Check(ShortTail.Has(clip)&&Math.Abs(clip.duration.Seconds-5)<0.000001,"medio corto conserva los cinco segundos editoriales");
  Check(Math.Abs(ShortTail.MissingStart(p,clip).Value-2)<0.000001,"el tramo faltante empieza al terminar la fuente");
  Store.Validate(p);var report=ExportPreflight.Inspect(p,true,0);
  Check(report.CanExport&&report.checks.Any(c=>c.code=="short-tail"&&Math.Abs(c.timeline_seconds.Value-2)<0.000001),"preflight advierte, pero permite exportar");
  using(var frame=Rendering.Frame(p,3,CancellationToken.None)){
   Color pixel=frame.GetPixel(320,180);Check(pixel.R<5&&pixel.G<5&&pixel.B<5,"monitor muestra negro durante el tramo sin fuente");
  }
  string rendered=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"short-tail-0312.mp4");
  Rendering.Export(p,rendered,CancellationToken.None);
  Check(File.Exists(rendered)&&new FileInfo(rendered).Length>1000,"la exportación con tramo faltante termina correctamente");
  string finalFrame=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"short-tail-final-0312.png");
  Tools.Run(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -y -ss 3.5 -i "+Tools.Q(rendered)+" -frames:v 1 -vf scale=16:9 "+Tools.Q(finalFrame),CancellationToken.None);
  using(var image=new Bitmap(finalFrame)){Color pixel=image.GetPixel(8,4);Check(pixel.R<10&&pixel.G<10&&pixel.B<10,"el tramo faltante también sale negro en el archivo exportado");}
  string saved=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"short-tail-0312.prometheus.json");Store.Save(p,saved);
  var loaded=Store.Load(saved);Check(ShortTail.MissingStart(loaded,loaded.Active.clips.Single()).HasValue,"el tramo rayado persiste al guardar y abrir");
  var editor=new Editor();editor.Reset(p);editor.Trim(clip.id,false,2);
  var trimmed=editor.Project.Active.clips.Single();
  Check(!ShortTail.Has(trimmed)&&!ShortTail.MissingStart(editor.Project,trimmed).HasValue,"al recortar la cola desaparecen las franjas");
  editor.Trim(clip.id,false,5);
  Check(Math.Abs(editor.Project.Active.clips.Single().End-2)<0.000001,"tras quitar la cola no se puede reextender más allá de la fuente");
  editor.Undo();Check(ShortTail.Has(editor.Project.Active.clips.Single()),"deshacer recupera el tramo faltante");
 }
}
