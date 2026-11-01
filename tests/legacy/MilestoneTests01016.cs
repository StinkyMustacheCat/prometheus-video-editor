using System;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class MilestoneTests01016 {
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static Clip Put(Project project,Asset asset,Track track,double start,double duration){var clip=new Clip{id=Store.Id(),name=asset.name,asset_id=asset.id,stream_id=asset.streams[0].id,track_id=track.id,timeline_start=Rational.Frame(start),source_in=Rational.Time(0),duration=Rational.Frame(duration)};project.Active.clips.Add(clip);return clip;}
 static int Main(string[] args){try{
  var nested=Store.New();var solid=GeneratedMedia.CreateSolid("Prueba","446688",6);nested.assets.Add(solid);var video=nested.Active.tracks.Where(t=>t.kind=="video").OrderBy(t=>t.order).ToArray();
  var lower=Put(nested,solid,video[0],0,2);var upper=Put(nested,solid,video[1],0,2);var outer=NestedMedia0910.Create(nested,new[]{lower.id,upper.id},"Anidado");var obstacle=Put(nested,solid,video[0],0,2);
  var inner=NestedMedia0910.Read(outer);var first=inner.clips.First(c=>c.id==lower.id);NestedMedia0910.UpdateChild(nested,outer.id,first.id,1,1,first.track_id);var updated=NestedMedia0910.Read(outer).clips.First(c=>c.id==lower.id);Check(Math.Abs(updated.source_in.Seconds-1)<.0001,"El recorte izquierdo interno no actualizó la fuente");
  var ids=NestedMedia0910.Unnest(nested,outer.id);Store.Validate(nested);Check(ids.Count==2&&nested.Active.clips.Any(c=>c.id==obstacle.id),"Desanidar eliminó un medio ajeno");Check(video[0].order==0&&video[1].order==1,"Desanidar reordenó las pistas existentes");Check(nested.Active.tracks.Count(t=>t.kind=="video")==3,"No se creó exactamente la pista necesaria");
  var context=Store.New();for(int i=0;i<50;i++)SubtitleTrack0817.Add(context,"Frase "+i,i*.5,.4);string full=Instructions.CopyState(context,"sequence",null),compact=Instructions.CopyCompactState(context,"sequence",null);Check(compact.Contains("prometheus-ai-context")&&compact.Contains("Frase 49")&&compact.Contains("\"required\"")&&compact.Length<full.Length,"El contexto compacto perdió datos o no redujo tamaño");
  var audio=Store.New();var file=Path.GetFullPath(args[0]);var media=Media.Inspect(file,CancellationToken.None);audio.assets.Add(media);var tracks=audio.Active.tracks.Where(t=>t.kind=="audio").OrderBy(t=>t.order).ToArray();Media.Insert(audio,media.id,tracks[0].id,0);Media.Insert(audio,media.id,tracks[1].id,0);
  audio.Active.clips.First(c=>c.track_id==tracks[0].id).effects.Add(EffectContracts.New(EffectContracts.VoiceCleanId));Ducking01016.Set(audio.Active,new DuckingRule01016{target_track_id=tracks[1].id,voice_track_id=tracks[0].id});Store.Validate(audio);
  var duckAction=Instructions.New(audio);duckAction.actions.Add(new InstructionAction{op="audio.ducking.set",track_id=tracks[1].id,voice_track_id=tracks[0].id,number=-9});var duckPlan=Instructions.Preview(audio,duckAction);Check(duckPlan.Cards[0].status=="valid"&&Math.Abs(Ducking01016.For(duckPlan.Result.Active,tracks[1].id).amount_db+9)<.001,"La instrucción Auto-Ducking no se aplicó");
  duckAction.context_sequence_id=audio.Active.id;duckAction.context_in_frame=0;duckAction.context_out_frame=60;var restricted=Instructions.Preview(audio,duckAction);Check(restricted.Cards[0].status=="invalid","Auto-Ducking global no debe cambiarse con contexto In/Out limitado");
  string saved=Path.ChangeExtension(Path.GetFullPath(args[1]),".prometheus.json");Store.Save(audio,saved);audio=Store.Load(saved);Check(Ducking01016.For(audio.Active,tracks[1].id)!=null&&audio.Active.clips.Any(c=>c.effects.Any(e=>e.effect_id==EffectContracts.VoiceCleanId)),"No se guardaron los ajustes de voz");
  string output=Path.GetFullPath(args[1]);string command=Rendering.Build(audio,false,true,320,180,60,output,0);Check(command.Contains("sidechaincompress")&&command.Contains("afftdn")&&command.Contains("adeclick"),"Falta limpieza o atenuación en la mezcla");Tools.Run(Tools.FFmpeg,command,CancellationToken.None);Check(File.Exists(output)&&new FileInfo(output).Length>1000,"No se exportó la prueba de audio");
  if(args.Length>=4){var vfr=Store.New();var input=Media.Inspect(Path.GetFullPath(args[2]),CancellationToken.None);vfr.assets.Add(input);Media.Insert(vfr,input.id,vfr.Active.tracks.First(t=>t.kind=="video").id,0);Store.Validate(vfr);var options=ExportOptions.For(vfr);options.Width=640;options.Height=360;options.Fps=60;Rendering.Export(vfr,Path.GetFullPath(args[3]),CancellationToken.None,null,options);Check(File.Exists(Path.GetFullPath(args[3]))&&new FileInfo(Path.GetFullPath(args[3])).Length>1000,"La exportación VFR falló");}
  Console.WriteLine("PASS nested/context/voice/ducking");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
