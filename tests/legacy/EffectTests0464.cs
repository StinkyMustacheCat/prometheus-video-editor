using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using Prometheus;

class EffectTests0464 {
 static void Check(bool condition,string text){if(!condition)throw new Exception(text);Console.WriteLine("PASS "+text);}
 static bool Red(Color color){return color.R>150&&color.B<100;}
 static bool Blue(Color color){return color.B>150&&color.R<100;}
 static void Main(){
  Tools.Root=AppDomain.CurrentDomain.BaseDirectory;
  var old=Store.New();old.schema_version="0.3.0";
  var raw=(Dictionary<string,object>)Store.Json().DeserializeObject(Store.Encode(old));
  foreach(var sequence in (object[])raw["sequences"]){var seq=(Dictionary<string,object>)sequence;
   foreach(var track in (object[])seq["tracks"]){var item=(Dictionary<string,object>)track;item.Remove("effects");item.Remove("effects_enabled");}
   foreach(var clip in (object[])seq["clips"])((Dictionary<string,object>)clip).Remove("inherit_track_effects");
  }
  var migrated=Store.Decode(Store.Json().Serialize(raw));
  Check(migrated.schema_version=="0.4.0"&&migrated.Active.tracks.All(t=>t.effects_enabled&&t.effects.Count==0),"proyecto 0.4.5.3 migra con efectos de pista activos por defecto");
  string directory=Path.Combine(Tools.Root,"effects-0464-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
  try{
   string image=Path.Combine(directory,"dos-colores.png");using(var bitmap=new Bitmap(64,64)){for(int y=0;y<64;y++)for(int x=0;x<64;x++)bitmap.SetPixel(x,y,x<32?Color.Red:Color.Blue);bitmap.Save(image,ImageFormat.Png);}
   var p=Store.New();p.Active.width=320;p.Active.height=320;
   var asset=Media.Inspect(image,CancellationToken.None);p.assets.Add(asset);
   var v1=p.Active.tracks.First(t=>t.kind=="video");var v2=p.Active.tracks.Where(t=>t.kind=="video").Skip(1).First();
   Media.Insert(p,asset.id,v1.id,0);var clip=p.Active.clips.Single();
   v1.effects.Add(EffectContracts.New(EffectContracts.FlipId));
   clip.effects.Add(EffectContracts.New(EffectContracts.BasicColorId));
   clip.effects.Add(EffectContracts.New(EffectContracts.BlurId));
   clip.effects.Add(EffectContracts.New(EffectContracts.SharpenId));
   Store.Validate(p);
   string filters=FFmpegEffectAdapter.VideoFilters(p,clip);
   Check(filters.IndexOf("hflip",StringComparison.Ordinal)<filters.IndexOf("eq=",StringComparison.Ordinal)&&filters.IndexOf("eq=",StringComparison.Ordinal)<filters.IndexOf("gblur",StringComparison.Ordinal)&&filters.IndexOf("gblur",StringComparison.Ordinal)<filters.IndexOf("unsharp",StringComparison.Ordinal),"pila de pista precede a la pila ordenada del clip");
   using(var frame=Rendering.Frame(p,0.5,CancellationToken.None,320,320))Check(Blue(frame.GetPixel(70,160))&&Red(frame.GetPixel(250,160)),"vista previa aplica efectos de pista y clip");
   string rendered=Path.Combine(directory,"resultado.mp4");Rendering.Export(p,rendered,CancellationToken.None);
   string exported=Path.Combine(directory,"fotograma.png");Tools.Run(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -y -i "+Tools.Q(rendered)+" -frames:v 1 "+Tools.Q(exported),CancellationToken.None);
   using(var frame=new Bitmap(exported))Check(Blue(frame.GetPixel(70,160))&&Red(frame.GetPixel(250,160)),"exportación conserva el mismo efecto de pista");
   clip.inherit_track_effects=false;
   using(var frame=Rendering.Frame(p,0.5,CancellationToken.None,320,320))Check(Red(frame.GetPixel(70,160))&&Blue(frame.GetPixel(250,160)),"el clip excluido no hereda la pila de pista");
   clip.inherit_track_effects=true;v1.effects_enabled=false;
   using(var frame=Rendering.Frame(p,0.5,CancellationToken.None,320,320))Check(Red(frame.GetPixel(70,160))&&Blue(frame.GetPixel(250,160)),"Fx apagado omite efectos de pista");
   v1.effects_enabled=true;clip.track_id=v2.id;
   using(var frame=Rendering.Frame(p,0.5,CancellationToken.None,320,320))Check(Red(frame.GetPixel(70,160))&&Blue(frame.GetPixel(250,160)),"mover clip a otra pista cambia la herencia");
   clip.track_id=v1.id;string file=Path.Combine(directory,"prueba.prometheus.json");Store.Save(p,file);var reopened=Store.Load(file);
   Check(reopened.Active.tracks.First(t=>t.id==v1.id).effects.Count==1&&reopened.Active.clips.Single().inherit_track_effects,"guardar y reabrir conserva la pila y la herencia");
   var missing=new EffectResource{id=Store.Id(),kind="lut3d",uri=Path.Combine(directory,"ausente.cube")};p.effect_resources.Add(missing);v1.effects.Add(EffectContracts.NewLut3D(missing.id));
   Check(ExportPreflight.Inspect(p,true,0).checks.Any(c=>c.code=="lut-missing"),"preflight detecta LUT ausente en pista activa");
   v1.effects_enabled=false;Check(!ExportPreflight.Inspect(p,true,0).checks.Any(c=>c.code=="lut-missing"),"Fx apagado no exige recursos sin uso");
  }finally{Directory.Delete(directory,true);}
 }
}
