using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class UiTests02519 {
 static int count;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);count++;}
 static T Field<T>(object target,string name){return (T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
 static System.Collections.Generic.IEnumerable<Control> Children(Control parent){foreach(Control child in parent.Controls){yield return child;foreach(var inner in Children(child))yield return inner;}}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();using(var form=new MainForm()){
   var workspace=Field<PanelWorkspace>(form,"workspace");workspace.PersistenceEnabled=false;
   form.Show();Application.DoEvents();
   var view=Field<MonitorPanelView>(form,"monitorView");var timeline=Field<Timeline>(form,"timeline");
   Check(workspace.Entries.Single(x=>x.Id=="monitor").Content==view&&view.Canvas.Parent==view,"Monitor registra un único control autónomo");
   Check(Children(view).OfType<ComboBox>().Count()==2&&Children(view).OfType<TrackBar>().Single()==view.ZoomSlider,"calidad y zoom pertenecen al panel Monitor");
   view.FpsChoice.SelectedIndex=2;view.ResolutionChoice.SelectedIndex=0;
   Check(view.PreviewFps==60&&view.PreviewWidth==320&&view.PreviewHeight==180,"las opciones del monitor definen la calidad de reproducción");
   view.ZoomPercent.Value=250;
   Check(view.ZoomSlider.Value==200&&view.ZoomPercent.Value==250,"el zoom manual supera el tramo del deslizador");
   view.SetTime(2.5);Check(view.TimeLabel.Text==Timeline.TimeLabel(2.5),"el tiempo se presenta dentro del monitor");
   view.StepRequested(1);Check(Math.Abs(timeline.Cursor-1.0/60)<0.000001,"transporte del monitor solicita avanzar un fotograma");
   workspace.FloatPanel("monitor");Application.DoEvents();
   Check(workspace.Entries.Single(x=>x.Id=="monitor").Content==view&&!view.IsDisposed&&view.Parent!=null,"monitor conserva estado al flotar");
   workspace.ApplyPreset("revision");Application.DoEvents();
   Check(view.Parent!=null&&view.ZoomPercent.Value==250&&view.FpsChoice.SelectedIndex==2,"monitor conserva ajustes al recuperar un diseño");
  }
  Console.WriteLine("PASS TOTAL "+count);
 }
}
