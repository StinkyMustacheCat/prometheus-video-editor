using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class GeneratedTests034 {
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Main(){
  Tools.Root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","..","dev-030","package","Prometheus-0.2.0-preview-win-x64"));
  string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"generated-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   Rendering.GeneratedCacheRoot=Path.Combine(root,"cache");
   var project=Store.New();string track=project.Active.tracks.First(t=>t.kind=="video").id;
   var solid=GeneratedMedia.CreateSolid("Fondo naranja","FF8844",2);
   var placeholder=GeneratedMedia.CreatePlaceholder("B-roll del castillo","Buscar plano nocturno",3);
   project.assets.Add(solid);project.assets.Add(placeholder);Media.Insert(project,solid.id,track,0);Media.Insert(project,placeholder.id,track,2);
   Check(project.Active.clips.Count==2&&Math.Abs(project.Active.clips[1].duration.Seconds-3)<0.00001,"la duración del espacio reservado llega a la timeline");
   string save=Path.Combine(root,"generated.prometheus.json");Store.Save(project,save);project=Store.Load(save);
   Check(project.assets.Any(GeneratedMedia.IsPlaceholder)&&project.assets.Any(a=>GeneratedMedia.Kind(a)=="solid"),"generadores sobreviven a guardar y reabrir");
   using(var frame=Rendering.Frame(project,0.5,CancellationToken.None)){Color pixel=frame.GetPixel(320,180);Check(pixel.R>210&&pixel.G>80&&pixel.B<120,"color sólido aparece en monitor");}
   using(var frame=Rendering.Frame(project,2.5,CancellationToken.None)){Color pixel=frame.GetPixel(320,180);Check(pixel.R>30&&pixel.G>35,"placeholder aparece en monitor");frame.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"placeholder-034.png"));}
   bool blocked=false;try{Rendering.Build(project,true,true,640,360,60,Path.Combine(root,"out.mp4"),0);}catch(Exception ex){blocked=ex.Message.Contains("espacios reservados");}
   Check(blocked,"exportación de vídeo evita un placeholder pendiente");
   project.Active.clips.RemoveAll(c=>c.asset_id==placeholder.id);Store.Validate(project);
   string output=Path.Combine(root,"solid.mp4");Rendering.Export(project,output,CancellationToken.None);
   Check(File.Exists(output)&&new FileInfo(output).Length>0,"color sólido se exporta como vídeo");
  }finally{try{if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory),StringComparison.OrdinalIgnoreCase))Directory.Delete(root,true);}catch{}}
 }
}
