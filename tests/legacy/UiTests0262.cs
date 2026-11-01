using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Prometheus;

class WheelTimeline0262:Timeline {
 public Keys Modifiers;
 protected override Keys CurrentModifiers{get{return Modifiers;}}
 public void Spin(int delta){OnMouseWheel(new MouseEventArgs(MouseButtons.None,0,400,100,delta));}
}
class UiTests0262 {
 static int count;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);count++;}
 [STAThread]static void Main(){
  var file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"prometheus-shortcuts-"+Guid.NewGuid().ToString("N")+".json");
  try{
   var map=new ShortcutMap(file);
   Check(map.Get("nav.frame.previous")==Keys.Left&&map.Get("nav.frame.next")==Keys.Right,"flechas mueven un fotograma");
   Check(map.Get("nav.five.previous")== (Keys.Shift|Keys.Left)&&map.Get("nav.five.next")== (Keys.Shift|Keys.Right),"Mayús y flecha mueve cinco fotogramas");
   Check(map.Get("nav.cut.next")==Keys.Up&&map.Get("nav.cut.previous")==Keys.Down,"arriba y abajo saltan entre límites");
   Check(map.WheelZoomModifiers==Keys.Alt,"zoom con rueda usa Alt por defecto");
   map.Assign("nav.five.next",Keys.F6,true);map.AssignWheelZoom(Keys.Alt);var restored=new ShortcutMap(file);
   Check(restored.Get("nav.five.next")==Keys.F6&&restored.WheelZoomModifiers==Keys.Alt,"atajos y rueda persisten");
   using(var t=new WheelTimeline0262{Width=800,Height=300,Zoom=55,WheelZoomModifiers=Keys.Control|Keys.Alt}){
    t.Modifiers=Keys.Control;t.Spin(120);Check(Math.Abs(t.Zoom-55)<0.01&&t.Scroll==0,"Ctrl+rueda desplaza sin ampliar");
    t.Modifiers=Keys.Control|Keys.Alt;t.Spin(120);Check(t.Zoom>55,"Ctrl+Alt+rueda amplía");
    t.WheelZoomModifiers=Keys.Alt;t.Modifiers=Keys.Alt;t.Spin(-120);Check(Math.Abs(t.Zoom-55)<0.01,"modificador configurable cambia el zoom");
   }
   Console.WriteLine("PASS TOTAL "+count);
  }finally{foreach(var path in new[]{file,file+".bak"})if(File.Exists(path))File.Delete(path);}
 }
}
