using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Prometheus;

class WebUiTests030 {
 static int count;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);count++;}
 static FieldInfo Field(string name){return typeof(MainForm).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);}
 static bool Wait(Func<bool> condition,int seconds){var deadline=DateTime.UtcNow.AddSeconds(seconds);while(DateTime.UtcNow<deadline){Application.DoEvents();if(condition())return true;Thread.Sleep(50);}return false;}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  using(var form=new MainForm()){
   Field("webCacheDir").SetValue(form,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"web-ui-cache"));
   form.Opacity=0;form.Show();Application.DoEvents();
   var editor=(Editor)Field("editor").GetValue(form);string videoTrack=editor.Project.Active.tracks.First(t=>t.kind=="video").id;
   var import=typeof(MainForm).GetMethod("StartWebImport",BindingFlags.Instance|BindingFlags.NonPublic);
   string image="https://www.gstatic.com/images/branding/googlelogo/2x/googlelogo_color_272x92dp.png";
   import.Invoke(form,new object[]{new WebDropInfo{Url=image,PageUrl="https://www.google.com/"},videoTrack,0.0});
   bool imageReady=Wait(()=>editor.Project.assets.Any(a=>a.source!=null&&a.source.uri==image),30);
   Check(imageReady,"arrastre web añade imagen al proyecto");
   Check(editor.Project.Active.clips.Any(c=>c.asset_id==editor.Project.assets.First(a=>a.source.uri==image).id),"arrastre web coloca imagen en la pista");
   string youtube="https://www.youtube.com/watch?v=jNQXAC9IVRw";
   import.Invoke(form,new object[]{new WebDropInfo{Url=youtube,PageUrl=youtube,IsYouTube=true},videoTrack,2.0});
   Check(Wait(()=>editor.Project.assets.Any(a=>a.source!=null&&a.source.resolver_type=="youtube/yt-dlp"),30),"enlace de YouTube crea asset desde la interfaz");
   var asset=editor.Project.assets.First(a=>a.source.resolver_type=="youtube/yt-dlp");
   Check(editor.Project.Active.clips.Any(c=>c.asset_id==asset.id&&c.timeline_start.Seconds>=2),"medio virtual queda en la línea de tiempo");
   typeof(MainForm).GetMethod("CancelWebWork",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);
   Field("dirty").SetValue(form,false);form.Close();
  }
  Console.WriteLine("PASS TOTAL "+count);
 }
}
