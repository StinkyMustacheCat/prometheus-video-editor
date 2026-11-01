using System;
using System.IO;
using System.Threading;
using Prometheus;

class SubtitleExportTests0820 {
 static void Main(string[] args){Tools.Root=args[0];var project=Store.New();var cue=SubtitleTrack0817.Add(project,"Hola mundo",0,1);var text=project.assets.Find(a=>a.id==cue.asset_id);text.extensions.Find(e=>e.@namespace==GeneratedMedia.Namespace).payload["timed_words"]="[{\"start\":0,\"end\":0.5,\"text\":\"Hola\"},{\"start\":0.5,\"end\":1,\"text\":\"mundo\"}]";SubtitleAnimation0820.Set(project.Active,File.ReadAllText(args[1]));string output=SubtitleAnimation0820.PrepareExport(project,args[2],CancellationToken.None,null);if(output==null||!File.Exists(output)||SubtitleTrack0817.Cues(project).Count!=0)throw new Exception("La capa MGT no sustituyó los subtítulos de texto para exportación");Store.Validate(project);using(var frame=Rendering.Frame(project,.5,CancellationToken.None,640,360)){int colored=0;for(int y=0;y<frame.Height;y+=8)for(int x=0;x<frame.Width;x+=8){var pixel=frame.GetPixel(x,y);if(pixel.R>20||pixel.G>20||pixel.B>20)colored++;}if(colored==0)throw new Exception("La capa MGT no aparece en el monitor de Prometheus");}string final=Path.Combine(args[2],"final.mp4");Rendering.Export(project,final,CancellationToken.None);if(!File.Exists(final)||new FileInfo(final).Length<1000)throw new Exception("La exportación final con subtítulos falló");Console.WriteLine("OK: capa MGT y MP4 final · "+new FileInfo(final).Length+" bytes");}
}
