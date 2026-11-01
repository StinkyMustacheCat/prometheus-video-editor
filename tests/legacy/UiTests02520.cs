using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class UiTests02520 {
 static int count;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);count++;}
 static T Field<T>(object target,string name){return (T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
 static System.Collections.Generic.IEnumerable<Control> Children(Control parent){foreach(Control child in parent.Controls){yield return child;foreach(var inner in Children(child))yield return inner;}}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();using(var form=new MainForm()){
   var workspace=Field<PanelWorkspace>(form,"workspace");workspace.PersistenceEnabled=false;
   form.Show();Application.DoEvents();
   var view=Field<TimelinePanelView>(form,"timelineView");var timeline=Field<Timeline>(form,"timeline");
   Check(workspace.Entries.Single(x=>x.Id=="timeline").Content==view&&timeline.Parent==view,"Línea de tiempo registra un control con su barra propia");
   Check(view.ToolButtons.Count==5&&view.ToolButtons.Values.All(x=>Children(view).Contains(x)),"herramientas de mouse viven en el panel");
   Check(new[]{view.AddTrackButton,view.UndoButton,view.RedoButton,view.DivideButton,view.DirectionButton,view.DirectionDropButton,view.SnapButton,view.LoopButton}.All(x=>Children(view).Contains(x)),"controles de pistas, edición y utilidades pertenecen al panel");
   view.ToolButtons[Timeline.ToolMode.Razor].PerformClick();Application.DoEvents();
   Check(timeline.ActiveTool==Timeline.ToolMode.Razor&&view.ToolButtons[Timeline.ToolMode.Razor].BackColor.R>200,"herramienta activa muestra su acento");
   bool snap=timeline.Snap;view.SnapButton.PerformClick();Check(timeline.Snap!=snap,"imán cambia el ajuste de la línea de tiempo");
   view.LoopButton.PerformClick();Check(Field<bool>(form,"loopEnabled"),"loop se coordina desde el panel");
   view.LoopButton.PerformClick();
   workspace.FloatPanel("timeline");Application.DoEvents();
   Check(workspace.Entries.Single(x=>x.Id=="timeline").Content==view&&!view.IsDisposed,"la barra y el contenido sobreviven al flotar");
   workspace.ApplyPreset("montaje");Application.DoEvents();
   Check(view.Parent!=null&&timeline.ActiveTool==Timeline.ToolMode.Razor,"herramienta y panel sobreviven a cambio de diseño");
  }
  Console.WriteLine("PASS TOTAL "+count);
 }
}
