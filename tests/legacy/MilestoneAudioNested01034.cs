using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class MilestoneAudioNested01034 {
 static int Main(string[] args){try{
  string folder=Path.GetFullPath(args[0]);Directory.CreateDirectory(folder);
  string video=Path.Combine(folder,"video.png"),first=Path.Combine(folder,"a.wav"),second=Path.Combine(folder,"b.wav");
  using(var image=new Bitmap(640,360)){using(var g=Graphics.FromImage(image))g.Clear(Color.Orange);image.Save(video,ImageFormat.Png);}
  Tools.Run(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -y -f lavfi -i sine=frequency=440:sample_rate=48000:duration=1 -c:a pcm_s16le "+Tools.Q(first),CancellationToken.None);
  Tools.Run(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -y -f lavfi -i sine=frequency=660:sample_rate=48000:duration=1 -c:a pcm_s16le "+Tools.Q(second),CancellationToken.None);
  var project=Store.New();SequenceCanvas.Set(project.Active,640,360);var videoTrack=project.Active.tracks.First(t=>t.kind=="video");var audioTracks=project.Active.tracks.Where(t=>t.kind=="audio").OrderBy(t=>t.order).ToList();while(audioTracks.Count<2){Store.AddTrack(project.Active,"audio");audioTracks=project.Active.tracks.Where(t=>t.kind=="audio").OrderBy(t=>t.order).ToList();}
  var paths=new[]{Tuple.Create(video,videoTrack.id),Tuple.Create(first,audioTracks[0].id),Tuple.Create(second,audioTracks[1].id)};
  foreach(var pair in paths){var asset=Media.Inspect(pair.Item1,CancellationToken.None);project.assets.Add(asset);Media.Insert(project,asset.id,pair.Item2,0);}
  var ids=project.Active.clips.Select(c=>c.id).ToList();var outer=NestedMedia0910.Create(project,ids,"Audio compuesto");var companion=NestedMedia0910.Companion(project,outer);if(companion==null)throw new Exception("No se creó audio anidado");
  companion.gain_db=6;companion.effects.Add(EffectContracts.New(EffectContracts.HighPassId));Store.Validate(project);
  var flat=NestedMedia0910.Flatten(project);if(flat.Active.clips.Any(c=>c.effects.Any(e=>e.effect_id==EffectContracts.HighPassId)))throw new Exception("El efecto del contenedor pasó a un hijo");
  string output=Path.Combine(folder,"nested.wav"),command=Rendering.Build(project,false,true,640,360,30,output,0,false);
  if(command.Split(new[]{"highpass="},StringSplitOptions.None).Length!=2||!command.Contains("nested_audio_out_"))throw new Exception("El efecto no se aplicó una vez después de mezclar");
  Tools.Run(Tools.FFmpeg,command,CancellationToken.None);if(!File.Exists(output)||new FileInfo(output).Length<30000)throw new Exception("No se produjo audio anidado");
  Console.WriteLine("PASS nested-audio/composite-effect/export");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
