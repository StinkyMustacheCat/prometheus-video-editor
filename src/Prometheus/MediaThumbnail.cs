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
 public static class MediaThumbnail {
  public static string CacheRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","Thumbnails");
  public static string WebRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","WebCache");
  const int Width=120,Height=68;
  public static string Key(Asset asset){
   string source=asset.source==null?"":asset.source.kind+"|"+asset.source.uri;
   string generated=GeneratedMedia.Kind(asset);if(generated!=null)return source+"|"+generated+"|"+(generated=="text"?GeneratedMedia.Text(asset)+"|"+GeneratedMedia.TextSize(asset)+"|"+GeneratedMedia.TextColor(asset):GeneratedMedia.ColorHex(asset))+"|"+asset.name;
   string webThumb=asset.source!=null&&asset.source.kind=="web"?WebMedia.ThumbnailPath(asset,WebRoot):null;
   if(webThumb!=null&&File.Exists(webThumb))return source+"|thumb|"+File.GetLastWriteTimeUtc(webThumb).Ticks;
   var rep=Media.SelectRepresentation(asset,true);if(rep==null||rep.locator==null)return source+"|pending";
   string file=rep.locator.path;return source+"|"+file+"|"+(File.Exists(file)?File.GetLastWriteTimeUtc(file).Ticks.ToString():"missing");
  }
  public static Bitmap Create(Asset asset,string key){
   string generated=GeneratedMedia.Kind(asset);
   if(generated=="solid")return Card(ColorTranslator.FromHtml("#"+GeneratedMedia.ColorHex(asset)),"COLOR");
   if(generated=="placeholder")return Card(asset.streams.All(s=>s.kind=="audio")?Color.FromArgb(53,81,103):Color.FromArgb(73,80,91),asset.streams.All(s=>s.kind=="audio")?"AUDIO\nRESERVADO":"RESERVADO");
   if(generated=="text")return ReadScaled(TextRaster0817.RenderFile(asset,320,180));
   if(asset.streams.All(s=>s.kind=="audio"))return AudioCard();
   string digest;using(var hash=SHA256.Create())digest=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-","").Substring(0,24);
   string target=Path.Combine(CacheRoot,digest+".png");
   if(File.Exists(target))try{return ReadScaled(target);}catch{}
   string input=null;
   if(asset.source!=null&&asset.source.kind=="web"){
    string thumbnail=WebMedia.ThumbnailPath(asset,WebRoot);if(File.Exists(thumbnail))input=thumbnail;
   }
   var rep=Media.SelectRepresentation(asset,true);
   if(input==null&&rep!=null&&rep.locator!=null){
    if(asset.streams.Any(s=>s.kind=="image"))input=rep.locator.path;
    else if(asset.streams.Any(s=>s.kind=="video")){
     Directory.CreateDirectory(CacheRoot);
     string extracted=Path.Combine(CacheRoot,digest+"-frame.png");
     if(!File.Exists(extracted)){
      string temp=extracted+".partial-"+Store.Id()+".png";
      try{Tools.Run(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -y -ss 0.5 -i "+Tools.Q(rep.locator.path)+" -frames:v 1 -vf scale=120:68:force_original_aspect_ratio=decrease "+Tools.Q(temp),CancellationToken.None);if(File.Exists(temp)){try{File.Move(temp,extracted);}catch(UnauthorizedAccessException){File.Copy(temp,extracted,true);}}}catch{}finally{try{if(File.Exists(temp))File.Delete(temp);}catch{}}
     }
     if(File.Exists(extracted))input=extracted;
    }
   }
   if(input==null)return Card(Color.FromArgb(53,57,63),"VÍDEO");
   Bitmap result;try{result=ReadScaled(input);}catch{return Card(Color.FromArgb(53,57,63),"VÍDEO");}
   try{Directory.CreateDirectory(CacheRoot);if(!File.Exists(target))result.Save(target,ImageFormat.Png);}catch{}
   return result;
  }
  static Bitmap ReadScaled(string file){
   using(var stream=new MemoryStream(File.ReadAllBytes(file)))using(var original=Image.FromStream(stream,true,true)){
    var bitmap=new Bitmap(Width,Height);using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.FromArgb(37,40,45));g.InterpolationMode=InterpolationMode.HighQualityBicubic;float ratio=Math.Min(Width/(float)original.Width,Height/(float)original.Height);int w=Math.Max(1,(int)Math.Round(original.Width*ratio)),h=Math.Max(1,(int)Math.Round(original.Height*ratio));g.DrawImage(original,(Width-w)/2,(Height-h)/2,w,h);}return bitmap;
   }
  }
  static Bitmap Card(Color color,string label){var image=new Bitmap(Width,Height);using(var g=Graphics.FromImage(image)){g.Clear(color);using(var brush=new SolidBrush(Color.WhiteSmoke))using(var font=new Font("Segoe UI",9,FontStyle.Bold))g.DrawString(label,font,brush,new RectangleF(4,4,Width-8,Height-8),new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center});}return image;}
  static Bitmap AudioCard(){var image=Card(Color.FromArgb(53,81,103),"");using(var g=Graphics.FromImage(image))using(var pen=new Pen(Color.FromArgb(159,211,237),2)){for(int x=12;x<Width-12;x+=6){int amp=4+(x*17%19);g.DrawLine(pen,x,Height/2-amp,x,Height/2+amp);}}return image;}
 }
}
