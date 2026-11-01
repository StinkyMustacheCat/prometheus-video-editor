using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class PreviewPendingTests0310 {
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Main(string[] args){
  Tools.Root=Path.GetFullPath(args[0]);
  Rendering.GeneratedCacheRoot=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"generated-test-0310");
  var project=Store.New();var video=Media.Inspect(Path.Combine(Tools.Root,"demo","video.mp4"),CancellationToken.None);
  video.source=new MediaSource{kind="web",uri="https://www.youtube.com/watch?v=placeholder",resolver_type="youtube/yt-dlp"};
  video.representations.Clear();video.locator=null;project.assets.Add(video);
  string v1=project.Active.tracks.First(t=>t.kind=="video"&&t.order==0).id;
  string v2=project.Active.tracks.First(t=>t.kind=="video"&&t.order==1).id;
  Media.Insert(project,video.id,v1,0);Store.Validate(project);
  string command=Rendering.Build(project,true,false,640,360,15,"raw",0,true);
  Check(command.Contains("vista-ligera-640x360.png"),"la vista previa usa una tarjeta local para el medio web pendiente");
  using(var frame=Rendering.Frame(project,0.5,CancellationToken.None)){
   Color pixel=frame.GetPixel(6,6);Check(pixel.R>20&&pixel.B>20,"la tarjeta aparece en el monitor sin proxy");
  }
  var solid=GeneratedMedia.CreateSolid("Superior","EE3311",2);project.assets.Add(solid);Media.Insert(project,solid.id,v2,0);
  using(var frame=Rendering.Frame(project,0.5,CancellationToken.None)){
   Color pixel=frame.GetPixel(6,6);Check(pixel.R>pixel.B*2,"una capa superior sigue visible sobre la tarjeta");
  }
  int count=0;double last=-1;
  using(var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(12))){
   Rendering.Play(project,0,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"play-test-0310"),cancel.Token,(image,time)=>{count++;last=time;image.Dispose();},message=>{},15,320,180);
  }
  Check(count>=5&&last>0.2,"la reproducción avanza mientras la vista ligera está pendiente");
 }
}
