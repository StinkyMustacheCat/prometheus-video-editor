using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Prometheus;

class WebTests030 {
 static int count;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);count++;}
 [STAThread]static void Main(){
  string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"web-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   var html=new DataObject();html.SetData(DataFormats.Html,"Version:0.9\r\nSourceURL:https://en.wikipedia.org/wiki/Example\r\n<html><img src='https://upload.wikimedia.org/example.png'></html>");
   var drop=WebDrop.Read(html);Check(drop!=null&&drop.Url=="https://upload.wikimedia.org/example.png"&&drop.PageUrl=="https://en.wikipedia.org/wiki/Example","arrastre HTML conserva recurso y página de origen");
   var data=new DataObject();data.SetData(DataFormats.UnicodeText,"https://www.youtube.com/watch?v=jNQXAC9IVRw");
   Check(WebDrop.Read(data).IsYouTube,"arrastre de enlace reconoce YouTube");
   string imageUrl="https://www.gstatic.com/images/branding/googlelogo/2x/googlelogo_color_272x92dp.png";
   var image=WebMedia.DownloadImage(imageUrl,"https://www.google.com/",root,CancellationToken.None);
   Check(image.source.kind=="web"&&image.source.page_url=="https://www.google.com/"&&Media.SelectRepresentation(image,false)!=null,"imagen web se guarda como medio local con origen");
   var youtube=WebMedia.InspectYouTube("https://www.youtube.com/watch?v=jNQXAC9IVRw",CancellationToken.None);
   Check(youtube.source.kind=="web"&&youtube.streams.Any(s=>s.kind=="video")&&youtube.streams[0].duration.Seconds>=18&&youtube.representations.Count==0,"YouTube crea medio virtual con duración real");
   string thumbnail=WebMedia.DownloadThumbnail(youtube,root,CancellationToken.None);Check(thumbnail!=null&&File.Exists(thumbnail),"miniatura de YouTube queda guardada en caché");
   var project=Store.New();project.assets.Add(youtube);Media.Insert(project,youtube.id,project.Active.tracks.First(t=>t.kind=="video").id,0);Store.Validate(project);
   string saved=Path.Combine(root,"video.prometheus.json");Store.Save(project,saved);project=Store.Load(saved);youtube=project.assets.First(a=>a.id==youtube.id);
   Check(project.Active.clips.Any(c=>c.asset_id==youtube.id)&&Media.SelectRepresentation(project.assets.First(a=>a.id==youtube.id),true)==null,"medio virtual persiste y queda editable antes de descargar");
   using(var blank=Rendering.Frame(project,0.5,CancellationToken.None))Check(blank.Width==640,"monitor tolera vídeo web aún no descargado");
   var preview=WebMedia.ResolveYouTube(youtube,root,CancellationToken.None,null,true);youtube.representations.Add(preview);
   Check(preview.role=="preview"&&File.Exists(preview.locator.path),"vista ligera descargada y local");
   using(var frame=Rendering.Frame(project,0.5,CancellationToken.None))Check(frame.Width==640,"monitor usa vista ligera web");
   var final=WebMedia.ResolveYouTube(youtube,root,CancellationToken.None,null);youtube.representations.Add(final);
   Check(final.role=="final"&&File.Exists(final.locator.path),"vídeo completo resuelto para exportación");
   string args=Rendering.Build(project,true,true,640,360,60,Path.Combine(root,"out.mp4"),0);
   Check(args.Contains(final.locator.path),"render final utiliza el medio completo");
   Check(WebMedia.ResolveYouTube(youtube,root,CancellationToken.None,null).locator.path==final.locator.path,"medio completo se reutiliza desde caché");
   foreach(var clip in project.Active.clips)clip.duration=Rational.Time(1);
   Store.Validate(project);string exported=Path.Combine(root,"resultado.mp4");Rendering.Export(project,exported,CancellationToken.None);
   Check(File.Exists(exported)&&new FileInfo(exported).Length>0,"vídeo web resuelto se exporta correctamente");
   Console.WriteLine("PASS TOTAL "+count);
  }finally{try{Directory.Delete(root,true);}catch{}}
 }
}
