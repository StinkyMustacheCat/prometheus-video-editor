using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Prometheus;

class TestTimeline02510:Timeline {
 public void Down(int x,int y){OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,x,y,0));}
 public void DragTo(int x,int y){OnMouseMove(new MouseEventArgs(MouseButtons.Left,0,x,y,0));}
 public void Up(int x,int y){OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,x,y,0));}
}
class UiTests02510 {
 static int count;
 static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);count++;}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();
  using(var form=new Form{Width=1000,Height=400})using(var timeline=new TestTimeline02510{Dock=DockStyle.Fill,Snap=false,Project=Store.New()}){
   form.Controls.Add(timeline);form.Show();Application.DoEvents();
   var track=timeline.Tracks().First();
   var first=new Clip{id=Store.Id(),track_id=track.id,timeline_start=Rational.Time(1),duration=Rational.Time(2),source_in=Rational.Time(0),speed=Rational.Time(1)};
   var second=new Clip{id=Store.Id(),track_id=track.id,timeline_start=Rational.Time(3),duration=Rational.Time(2),source_in=Rational.Time(0),speed=Rational.Time(1)};
   timeline.Project.Active.clips.Add(first);timeline.Project.Active.clips.Add(second);
   int trims=0,rolls=0,cuts=0;timeline.TrimClip=(id,leading,at)=>{if(id==first.id)trims++;};timeline.RollClip=(a,b,at)=>{if(a==first.id&&b==second.id)rolls++;};timeline.RazorCut=(id,at)=>cuts++;
   var bounds=timeline.ClipBounds(first);int upper=(int)bounds.Top+8,bottom=(int)bounds.Bottom-4,left=(int)bounds.Left+2,right=(int)bounds.Right-2,seam=(int)bounds.Right;
   Check(timeline.RollPairAt(new Point(seam,upper),track)==null,"zona superior mantiene Trim");
   Check(timeline.RollPairAt(new Point(seam,bottom),track)!=null,"zona inferior activa Roll Edit");
   foreach(Timeline.ToolMode tool in Enum.GetValues(typeof(Timeline.ToolMode))){
    timeline.ActiveTool=tool;timeline.Down(left,upper);timeline.DragTo(left+13,upper);timeline.Up(left+13,upper);
    timeline.Down(right,upper);timeline.DragTo(right-13,upper);timeline.Up(right-13,upper);
    Check(trims==((int)tool+1)*2,"ambos bordes recortan con "+tool);
   }
   Check(cuts==0,"el borde no dispara la herramienta de cortar");
   foreach(Timeline.ToolMode tool in Enum.GetValues(typeof(Timeline.ToolMode))){
    timeline.ActiveTool=tool;timeline.Down(seam,bottom);timeline.DragTo(seam+12,bottom);timeline.Up(seam+12,bottom);
    Check(rolls==(int)tool+1,"Roll Edit funciona con "+tool);
   }
   Check(trims==Enum.GetValues(typeof(Timeline.ToolMode)).Length*2,"Roll Edit no recorta un solo clip");
  }
  Console.WriteLine("PASS TOTAL "+count);
 }
}
