using System;
using System.IO;
using System.Linq;
using Prometheus;

class IntegrationTests0298 {
 static int count;
 static void Check(bool condition,string label){if(!condition)throw new Exception(label);Console.WriteLine("PASS "+label);count++;}
 static Clip ImageClip(string id,string track,double start,double duration){return new Clip{id=id,name=id,asset_id="image",stream_id="still",track_id=track,timeline_start=Rational.Time(start),duration=Rational.Time(duration),source_in=Rational.Time(0)};}
 static void Main(){
  var editor=new Editor();var sequence=editor.Project.Active;
  var videos=sequence.tracks.Where(t=>t.kind=="video").OrderByDescending(t=>t.order).ToList();
  editor.Project.assets.Add(new Asset{id="image",name="Imagen",source=new MediaSource{kind="local",uri="still.png"},presentation_origin=Rational.Time(0),streams={new StreamInfo{id="still",ordinal=0,kind="image",codec="png",pts_origin=Rational.Time(0),time_base=new Rational(1,60)}}});
  sequence.clips.Add(ImageClip("left",videos[0].id,0,2));sequence.clips.Add(ImageClip("right",videos[0].id,2,2));
  Store.Validate(editor.Project);
  editor.RollEditLinked("left","right",1.5,true);
  Check(editor.Project.Active.clips.First(c=>c.id=="left").End==1.5&&editor.Project.Active.clips.First(c=>c.id=="right").duration.Seconds==2.5,"Roll Edit de imágenes funciona hacia la izquierda");
  Check(editor.Project.Active.clips.First(c=>c.id=="right").source_in.Seconds==0,"una imagen no obtiene tiempo de fuente negativo");
  editor.RollEditLinked("left","right",2.5,true);
  Check(editor.Project.Active.clips.First(c=>c.id=="left").End==2.5&&editor.Project.Active.clips.First(c=>c.id=="right").duration.Seconds==1.5,"Roll Edit de imágenes funciona hacia la derecha");
  Markers.Add(editor.Project.Active,7.25,"Referencia");
  TrackPresentation.SetHeight(editor.Project.Active,videos[0].id,74);
  TrackPresentation.SetShowNames(editor.Project.Active,videos[0].id,false);
  string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"prometheus-integration-"+Guid.NewGuid().ToString("N")+".json");
  try{
   Store.Save(editor.Project,file);var reopened=Store.Load(file);
   Check(Markers.Read(reopened.Active).Single().Seconds==7.25&&TrackPresentation.Height(reopened.Active,videos[0].id)==74,"marcadores y presentación conviven en un proyecto guardado");
   Check(!TrackPresentation.ShowNames(reopened.Active,videos[0]),"mostrar nombres por pista persiste junto al montaje");
  }finally{if(File.Exists(file))File.Delete(file);if(File.Exists(file+".bak"))File.Delete(file+".bak");}
  editor.MoveTrackContents(videos[0].id,videos[1].id);
  Check(editor.Project.Active.clips.All(c=>c.track_id==videos[1].id)&&Markers.Read(editor.Project.Active).Count==1,"mover la pista conserva marcadores de secuencia");
  editor.Undo();Check(editor.Project.Active.clips.All(c=>c.track_id==videos[0].id),"deshacer el movimiento restaura ambos medios");
  Console.WriteLine("PASS TOTAL "+count);
 }
}
