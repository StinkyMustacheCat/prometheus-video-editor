using System;
using System.IO;
using System.Linq;
using Prometheus;

class PlaceholderTests035 {
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
 static Asset Video(string name,string sourceKind,double length){
  var asset=new Asset{id=Store.Id(),name=name,source=new MediaSource{kind=sourceKind,uri=sourceKind=="web"?"https://www.youtube.com/watch?v=test":"C:\\test.mp4"},presentation_origin=Rational.Time(0),recommended_duration=Rational.Frame(length)};
  asset.streams.Add(new StreamInfo{id=Store.Id(),ordinal=0,kind="video",codec="h264",pts_origin=Rational.Time(0),time_base=new Rational(1,60),duration=Rational.Frame(length),width=1920,height=1080});return asset;
 }
 static void Main(){
  var p=Store.New();string track=p.Active.tracks.First(t=>t.kind=="video").id;
  var reserve=GeneratedMedia.CreatePlaceholder("B-roll", "Escena por definir",5);p.assets.Add(reserve);
  Media.Insert(p,reserve.id,track,10);Media.Insert(p,reserve.id,track,20);
  var first=p.Active.clips.OrderBy(c=>c.timeline_start.Seconds).First();string id=first.id;
  var shortVideo=Video("Corto","local",3);var video=Video("Fuente larga","local",20);var web=Video("Fuente web","web",20);var solid=GeneratedMedia.CreateSolid("Imagen","F08020",5);
  p.assets.Add(shortVideo);p.assets.Add(video);p.assets.Add(web);p.assets.Add(solid);Store.Validate(p);
  var editor=new Editor();editor.Reset(p);editor.Commit(x=>PlaceholderReplacement.SetExpectedType(x,id,"video"));
  Check(PlaceholderReplacement.ExpectedType(editor.Project.Active.clips.Find(c=>c.id==id))=="video","tipo esperado persiste en el clip");
  int before=editor.Project.revision;bool rejected=false;try{editor.Commit(x=>PlaceholderReplacement.Replace(x,id,shortVideo.id,0));}catch(Exception){rejected=true;}
  Check(rejected&&editor.Project.revision==before,"fuente demasiado corta se rechaza sin cambio parcial");
  rejected=false;try{editor.Commit(x=>PlaceholderReplacement.Replace(x,id,solid.id,0));}catch(Exception){rejected=true;}
  Check(rejected,"tipo video rechaza una imagen");
  editor.Commit(x=>PlaceholderReplacement.Replace(x,id,video.id,7));
  var changed=editor.Project.Active.clips.Find(c=>c.id==id);
  Check(changed.asset_id==video.id&&changed.source_in.Seconds==7&&changed.timeline_start.Seconds==10&&changed.duration.Seconds==5,"sustitución conserva posición y duración");
  Check(editor.Project.Active.clips.Count(c=>GeneratedMedia.IsPlaceholder(editor.Project.assets.Find(a=>a.id==c.asset_id)))==1,"otra instancia del mismo placeholder queda intacta");
  editor.Undo();Check(GeneratedMedia.IsPlaceholder(editor.Project.assets.Find(a=>a.id==editor.Project.Active.clips.Find(c=>c.id==id).asset_id)),"deshacer recupera placeholder");
  editor.Redo();Check(editor.Project.Active.clips.Find(c=>c.id==id).asset_id==video.id,"rehacer recupera medio conectado");
  string saved=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"placeholder-test-035.prometheus.json");
  try{Store.Save(editor.Project,saved);var reloaded=Store.Load(saved);Check(reloaded.Active.clips.Find(c=>c.id==id).asset_id==video.id,"conexión persiste al guardar y reabrir");}
  finally{if(File.Exists(saved))File.Delete(saved);}
  string other=editor.Project.Active.clips.Find(c=>c.id!=id).id;
  editor.Commit(x=>PlaceholderReplacement.SetExpectedType(x,other,"web"));
  editor.Commit(x=>PlaceholderReplacement.Replace(x,other,web.id,2));
  Check(editor.Project.Active.clips.Find(c=>c.id==other).asset_id==web.id,"medio web virtual se conecta sin descargarlo completo");
 }
}
