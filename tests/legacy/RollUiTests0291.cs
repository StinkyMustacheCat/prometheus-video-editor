using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Prometheus;

class RollTimeline0291:Timeline {
 public Keys Modifiers;
 protected override Keys CurrentModifiers { get { return Modifiers; } }
 public void Down(int x,int y){OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,x,y,0));}
 public void Drag(int x,int y){OnMouseMove(new MouseEventArgs(MouseButtons.Left,0,x,y,0));}
 public void Up(int x,int y){OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,x,y,0));}
}

class RollUiTests0291 {
 static int count;
 static void Check(bool condition,string label){if(!condition)throw new Exception(label);Console.WriteLine("PASS "+label);count++;}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();
  using(var form=new Form{Width=1000,Height=400})using(var timeline=new RollTimeline0291{Dock=DockStyle.Fill,Snap=false,Project=Store.New()}){
   form.Controls.Add(timeline);form.Show();Application.DoEvents();
   var track=timeline.Tracks().First();
   var left=new Clip{id="left",name="L",track_id=track.id,timeline_start=Rational.Time(1),duration=Rational.Time(2),source_in=Rational.Time(0),speed=Rational.Time(1)};
   var right=new Clip{id="right",name="R",track_id=track.id,timeline_start=Rational.Time(3),duration=Rational.Time(2),source_in=Rational.Time(2),speed=Rational.Time(1)};
   timeline.Project.Active.clips.AddRange(new[]{left,right});
   int calls=0;bool includeLinked=false;double requested=0;
   timeline.LimitRoll=(l,r,time,linked)=>{Check(l==left.id&&r==right.id,"la guía sigue el corte elegido");return time;};
   timeline.RollClipLinked=(l,r,time,linked)=>{calls++;includeLinked=linked;requested=time;};
   var bounds=timeline.ClipBounds(left);int seam=(int)bounds.Right,y=(int)bounds.Bottom-4;
   timeline.Down(seam,y);timeline.Drag(seam+15,y);timeline.Up(seam+15,y);
   Check(calls==1&&includeLinked&&requested>left.End,"gesto normal solicita Roll Edit vinculado");
   timeline.Modifiers=Keys.Alt;timeline.Down(seam,y);timeline.Drag(seam+15,y);timeline.Up(seam+15,y);
   Check(calls==2&&!includeLinked,"Alt solicita Roll Edit independiente");
  }
  Console.WriteLine("PASS TOTAL "+count);
 }
}
