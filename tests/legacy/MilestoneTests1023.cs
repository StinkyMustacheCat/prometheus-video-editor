using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using System.Diagnostics;
using Prometheus;

class MilestoneTests1023 {
 static int Main(string[] args){try{
  string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
  string imageFile=Path.Combine(output,"subject.png");
  using(var image=new Bitmap(640,360))using(var g=Graphics.FromImage(image)){g.Clear(Color.White);g.FillRectangle(Brushes.DarkBlue,180,65,280,245);image.Save(imageFile,ImageFormat.Png);}
  var project=Store.New();SequenceCanvas.Set(project.Active,640,360);var asset=Media.Inspect(imageFile,CancellationToken.None);project.assets.Add(asset);
  var lane=project.Active.tracks.First(t=>t.kind=="video");Media.Insert(project,asset.id,lane.id,0);var clip=project.Active.clips.First(c=>c.track_id==lane.id);
  ClipKeyframes.Set(clip,"transform.x",0,-.2);ClipKeyframes.Set(clip,"transform.x",4,.2);
  var window=Rendering.PreviewWindow(project,2,1.0/60);var windowClip=window.Active.clips.First(c=>c.id==clip.id);
  double expected=ClipKeyframes.Value(clip,"transform.x",2,0),actual=ClipKeyframes.Value(windowClip,"transform.x",0,0);
  if(Math.Abs(expected-actual)>.001)throw new Exception("Los fotogramas clave de imagen no siguen el reloj de vista previa");
  var prompt=new List<Sam2Prompt090>{new Sam2Prompt090{frame=0,x=.5,y=.5,positive=true,object_id=1}};
  var watch=Stopwatch.StartNew();using(var first=Sam2Runner090.Preview(project,clip.id,0,prompt,CancellationToken.None)){}long coldMs=watch.ElapsedMilliseconds;
  watch.Restart();using(var mask=Sam2Runner090.Preview(project,clip.id,0,prompt,CancellationToken.None)){
   string folder=Path.Combine(output,"mask"),layer=Path.Combine(folder,"object-1");Directory.CreateDirectory(layer);mask.Save(Path.Combine(layer,"000000.png"),ImageFormat.Png);
   var data=new Sam2MaskTrack090{directory=folder,frames=1,width=mask.Width,height=mask.Height,static_preview=true,anchor_frame=0,source_in=clip.source_in.Seconds,duration=clip.duration.Seconds,speed=clip.speed.Seconds,target="opacity",layers=new List<Sam2Layer0104>{new Sam2Layer0104{object_id=1}}};
   Sam2MaskStore090.Put(clip,data);Store.Validate(project);
   if(!Sam2MaskStore090.Ready(clip))throw new Exception("La máscara estática no quedó lista");
   using(var frame=Rendering.FrameExact(project,2,CancellationToken.None,640,360,false))frame.Save(Path.Combine(output,"masked-frame.png"),ImageFormat.Png);
  }long warmMs=watch.ElapsedMilliseconds;
  watch.Restart();foreach(var candidate in Sam2Runner090.Candidates(project,clip.id,1,CancellationToken.None))candidate.Dispose();long candidateCold=watch.ElapsedMilliseconds;
  watch.Restart();foreach(var candidate in Sam2Runner090.Candidates(project,clip.id,2,CancellationToken.None))candidate.Dispose();long candidateWarm=watch.ElapsedMilliseconds;
  Sam2Worker01023.Shutdown();
  Console.WriteLine("PASS image-keyframes/SAM2-preview/static-mask/render preview="+coldMs+"/"+warmMs+"ms candidates="+candidateCold+"/"+candidateWarm+"ms");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
