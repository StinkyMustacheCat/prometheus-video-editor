using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class UiTests02514 {
 static int count;
 static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);count++;}
 static T Field<T>(object obj,string name){return (T)obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj);}
 static object Call(object obj,string method,params object[] args){return obj.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,args);}
 [STAThread]static void Main(string[] args){
  Application.EnableVisualStyles();Tools.Root=Path.GetFullPath(args[0]);
  string fixture=Path.Combine(Tools.Root,"demo","Demostracion.prometheus.json");
  string saved=Path.Combine(Tools.Root,"color-test-"+Guid.NewGuid().ToString("N")+".prometheus.json");
  try{
   var project=Store.Load(fixture);var clips=project.Active.clips.ToList();var first=clips[0];var second=clips[1];
   Check(ClipLabels.Get(first)==null,"el proyecto previo conserva colores predeterminados");
   ClipLabels.Set(first,"violet");Check(ClipLabels.Get(first)=="violet"&&ClipLabels.Get(second)==null,"color independiente por clip");
   var restored=Store.Decode(Store.Encode(project));Check(ClipLabels.Get(restored.Active.clips.Find(c=>c.id==first.id))=="violet","el color sobrevive a serialización sin cambiar esquema");
   Store.Save(project,saved);var loaded=Store.Load(saved);Check(ClipLabels.Get(loaded.Active.clips.Find(c=>c.id==first.id))=="violet","el color persiste al guardar y abrir");
   Check(Rendering.Build(loaded,true,false,640,360,60,"raw",0).Contains("filter_complex"),"la etiqueta visual no bloquea vista previa ni exportación");
   var unknown=Store.Copy(loaded);unknown.Active.clips[0].extensions.Add(new Extension{@namespace="future.render.effect",version="1",payload=new Dictionary<string,object>()});
   bool rejected=false;try{Rendering.Build(unknown,true,false,640,360,60,"raw",0);}catch(Exception){rejected=true;}Check(rejected,"una extensión de render desconocida sigue bloqueada");
   ClipLabels.Set(first,null);Check(ClipLabels.Get(first)==null,"restaurar color por tipo quita solo la etiqueta visual");
   using(var form=new MainForm()){
    var workspace=Field<PanelWorkspace>(form,"workspace");workspace.PersistenceEnabled=false;
    var editor=Field<Editor>(form,"editor");editor.Reset(Store.Load(fixture));Call(form,"RefreshProject");
    var timeline=Field<Timeline>(form,"timeline");timeline.Selection=new List<string>{first.id,second.id};timeline.Selected=first.id;
    using(var menu=new ContextMenuStrip()){
     Call(form,"AddClipColorMenu",menu);
     var color=(ToolStripMenuItem)menu.Items.Cast<ToolStripItem>().Single(x=>x.Text=="Color de los clips seleccionados");
     Check(color.Enabled&&color.DropDownItems.Count==ClipLabels.Palette.Length+2,"paleta contextual aparece para selección múltiple");
     Check(color.DropDownItems.Cast<ToolStripItem>().Where(x=>x.Text=="Violeta").All(x=>x.Image!=null),"cada color tiene muestra visible");
     color.DropDownItems.Cast<ToolStripItem>().Single(x=>x.Text=="Violeta").PerformClick();
     Check(ClipLabels.Get(editor.Project.Active.clips.Find(c=>c.id==first.id))=="violet"&&ClipLabels.Get(editor.Project.Active.clips.Find(c=>c.id==second.id))=="violet","un clic cambia toda la selección");
     Check(editor.Project.Active.clips.Where(c=>c.id!=first.id&&c.id!=second.id).All(c=>ClipLabels.Get(c)==null),"clips no seleccionados mantienen su color");
     editor.Undo();Check(ClipLabels.Get(editor.Project.Active.clips.Find(c=>c.id==first.id))==null,"deshacer restaura colores");
     editor.Redo();Check(ClipLabels.Get(editor.Project.Active.clips.Find(c=>c.id==first.id))=="violet","rehacer recupera colores");
    }
    var original=editor.Project.Active.clips.Find(c=>c.id==first.id);double at=original.timeline_start.Seconds+original.duration.Seconds/2;
    bool cut=editor.CutSelection(new List<string>{first.id},at);Check(cut,"clip etiquetado se puede dividir");
    Check(editor.Project.Active.clips.Where(c=>c.asset_id==original.asset_id&&c.track_id==original.track_id).Count(c=>ClipLabels.Get(c)=="violet")>=2,"ambas mitades heredan el color al dividir");
   }
   if(args.Length>1)Store.Save(loaded,args[1]);
   Console.WriteLine("PASS TOTAL "+count);
  }finally{if(File.Exists(saved))File.Delete(saved);if(File.Exists(saved+".bak"))File.Delete(saved+".bak");}
 }
}
