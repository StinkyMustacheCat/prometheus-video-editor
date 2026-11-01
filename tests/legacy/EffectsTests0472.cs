using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class EffectsTests0472 {
 static void Check(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static Color Pixel(Project project,int x,int y){using(var frame=Rendering.Frame(project,0.5,CancellationToken.None,320,320))return frame.GetPixel(x,y);}
 static void Main(){
  Tools.Root=AppDomain.CurrentDomain.BaseDirectory;
  string folder=Path.Combine(Tools.Root,"effects-0472-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  try{
   var p=Store.New();p.Active.width=320;p.Active.height=320;
   string picture=Path.Combine(folder,"pattern.png");using(var image=new Bitmap(160,90)){using(var g=Graphics.FromImage(image))g.Clear(Color.Lime);image.Save(picture);}
   var asset=Media.Inspect(picture,CancellationToken.None);p.assets.Add(asset);
   Media.Insert(p,asset.id,p.Active.tracks.First(t=>t.kind=="video").id,0);
   var clip=p.Active.clips.Single(c=>c.asset_id==asset.id);
   Check(ClipFraming.Read(clip).mode=="fit"&&Math.Abs(ClipFraming.Read(clip).scale-1)<0.0001,"clips nuevos ajustan al lienzo con escala 100 %");
   Check(Pixel(p,10,160).G>150,"ajuste activo amplía una imagen pequeña");
   ClipFraming.Set(clip,"native",1,0,0);
   Check(Pixel(p,10,160).G<80&&Pixel(p,160,160).G>150,"ajuste apagado usa el tamaño original centrado");
   ClipFraming.Set(clip,"native",1.25,0,0);
   Check(Pixel(p,65,160).G>150,"escala adicional conserva su porcentaje con ajuste apagado");
   ClipFraming.Set(clip,"fit",1.25,0,0);
   Check(Pixel(p,10,160).G>150,"escala adicional se aplica sobre el ajuste automático");
   ClipFraming.SetOpacity(clip,0.5);
   var translucent=Pixel(p,160,160);Check(translucent.G>75&&translucent.G<210,"opacidad mezcla el clip con el fondo");
   ClipFraming.SetOpacity(clip,1);
   clip.effects.Add(EffectContracts.New(EffectContracts.CropId));
   clip.effects.Last().parameters["left"].number=50;
   Check(Pixel(p,20,160).G<80&&Pixel(p,230,160).G>150,"recorte deja transparente la parte retirada sin redimensionar");
   clip.effects.Clear();
   clip.effects.Add(EffectContracts.New(EffectContracts.VignetteId));
   clip.effects.Add(EffectContracts.New(EffectContracts.HueId));
   clip.effects.Add(EffectContracts.New(EffectContracts.FlipVerticalId));
   string filter=FFmpegEffectAdapter.VideoFilters(p,clip);
   Check(filter.Contains("vignette")&&filter.Contains("hue=h=")&&filter.Contains("vflip"),"la segunda tanda se conecta al adaptador FFmpeg");
   string file=Path.Combine(folder,"project.prometheus.json");Store.Save(p,file);var restored=Store.Load(file);
   Check(restored.Active.clips.Single().effects.Count==3&&ClipFraming.Read(restored.Active.clips.Single()).opacity==1,"efectos y transformación sobreviven al guardado");
   var proxy=Path.Combine(folder,"proxy.png");using(var image=new Bitmap(80,45)){using(var g=Graphics.FromImage(image))g.Clear(Color.Lime);image.Save(proxy);}
   clip.effects.Clear();ClipFraming.Set(clip,"native",1,0,0);
   asset.representations.Add(new MediaRepresentation{role="proxy",locator=new Locator{path=proxy}});
   var originalGraph=Rendering.Build(p,true,false,320,320,60,"raw",0,true);
   Check(originalGraph.Contains("scale=160:90:flags=bicubic"),"tamaño original usa metadatos de fuente y no dimensiones del proxy");
   Check(Pixel(p,160,160).G>150&&Pixel(p,10,160).G<80,"proxy conserva el tamaño nativo de la fuente en monitor");
   ClipFraming.Set(clip,"fill",1,0,0);Check(Pixel(p,10,160).G>150,"modo Rellenar de proyectos anteriores sigue renderizando");
   var layered=Store.New();layered.Active.width=320;layered.Active.height=320;
   var red=GeneratedMedia.CreateSolid("Rojo","FF0000",1);var green=GeneratedMedia.CreateSolid("Verde","00FF00",1);layered.assets.Add(red);layered.assets.Add(green);
   Media.Insert(layered,red.id,layered.Active.tracks.First(t=>t.kind=="video"&&t.order==0).id,0);
   Media.Insert(layered,green.id,layered.Active.tracks.First(t=>t.kind=="video"&&t.order==1).id,0);
   var upper=layered.Active.clips.First(c=>c.asset_id==green.id);upper.effects.Add(EffectContracts.New(EffectContracts.CropId));upper.effects[0].parameters["left"].number=50;ClipFraming.SetOpacity(upper,0.5);
   var left=Pixel(layered,20,160);var right=Pixel(layered,280,160);
   Check(left.R>180&&left.G<80&&right.R>75&&right.G>75,"recorte y opacidad componen sobre la pista inferior");
   string exported=Path.Combine(folder,"composition.mp4");Rendering.Export(layered,exported,CancellationToken.None);
   string rendered=Path.Combine(folder,"composition.png");Tools.Run(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -y -i "+Tools.Q(exported)+" -frames:v 1 "+Tools.Q(rendered),CancellationToken.None);
   using(var result=new Bitmap(rendered)){var l=result.GetPixel(20,160);var r=result.GetPixel(280,160);Check(l.R>170&&l.G<90&&r.R>65&&r.G>65,"exportación mantiene recorte y opacidad del monitor");}
   var mirrored=Store.New();mirrored.Active.width=320;mirrored.Active.height=320;
   string stripes=Path.Combine(folder,"stripes.png");using(var image=new Bitmap(320,320)){using(var g=Graphics.FromImage(image)){g.Clear(Color.Blue);g.FillRectangle(Brushes.Red,0,0,320,160);}image.Save(stripes);}
   var stripeAsset=Media.Inspect(stripes,CancellationToken.None);mirrored.assets.Add(stripeAsset);Media.Insert(mirrored,stripeAsset.id,mirrored.Active.tracks.First(t=>t.kind=="video").id,0);
   mirrored.Active.clips.Single().effects.Add(EffectContracts.New(EffectContracts.FlipVerticalId));
   Check(Pixel(mirrored,160,40).B>150,"voltear vertical modifica la imagen en monitor");
   mirrored.Active.clips.Single().effects.Clear();mirrored.Active.clips.Single().effects.Add(EffectContracts.New(EffectContracts.HueId));mirrored.Active.clips.Single().effects[0].parameters["degrees"].number=120;
   Check(Pixel(mirrored,160,40).G>120,"tono modifica el color en monitor");
   mirrored.Active.clips.Single().effects.Clear();mirrored.Active.clips.Single().effects.Add(EffectContracts.New(EffectContracts.VignetteId));mirrored.Active.clips.Single().effects[0].parameters["amount"].number=0.8;
   var center=Pixel(mirrored,160,100);var corner=Pixel(mirrored,5,5);Check(center.R>corner.R+30,"viñeta oscurece los bordes");
  }finally{Directory.Delete(folder,true);}
 }
}

