using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;
class MilestoneFrame01034 {
 static int Main(string[] args){try{
  string folder=Path.GetFullPath(args[0]);Directory.CreateDirectory(folder);string path=Path.Combine(folder,"capture.png");
  using(var image=new Bitmap(640,360))image.Save(path,ImageFormat.Png);
  var p=Store.New();SequenceCanvas.Set(p.Active,640,360);var original=Media.Inspect(path,CancellationToken.None);p.assets.Add(original);
  var video=p.Active.tracks.First(t=>t.kind=="video");Media.Insert(p,original.id,video.id,0);p.Active.clips.First().duration=Rational.Frame(10);
  string marker=Markers.Add(p.Active,4,"Después");var inserted=Media.Inspect(path,CancellationToken.None);
  var clip=FrameInsertion01034.Insert(p,inserted,3);Store.Validate(p);
  if(Math.Abs(clip.timeline_start.Seconds-3)>0.000001||Math.Abs(clip.duration.Seconds-5)>0.000001)throw new Exception("Captura no insertada en la duración prevista");
  if(!p.Active.clips.Any(c=>c.asset_id==original.id&&Math.Abs(c.timeline_start.Seconds-8)<0.000001))throw new Exception("El medio posterior no se desplazó");
  if(Markers.Read(p.Active).First(m=>m.Id==marker).Frame!=540)throw new Exception("El marcador no se desplazó");
  Console.WriteLine("PASS frame-insert/ripple/marker/undo-ready");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
