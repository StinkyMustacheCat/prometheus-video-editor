using System;
using System.Linq;
using Prometheus;

class PlaceholderUndoTests0491 {
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Main(){
  var editor=new Editor();var original=editor.Project;
  var placeholder=GeneratedMedia.CreatePlaceholder("Pendiente","Prueba",5);
  editor.Commit(p=>{p.assets.Add(placeholder);Media.Insert(p,placeholder.id,p.Active.tracks.First(t=>t.kind=="video").id,0);});
  string clipId=editor.Project.Active.clips.Single().id;
  var web=new Asset{id=Store.Id(),name="Web de prueba",source=new MediaSource{kind="web",uri="https://www.youtube.com/watch?v=demo",resolver_type="youtube/yt-dlp"},recommended_duration=Rational.Frame(10),presentation_origin=Rational.Time(0)};
  web.streams.Add(new StreamInfo{id=Store.Id(),ordinal=0,kind="video",codec="h264",pts_origin=Rational.Time(0),time_base=new Rational(1,60),duration=Rational.Frame(10),width=640,height=360});
  Action<Project> prepared=p=>{var asset=p.assets.Single(a=>a.id==web.id);asset.representations.RemoveAll(r=>r.role=="proxy");asset.representations.Add(new MediaRepresentation{role="proxy",locator=new Locator{path="C:\\cache\\preview.mp4"}});};
  editor.Commit(p=>{p.assets.Add(web);PlaceholderReplacement.Replace(p,clipId,web.id,0);});
  Check(editor.UpdateDerivedAsset(web.id,web.source.uri,prepared),"la vista ligera actualiza el proyecto visible");
  editor.Undo();
  Check(GeneratedMedia.IsPlaceholder(editor.Project.assets.Single(a=>a.id==editor.Project.Active.clips.Single().asset_id)),"un solo deshacer recupera el espacio reservado");
  Check(editor.Project.assets.All(a=>a.id!=web.id),"deshacer retira el nuevo medio del proyecto");
  editor.Redo();
  Check(editor.Project.Active.clips.Single().asset_id==web.id&&editor.Project.assets.Single(a=>a.id==web.id).representations.Count==1,"rehacer restaura el vínculo con vista ligera");
  editor.Undo();
  Check(!editor.UpdateDerivedAsset(web.id,web.source.uri,prepared),"preparación posterior a deshacer no reconecta el clip");
  editor.Redo();
  Check(editor.Project.Active.clips.Single().asset_id==web.id&&editor.Project.assets.Single(a=>a.id==web.id).representations.Count==1,"rehacer recupera preparación terminada mientras estaba deshecho");
  editor.Commit(p=>p.name="Edición posterior");
  Check(editor.UpdateDerivedAsset(web.id,web.source.uri,p=>p.assets.Single(a=>a.id==web.id).representations.Add(new MediaRepresentation{role="original",locator=new Locator{path="C:\\cache\\final.mp4"}})),"preparación tras otra edición se conserva");
  editor.Undo();
  Check(editor.Project.Active.clips.Single().asset_id==web.id&&editor.Project.assets.Single(a=>a.id==web.id).representations.Count==2,"deshacer edición posterior mantiene las representaciones");
  editor.Undo();
  Check(GeneratedMedia.IsPlaceholder(editor.Project.assets.Single(a=>a.id==editor.Project.Active.clips.Single().asset_id)),"otro deshacer sí revierte el vínculo");
 }
}
