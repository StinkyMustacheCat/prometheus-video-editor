using System;
using System.Linq;
using Prometheus;

class RollEditTests0291 {
 static int count;
 static void Check(bool condition,string label){if(!condition)throw new Exception(label);Console.WriteLine("PASS "+label);count++;}
 static void Reject(Action action,string label){try{action();}catch{Console.WriteLine("PASS "+label);count++;return;}throw new Exception("Se aceptó una edición inválida: "+label);}
 static Clip Clip(string id,string track,string stream,double start,double duration,double source){return new Clip{id=id,name=id,track_id=track,asset_id="asset",stream_id=stream,timeline_start=Rational.Time(start),duration=Rational.Time(duration),source_in=Rational.Time(source)};}
 static void Main(){
  var editor=new Editor();var sequence=editor.Project.Active;
  var video=sequence.tracks.First(t=>t.kind=="video");var audio=sequence.tracks.First(t=>t.kind=="audio");
  editor.Project.assets.Add(new Asset{id="asset",name="Prueba",source=new MediaSource{kind="local",uri="test.mp4"},presentation_origin=Rational.Time(0),streams={new StreamInfo{id="v",ordinal=0,kind="video",codec="h264",pts_origin=Rational.Time(0),time_base=new Rational(1,60),duration=Rational.Time(10)},new StreamInfo{id="a",ordinal=1,kind="audio",codec="aac",pts_origin=Rational.Time(0),time_base=new Rational(1,48000),duration=Rational.Time(10)}}});
  sequence.clips.AddRange(new[]{Clip("lv",video.id,"v",0,2,0),Clip("la",audio.id,"a",0,2,0),Clip("rv",video.id,"v",2,2,2),Clip("ra",audio.id,"a",2,2,2)});
  sequence.link_groups.Add(new LinkGroup{id="group-left",clip_ids={"lv","la"}});
  sequence.link_groups.Add(new LinkGroup{id="group-right",clip_ids={"rv","ra"}});
  Store.Validate(editor.Project);
  editor.RollEditLinked("lv","rv",2.5,true);
  Check(sequence!=editor.Project.Active&&editor.Project.Active.clips.Single(c=>c.id=="lv").End==2.5&&editor.Project.Active.clips.Single(c=>c.id=="la").End==2.5,"Roll Edit conjunto mueve vídeo y audio");
  Check(editor.Project.Active.clips.Single(c=>c.id=="rv").timeline_start.Seconds==2.5&&editor.Project.Active.clips.Single(c=>c.id=="ra").timeline_start.Seconds==2.5,"ambas entradas permanecen alineadas");
  editor.Undo();Check(editor.Project.Active.clips.Single(c=>c.id=="lv").End==2&&editor.Project.Active.clips.Single(c=>c.id=="la").End==2,"deshacer restaura el par vinculado en una acción");
  editor.Redo();Check(editor.Project.Active.clips.Single(c=>c.id=="rv").source_in.Seconds==2.5&&editor.Project.Active.clips.Single(c=>c.id=="ra").source_in.Seconds==2.5,"rehacer conserva el tiempo de origen");
  editor.Undo();editor.RollEditLinked("lv","rv",2.5,false);
  Check(editor.Project.Active.clips.Single(c=>c.id=="lv").End==2.5&&editor.Project.Active.clips.Single(c=>c.id=="la").End==2,"Alt ajusta solo la pista indicada");
  editor.Undo();
  double limited=editor.LimitRoll("lv","rv",99,true);
  Check(limited<4&&limited>3.9,"el límite común detiene el gesto antes de agotar el clip derecho");
  editor.Commit(p=>p.Active.tracks.Single(t=>t.id==audio.id).locked=true);
  string before=Store.Encode(editor.Project);Reject(()=>editor.RollEditLinked("lv","rv",2.5,true),"pista vinculada bloqueada impide Roll Edit");
  Check(Store.Encode(editor.Project)==before,"edición rechazada no modifica el proyecto");
  editor.Undo();
  editor.Commit(p=>p.Active.clips.Single(c=>c.id=="ra").timeline_start=Rational.Time(2.1));
  before=Store.Encode(editor.Project);Reject(()=>editor.RollEditLinked("lv","rv",2.5,true),"cortes vinculados desalineados se rechazan");
  Check(Store.Encode(editor.Project)==before,"rechazo por desalineación es atómico");
  Console.WriteLine("PASS TOTAL "+count);
 }
}
