using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class MarkerPanelTests0297 {
 static T Field<T>(object target,string name){return (T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();
  using(var form=new MainForm()){
   var workspace=Field<PanelWorkspace>(form,"workspace");workspace.PersistenceEnabled=false;
   form.Show();Application.DoEvents();
   var panel=Field<MarkerPanelView>(form,"markerPanelView");
   if(workspace.Entries.Single(item=>item.Id=="marcadores").Content!=panel)throw new Exception("Marcadores sigue siendo un panel vacío");
   var editor=Field<Editor>(form,"editor");editor.AddMarker(12.5,"Revisión");
   typeof(MainForm).GetMethod("RefreshProject",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);
   var list=panel.Controls.OfType<ListBox>().Single();
   if(list.Items.Count!=1||((SequenceMarker)list.Items[0]).Name!="Revisión")throw new Exception("El panel no refleja los marcadores del proyecto");
   Console.WriteLine("PASS panel de marcadores integrado y actualizado");
  }
 }
}
