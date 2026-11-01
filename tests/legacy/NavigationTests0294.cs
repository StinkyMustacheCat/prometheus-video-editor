using System;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class WheelTimeline0294:Timeline {
 public Keys Modifiers;
 protected override Keys CurrentModifiers {get{return Modifiers;}}
 public void Spin(int delta){OnMouseWheel(new MouseEventArgs(MouseButtons.None,0,400,100,delta));}
 public void SpinAt(int y,int delta){OnMouseWheel(new MouseEventArgs(MouseButtons.None,0,400,y,delta));}
}
class NavigationTests0294 {
 static int count;
 static void Check(bool condition,string label){if(!condition)throw new Exception(label);Console.WriteLine("PASS "+label);count++;}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();
  using(var form=new Form{Width=950,Height=450})using(var view=new MonitorPanelView{Dock=DockStyle.Fill}){
   form.Controls.Add(view);form.Show();Application.DoEvents();
   double parsed;
   Check(MonitorPanelView.TryParseTimecode("01:02:03:45",out parsed)&&Math.Abs(parsed-3723.75)<0.000001,"HH:MM:SS:FF se interpreta a 60 fps");
   Check(!MonitorPanelView.TryParseTimecode("01:02:03:60",out parsed)&&!MonitorPanelView.TryParseTimecode("1:70:00:00",out parsed),"los campos fuera de rango se rechazan");
   double requested=-1;view.SeekRequested=value=>requested=value;
   typeof(Control).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view.TimeLabel,new object[]{EventArgs.Empty});
   var input=(TextBox)typeof(MonitorPanelView).GetField("timeEditor",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
   Check(input.Visible,"el clic sobre el tiempo abre la entrada exacta");
   input.Text="00:10:25:12";
   typeof(Control).GetMethod("OnKeyDown",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(input,new object[]{new KeyEventArgs(Keys.Enter)});
   Check(Math.Abs(requested-(625+12.0/60))<0.000001&&!input.Visible,"Enter solicita mover el lector al fotograma escrito");
  }
  using(var timeline=new WheelTimeline0294{Width=950,Height=350,Project=Store.New(),Zoom=55,WheelZoomModifiers=Keys.Alt}){
   timeline.Modifiers=Keys.Control;timeline.Spin(-120);
   Check(timeline.Vertical==0&&timeline.Zoom==55&&timeline.Scroll==0,"Ctrl+rueda conserva el grupo de vídeo si todas las pistas caben");
   timeline.Height=160;timeline.SpinAt(60,120);Check(timeline.Vertical==1,"Ctrl+rueda revela pistas de vídeo ocultas");
   timeline.SpinAt(60,-120);Check(timeline.Vertical==0,"Ctrl+rueda vuelve a las pistas inferiores");timeline.Height=350;
   timeline.Modifiers=Keys.None;timeline.Spin(-120);
   Check(timeline.Scroll>0&&timeline.Zoom==55,"rueda sola desplaza el tiempo horizontalmente");
   timeline.Modifiers=Keys.Alt;timeline.Spin(120);
   Check(timeline.Zoom>55,"Alt+rueda amplía la línea de tiempo");
  }
  Console.WriteLine("PASS TOTAL "+count);
 }
}
