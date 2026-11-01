using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class PreviewSyncTests08201 {
 static void Main(string[] args){Tools.Root=args[0];var project=Store.New();SequenceCanvas.Set(project.Active,640,360);var sound=Media.Inspect(args[1],CancellationToken.None);project.assets.Add(sound);Media.Insert(project,sound.id,project.Active.tracks.First(t=>t.kind=="audio").id,0);var image=GeneratedMedia.CreateSolid("Vista","335577",1);project.assets.Add(image);Media.Insert(project,image.id,project.Active.tracks.First(t=>t.kind=="video"&&t.order==0).id,0);int frames=0;double last=-1;var clock=Stopwatch.StartNew();Rendering.Play(project,0,args[2],CancellationToken.None,(bitmap,time)=>{frames++;if(time<=last)throw new Exception("El tiempo de vista previa retrocedió");last=time;bitmap.Dispose();},message=>{},30,640,360,1);if(frames<10||clock.Elapsed.TotalSeconds>6)throw new Exception("La vista previa se bloqueó o perdió demasiados fotogramas: "+frames+" / "+clock.Elapsed.TotalSeconds);Console.WriteLine("OK vista previa: "+frames+" fotogramas en "+clock.Elapsed.TotalSeconds.ToString("F2")+" s");}
}
