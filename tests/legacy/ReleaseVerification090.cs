using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class ReleaseVerification090 {
 static int Main(string[] args) {
  try {
   string input=Path.GetFullPath(args[0]),copy=Path.GetFullPath(args[1]),output=Path.GetFullPath(args[2]);
   var project=Store.Load(input);var clip=project.Active.clips.First(c=>Sam2MaskStore090.Get(c)!=null);
   if(!Sam2MaskStore090.Ready(clip))throw new Exception("SAM2 no está listo al abrir el proyecto");
   Sam2MaskStore090.Materialize(project,copy);Store.Save(project,copy);
   var reopened=Store.Load(copy);Sam2MaskStore090.Reconnect(reopened,copy);
   var reopenedClip=reopened.Active.clips.First(c=>c.id==clip.id);
   if(!Sam2MaskStore090.Ready(reopenedClip))throw new Exception("SAM2 se perdió al guardar una copia");
   var mask=Sam2MaskStore090.Get(reopenedClip);
   string expected=Sam2MaskStore090.DirectoryFor(copy,reopened.id,clip.id);
   if(!String.Equals(Path.GetFullPath(mask.directory),Path.GetFullPath(expected),StringComparison.OrdinalIgnoreCase))throw new Exception("La copia usa la carpeta original");
   var options=ExportOptions.For(reopened);options.Width=640;options.Height=360;options.Fps=30;
   Rendering.Export(reopened,output,CancellationToken.None,null,options);
   if(!File.Exists(output)||new FileInfo(output).Length<1000)throw new Exception("La exportación no creó un vídeo válido");
   using(var frame=Rendering.FrameExact(reopened,0.25,CancellationToken.None,256,144,false)){
    string preview=Path.ChangeExtension(output,".png");frame.Save(preview);
   }
   Rendering.GeneratedCacheRoot=Path.Combine(Path.GetDirectoryName(output),"render-cache");
   var title=GeneratedMedia.CreateText("Prueba 0.9.0",2);
   string plain=TextRaster0817.RenderFile(title,640,360);
   GeneratedMedia.SetTextStyleNumber(title,"stroke_px",7);
   GeneratedMedia.SetTextStyleColor(title,"stroke_color","EF7210");
   GeneratedMedia.SetTextStyleNumber(title,"shadow_opacity",0.75);
   GeneratedMedia.SetTextStyleNumber(title,"shadow_x",8);
   GeneratedMedia.SetTextStyleNumber(title,"shadow_y",9);
   string styled=TextRaster0817.RenderFile(title,640,360);
   if(plain==styled||!File.Exists(styled))throw new Exception("El estilo del texto no se aplicó al render");
   Console.WriteLine("PASS copia, reapertura, máscara SAM2, exportación y texto con estilo: "+new FileInfo(output).Length+" bytes");
   return 0;
  } catch(Exception ex) {Console.Error.WriteLine(ex);return 1;}
 }
}
