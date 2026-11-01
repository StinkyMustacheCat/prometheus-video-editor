using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class AudioPlaceholderUiTests0313 {
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static object Field(object target,string name){return target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
 static void Call(object target,string name,params object[] args){target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  using(var form=new MainForm()){
   form.Show();Application.DoEvents();var editor=(Editor)Field(form,"editor");var timeline=(Timeline)Field(form,"timeline");var menu=(ContextMenuStrip)Field(form,"clipMenu");
   var p=Store.New();var asset=GeneratedMedia.CreateAudioPlaceholder("Audio reservado","",5);p.assets.Add(asset);
   Media.Insert(p,asset.id,p.Active.tracks.First(t=>t.kind=="audio").id,0);
   editor.Reset(p);Call(form,"RefreshProject");timeline.Selected=p.Active.clips.Single().id;timeline.Selection.Add(timeline.Selected);
   Call(form,"ShowClipContext",new Point(230,70));
   var content=menu.Items.Cast<ToolStripItem>().FirstOrDefault(item=>item.Text=="Definir contenido") as ToolStripMenuItem;
   Check(content!=null,"menú contextual reúne opciones en Definir contenido");
   Check(content.DropDownItems.Cast<ToolStripItem>().Any(item=>item.Text=="Audio local…")&&
    !content.DropDownItems.Cast<ToolStripItem>().Any(item=>item.Text.Contains("Imagen")),"audio reservado ofrece opciones compatibles");
   Check(!menu.Items.Cast<ToolStripItem>().Any(item=>item.Text=="Definir tipo"),"opción Definir tipo fue retirada");
   menu.Close();form.Close();
  }
 }
}
