using System;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class AudioPlaceholderTests0313 {
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Main(string[] args){
  Tools.Root=Path.GetFullPath(args[0]);var p=Store.New();
  var reserved=GeneratedMedia.CreateAudioPlaceholder("Narración pendiente","",5);p.assets.Add(reserved);
  string track=p.Active.tracks.First(t=>t.kind=="audio").id;Media.Insert(p,reserved.id,track,0);
  var clip=p.Active.clips.Single();Store.Validate(p);
  Check(clip.stream_id==reserved.streams.Single().id&&reserved.streams.Single().kind=="audio","espacio reservado de audio ocupa una pista de audio");
  Check(ExportPreflight.Inspect(p,false,0).checks.Any(c=>c.code=="placeholder"&&c.severity=="error"),"preflight exige conectar el audio antes de exportar");
  string argsPreview=Rendering.Build(p,false,true,640,360,15,"audio-raw",0,true);
  Check(argsPreview.Contains("anullsrc=r=48000:cl=stereo"),"la reproducción temporal genera silencio compatible");
  var wav=Media.Inspect(Path.Combine(Tools.Root,"demo","voz.wav"),CancellationToken.None);p.assets.Add(wav);
  PlaceholderReplacement.Replace(p,clip.id,wav.id,0);Store.Validate(p);
  Check(p.Active.clips.Single().stream_id==wav.streams.Single().id&&ShortTail.Has(clip),"audio corto se conecta sin cambiar duración editorial");
  Check(ExportPreflight.Inspect(p,false,0).checks.Any(c=>c.code=="short-tail"),"preflight avisa del tramo de audio sin fuente");
  string output=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"audio-placeholder-0313.wav");Rendering.Export(p,output,CancellationToken.None);
  Check(File.Exists(output)&&new FileInfo(output).Length>1000,"audio conectado se exporta durante los cinco segundos");
  var editor=new Editor();editor.Reset(p);editor.Trim(clip.id,false,2);
  Check(!ShortTail.Has(editor.Project.Active.clips.Single()),"recortar la cola de silencio devuelve el límite real");
 }
}
