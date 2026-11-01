using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class EffectRenderTests0452 {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static bool Red(Color c){return c.R>185&&c.G<70&&c.B<70;}
 static void Main(){
  Tools.Root=AppDomain.CurrentDomain.BaseDirectory;
  string directory=Path.Combine(Tools.Root,"effects-render-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(directory);
  try{
   string lut=Path.Combine(directory,"O'Brien test.cube");
   using(var writer=new StreamWriter(lut)){
    writer.WriteLine("TITLE \"Constant red\"");writer.WriteLine("LUT_3D_SIZE 2");
    for(int i=0;i<8;i++)writer.WriteLine("1 0 0");
   }
   var project=Store.New();project.Active.width=320;project.Active.height=320;
   var asset=GeneratedMedia.CreateSolid("Gris","808080",1);project.assets.Add(asset);
   Media.Insert(project,asset.id,project.Active.tracks.First(t=>t.kind=="video").id,0);
   var resource=new EffectResource{id=Store.Id(),kind="lut3d",uri=lut};project.effect_resources.Add(resource);
   project.Active.clips.Single().effects.Add(EffectContracts.NewLut3D(resource.id));
   string graph=Rendering.Build(project,true,false,320,320,60,"raw",0,true);
   Check(graph.Contains("lut3d=file=")&&graph.Contains("interp=tetrahedral"),"el adaptador incorpora LUT al grafo de previsualización");
   using(var frame=Rendering.Frame(project,0.5,CancellationToken.None,320,320))
    Check(Red(frame.GetPixel(160,160)),"la previsualización aplica el LUT incluso con espacios y apóstrofo en la ruta");
   string output=Path.Combine(directory,"resultado.mp4");
   Rendering.Export(project,output,CancellationToken.None);
   string image=Path.Combine(directory,"exportado.png");
   Tools.Run(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -y -i "+Tools.Q(output)+" -frames:v 1 "+Tools.Q(image),CancellationToken.None);
   using(var rendered=new Bitmap(image))Check(Red(rendered.GetPixel(160,160)),"la exportación coincide con la previsualización");
  }finally{Directory.Delete(directory,true);}
 }
}
