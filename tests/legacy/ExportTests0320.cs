using System;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class ExportTests0320 {
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Main(string[] args){
  Tools.Root=Path.GetFullPath(args[0]);
  var project=Store.New();SequenceCanvas.Set(project.Active,360,640);
  var image=Media.Inspect(Path.Combine(Tools.Root,"demo","grafico.png"),CancellationToken.None);project.assets.Add(image);
  Media.Insert(project,image.id,project.Active.tracks.First(t=>t.kind=="video").id,0);
  project.Active.clips.First().duration=Rational.Frame(1);Store.Validate(project);
  var options=ExportOptions.For(project);options.Fps=30;options.Crf=23;options.Preset="fast";options.AudioBitrateKbps=256;
  string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"export-test-"+Guid.NewGuid().ToString("N")+".mp4");
  try{
   string command=Rendering.Build(project,true,true,360,640,30,file,0,false,1,options);
   Check(command.Contains("-crf 23")&&command.Contains("-r 30")&&command.Contains("-b:a 256k"),"opciones avanzadas llegan al codificador");
   Rendering.Export(project,file,CancellationToken.None,null,options);
   var exported=Media.Inspect(file,CancellationToken.None);var video=exported.streams.First(s=>s.kind=="video");
   Check(video.width==360&&video.height==640&&Math.Abs(video.nominal_fps.Seconds-30)<0.1,"MP4 vertical exportado a 30 FPS");
   options.RateControl="bitrate";options.TargetBitrateKbps=1200;
   Check(Rendering.Build(project,true,true,360,640,30,file,0,false,1,options).Contains("-b:v 1200k"),"bitrate objetivo disponible en avanzado");
   options.Width=640;options.Height=360;bool wrongRatio=false;try{options.Validate(project);}catch{wrongRatio=true;}Check(wrongRatio,"exportación rechaza proporción distinta del lienzo");
   options=ExportOptions.For(project);options.UsePreview=true;
   var proxyReport=ExportPreflight.Inspect(project,true,0,options.Width,options.Height,true);
   Check(proxyReport.CanExport&&proxyReport.checks.Any(c=>c.code=="preview-export"),"preflight avisa sobre exportación con vistas ligeras");
   image.source=new MediaSource{kind="web",uri="https://example.org/pending"};image.representations.Clear();image.locator=null;
   var missing=ExportPreflight.Inspect(project,true,0,360,640,true);
   Check(!missing.CanExport&&missing.checks.Any(c=>c.code=="preview-unavailable"),"vista ligera ausente bloquea exportación rápida");
   var normal=ExportPreflight.Inspect(project,true,0,360,640,false);
   Check(normal.CanExport&&normal.checks.Any(c=>c.code=="web-pending"&&c.message.Contains("completo")),"exportación final resuelve web completo");
   var audioProject=Store.New();var voice=Media.Inspect(Path.Combine(Tools.Root,"demo","voz.wav"),CancellationToken.None);audioProject.assets.Add(voice);
   Media.Insert(audioProject,voice.id,audioProject.Active.tracks.First(t=>t.kind=="audio").id,0);
   audioProject.Active.clips.First().duration=Rational.Time(1);Store.Validate(audioProject);
   string wav=Path.ChangeExtension(file,".wav");
   try{var wavOptions=ExportOptions.For(audioProject);wavOptions.Video=false;Rendering.Export(audioProject,wav,CancellationToken.None,null,wavOptions);
    Check(new FileInfo(wav).Length>1000&&Media.Inspect(wav,CancellationToken.None).streams.Any(s=>s.kind=="audio"),"WAV de audio exportado realmente");}
   finally{if(File.Exists(wav))File.Delete(wav);}
  }finally{if(File.Exists(file))File.Delete(file);}
 }
}
