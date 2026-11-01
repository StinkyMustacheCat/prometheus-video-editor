using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Prometheus;

class UiTests0263 {
 static int count;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);count++;}
 static T Field<T>(object target,string name){return (T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
 [STAThread]static void Main(string[] args){
  Application.EnableVisualStyles();Tools.Root=Path.GetFullPath(args[0]);
  typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  using(var form=new MainForm()){
   Field<PanelWorkspace>(form,"workspace").PersistenceEnabled=false;
   form.Show();Application.DoEvents();
   var editor=Field<Editor>(form,"editor");var timeline=Field<Timeline>(form,"timeline");
   string track=editor.Project.Active.tracks.First(t=>t.kind=="video").id;
   string demo=Path.Combine(Tools.Root,"demo");string[] files={Path.Combine(demo,"grafico.png"),Path.Combine(demo,"video.mp4")};
   form.GetType().GetMethod("StartImport",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,new object[]{files,track,2.0});
   Check(timeline.PendingImports.Count==2&&timeline.PendingImports[0].Start==2.0,"la suelta reserva espacios visuales mientras inspecciona");
   Check(!Field<bool>(form,"busy"),"la importación no bloquea edición general");
   DateTime until=DateTime.UtcNow.AddSeconds(15);
   while(Field<CancellationTokenSource>(form,"importToken")!=null&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(15);}
   Application.DoEvents();
   Check(Field<CancellationTokenSource>(form,"importToken")==null,"lote termina en segundo plano");
   Check(editor.Project.assets.Count==2&&timeline.PendingImports.Count==0,"cada medio reemplaza su silueta al validarse");
   var clips=editor.Project.Active.clips.Where(c=>c.track_id==track).OrderBy(c=>c.timeline_start.Seconds).ToList();
   Check(clips.Count==2&&Math.Abs(clips[0].timeline_start.Seconds-2)<0.0001&&clips[1].timeline_start.Seconds>=clips[0].End-0.0001,"medios conservan orden y duración real");
   form.GetType().GetField("dirty",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,false);form.Close();
  }
  Console.WriteLine("PASS TOTAL "+count);
 }
}
