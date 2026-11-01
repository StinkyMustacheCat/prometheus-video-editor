using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Prometheus {
 public static class ClipMaskRaster {
  public static readonly string Root=String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROMETHEUS_MASK_CACHE_ROOT"))?
   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","mk"):
   Path.GetFullPath(Environment.GetEnvironmentVariable("PROMETHEUS_MASK_CACHE_ROOT"));
  public static string RenderFile(ClipMask mask,int width,int height){
   ClipMasks.Validate(mask);if(mask==null||!mask.enabled)throw new Exception("La máscara no está activa");
   string identity="preblur-v1|"+Store.Json().Serialize(Store.Fields(mask))+"|"+width+"x"+height;
   string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-","").Substring(0,40).ToLowerInvariant();
   Directory.CreateDirectory(Root);string path=Path.Combine(Root,hash+".png");if(File.Exists(path))return path;
   string temporary=Path.Combine(Root,hash+"-"+Store.Id().Substring(0,8)+".png");
   string raw=temporary+".raw.png";
   try{
    using(var image=new Bitmap(width,height,PixelFormat.Format24bppRgb))using(var graphics=Graphics.FromImage(image))using(var shape=new GraphicsPath()){
     graphics.SmoothingMode=SmoothingMode.AntiAlias;graphics.Clear(mask.inverted?Color.White:Color.Black);
     if(mask.shape=="rectangle"||mask.shape=="ellipse"){
      float x=(float)(mask.points[0].x*width),y=(float)(mask.points[0].y*height),w=(float)((mask.points[1].x-mask.points[0].x)*width),h=(float)((mask.points[1].y-mask.points[0].y)*height);
      if(mask.shape=="rectangle")shape.AddRectangle(new RectangleF(x,y,w,h));else shape.AddEllipse(x,y,w,h);
     }else shape.AddPolygon(mask.points.Select(p=>new PointF((float)(p.x*width),(float)(p.y*height))).ToArray());
     using(var fill=new SolidBrush(mask.inverted?Color.Black:Color.White))graphics.FillPath(fill,shape);
     image.Save(mask.feather>0.00001?raw:temporary,ImageFormat.Png);
    }
    if(mask.feather>0.00001){double sigma=Math.Min(100,mask.feather*Math.Min(width,height));Tools.Run(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -i "+Tools.Q(raw)+" -vf "+Tools.Q("format=gray,gblur=sigma="+Tools.N(sigma))+" -frames:v 1 -y "+Tools.Q(temporary),CancellationToken.None);}
    if(!File.Exists(path)){try{File.Move(temporary,path);}catch(IOException){if(!File.Exists(path))throw;}}
    return path;
   }finally{if(File.Exists(temporary))File.Delete(temporary);if(File.Exists(raw))File.Delete(raw);}
  }
  public static string RenderSequence(Clip clip,int width,int height,int fps){
   if(!TemporalMasks0823.HasAnimation(clip))throw new Exception("No hay máscara temporal");
   string identity="temporal-v1|"+Store.Json().Serialize(Store.Fields(clip.mask))+"|"+Store.Json().Serialize(Store.Fields(TemporalMasks0823.Get(clip)))+"|"+width+"x"+height+"@"+fps+"|"+clip.source_in.Seconds+"|"+clip.speed.Seconds+"|"+clip.duration.Seconds;
   string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-","").Substring(0,40).ToLowerInvariant();
   string folder=Path.Combine(Root,"temporal-"+hash),done=Path.Combine(folder,"complete.txt");int count=Math.Max(1,(int)Math.Ceiling(clip.duration.Seconds*fps));
   Directory.CreateDirectory(folder);if(!File.Exists(done)){
    for(int i=0;i<count;i++){double at=clip.timeline_start.Seconds+i/(double)fps;string source=RenderFile(TemporalMasks0823.At(clip,at),width,height);string target=Path.Combine(folder,i.ToString("D6")+".png");if(!File.Exists(target))File.Copy(source,target);}
    File.WriteAllText(done,count.ToString(System.Globalization.CultureInfo.InvariantCulture));
   }
   return Path.Combine(folder,"%06d.png");
  }
 }
}
