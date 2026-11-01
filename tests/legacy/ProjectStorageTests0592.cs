using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Prometheus;

public static class ProjectStorageTests0592 {
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 public static void Main(){
  string package=AppDomain.CurrentDomain.BaseDirectory;
  string image=Path.Combine(package,"demo","grafico.png"),video=Path.Combine(package,"demo","video.mp4");
  string work=Path.Combine(package,"test-"+Guid.NewGuid().ToString("N").Substring(0,8));
  string first=Path.Combine(work,"original"),moved=Path.Combine(work,"movido"),orphan=Path.Combine(work,"sin-recursos");
  Directory.CreateDirectory(first);Directory.CreateDirectory(moved);Directory.CreateDirectory(orphan);
  var project=Store.New();
  var importedImage=Media.Inspect(image,System.Threading.CancellationToken.None);
  var linkedVideo=Media.Inspect(video,System.Threading.CancellationToken.None);
  var web=Media.Inspect(video,System.Threading.CancellationToken.None);
  web.source.kind="web";web.source.uri="https://example.invalid/video";web.source.page_url=web.source.uri;web.source.resolver_type="youtube/yt-dlp";
  var segmentInfo=new FileInfo(video);
  RangeMedia.Write(web,new List<MediaRange>{new MediaRange{start=0,end=1,path=video,size_bytes=segmentInfo.Length,modified_utc=segmentInfo.LastWriteTimeUtc.ToString("o",System.Globalization.CultureInfo.InvariantCulture)}});
  project.assets.Add(importedImage);project.assets.Add(linkedVideo);project.assets.Add(web);
  project.effect_resources.Add(new EffectResource{id=Store.Id(),kind="lut3d",uri=Path.Combine(package,"assets","luts","neutral.cube")});
  string projectFile=Path.Combine(first,"Prueba.pthproj");
  ProjectResourceTransfer.Materialize(project,projectFile);EffectResourceStorage.Materialize(project,projectFile);Store.Save(project,projectFile);
  Check(File.Exists(projectFile),"archivo principal");
  Check(File.Exists(importedImage.source.uri)&&importedImage.source.uri.StartsWith(ProjectStorage.DataDirectory(projectFile),StringComparison.OrdinalIgnoreCase),"imagen gestionada");
  Check(linkedVideo.source.uri==video,"vídeo local sigue enlazado");
  Check(web.representations.First().locator.path.StartsWith(ProjectStorage.DataDirectory(projectFile),StringComparison.OrdinalIgnoreCase),"descarga web gestionada");
  Check(RangeMedia.Read(web).First().path.StartsWith(ProjectStorage.DataDirectory(projectFile),StringComparison.OrdinalIgnoreCase),"tramo web gestionado");
  Check(File.Exists(project.effect_resources[0].uri)&&project.effect_resources[0].uri.StartsWith(ProjectStorage.DataDirectory(projectFile),StringComparison.OrdinalIgnoreCase),"LUT gestionada");
  Check(!Directory.Exists(ProjectStorage.ProxyDirectory(projectFile)),"no crear carpetas vacías");
  string originalImagePath=importedImage.source.uri;
  ProjectResourceTransfer.Materialize(project,projectFile);Check(importedImage.source.uri==originalImagePath,"guardar otra vez no duplica imagen");
  string movedFile=Path.Combine(moved,"Prueba.pthproj");File.Copy(projectFile,movedFile);
  CopyDirectory(ProjectStorage.DataDirectory(projectFile),ProjectStorage.DataDirectory(movedFile));
  var reopened=Store.Load(movedFile);
  Check(!ProjectMediaRecovery.RequiresRecovery(reopened),"proyecto movido con recursos abre completo");
  Check(File.Exists(RangeMedia.Read(reopened.assets.First(a=>a.id==web.id)).First().path),"tramo web conserva ruta relativa al trasladar");
  Check(reopened.assets.First(a=>a.id==linkedVideo.id).source.uri==video,"enlace local externo conservado");
  string orphanFile=Path.Combine(orphan,"Prueba.pthproj");File.Copy(projectFile,orphanFile);
  var disconnected=Store.Load(orphanFile);
  Check(ProjectMediaRecovery.Missing(disconnected).Count==2,"faltan imagen y vídeo web gestionados");
  Console.WriteLine("OK .pthproj, recursos, traslado y detección de medios faltantes");
 }
 static void CopyDirectory(string source,string destination){
  Directory.CreateDirectory(destination);
  foreach(var file in Directory.GetFiles(source))File.Copy(file,Path.Combine(destination,Path.GetFileName(file)));
  foreach(var folder in Directory.GetDirectories(source))CopyDirectory(folder,Path.Combine(destination,Path.GetFileName(folder)));
 }
}
