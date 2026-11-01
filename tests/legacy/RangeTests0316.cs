using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class RangeTests0316 {
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
 static void Main(string[] args){
  if(args.Length!=1)throw new Exception("Indica el paquete completo con runtime");Tools.Root=Path.GetFullPath(args[0]);
  string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"range-test-0316");Directory.CreateDirectory(root);
  string segmentPath=Path.Combine(root,"segment.mp4");
  if(!File.Exists(segmentPath))Tools.Run(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -y -f lavfi -i "+Tools.Q("testsrc2=duration=9:size=320x180:rate=60")+" -f lavfi -i "+Tools.Q("sine=frequency=440:duration=9")+" -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -ar 48000 -ac 2 -shortest "+Tools.Q(segmentPath),CancellationToken.None);
  var asset=WebMedia.FromMetadata("https://www.youtube.com/watch?v=test123","{\"id\":\"test123\",\"title\":\"Rangos de prueba\",\"duration\":100,\"fps\":60,\"width\":320,\"height\":180,\"vcodec\":\"h264\",\"acodec\":\"aac\"}");
  var project=Store.New();project.assets.Add(asset);
  string track=project.Active.tracks.First(t=>t.kind=="video").id;
  var result=Media.PlaceInFreeTrack(project,asset.id,track,0,20,5);
  Check(result.ClipIds.Count==2&&project.Active.clips.All(c=>Math.Abs(c.source_in.Seconds-20)<0.0001&&Math.Abs(c.duration.Seconds-5)<0.0001),"rango editorial crea vídeo y audio vinculados con fuente desplazada");
  var planned=RangeMedia.Plan(project,asset);
  Check(planned.Count==1&&Math.Abs(planned[0].start-18)<0.0001&&Math.Abs(planned[0].end-27)<0.0001,"plan añade handles y agrupa A/V en un solo tramo");
  var distant=Store.Copy(project);Media.PlaceInFreeTrack(distant,asset.id,track,10,80,4);
  Check(RangeMedia.Plan(distant,distant.assets.Single()).Count==2,"usos lejanos conservan dos descargas independientes");
  var inspected=Media.Inspect(segmentPath,CancellationToken.None);
  var info=new FileInfo(segmentPath);
  RangeMedia.Write(asset,new[]{new MediaRange{start=18,end=27,path=segmentPath,size_bytes=info.Length,modified_utc=info.LastWriteTimeUtc.ToString("o",CultureInfo.InvariantCulture),stream_ordinals=new Dictionary<string,int>{{asset.streams[0].id,inspected.streams.First(s=>s.kind=="video").ordinal},{asset.streams[1].id,inspected.streams.First(s=>s.kind=="audio").ordinal}}}});
  project=Store.Decode(Store.Encode(project));asset=project.assets.Single();
  Check(RangeMedia.CoversUsed(project,asset),"caché por tramos sobrevive al guardado y cubre los clips");
  Check(RangeMedia.ResolveUsed(project,asset,root,CancellationToken.None,null)==0,"resolver de nuevo reutiliza la caché sin red");
  Media.VerifySources(project,CancellationToken.None,false);
  string output=Path.Combine(root,"render.mp4");string command=Rendering.Build(project,true,true,320,180,60,output,0,false);
  Check(command.Contains(Tools.Q(segmentPath))&&command.Contains("trim=start=2"),"render usa el tramo y desplaza la lectura a 2 segundos");
  Tools.Run(Tools.FFmpeg,command,CancellationToken.None);
  var exported=Media.Inspect(output,CancellationToken.None);
  Check(Math.Abs(exported.streams.First(s=>s.kind=="video").duration.Seconds-5)<0.1,"exportación real desde caché selectiva dura 5 segundos");
  project.Active.clips.ForEach(c=>c.source_in=Rational.Time(60));
  Check(!RangeMedia.CoversUsed(project,asset),"mover el contenido fuera del tramo exige resolver más datos");
  Console.WriteLine("PASS rangos completos");
 }
}
