using System;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class SyncExportTests08201 {
 static void Main(string[] args){Tools.Root=args[0];var project=Store.New();SequenceCanvas.Set(project.Active,640,360);var asset=Media.Inspect(args[1],CancellationToken.None);project.assets.Add(asset);var video=project.Active.tracks.First(t=>t.kind=="video"&&t.order==0);Media.Insert(project,asset.id,video.id,0);Console.WriteLine("origin "+asset.presentation_origin.Seconds+" streams "+String.Join(",",asset.streams.Select(st=>st.kind+":"+st.pts_origin.Seconds))+" clips "+String.Join(",",project.Active.clips.Select(c=>c.timeline_start.Seconds.ToString())));var clip=project.Active.clips.First(c=>c.track_id==video.id);ClipFraming.Set(clip,"fit",1.2,0,0);Store.Validate(project);Rendering.Export(project,args[2],CancellationToken.None);if(!File.Exists(args[2]))throw new Exception("No se exportó la muestra");Console.WriteLine("OK exportación audiovisual con escala: "+new FileInfo(args[2]).Length+" bytes");}
}
