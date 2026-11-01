using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class MilestoneTests1017 {
 static int Main(string[] args){try{
  string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
  string imageFile=Path.Combine(output,"grid.png");using(var image=new Bitmap(640,360))using(var graphics=Graphics.FromImage(image)){graphics.Clear(Color.DarkSlateBlue);graphics.FillRectangle(Brushes.Gold,30,30,120,80);image.Save(imageFile,ImageFormat.Png);}
  var project=Store.New();SequenceCanvas.Set(project.Active,640,360);var asset=Media.Inspect(imageFile,CancellationToken.None);project.assets.Add(asset);
  var track=project.Active.tracks.First(t=>t.kind=="video");Media.Insert(project,asset.id,track.id,0);var clip=project.Active.clips.First(c=>c.track_id==track.id);clip.duration=Rational.Frame(2);
  var zoom=EffectContracts.New(EffectContracts.ZoomOutDynamicId);zoom.parameters["zoom_percent"].number=80;clip.effects.Add(zoom);
  ClipKeyframes.Set(clip,"transform.scale",0,.5);ClipKeyframes.Set(clip,"transform.scale",1.5,2.5);
  Store.Validate(project);
  using(var frame=Rendering.FrameExact(project,1.8,CancellationToken.None,640,360,false))frame.Save(Path.Combine(output,"late-frame.png"),ImageFormat.Png);
  string video=Path.Combine(output,"animated.mp4");Rendering.Export(project,video,CancellationToken.None);
  if(new FileInfo(video).Length<1000)throw new Exception("La exportación animada está vacía");
  Console.WriteLine("PASS zoom/keyframes/FFmpeg export");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
