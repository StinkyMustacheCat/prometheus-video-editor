using System;
using System.Linq;
using Prometheus;

class MediaPropertiesTests0318 {
 static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static void Main(){
  var project=Store.New();
  var reserved=GeneratedMedia.CreatePlaceholder("B-roll pendiente","buscar escena",5);
  var solid=GeneratedMedia.CreateSolid("Fondo","102030",5);
  project.assets.Add(reserved);project.assets.Add(solid);
  string track=project.Active.tracks.First(t=>t.kind=="video").id;
  var first=new Clip{id=Store.Id(),name=reserved.name,track_id=track,asset_id=reserved.id,stream_id=reserved.streams[0].id,timeline_start=Rational.Frame(0),duration=Rational.Frame(5),source_in=Rational.Frame(0)};
  var second=new Clip{id=Store.Id(),name=solid.name,track_id=track,asset_id=solid.id,stream_id=solid.streams[0].id,timeline_start=Rational.Frame(8),duration=Rational.Frame(5),source_in=Rational.Frame(0)};
  project.Active.clips.Add(first);project.Active.clips.Add(second);Store.Validate(project);
  var editor=new Editor();editor.Reset(project);
  editor.Commit(p=>GeneratedMedia.Update(p,reserved.id,first.id,"Nuevo B-roll","otra escena",7));
  var updated=editor.Project.Active.clips.First(c=>c.id==first.id);
  Check(updated.duration.Seconds==7&&updated.name=="Nuevo B-roll"&&GeneratedMedia.Note(editor.Project.assets.First(a=>a.id==reserved.id))=="otra escena","edición conserva clip y asset, cambia nota, nombre y duración");
  bool collided=false;try{editor.Commit(p=>GeneratedMedia.Update(p,reserved.id,first.id,"Error","",9));}catch{collided=true;}
  Check(collided&&editor.Project.Active.clips.First(c=>c.id==first.id).duration.Seconds==7,"colisión rechazada sin cambiar el proyecto");
  editor.Commit(p=>GeneratedMedia.Update(p,solid.id,null,"Fondo azul","ABCDEF",10));
  Check(GeneratedMedia.ColorHex(editor.Project.assets.First(a=>a.id==solid.id))=="ABCDEF"&&editor.Project.Active.clips.First(c=>c.id==second.id).duration.Seconds==5,"color y duración inicial no alteran clips existentes");
  editor.Undo();Check(GeneratedMedia.ColorHex(editor.Project.assets.First(a=>a.id==solid.id))=="102030","deshacer recupera color");
  editor.Redo();Check(GeneratedMedia.ColorHex(editor.Project.assets.First(a=>a.id==solid.id))=="ABCDEF","rehacer restaura color");
  var audio=GeneratedMedia.CreateAudioPlaceholder("Audio","",5);editor.Commit(p=>p.assets.Add(audio));
  Check(GeneratedMedia.IsPlaceholder(audio)&&audio.streams[0].kind=="audio","propiedades también admiten audio reservado");
 }
}
