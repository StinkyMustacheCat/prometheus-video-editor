using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Prometheus;

class MarkerTimeline0297:Timeline {
 public void Down(int x,int y){OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,x,y,0));}
 public void Drag(int x,int y){OnMouseMove(new MouseEventArgs(MouseButtons.Left,0,x,y,0));}
 public void Up(int x,int y){OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,x,y,0));}
}
class MarkerTests0297 {
 static int count;
 static void Check(bool condition,string label){if(!condition)throw new Exception(label);Console.WriteLine("PASS "+label);count++;}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();
  var editor=new Editor();
  editor.AddMarker(5.013,"Revisar corte");
  var marker=Markers.Read(editor.Project.Active).Single();
  Check(Math.Abs(marker.Seconds-5.0166666667)<0.000001&&marker.Name=="Revisar corte","marcador se guarda al fotograma más cercano");
  editor.MoveMarker(marker.Id,6.5);editor.RenameMarker(marker.Id,"Entrada");editor.SetMarkerColor(marker.Id,unchecked((int)0xFF83C8E9));
  Check(Markers.Read(editor.Project.Active).Single().Seconds==6.5&&Markers.Read(editor.Project.Active).Single().ColorArgb==unchecked((int)0xFF83C8E9),"mover, renombrar y colorear conservan la identidad");
  editor.Undo();Check(Markers.Read(editor.Project.Active).Single().ColorArgb==Markers.DefaultColor,"deshacer recupera el color anterior");
  editor.Redo();Check(Markers.Read(editor.Project.Active).Single().Name=="Entrada","rehacer conserva el nombre");
  string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"prometheus-markers-"+Guid.NewGuid().ToString("N")+".json");
  try{Store.Save(editor.Project,file);Check(Markers.Read(Store.Load(file).Active).Single().Seconds==6.5,"marcador persiste en el proyecto");}
  finally{if(File.Exists(file))File.Delete(file);if(File.Exists(file+".bak"))File.Delete(file+".bak");}
  var track=editor.Project.Active.tracks.First(t=>t.kind=="video");
  editor.Project.assets.Add(new Asset{id="asset",name="Fixture",source=new MediaSource{kind="local",uri="missing.mp4"},presentation_origin=Rational.Time(0),streams={new StreamInfo{id="stream",ordinal=0,kind="video",codec="h264",pts_origin=Rational.Time(0),time_base=new Rational(1,60),duration=Rational.Time(10)}}});
  editor.Project.Active.clips.Add(new Clip{id="clip",name="Clip",asset_id="asset",stream_id="stream",track_id=track.id,timeline_start=Rational.Time(0),duration=Rational.Time(1),source_in=Rational.Time(0)});
  Store.Validate(editor.Project);
  using(var form=new Form{Width=950,Height=370})using(var timeline=new MarkerTimeline0297{Dock=DockStyle.Fill,Project=editor.Project,Snap=true}){
   form.Controls.Add(timeline);form.Show();Application.DoEvents();
   Check(Math.Abs(timeline.AdjustMove(6.4,"clip")-6.5)<0.000001,"el imán alinea un clip con el marcador");
   timeline.Fit();Check(timeline.X(6.5)<timeline.Width,"Ajustar a contenido incluye marcadores posteriores al último clip");
   timeline.Zoom=55;timeline.Scroll=0;
   string moved=null;double movedTo=-1;timeline.MoveMarkerRequested=(id,time)=>{moved=id;movedTo=time;};
   int x=(int)timeline.X(6.5);timeline.Down(x,27);timeline.Drag(x+22,27);timeline.Up(x+22,27);
   Check(moved==marker.Id&&movedTo>6.5,"el marcador se puede arrastrar en la regla");
  }
  editor.DeleteMarker(marker.Id);Check(Markers.Read(editor.Project.Active).Count==0,"eliminar quita el marcador");
  editor.Undo();Check(Markers.Read(editor.Project.Active).Count==1,"deshacer restaura el marcador eliminado");
  var keys=new ShortcutMap(null);
  Check(keys.Get("marker.add")==Keys.M&&keys.Get("nav.marker.next")== (Keys.Control|Keys.Right),"marcadores tienen atajos configurables");
  Console.WriteLine("PASS TOTAL "+count);
 }
}
