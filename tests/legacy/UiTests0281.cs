using System;
using System.IO;
using System.Windows.Forms;
using Prometheus;

class WheelTimeline0281:Timeline {
 public Keys Modifiers;
 protected override Keys CurrentModifiers { get { return Modifiers; } }
 public void Spin(int x,int delta){OnMouseWheel(new MouseEventArgs(MouseButtons.None,0,x,100,delta));}
}

class UiTests0281 {
 static int count;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);count++;}
 [STAThread]static void Main(){
  string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"prometheus-shortcuts-0281-"+Guid.NewGuid().ToString("N")+".json");
  try{
   var map=new ShortcutMap(path);
   Check(map.WheelZoomModifiers==Keys.Alt,"Alt+rueda es el valor inicial");
   File.WriteAllText(path,"{\"timeline.zoom-wheel-modifiers\":"+(int)(Keys.Control|Keys.Alt)+"}");
   Check(new ShortcutMap(path).WheelZoomModifiers==Keys.Alt,"la preferencia anterior se migra a Alt");
   map.AssignWheelZoom(Keys.Control|Keys.Alt);
   Check(new ShortcutMap(path).WheelZoomModifiers==(Keys.Control|Keys.Alt),"la personalización nueva se conserva");
   using(var timeline=new WheelTimeline0281{Width=800,Height=300,Zoom=50,Scroll=10,WheelZoomModifiers=Keys.Alt}){
    timeline.Modifiers=Keys.Alt;
    double anchor=(400-timeline.HeaderWidth)/timeline.Zoom+timeline.Scroll;
    timeline.Spin(400,120);
    Check(timeline.Zoom>50,"Alt+rueda acerca");
    Check(Math.Abs(anchor-((400-timeline.HeaderWidth)/timeline.Zoom+timeline.Scroll))<0.00001,"el tiempo bajo el puntero permanece fijo");
    timeline.Modifiers=Keys.None;
    double before=timeline.Zoom;
    timeline.Spin(400,-120);
    Check(Math.Abs(before-timeline.Zoom)<0.00001,"la rueda sola no amplía");
   }
   Console.WriteLine("PASS TOTAL "+count);
  }finally{foreach(string file in new[]{path,path+".bak"})if(File.Exists(file))File.Delete(file);}
 }
}
