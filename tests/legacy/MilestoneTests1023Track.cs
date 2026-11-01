using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using Prometheus;

class MilestoneTests1023Track {
 static int Main(string[] args){try{
  string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);string still=Path.Combine(output,"track-source.png"),video=Path.Combine(output,"track-source.mp4");
  using(var bitmap=new Bitmap(640,360))using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.LightGray);g.FillEllipse(Brushes.DarkBlue,190,40,260,280);bitmap.Save(still,ImageFormat.Png);}
  Tools.Run(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -y -loop 1 -framerate 60 -i "+Tools.Q(still)+" -t 1 -c:v libx264 -pix_fmt yuv420p "+Tools.Q(video),CancellationToken.None);
  var project=Store.New();SequenceCanvas.Set(project.Active,640,360);var asset=Media.Inspect(video,CancellationToken.None);project.assets.Add(asset);var lane=project.Active.tracks.First(t=>t.kind=="video");Media.Insert(project,asset.id,lane.id,0);var clip=project.Active.clips.First(c=>c.track_id==lane.id);clip.duration=Rational.Frame(1);Store.Validate(project);
  int progress=0,visuals=0;var prompts=new List<Sam2Prompt090>{new Sam2Prompt090{frame=0,x=.5,y=.5,positive=true,object_id=1}};
  var result=Sam2Runner090.Track(project,clip.id,Path.Combine(output,"project.pthproj"),prompts,CancellationToken.None,(pct,msg)=>progress=pct,null,(frame,mask,index)=>{if(!File.Exists(frame)||!File.Exists(mask))throw new Exception("Vista de máscara antes de producir fotograma");visuals++;});
  Sam2MaskStore090.Put(clip,result);Store.Validate(project);if(progress!=100||visuals<1||!Sam2MaskStore090.Ready(clip))throw new Exception("Seguimiento progresivo incompleto");
  Sam2Worker01023.Shutdown();Console.WriteLine("PASS SAM2 track frames="+result.frames+" previews="+visuals);return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
