using System;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class ExportRangeTests0321 {
 static void Check(bool yes,string name){if(!yes)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Main(string[] args){
  Tools.Root=Path.GetFullPath(args[0]);var project=Store.New();
  var image=Media.Inspect(Path.Combine(Tools.Root,"demo","grafico.png"),CancellationToken.None);project.assets.Add(image);
  Media.Insert(project,image.id,project.Active.tracks.First(t=>t.kind=="video").id,0);
  project.Active.clips.First().duration=Rational.Frame(2);
  var placeholder=GeneratedMedia.CreatePlaceholder("Fuera del rango","",1);project.assets.Add(placeholder);Media.Insert(project,placeholder.id,project.Active.tracks.First(t=>t.kind=="video").id,4);
  SequenceExportRange.SetIn(project.Active,1);SequenceExportRange.SetOut(project.Active,3);
  var marks=SequenceExportRange.Read(Store.Copy(project).Active);
  Check(marks.Complete&&marks.InFrame==60&&marks.OutFrame==180,"marcas I/O persisten en el proyecto");
  var window=SequenceExportRange.Window(project,60,180);
  Check(window.Active.clips.Count==1&&Math.Abs(window.Active.clips[0].duration.Seconds-1)<0.0001,"ventana excluye clip externo y recorta el visible");
  Check(ExportPreflight.Inspect(window,true,0).CanExport,"preflight considera solo el intervalo exportado");
  var options=ExportOptions.For(project);options.Width=640;options.Height=360;options.StartFrame=60;options.EndFrame=180;options.Validate(project);
  var renderOptions=options.ForWindow(2);string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"range-"+Guid.NewGuid().ToString("N")+".mp4");
  try{Rendering.Export(window,file,CancellationToken.None,null,renderOptions);var actual=Media.Inspect(file,CancellationToken.None).streams.First(s=>s.kind=="video").duration.Seconds;Check(Math.Abs(actual-2)<0.12,"render conserva el hueco final y dura exactamente el rango");}
  finally{if(File.Exists(file))File.Delete(file);}
  SequenceExportRange.Clear(project.Active);Check(!SequenceExportRange.Read(project.Active).Complete,"marcas se pueden borrar");
 }
}
