using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Prometheus;

class AudioWebUiTests0314 {
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static object Field(object target,string name){return target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
 static void SetField(object target,string name,object value){target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);}
 static void Call(object target,string name,params object[] args){target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);}
 [STAThread]static void Main(string[] args){
  Tools.Root=Path.GetFullPath(args[0]);
  Application.EnableVisualStyles();typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  using(var form=new MainForm()){
   form.Show();Application.DoEvents();var editor=(Editor)Field(form,"editor");var timeline=(Timeline)Field(form,"timeline");var menu=(ContextMenuStrip)Field(form,"gapMenu");
   string url="https://www.youtube.com/watch?v=abc123";
   string json="{\"id\":\"abc123\",\"title\":\"Audio de prueba\",\"duration\":2,\"vcodec\":\"h264\",\"acodec\":\"aac\"}";
   var asset=WebMedia.FromMetadataAudio(url,json);var project=Store.New();project.assets.Add(asset);
   var track=project.Active.tracks.First(t=>t.kind=="audio");Media.PlaceInFreeTrack(project,asset.id,track.id,0);
   editor.Reset(project);Call(form,"RefreshProject");
   Call(form,"BuildEmptyContextMenu",track.id,3d,new Point(300,150));
   Check(menu.Items.Cast<ToolStripItem>().Any(x=>x.Text=="Importar enlace de audio…"),"pista de audio ofrece importar enlace desde menú contextual");menu.Close();
   SetField(form,"webCacheDir",Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"audio-web-cache-0314"));
   Call(form,"BeginWebPreview",asset.id);
   DateTime limit=DateTime.UtcNow.AddSeconds(8);
   while(DateTime.UtcNow<limit&&!editor.Project.assets.Find(a=>a.id==asset.id).representations.Any(r=>r.role=="final")){Application.DoEvents();Thread.Sleep(40);}
   Check(editor.Project.assets.Find(a=>a.id==asset.id).representations.Any(r=>r.role=="final"),"resolución de audio en segundo plano conecta archivo final desde caché");
   SetField(form,"dirty",false);form.Close();
  }
 }
}
