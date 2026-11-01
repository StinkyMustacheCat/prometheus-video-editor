using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class MilestoneTests1022 {
 static int Main(string[] args){try{
  string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);string imageFile=Path.Combine(output,"portrait.png");
  using(var bitmap=new Bitmap(360,640))using(var graphics=Graphics.FromImage(bitmap)){graphics.Clear(Color.CadetBlue);graphics.FillEllipse(Brushes.Gold,65,100,230,390);bitmap.Save(imageFile,ImageFormat.Png);}
  var p=Store.New();SequenceCanvas.Set(p.Active,640,360);var asset=Media.Inspect(imageFile,CancellationToken.None);p.assets.Add(asset);var first=p.Active.tracks.Where(t=>t.kind=="video").OrderBy(t=>t.order).First();Media.Insert(p,asset.id,first.id,0);var clip=p.Active.clips.First(c=>c.track_id==first.id);
  if(!CanvasFit0913.Apply(p,clip,"deform"))throw new Exception("No se deformó el medio");var frame=ClipFraming.Read(clip);if(frame.scaleLinked||Math.Abs(frame.scale-frame.scaleY)<.01)throw new Exception("La deformación no separó las escalas X/Y");
  int before=p.Active.tracks.Count(t=>t.kind=="video");var nested=NestedMedia0910.CreateBlurredBackground(p,clip.id);Store.Validate(p);int after=p.Active.tracks.Count(t=>t.kind=="video");if(after!=before)throw new Exception("Fondo desenfocado dejó una pista auxiliar vacía");
  var content=NestedMedia0910.Read(nested);if(content.clips.Count(c=>content.video_orders.ContainsKey(c.track_id))!=2||!content.clips.Any(c=>c.effects.Any(e=>e.effect_id==EffectContracts.BlurId)))throw new Exception("Faltan las dos capas del fondo");
  using(var picture=Rendering.FrameExact(p,.2,CancellationToken.None,640,360,false))picture.Save(Path.Combine(output,"blurred.png"),ImageFormat.Png);
  Console.WriteLine("PASS deform/blurred-background/no-residual-track/render");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
