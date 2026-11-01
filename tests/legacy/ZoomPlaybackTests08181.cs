using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class ZoomPlaybackTests08181 {
 static void Main(string[] args){
  Tools.Root=Path.GetFullPath(args[0]);var asset=Media.Inspect(Path.Combine(Tools.Root,"demo","video.mp4"),CancellationToken.None);
  var project=Store.New();project.assets.Add(asset);var track=project.Active.tracks.First(t=>t.kind=="video");var stream=asset.streams.First(s=>s.kind=="video");
  var clip=new Clip{id=Store.Id(),name="Zoom",asset_id=asset.id,stream_id=stream.id,track_id=track.id,timeline_start=Rational.Frame(0),duration=Rational.Frame(1),source_in=Rational.Time(0)};project.Active.clips.Add(clip);
  ClipKeyframes.Arm(clip,"transform.scale");ClipKeyframes.Set(clip,"transform.scale",0,1);ClipKeyframes.Set(clip,"transform.scale",1,1.5);Store.Validate(project);
  foreach(int fps in new[]{30,60}){
   var times=new List<double>();Rendering.Play(project,0,Path.GetTempPath(),CancellationToken.None,(bitmap,time)=>{times.Add(time);bitmap.Dispose();},message=>{},fps,160,90,1);
   if(times.Count<fps*0.85)throw new Exception("Se perdieron demasiados fotogramas de zoom a "+fps+" FPS: "+times.Count);
   for(int i=1;i<times.Count;i++)if(times[i]<=times[i-1])throw new Exception("Tiempos de zoom no monotónicos");
   Console.WriteLine("PASS Zoom "+fps+" FPS: "+times.Count+" fotogramas, tiempos crecientes");
  }
  Console.WriteLine("ZoomPlaybackTests08181 OK");
 }
}
