using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class UiTests0259 {
 static int count;
 static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);count++;}
 static T Field<T>(object obj,string name){return (T)obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj);}
 [STAThread]static void Main(string[] args){
  Application.EnableVisualStyles();Tools.Root=System.IO.Path.GetFullPath(args[0]);
  string layout=System.IO.Path.Combine(System.IO.Path.GetFullPath(args[0]),"layout-test-"+Guid.NewGuid().ToString("N")+".json");
  using(var form=new MainForm()){
   var workspace=Field<PanelWorkspace>(form,"workspace");workspace.PersistenceEnabled=false;
   form.CreateControl();
   var editor=Field<Editor>(form,"editor");string before=Store.Encode(editor.Project);
   workspace.Minimize("propiedades");
   Check(workspace.Entries.First(p=>p.Id=="propiedades").State=="minimized"&&workspace.Root!=null,"panel minimizes without removing the layout");
   Check(workspace.PanelShelf.Controls.Count>1,"minimized panel appears in the upper shelf");
   workspace.FloatPanel("propiedades");
   Check(workspace.Entries.First(p=>p.Id=="propiedades").State=="floating"&&workspace.Entries.First(p=>p.Id=="propiedades").Floating!=null,"minimized panel opens floating");
   workspace.DockPanel("propiedades","monitor","derecha");
   Check(workspace.Entries.First(p=>p.Id=="propiedades").State=="docked"&&workspace.Entries.First(p=>p.Id=="propiedades").Floating==null,"floating panel docks beside another");
   workspace.ReplacePanel("monitor","medios");
   Check(workspace.Entries.First(p=>p.Id=="monitor").State=="minimized"&&workspace.Entries.First(p=>p.Id=="medios").State=="docked","panel replacement minimizes the displaced panel");
   workspace.ClosePanel("medios");
   Check(workspace.Entries.First(p=>p.Id=="medios").State=="closed","closed panel leaves the workspace");
   workspace.DockPanel("medios",null,"derecha");
   Check(workspace.Entries.First(p=>p.Id=="medios").State=="docked"&&workspace.Entries.All(p=>!p.Content.IsDisposed),"closed panel can be restored without recreating content");
   Check(Store.Encode(editor.Project)==before,"panel operations do not modify the video project");
   workspace.LayoutPath=layout;workspace.PersistenceEnabled=true;workspace.SaveLayout();
   Check(System.IO.File.Exists(layout),"panel arrangement saves separately from the project");
  }
  using(var next=new MainForm()){
   var workspace=Field<PanelWorkspace>(next,"workspace");workspace.LayoutPath=layout;
   workspace.LoadLayout();
   Check(workspace.Entries.First(p=>p.Id=="monitor").State=="minimized"&&workspace.Entries.First(p=>p.Id=="medios").State=="docked","panel arrangement restores after reopening");
  }
  System.IO.File.Delete(layout);
  Console.WriteLine("PASS TOTAL "+count);
 }
}
