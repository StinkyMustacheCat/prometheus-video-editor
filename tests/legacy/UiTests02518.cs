using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class UiTests02518 {
 static int count;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);count++;}
 static T Field<T>(object target,string name){return (T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
 static void Mouse(Control control,string method,MouseEventArgs args){typeof(Control).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(control,new object[]{args});}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();using(var form=new MainForm()){
   var workspace=Field<PanelWorkspace>(form,"workspace");workspace.PersistenceEnabled=false;
   form.Show();Application.DoEvents();
   workspace.FloatPanel("propiedades");Application.DoEvents();
   var entry=workspace.Entries.Single(x=>x.Id=="propiedades");
   var floating=(FloatingPanelWindow)entry.Floating;
   Check(floating.Controls.Count==1&&floating.Controls[0]==floating.ContentHost,"la ventana flotante no tiene segunda cabecera");
   var holder=(Panel)floating.ContentHost.Controls[0];
   var header=holder.Controls.OfType<Panel>().Single(x=>x.Dock==DockStyle.Top);
   var title=header.Controls.OfType<Label>().Single();
   var buttons=header.Controls.OfType<Panel>().SelectMany(x=>x.Controls.OfType<Button>()).ToList();
   Check(buttons.Any(x=>x.Text=="☰")&&buttons.Any(x=>x.Text=="×")&&buttons.All(x=>x.Visible&&x.Width>0&&x.Height>0),"la cabecera única conserva menú y cierre");
   Point start=floating.Location;
   Mouse(title,"OnMouseDown",new MouseEventArgs(MouseButtons.Left,1,30,10,0));
   Mouse(title,"OnMouseMove",new MouseEventArgs(MouseButtons.Left,0,72,35,0));Application.DoEvents();
   Check(floating.Left==start.X+42&&floating.Top==start.Y+25,"arrastrar el nombre mueve el panel flotante");
   Mouse(title,"OnMouseUp",new MouseEventArgs(MouseButtons.Left,1,30,10,0));
   Check(entry.State=="floating","soltar fuera del acoplamiento conserva el panel flotante");
   var targets=Field<System.Collections.Generic.Dictionary<string,Panel>>(workspace,"edgeTargets");
   var left=targets["izquierda"];
   Mouse(title,"OnMouseDown",new MouseEventArgs(MouseButtons.Left,1,30,10,0));
   Point drop=title.PointToClient(left.PointToScreen(new Point(3,Math.Max(3,left.Height/2))));
   Mouse(title,"OnMouseMove",new MouseEventArgs(MouseButtons.Left,0,drop.X,drop.Y,0));
   Mouse(title,"OnMouseUp",new MouseEventArgs(MouseButtons.Left,1,30,10,0));Application.DoEvents();
   Check(entry.State=="docked"&&entry.Floating==null,"la misma cabecera acopla al soltar en un borde");
  }
  Console.WriteLine("PASS TOTAL "+count);
 }
}
