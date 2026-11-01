using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using Prometheus;

class PreviewTests027 {
 static int count;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);count++;}
 static void Main(string[] args){
  Tools.Root=Path.GetFullPath(args[0]);var p=Store.Load(Path.Combine(Tools.Root,"demo","Demostracion.prometheus.json"));
  var w=Rendering.PreviewWindow(p,3.5,1.0);
  Check(w.Active.clips.All(c=>c.timeline_start.Seconds>=0&&c.End<=1.00001),"ventana limita clips al rango visible");
  Check(w.Active.clips.Count<p.Active.clips.Count,"omite clips anteriores al cursor");
  Store.Validate(w);Check(true,"ventana conserva un proyecto válido");
  string command=Rendering.Build(w,true,false,640,360,15,"raw",0,true);
  Check(command.Contains("-ss "),"vista previa busca dentro de medios recortados antes de decodificar");
  using(var image=Rendering.Frame(p,3.5,CancellationToken.None))Check(image.Width==640&&image.Height==360,"fotograma aleatorio se genera desde la ventana");
  using(var stop=new CancellationTokenSource()){
   bool first=false;var clock=Stopwatch.StartNew();
   var statuses=new System.Collections.Generic.List<string>();
   try{Rendering.Play(p,0,Tools.Root,stop.Token,(image,time)=>{image.Dispose();first=true;stop.Cancel();},message=>statuses.Add(message),15,320,180);}catch(OperationCanceledException){}
   Check(first,"reproducción con audio entrega el primer fotograma");
   Console.WriteLine("Ruta de audio: "+(statuses.Any(x=>x.Contains("alternativa"))?"alternativa":"streaming"));
   Console.WriteLine("Inicio de reproducción: "+clock.ElapsedMilliseconds+" ms");
  }
  int frames=0;Rendering.Play(p,0,Tools.Root,CancellationToken.None,(image,time)=>{frames++;image.Dispose();},message=>{},15,320,180);
  Check(frames>=40,"audio y video completan la reproducción sin detenerse antes");
  Console.WriteLine("PASS TOTAL "+count);
 }
}
