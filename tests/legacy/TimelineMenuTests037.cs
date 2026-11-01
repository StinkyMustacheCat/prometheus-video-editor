using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class TimelineMenuTests037 {
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static object Field(object target,string name){return target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
 static void Call(object target,string name,params object[] args){target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  using(var form=new MainForm()){
   form.Show();Application.DoEvents();var editor=(Editor)Field(form,"editor");var timeline=(Timeline)Field(form,"timeline");var menu=(ContextMenuStrip)Field(form,"clipMenu");
   var project=Store.New();string track=project.Active.tracks.First(t=>t.kind=="video").id;var solid=GeneratedMedia.CreateSolid("Fondo","FF9900",1);project.assets.Add(solid);
   Media.Insert(project,solid.id,track,0);Media.Insert(project,solid.id,track,2);editor.Reset(project);Call(form,"RefreshProject");
   var ids=editor.Project.Active.clips.Select(c=>c.id).ToList();timeline.Selection=ids.ToList();timeline.Selected=ids[0];Call(form,"Delete");
   Check(editor.Project.Active.clips.Count==0,"Suprimir en la ventana elimina toda la selección");editor.Undo();Call(form,"RefreshProject");
   timeline.Selection=new System.Collections.Generic.List<string>{ids[0]};timeline.Selected=ids[0];timeline.Cursor=1.5;
   Call(form,"ShowClipContext",new Point(240,70));
   Check(!menu.Items.Cast<ToolStripItem>().Any(x=>x.Text=="Dividir"),"menú oculta dividir cuando el lector no está en el clip");
   Check(menu.Items.Cast<ToolStripItem>().Where(x=>!(x is ToolStripSeparator)).All(x=>x.Enabled),"menú no presenta acciones inactivas");
   menu.Close();editor.Project.Active.tracks.Find(t=>t.id==track).locked=true;Call(form,"ShowClipContext",new Point(240,70));
   Check(!menu.Items.Cast<ToolStripItem>().Any(x=>x.Text.StartsWith("Eliminar")),"menú oculta edición de pista bloqueada");
   menu.Close();form.GetType().GetField("dirty",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,false);form.Close();
  }
 }
}
