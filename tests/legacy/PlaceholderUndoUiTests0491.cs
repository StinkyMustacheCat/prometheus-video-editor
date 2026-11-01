using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class PlaceholderUndoUiTests0491 {
 static object Field(object target,string name){return target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
 static void Call(object target,string name,params object[] args){target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);}
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  using(var form=new MainForm()){
   form.Show();Application.DoEvents();var editor=(Editor)Field(form,"editor");var timeline=(Timeline)Field(form,"timeline");var menu=(ContextMenuStrip)Field(form,"clipMenu");
   var placeholder=GeneratedMedia.CreatePlaceholder("Pendiente","",5);
   editor.Commit(p=>{p.assets.Add(placeholder);Media.Insert(p,placeholder.id,p.Active.tracks.First(t=>t.kind=="video").id,0);});
   var web=new Asset{id=Store.Id(),name="Fuente",source=new MediaSource{kind="web",uri="https://www.youtube.com/watch?v=demo",resolver_type="youtube/yt-dlp"},recommended_duration=Rational.Frame(10),presentation_origin=Rational.Time(0)};
   web.streams.Add(new StreamInfo{id=Store.Id(),ordinal=0,kind="video",codec="h264",pts_origin=Rational.Time(0),time_base=new Rational(1,60),duration=Rational.Frame(10),width=640,height=360});
   string clip=editor.Project.Active.clips.Single().id;timeline.Selected=clip;timeline.Selection.Add(clip);
   editor.Commit(p=>{p.assets.Add(web);PlaceholderReplacement.Replace(p,clip,web.id,0);});
   editor.UpdateDerivedAsset(web.id,web.source.uri,p=>p.assets.Single(a=>a.id==web.id).representations.Add(new MediaRepresentation{role="preview",locator=new Locator{path="C:\\cache\\preview.mp4"}}));
   editor.Undo();Call(form,"RefreshProject");Call(form,"ShowClipContext",new Point(230,70));
   Check(menu.Items.Cast<ToolStripItem>().Any(item=>item.Text=="Definir contenido"),"deshacer recupera Definir contenido en el menú contextual");
   menu.Close();form.Close();
  }
 }
}
