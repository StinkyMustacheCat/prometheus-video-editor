using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class EffectResourceTests0453 {
 static void Check(bool yes,string label){if(!yes)throw new Exception(label);Console.WriteLine("PASS "+label);}
 static void Main(){
  Tools.Root=AppDomain.CurrentDomain.BaseDirectory;
  string directory=Path.Combine(Tools.Root,"effect-resource-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(directory);
  try{
   string original=Path.Combine(directory,"original.cube");
   using(var writer=new StreamWriter(original)){
    writer.WriteLine("LUT_3D_SIZE 2");for(int i=0;i<8;i++)writer.WriteLine("1 0 0");
   }
   var p=Store.New();p.Active.width=320;p.Active.height=320;
   var asset=GeneratedMedia.CreateSolid("Fondo","808080",1);p.assets.Add(asset);
   Media.Insert(p,asset.id,p.Active.tracks.First(t=>t.kind=="video").id,0);
   var resource=new EffectResource{id=Store.Id(),kind="lut3d",uri=original};p.effect_resources.Add(resource);
   p.Active.clips.Single().effects.Add(EffectContracts.NewLut3D(resource.id));
   string projectFile=Path.Combine(directory,"Montaje.prometheus.json");
   EffectResourceStorage.Materialize(p,projectFile);
   Check(File.Exists(resource.uri)&&resource.uri.StartsWith(ProjectStorage.LutDirectory(projectFile),StringComparison.OrdinalIgnoreCase),
    "guardar incorpora una copia del LUT a la carpeta del proyecto");
   Store.Save(p,projectFile);File.Delete(original);
   var restored=Store.Load(projectFile);
   Check(File.Exists(restored.effect_resources.Single().uri),"el proyecto reabre sin depender del LUT externo");
   using(var frame=Rendering.Frame(restored,0.5,CancellationToken.None,320,320)){
    Color pixel=frame.GetPixel(160,160);Check(pixel.R>180&&pixel.G<70&&pixel.B<70,"el LUT empaquetado sigue aplicado en previsualización");
   }
   var ready=ExportPreflight.Inspect(restored,true,0);EffectRenderPreflight.Inspect(restored,ready,true);
   Check(ready.CanExport,"preflight acepta el recurso disponible");
   string stored=restored.effect_resources.Single().uri;File.Delete(stored);
   var missing=ExportPreflight.Inspect(restored,true,0);
   Check(!missing.CanExport&&missing.checks.Any(c=>c.code=="lut-missing"&&c.resource_id==resource.id),
    "preflight detecta LUT ausente antes del render");
   File.WriteAllText(stored,"archivo LUT dañado");
   var invalid=ExportPreflight.Inspect(restored,true,0);EffectRenderPreflight.Inspect(restored,invalid,true);
   Check(!invalid.CanExport&&invalid.checks.Any(c=>c.code=="lut-invalid"),"preflight detecta LUT ilegible con FFmpeg");
   restored.Active.clips.Single().effects.Single().effect_id="motor.desconocido";
   var unsupported=ExportPreflight.Inspect(restored,true,0);
   Check(!unsupported.CanExport&&unsupported.checks.Any(c=>c.code=="effect-unsupported"),
    "preflight no omite contratos desconocidos");
  }finally{Directory.Delete(directory,true);}
 }
}
