using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class ProxyTests028 {
 static int count;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);count++;}
 [STAThread]static void Main(string[] args){
  Tools.Root=Path.GetFullPath(args[0]);string cache=Path.GetFullPath(args[1]);Directory.CreateDirectory(cache);
  var asset=Media.Inspect(Path.Combine(Tools.Root,"demo","video.mp4"),CancellationToken.None);
  var rep=Media.GenerateProxy(asset,cache,CancellationToken.None,null);
  Check(File.Exists(rep.locator.path)&&rep.role=="proxy","genera proxy local como representación separada");
  Check(asset.streams.All(x=>rep.stream_ordinals.ContainsKey(x.id)),"proxy mapea los streams lógicos");
  var again=Media.GenerateProxy(asset,cache,CancellationToken.None,null);
  Check(again.locator.path==rep.locator.path,"reutiliza proxy de caché con mismo original");
  asset.representations.Add(rep);Check(Media.SelectRepresentation(asset,true).role=="proxy","vista previa prefiere proxy disponible");
  Media.SetProxyPreference(asset,false);Check(Media.SelectRepresentation(asset,true).role=="original","puede elegir original para vista previa");
  Media.SetProxyPreference(asset,true);Check(Media.SelectRepresentation(asset,false).role=="original","exportación conserva el original");
  var p=Store.New();p.assets.Add(asset);var track=p.Active.tracks.First(x=>x.kind=="video");Media.Insert(p,asset.id,track.id,0);Store.Validate(p);
  using(var frame=Rendering.Frame(p,0.5,CancellationToken.None))Check(frame.Width==640,"proxy produce un fotograma válido");
  string exported=Path.Combine(cache,"proxy-test-export.mp4");try{Rendering.Export(p,exported,CancellationToken.None);Check(File.Exists(exported)&&new FileInfo(exported).Length>0,"exportación final funciona con proxy presente");}finally{if(File.Exists(exported))File.Delete(exported);}
  typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  using(var form=new MainForm()){
   typeof(MainForm).GetField("proxyCacheDir",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,cache);
   var workspace=(PanelWorkspace)typeof(MainForm).GetField("workspace",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);workspace.PersistenceEnabled=false;
   form.Show();Application.DoEvents();form.LoadFixture(Path.Combine(Tools.Root,"demo","Demostracion.prometheus.json"));
   var editor=(Editor)typeof(MainForm).GetField("editor",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
   string id=editor.Project.assets.First(x=>x.streams.Any(s=>s.kind=="video")).id;
   typeof(MainForm).GetMethod("BeginProxy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{id});
   DateTime until=DateTime.UtcNow.AddSeconds(20);
   while(typeof(MainForm).GetField("proxyToken",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form)!=null&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(15);}Application.DoEvents();
   var current=editor.Project.assets.Find(x=>x.id==id);
   Check(current.representations.Any(x=>x.role=="proxy"),"interfaz genera proxy sin bloquear el proyecto");
   typeof(MainForm).GetMethod("ChangeProxyPreference",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{id,false});
   Check(Media.SelectRepresentation(editor.Project.assets.Find(x=>x.id==id),true).role=="original","preferencia de vista previa se aplica a todos los clips del medio");
   var persisted=Store.Copy(editor.Project);Check(!Media.PrefersProxy(persisted.assets.Find(x=>x.id==id)),"preferencia sobrevive al guardado del proyecto");
   typeof(MainForm).GetField("dirty",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,false);form.Close();
  }
  Console.WriteLine("PASS TOTAL "+count);
 }
}
