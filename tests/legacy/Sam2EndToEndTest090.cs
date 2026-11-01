using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class Sam2EndToEndTest090 {
 static int Main(string[] args){try{
  string source=Path.GetFullPath(args[0]),projectFile=Path.GetFullPath(args[1]);var project=Store.New();var asset=Media.Inspect(source,CancellationToken.None);project.assets.Add(asset);var track=project.Active.tracks.First(t=>t.kind=="video");Media.Insert(project,asset.id,track.id,0);var clip=project.Active.clips.First(c=>c.asset_id==asset.id&&c.track_id==track.id);Store.Validate(project);
  var prompts=new List<Sam2Prompt090>{new Sam2Prompt090{frame=0,x=0.45,y=0.5,positive=true}};
  var mask=Sam2Runner090.Track(project,clip.id,projectFile,prompts,CancellationToken.None,(n,s)=>{if(n%20==0)Console.WriteLine(n+" "+s);});Sam2MaskStore090.Put(clip,mask);Store.Validate(project);
  using(var frame=Rendering.FrameExact(project,0.25,CancellationToken.None,256,144,false)){string output=Path.Combine(Path.GetDirectoryName(projectFile),"masked.png");frame.Save(output);Console.WriteLine("FRAME "+output+" "+frame.GetPixel(0,0)+" "+frame.GetPixel(110,70));}
  Store.Save(project,projectFile);var opened=Store.Load(projectFile);Console.WriteLine("READY "+Sam2MaskStore090.Ready(opened.Active.clips.First(c=>c.id==clip.id))+" FRAMES "+mask.frames);return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
