using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Prometheus;

class SelectiveExportTests0322 {
 static void Check(bool yes,string name){if(!yes)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Main(string[] args){
  Tools.Root=Path.GetFullPath(args[0]);string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"selective-test-0322");Directory.CreateDirectory(root);
  string source=Path.Combine(root,"segment.mp4");
  if(!File.Exists(source))Tools.Run(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -y -f lavfi -i "+Tools.Q("testsrc2=duration=9:size=320x180:rate=60")+" -f lavfi -i "+Tools.Q("sine=frequency=440:duration=9")+" -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -ar 48000 -ac 2 -shortest "+Tools.Q(source),CancellationToken.None);
  string progressFile=Path.Combine(root,"progress-"+Guid.NewGuid().ToString("N")+".mp4");int observed=-1;
  try{Tools.RunProgress(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -y -progress pipe:1 -nostats -i "+Tools.Q(source)+" -t 1 -c:v libx264 -preset ultrafast -an "+Tools.Q(progressFile),1,CancellationToken.None,percent=>observed=Math.Max(observed,percent));Check(observed>0&&File.Exists(progressFile),"preparación informa porcentaje real de FFmpeg");}
  finally{if(File.Exists(progressFile))File.Delete(progressFile);}
  var asset=WebMedia.FromMetadata("https://www.youtube.com/watch?v=test123","{\"id\":\"test123\",\"title\":\"Prueba selectiva\",\"duration\":100,\"fps\":60,\"width\":320,\"height\":180,\"vcodec\":\"h264\",\"acodec\":\"aac\"}");
  var project=Store.New();SequenceCanvas.Set(project.Active,320,320);project.assets.Add(asset);
  Media.PlaceInFreeTrack(project,asset.id,project.Active.tracks.First(t=>t.kind=="video").id,0,20,5);
  var found=Media.Inspect(source,CancellationToken.None);var info=new FileInfo(source);
  RangeMedia.Write(asset,new[]{new MediaRange{start=18,end=27,path=source,size_bytes=info.Length,modified_utc=info.LastWriteTimeUtc.ToString("o",CultureInfo.InvariantCulture),stream_ordinals=new Dictionary<string,int>{{asset.streams[0].id,found.streams.First(s=>s.kind=="video").ordinal},{asset.streams[1].id,found.streams.First(s=>s.kind=="audio").ordinal}}}});
  Check(RangeMedia.CoversUsed(project,asset),"caché cubre vídeo y audio utilizados");
  using(var form=new MainForm()){
   int completed=0;var method=typeof(MainForm).GetMethod("ResolveUsedWeb",BindingFlags.Instance|BindingFlags.NonPublic);
   method.Invoke(form,new object[]{Store.Copy(project),CancellationToken.None,new Action<string>(message=>{}),null,null,false,true,new Action<int,int,string>((done,total,name)=>completed=done)});
   Check(completed==1,"resolución selectiva de exportación reutiliza la caché");
  }
  var report=ExportPreflight.Inspect(project,true,0,320,320,false,true);
  Check(report.CanExport&&report.checks.Any(x=>x.code=="web-ranges-cached"),"preflight selectivo reconoce tramos en caché");
  string output=Path.Combine(root,"selective-"+Guid.NewGuid().ToString("N")+".mp4");
  try{
   var options=ExportOptions.For(project);options.Width=320;options.Height=320;options.Fps=30;options.Preset="ultrafast";options.SelectiveWeb=true;
   Rendering.Export(project,output,CancellationToken.None,null,options);
   double length=Media.Inspect(output,CancellationToken.None).streams.First(s=>s.kind=="video").duration.Seconds;
   Check(Math.Abs(length-5)<0.12,"MP4 final usa únicamente tramo en caché");
   bool blocked=false;try{Rendering.Export(project,output,CancellationToken.None);}catch(Exception ex){blocked=ex.Message.Contains("medio completo");}
   Check(blocked,"modo normal sigue requiriendo medio completo");
  }finally{if(File.Exists(output))File.Delete(output);}
 }
}
