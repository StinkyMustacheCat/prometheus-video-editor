using System;
using System.Linq;
using Prometheus;

class TrackMoveTests0296 {
 static int count;
 static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);count++;}
 static void Reject(Action action,string label){try{action();}catch{Console.WriteLine("PASS "+label);count++;return;}throw new Exception("Se aceptó un movimiento inválido: "+label);}
 static Clip Make(string id,string track,string stream,double start,double duration){return new Clip{id=id,name=id,track_id=track,asset_id="asset",stream_id=stream,timeline_start=Rational.Time(start),duration=Rational.Time(duration),source_in=Rational.Time(0)};}
 [STAThread]static void Main(){
  var editor=new Editor();var sequence=editor.Project.Active;
  var videos=sequence.tracks.Where(t=>t.kind=="video").OrderByDescending(t=>t.order).ToList();
  var audio=sequence.tracks.First(t=>t.kind=="audio");
  editor.Project.assets.Add(new Asset{id="asset",name="Fixture",source=new MediaSource{kind="local",uri="missing.mp4"},presentation_origin=Rational.Time(0),streams={new StreamInfo{id="v",ordinal=0,kind="video",codec="h264",pts_origin=Rational.Time(0),time_base=new Rational(1,60),duration=Rational.Time(10)},new StreamInfo{id="a",ordinal=1,kind="audio",codec="aac",pts_origin=Rational.Time(0),time_base=new Rational(1,48000),duration=Rational.Time(10)}}});
  sequence.clips.AddRange(new[]{Make("moving",videos[0].id,"v",0,1),Make("linked",audio.id,"a",0,1),Make("existing",videos[1].id,"v",2,1)});
  sequence.link_groups.Add(new LinkGroup{id="link",clip_ids={"moving","linked"}});
  Store.Validate(editor.Project);
  using(var timeline=new Timeline{Project=editor.Project}){
   Check(timeline.AdjacentCompatibleTrack(videos[0].id,1)==videos[1].id,"la pista inferior de vídeo sigue el orden visible");
   Check(timeline.AdjacentCompatibleTrack(videos[1].id,1)==null,"no se salta de vídeo a audio");
  }
  editor.MoveTrackContents(videos[0].id,videos[1].id);
  Check(editor.Project.Active.clips.First(c=>c.id=="moving").track_id==videos[1].id&&editor.Project.Active.clips.First(c=>c.id=="moving").timeline_start.Seconds==0,"todos los recursos conservan su tiempo al cambiar de pista");
  Check(editor.Project.Active.link_groups.Single().clip_ids.Contains("linked"),"el vínculo con audio se conserva");
  editor.Undo();Check(editor.Project.Active.clips.First(c=>c.id=="moving").track_id==videos[0].id,"deshacer restaura la pista en una acción");
  editor.Redo();Check(editor.Project.Active.clips.First(c=>c.id=="moving").track_id==videos[1].id,"rehacer recupera el traslado");
  editor.Undo();
  editor.Commit(p=>p.Active.clips.Add(Make("collision",videos[1].id,"v",0.5,1)));
  string before=Store.Encode(editor.Project);
  Reject(()=>editor.MoveTrackContents(videos[0].id,videos[1].id),"una colisión impide mover la pista completa");
  Check(Store.Encode(editor.Project)==before,"la colisión no mueve ningún clip");
  editor.Undo();editor.Commit(p=>p.Active.tracks.First(t=>t.id==videos[1].id).locked=true);
  Reject(()=>editor.MoveTrackContents(videos[0].id,videos[1].id),"una pista de destino bloqueada impide mover");
  Console.WriteLine("PASS TOTAL "+count);
 }
}
