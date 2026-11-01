using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Prometheus {
 public static class TextRaster0817 {
  public static string RenderFile(Asset asset,int width,int height){
   GeneratedMedia.ValidateText(asset);if(!GeneratedMedia.IsText(asset))throw new Exception("No es un asset de texto");
   string text=GeneratedMedia.Text(asset),color=GeneratedMedia.TextColor(asset),role=GeneratedMedia.IsSubtitle(asset)?"subtitle":"title";
   int size=GeneratedMedia.TextSize(asset);string font=GeneratedMedia.TextFont(asset);double stroke=GeneratedMedia.TextStyleNumber(asset,"stroke_px",4),shadowX=GeneratedMedia.TextStyleNumber(asset,"shadow_x",0),shadowY=GeneratedMedia.TextStyleNumber(asset,"shadow_y",0),shadowBlur=GeneratedMedia.TextStyleNumber(asset,"shadow_blur",0),shadowOpacity=GeneratedMedia.TextStyleNumber(asset,"shadow_opacity",0);string strokeColor=GeneratedMedia.TextStyleColor(asset,"stroke_color","090B10"),shadowColor=GeneratedMedia.TextStyleColor(asset,"shadow_color","000000");string identity="text-0822|"+text+"|"+color+"|"+role+"|"+font+"|"+size+"|"+stroke+"|"+strokeColor+"|"+shadowX+"|"+shadowY+"|"+shadowBlur+"|"+shadowOpacity+"|"+shadowColor+"|"+width+"x"+height;
   string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-","").Substring(0,40).ToLowerInvariant();
   Directory.CreateDirectory(Rendering.GeneratedCacheRoot);string path=Path.Combine(Rendering.GeneratedCacheRoot,"text-"+hash+".png");if(File.Exists(path))return path;
   string temporary=path+".partial-"+Store.Id();
   try{
    using(var bitmap=new Bitmap(width,height,PixelFormat.Format32bppArgb))using(var graphics=Graphics.FromImage(bitmap))using(var shape=new GraphicsPath()){
     graphics.Clear(Color.Transparent);graphics.SmoothingMode=SmoothingMode.AntiAlias;graphics.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
     float factor=Math.Min(width/1920f,height/1080f),fontSize=Math.Max(8,size*factor);
     var bounds=role=="subtitle"?new RectangleF(width*.08f,height*.70f,width*.84f,height*.23f):new RectangleF(width*.10f,height*.17f,width*.80f,height*.66f);
     FontFamily selected;try{selected=new FontFamily(font);}catch{selected=new FontFamily("Segoe UI");}
     using(var family=selected)using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center}){
      shape.AddString(text,family,(int)FontStyle.Bold,fontSize,bounds,format);
     }
     if(shadowOpacity>0){using(var shadow=(GraphicsPath)shape.Clone()){using(var transform=new Matrix()){transform.Translate((float)(shadowX*factor),(float)(shadowY*factor));shadow.Transform(transform);}var baseColor=ColorTranslator.FromHtml("#"+shadowColor);int blur=(int)Math.Round(shadowBlur*factor);if(blur>0)for(int radius=blur;radius>=1;radius--){using(var pen=new Pen(Color.FromArgb(Math.Max(1,(int)(shadowOpacity*32.0/blur)),baseColor),radius*2){LineJoin=LineJoin.Round})graphics.DrawPath(pen,shadow);}using(var brush=new SolidBrush(Color.FromArgb((int)Math.Round(shadowOpacity*255),baseColor)))graphics.FillPath(brush,shadow);}}
     if(stroke>0)using(var outline=new Pen(ColorTranslator.FromHtml("#"+strokeColor),Math.Max(0.5f,(float)(stroke*factor))){LineJoin=LineJoin.Round})graphics.DrawPath(outline,shape);
     using(var fill=new SolidBrush(ColorTranslator.FromHtml("#"+color)))graphics.FillPath(fill,shape);
     bitmap.Save(temporary,ImageFormat.Png);
    }
    if(!File.Exists(path)){try{File.Move(temporary,path);}catch(IOException){if(!File.Exists(path))throw;}}
    return path;
   }finally{if(File.Exists(temporary))File.Delete(temporary);}
  }
  public static string RenderAss(Project p,List<Clip> cues,int width,int height){
   if(cues==null||cues.Count==0)return null;
   var sb=new StringBuilder();
   sb.AppendLine("[Script Info]");
   sb.AppendLine("ScriptType: v4.00+");
   sb.AppendLine("PlayResX: "+width);
   sb.AppendLine("PlayResY: "+height);
   sb.AppendLine("");
   sb.AppendLine("[V4+ Styles]");
   sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
   float factor=Math.Min(width/1920f,height/1080f);
   int marginV=(int)Math.Round(height*0.17f);
   int marginH=(int)Math.Round(width*0.08f);
   var styles=new Dictionary<string,string>();
   int styleIndex=0;
   foreach(var c in cues){
    var a=p.assets.Find(x=>x.id==c.asset_id);
    if(a==null)continue;
    string font=GeneratedMedia.TextFont(a);
    if(String.IsNullOrEmpty(font))font="Segoe UI";
    int size=Math.Max(8,(int)Math.Round(GeneratedMedia.TextSize(a)*factor));
    string color=ToAssColor(GeneratedMedia.TextColor(a));
    string strokeColor=ToAssColor(GeneratedMedia.TextStyleColor(a,"stroke_color","090B10"));
    double stroke=Math.Max(0.5,GeneratedMedia.TextStyleNumber(a,"stroke_px",4)*factor);
    string styleKey=font+"|"+size+"|"+color+"|"+strokeColor+"|"+stroke.ToString("0.#",CultureInfo.InvariantCulture);
    string styleName;
    if(!styles.TryGetValue(styleKey,out styleName)){
     styleName="S"+(styleIndex++);
     styles[styleKey]=styleName;
     sb.AppendLine(String.Format(CultureInfo.InvariantCulture,"Style: {0},{1},{2},{3},&H000000FF&,{4},&H00000000&,-1,0,0,0,100,100,0,0,1,{5:0.#},0,2,{6},{6},{7},1",
      styleName,font,size,color,strokeColor,stroke,marginH,marginV));
    }
   }
   sb.AppendLine("");
   sb.AppendLine("[Events]");
   sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
   foreach(var c in cues){
    var a=p.assets.Find(x=>x.id==c.asset_id);
    if(a==null)continue;
    string font=GeneratedMedia.TextFont(a);
    if(String.IsNullOrEmpty(font))font="Segoe UI";
    int size=Math.Max(8,(int)Math.Round(GeneratedMedia.TextSize(a)*factor));
    string color=ToAssColor(GeneratedMedia.TextColor(a));
    string strokeColor=ToAssColor(GeneratedMedia.TextStyleColor(a,"stroke_color","090B10"));
    double stroke=Math.Max(0.5,GeneratedMedia.TextStyleNumber(a,"stroke_px",4)*factor);
    string styleKey=font+"|"+size+"|"+color+"|"+strokeColor+"|"+stroke.ToString("0.#",CultureInfo.InvariantCulture);
    string styleName=styles[styleKey];
    string text=GeneratedMedia.Text(a).Replace("\r\n","\\N").Replace("\n","\\N");
    sb.AppendLine(String.Format(CultureInfo.InvariantCulture,"Dialogue: 0,{0},{1},{2},,0,0,0,,{3}",
     FormatAssTime(c.timeline_start.Seconds),FormatAssTime(c.End),styleName,text));
   }
   string identity="ass-0822|"+width+"x"+height+"|"+sb.ToString();
   string hash;
   using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-","").Substring(0,40).ToLowerInvariant();
   Directory.CreateDirectory(Rendering.GeneratedCacheRoot);
   string path=Path.Combine(Rendering.GeneratedCacheRoot,"subs-"+hash+".ass");
   if(!File.Exists(path)){
    string temporary=path+".partial-"+Store.Id();
    File.WriteAllText(temporary,sb.ToString(),new UTF8Encoding(false));
    if(!File.Exists(path)){try{File.Move(temporary,path);}catch(IOException){if(!File.Exists(path))throw;}}
    try{if(File.Exists(temporary))File.Delete(temporary);}catch{}
   }
   return path;
  }
  static string ToAssColor(string hex){
   if(hex==null)return "&H00FFFFFF&";
   hex=hex.TrimStart('#');
   if(hex.Length==6)return "&H00"+hex.Substring(4,2)+hex.Substring(2,2)+hex.Substring(0,2)+"&";
   return "&H00FFFFFF&";
  }
  static string FormatAssTime(double seconds){
   var ts=TimeSpan.FromSeconds(Math.Max(0,seconds));
   return String.Format(CultureInfo.InvariantCulture,"{0:D1}:{1:D2}:{2:D2}.{3:D2}",(int)ts.TotalHours,ts.Minutes,ts.Seconds,ts.Milliseconds/10);
  }
 }
}
