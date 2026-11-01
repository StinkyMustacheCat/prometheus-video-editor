using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Prometheus;

class BackgroundResolveTests031 {
 static FieldInfo Field(string name){return typeof(MainForm).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);}
 static MethodInfo Method(string name){return typeof(MainForm).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic);}
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static bool Wait(Func<bool> condition){var end=DateTime.UtcNow.AddSeconds(12);while(DateTime.UtcNow<end){Application.DoEvents();if(condition())return true;Thread.Sleep(25);}return false;}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();
  typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  string root=Path.Combine(Path.GetTempPath(),"prometheus-resolve-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   Tools.Root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","..","dev-030","package","Prometheus-0.2.0-preview-win-x64"));
   var video=Path.Combine(root,"sample.mp4");
   Tools.Run(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -y -f lavfi -i color=c=blue:s=320x180:r=60:d=3 -c:v libx264 -preset ultrafast -pix_fmt yuv420p "+Tools.Q(video),CancellationToken.None);
   var asset=Media.Inspect(video,CancellationToken.None);string url="https://www.youtube.com/watch?v=background-test";
   asset.source=new MediaSource{kind="web",uri=url,page_url=url,resolver_type="youtube/yt-dlp",imported_at=DateTime.UtcNow.ToString("o")};asset.representations.Clear();asset.locator=null;
   var project=Store.New();project.assets.Add(asset);Media.Insert(project,asset.id,project.Active.tracks.First(t=>t.kind=="video").id,0);Store.Validate(project);
   string cache=Path.Combine(root,"cache");Directory.CreateDirectory(cache);
   string key;using(var sha=SHA256.Create())key=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(url))).Replace("-","").ToLowerInvariant().Substring(0,24);
   File.Copy(video,Path.Combine(cache,key+"-final.mp4"));
   using(var form=new MainForm()){
    Field("webCacheDir").SetValue(form,cache);form.Opacity=0;form.Show();Application.DoEvents();
    var editor=(Editor)Field("editor").GetValue(form);editor.Reset(project);Method("RefreshProject").Invoke(form,null);
    Method("ResolveUsedWebNow").Invoke(form,null);
    Check(!(bool)Field("busy").GetValue(form),"resolver no bloquea la edición");
    Method("Edit").Invoke(form,new object[]{(Action)(()=>editor.Commit(p=>p.name="Edición durante descarga"))});
    Check(editor.Project.name=="Edición durante descarga","se puede editar durante la resolución");
    bool attached=Wait(()=>editor.Project.assets.First(a=>a.id==asset.id).representations.Any(r=>r.role=="final"));
    if(!attached)Console.WriteLine("STATUS "+((Label)Field("status").GetValue(form)).Text);
    Check(attached,"representación final se conecta al proyecto activo");
    Check(editor.Project.name=="Edición durante descarga","conectar medio conserva cambios posteriores");
    Check(Wait(()=>Field("webResolveToken").GetValue(form)==null),"resolución termina sin ocupar editor");
    editor.Commit(p=>{var current=p.assets.First(a=>a.id==asset.id);current.representations.Clear();current.locator=null;});Method("RefreshProject").Invoke(form,null);
    Method("PlayPause").Invoke(form,null);
    Check((bool)Field("playing").GetValue(form),"la vista previa puede empezar con el medio virtual");
    Method("ResolveUsedWebNow").Invoke(form,null);
    bool attachedPlaying=Wait(()=>editor.Project.assets.First(a=>a.id==asset.id).representations.Any(r=>r.role=="final"));
    if(!attachedPlaying)Console.WriteLine("STATUS PLAYING "+((Label)Field("status").GetValue(form)).Text);
    Check(attachedPlaying,"la resolución finaliza durante la reproducción");
    Check((bool)Field("playing").GetValue(form),"conectar el final no detiene la reproducción en curso");
    Method("Stop").Invoke(form,null);
    Field("dirty").SetValue(form,false);form.Close();
   }
  }finally{try{Directory.Delete(root,true);}catch{}}
 }
}
