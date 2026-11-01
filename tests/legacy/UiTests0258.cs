using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class UiTests0258 {
 static int count;
 static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);count++;}
 static T Field<T>(object obj,string name){return (T)obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj);}
 [STAThread]static void Main(string[] args){
  Application.EnableVisualStyles();Tools.Root=System.IO.Path.GetFullPath(args[0]);
  using(var form=new MainForm()){
   form.CreateControl();
   var workspace=Field<PanelWorkspace>(form,"workspace");
   Check(workspace.Entries.Count()==4,"four editor panels are registered independently");
   Check(workspace.Root!=null&&workspace.Root.Direction=="horizontal","workspace has an explicit layout tree");
   Check(workspace.Entries.All(p=>p.Content.Parent!=null),"all panel contents are hosted in the workspace");
   Check(Field<Timeline>(form,"timeline").Parent!=null,"timeline remains active in its panel");
   Check(Field<PreviewCanvas>(form,"monitor").Parent!=null,"monitor remains active in its panel");
   workspace.Rebuild();
   Check(workspace.Entries.All(p=>p.Content.Parent!=null),"rebuilding layout preserves panel content instances");
  }
  Console.WriteLine("PASS TOTAL "+count);
 }
}
