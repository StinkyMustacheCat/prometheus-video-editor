using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class DirectionTimeline02515:Timeline {
 public Keys Modifiers;
 protected override Keys CurrentModifiers{get{return Modifiers;}}
 public void Down(int x,int y){OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,x,y,0));}
 public void Drag(int x,int y){OnMouseMove(new MouseEventArgs(MouseButtons.Left,0,x,y,0));}
 public void Up(int x,int y){OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,x,y,0));}
}
class UiTests02515 {
 static int count;
 static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);count++;}
 static T Field<T>(object obj,string name){return (T)obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj);}
 [STAThread]static void Main(string[] args){
  Application.EnableVisualStyles();Tools.Root=Path.GetFullPath(args[0]);
  string fixture=Path.Combine(Tools.Root,"demo","Demostracion.prometheus.json");
  var initial=Store.Load(fixture);var v1=initial.Active.tracks.First(t=>t.kind=="video"&&t.order==0);var v2=initial.Active.tracks.First(t=>t.kind=="video"&&t.order==1);var a1=initial.Active.tracks.First(t=>t.kind=="audio"&&t.order==0);var a2=initial.Active.tracks.First(t=>t.kind=="audio"&&t.order==1);
  var laterVideo=initial.Active.clips.Single(c=>c.track_id==v1.id&&Math.Abs(c.timeline_start.Seconds-3)<0.001);
  var laterAudio=initial.Active.clips.Single(c=>c.track_id==a1.id&&Math.Abs(c.timeline_start.Seconds-3)<0.001);
  using(var timeline=new DirectionTimeline02515{Width=1000,Height=360,Project=initial,Snap=false}){
   timeline.SelectArea(v1.id,2.5,Timeline.ToolMode.TrackUp,false,true);
   Check(timeline.Selection.All(id=>{var t=initial.Active.clips.Find(c=>c.id==id).track_id;return t==v1.id||t==v2.id;})&&timeline.Selection.Count==3,"arriba incluye pista pulsada y superiores sin límite temporal");
   timeline.SelectArea(v1.id,2.5,Timeline.ToolMode.TrackDown,false,true);
   Check(timeline.Selection.Count==5&&!timeline.Selection.Any(id=>initial.Active.clips.Find(c=>c.id==id).track_id==v2.id),"abajo incluye pista pulsada e inferiores");
   timeline.SelectArea(v1.id,2.5,Timeline.ToolMode.TrackAll,false,true);
   Check(timeline.Selection.Count==initial.Active.clips.Count,"todas selecciona la línea de tiempo completa");
   timeline.SelectArea(v1.id,2.5,Timeline.ToolMode.TrackUp,false,false);
   Check(timeline.Selection.Contains(laterAudio.id),"los vínculos se incluyen también al seleccionar arriba");
  }
  var editor=new Editor();editor.Reset(Store.Load(fixture));
  using(var timeline=new DirectionTimeline02515{Width=1000,Height=360,Project=editor.Project,Snap=false,ActiveTool=Timeline.ToolMode.TrackForward}){
   int y=timeline.TrackTop(v1.id)+25,x=(int)timeline.X(2.5),moves=0;
   timeline.MoveGroup=(ids,id,start,track,linked)=>{moves++;editor.MoveSelectedGroup(ids,id,start,track,linked);timeline.Project=editor.Project;};
   timeline.Down(x,y);timeline.Up(x,y);
   Check(moves==0&&timeline.Selection.Contains(laterVideo.id)&&timeline.Selection.Contains(laterAudio.id),"clic desde hueco selecciona sin mover");
   timeline.Down(x,y);timeline.Drag(x+55,y);timeline.Up(x+55,y);
   Check(moves==1&&Math.Abs(editor.Project.Active.clips.Find(c=>c.id==laterVideo.id).timeline_start.Seconds-4)<0.0001,"arrastrar desde hueco mueve el grupo");
   Check(Math.Abs(editor.Project.Active.clips.Find(c=>c.id==laterAudio.id).timeline_start.Seconds-4)<0.0001,"audio vinculado conserva sincronía");
  }
  editor.Reset(Store.Load(fixture));
  using(var timeline=new DirectionTimeline02515{Width=1000,Height=360,Project=editor.Project,Snap=false,ActiveTool=Timeline.ToolMode.TrackForward}){
   int y1=timeline.TrackTop(v1.id)+25,y2=timeline.TrackTop(v2.id)+25,x=(int)timeline.X(3.5);
   timeline.MoveGroup=(ids,id,start,track,linked)=>{editor.MoveSelectedGroup(ids,id,start,track,linked);timeline.Project=editor.Project;};
   timeline.Down(x,y1);timeline.Drag(x+165,y2);timeline.Up(x+165,y2);
   var movedVideo=editor.Project.Active.clips.Find(c=>c.id==laterVideo.id);var movedAudio=editor.Project.Active.clips.Find(c=>c.id==laterAudio.id);
   Check(movedVideo.track_id==v2.id&&movedAudio.track_id==a2.id,"arrastre vertical traslada video y audio a pistas compatibles");
   Check(Math.Abs(movedVideo.timeline_start.Seconds-6)<0.0001&&Math.Abs(movedAudio.timeline_start.Seconds-6)<0.0001,"arrastre preserva posiciones relativas");
  }
  editor.Reset(Store.Load(fixture));
  using(var timeline=new DirectionTimeline02515{Width=1000,Height=360,Project=editor.Project,Snap=false,ActiveTool=Timeline.ToolMode.TrackForward,Modifiers=Keys.Alt}){
   int y=timeline.TrackTop(v1.id)+25,x=(int)timeline.X(3.5);
   timeline.MoveGroup=(ids,id,start,track,linked)=>{editor.MoveSelectedGroup(ids,id,start,track,linked);timeline.Project=editor.Project;};
   timeline.Down(x,y);timeline.Drag(x+55,y);timeline.Up(x+55,y);
   Check(Math.Abs(editor.Project.Active.clips.Find(c=>c.id==laterVideo.id).timeline_start.Seconds-4)<0.0001&&Math.Abs(editor.Project.Active.clips.Find(c=>c.id==laterAudio.id).timeline_start.Seconds-3)<0.0001,"Alt mueve solo la selección explícita sin arrastrar audio vinculado");
  }
  editor.Reset(Store.Load(fixture));
  var firstVideo=editor.Project.Active.clips.Single(c=>c.track_id==v1.id&&Math.Abs(c.timeline_start.Seconds)<0.001);
  editor.MoveSelectedGroup(editor.Project.Active.clips.Where(c=>c.track_id==v1.id).Select(c=>c.id).ToList(),firstVideo.id,1,v1.id,false);
  Check(Math.Abs(editor.Project.Active.clips.Find(c=>c.id==firstVideo.id).timeline_start.Seconds-1)<0.0001&&Math.Abs(editor.Project.Active.clips.Find(c=>c.id==laterVideo.id).timeline_start.Seconds-4)<0.0001,"varios clips conservan su separación al moverlos");
  editor.Undo();Check(Math.Abs(editor.Project.Active.clips.Find(c=>c.id==firstVideo.id).timeline_start.Seconds)<0.0001,"deshacer revierte el movimiento del grupo");
  editor.MoveSelectedGroup(new System.Collections.Generic.List<string>{laterVideo.id},laterVideo.id,1,v1.id,false);
  Check(editor.Project.Active.clips.Find(c=>c.id==firstVideo.id).End<=1.0001,"mover sobre otro clip aplica Overwrite sin alterar la fuente");
  editor.Reset(Store.Load(fixture));
  string before=Store.Encode(editor.Project);bool rejected=false;
  try{editor.MoveSelectedGroup(editor.Project.Active.clips.Select(c=>c.id).ToList(),laterVideo.id,6,v2.id,true);}catch(Exception){rejected=true;}
  Check(rejected&&Store.Encode(editor.Project)==before,"destino sin pistas suficientes rechaza el movimiento completo");
  using(var form=new MainForm()){
   var group=Field<Button>(form,"directionMainButton");var arrow=Field<Button>(form,"directionDropButton");
   Check(group!=null&&arrow!=null&&group.Text=="⇥","las cinco direcciones comparten un botón y desplegable");
   form.GetType().GetMethod("SetTool",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{Timeline.ToolMode.TrackUp});
   Check(group.Text=="↑"&&group.BackColor.R>200&&group.AccessibleName.Contains("Sin asignar"),"el botón muestra la dirección activa y su atajo");
  }
  Console.WriteLine("PASS TOTAL "+count);
 }
}
