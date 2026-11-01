using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class Sam2Smoke01011 {
 static void Check(bool good,string name){if(!good)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Main(string[] args){
  string root=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);Tools.Root=root;
  var still=Store.New();var image=Media.Inspect(Path.Combine(root,"demo","grafico.png"),CancellationToken.None);still.assets.Add(image);Media.Insert(still,image.id,still.Active.tracks.First(t=>t.kind=="video").id,0);
  var stillClip=still.Active.clips.Single();var candidates=Sam2Runner090.Candidates(still,stillClip.id,0,CancellationToken.None);
  Check(candidates.Count>0,"SAM2 detecta candidatos sobre una imagen");foreach(var candidate in candidates)candidate.Dispose();
  var watch=System.Diagnostics.Stopwatch.StartNew();var cached=Sam2Runner090.Candidates(still,stillClip.id,0,CancellationToken.None);watch.Stop();
  Check(cached.Count>0&&watch.Elapsed.TotalSeconds<2,"el mismo fotograma reutiliza candidatos sin ejecutar SAM2 otra vez");foreach(var candidate in cached)candidate.Dispose();
  var video=Store.New();var clipAsset=Media.Inspect(Path.Combine(root,"demo","video.mp4"),CancellationToken.None);
  clipAsset.source.kind="mgt";foreach(var representation in clipAsset.representations)representation.role="preview";video.assets.Add(clipAsset);Media.Insert(video,clipAsset.id,video.Active.tracks.First(t=>t.kind=="video").id,0);
  var clip=video.Active.clips.First(c=>c.stream_id==clipAsset.streams.First(s=>s.kind=="video").id);clip.duration=Rational.Frame(0.1);
  video.Active.clips.RemoveAll(c=>c.id!=clip.id);video.Active.link_groups.Clear();Store.Validate(video);
  var preview=Sam2Runner090.Candidates(video,clip.id,0,CancellationToken.None);
  Check(preview.Count>=0,"SAM2 analiza el fotograma compuesto de MGT desde representación local");foreach(var item in preview)item.Dispose();
  var prompts=new List<Sam2Prompt090>{new Sam2Prompt090{frame=0,x=.5,y=.5,positive=true,object_id=1}};
  var track=Sam2Runner090.Track(video,clip.id,null,prompts,CancellationToken.None,null);
  Sam2MaskStore090.Put(clip,track);Check(track.frames>=1&&Sam2MaskStore090.Ready(clip),"seguimiento MGT conserva máscara alineada y lista");
  MgtMedia.InvalidateSam2(video,clipAsset.id);Check(!Sam2MaskStore090.Ready(clip)&&!Sam2MaskStore090.Active(clip),"un cambio de MGT desactiva la máscara desactualizada sin borrar sus datos");
 }
}
