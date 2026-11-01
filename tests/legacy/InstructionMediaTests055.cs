using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Prometheus;

class InstructionMediaTests055 {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Seed(InstructionMediaAdapter adapter,string url,string kind,Asset asset){var field=typeof(InstructionMediaAdapter).GetField("prepared",BindingFlags.Instance|BindingFlags.NonPublic);((Dictionary<string,Asset>)field.GetValue(adapter))[kind+"|"+url]=asset;}
 static void Main(){
  const string url="https://www.youtube.com/watch?v=instruction055";
  const string json="{\"id\":\"instruction055\",\"title\":\"Vídeo de prueba\",\"duration\":10,\"vcodec\":\"avc1\",\"acodec\":\"aac\",\"width\":1920,\"height\":1080}";
  var asset=WebMedia.FromMetadata(url,json);var p=Store.New();string track=p.Active.tracks.First(t=>t.kind=="video").id;
  var adapter=new InstructionMediaAdapter();Seed(adapter,url,"video",asset);
  var doc=Instructions.New(p);doc.actions.Add(new InstructionAction{op="media.import_url",url=url,source_kind="video",track_id=track,at_seconds=2,placement="empty",result="web"});doc.actions.Add(new InstructionAction{op="effect.apply",scope="clip",clip_id="$web",catalog_id=EffectContracts.BasicColorId});
  var plan=Instructions.Preview(p,doc,adapter);Check(p.assets.Count==0&&plan.Result.assets.Count==1&&plan.Result.Active.clips.Count==2,"Importación simulada con vídeo y audio vinculados");Check(plan.Result.Active.clips.First(c=>c.track_id==track).effects.Count==1,"Alias de medio importado");
  var editor=new Editor();editor.Reset(p);Instructions.Apply(editor,doc,plan,adapter);Check(editor.Project.assets.Count==1&&editor.Project.Active.clips.Count==2,"Importar y colocar");editor.Undo();Check(editor.Project.assets.Count==0&&editor.Project.Active.clips.Count==0,"Importación reversible en un paso");
  var linked=Store.New();string vt=linked.Active.tracks.First(t=>t.kind=="video").id,at=linked.Active.tracks.First(t=>t.kind=="audio").id;
  var videoPlaceholder=GeneratedMedia.CreatePlaceholder("Reservado vídeo","",6);var audioPlaceholder=GeneratedMedia.CreateAudioPlaceholder("Reservado audio","",6);linked.assets.Add(videoPlaceholder);linked.assets.Add(audioPlaceholder);
  var videoClip=new Clip{id=Store.Id(),name="Reservado vídeo",asset_id=videoPlaceholder.id,stream_id=videoPlaceholder.streams[0].id,track_id=vt,timeline_start=Rational.Frame(3),duration=Rational.Frame(6),source_in=Rational.Time(0)};
  var audioClip=new Clip{id=Store.Id(),name="Reservado audio",asset_id=audioPlaceholder.id,stream_id=audioPlaceholder.streams[0].id,track_id=at,timeline_start=Rational.Frame(3),duration=Rational.Frame(6),source_in=Rational.Time(0)};
  linked.Active.clips.Add(videoClip);linked.Active.clips.Add(audioClip);linked.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=new List<string>{videoClip.id,audioClip.id}});Store.Validate(linked);
  var fill=Instructions.New(linked);fill.actions.Add(new InstructionAction{op="placeholder.fill_url",clip_id=videoClip.id,url=url,include_linked=true});
  var fillPlan=Instructions.Preview(linked,fill,adapter);Check(fillPlan.Result.Active.clips.All(c=>c.asset_id==asset.id),"Una URL rellena ambos componentes vinculados");Check(fillPlan.Result.Active.clips.All(c=>c.timeline_start.Seconds==3&&c.duration.Seconds==6),"Posición y duración preservadas");
  const string shortUrl="https://www.youtube.com/watch?v=instruction055short";var shortAsset=WebMedia.FromMetadata(shortUrl,"{\"id\":\"instruction055short\",\"title\":\"Corto\",\"duration\":2,\"vcodec\":\"avc1\",\"acodec\":\"aac\"}");Seed(adapter,shortUrl,"video",shortAsset);
  var shortFill=Instructions.New(linked);shortFill.actions.Add(new InstructionAction{op="placeholder.fill_url",clip_id=videoClip.id,url=shortUrl});var shortPlan=Instructions.Preview(linked,shortFill,adapter);
  Check(shortPlan.Result.Active.clips.All(c=>ShortTail.Has(c)&&c.duration.Seconds==6),"Un enlace más corto conserva la cola rayada");
  foreach(string mode in new[]{"overwrite","insert"}){
   var occupied=Store.New();string target=occupied.Active.tracks.First(t=>t.kind=="video").id;
   var solid=GeneratedMedia.CreateSolid("Base","112233",5);occupied.assets.Add(solid);
   occupied.Active.clips.Add(new Clip{id=Store.Id(),name="Base",asset_id=solid.id,stream_id=solid.streams[0].id,track_id=target,timeline_start=Rational.Frame(0),duration=Rational.Frame(5),source_in=Rational.Time(0)});Store.Validate(occupied);
   var command=Instructions.New(occupied);command.actions.Add(new InstructionAction{op="media.import_url",url=url,source_kind="video",track_id=target,at_seconds=2,placement=mode});
   var check=Instructions.Preview(occupied,command,adapter);Check(check.Result.Active.clips.Any(c=>c.asset_id==asset.id&&c.track_id==target&&Math.Abs(c.timeline_start.Seconds-2)<0.001),"Destino "+mode+" en pista elegida");
  }
  Console.WriteLine("InstructionMediaTests055 OK");
 }
}
