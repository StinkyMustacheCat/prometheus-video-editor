using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Prometheus;

class RollMonitorTests0292 {
 static T Field<T>(object value,string name){return (T)value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);}
 [STAThread]static void Main(string[] args){
  Application.EnableVisualStyles();
  Tools.Root=Path.GetFullPath(args[0]);
  using(var form=new MainForm()){
   form.Show();Application.DoEvents();
   form.LoadFixture(Path.Combine(Tools.Root,"demo","Demostracion.prometheus.json"));
   var editor=Field<Editor>(form,"editor");
   string firstVideo=editor.Project.Active.clips.First(c=>editor.Project.Active.tracks.Find(t=>t.id==c.track_id).kind=="video").id;
   editor.Cut(firstVideo,1);
   var timeline=Field<Timeline>(form,"timeline");timeline.Project=editor.Project;
   var pair=editor.Project.Active.clips.Where(c=>c.track_id==editor.Project.Active.clips.Find(x=>x.id==firstVideo).track_id).OrderBy(c=>c.timeline_start.Seconds).Take(2).ToList();
   timeline.RollPreviewChanged(pair[0].id,pair[1].id,1.25,true);
   var timer=Stopwatch.StartNew();
   while(!Field<bool>(form,"rollPreviewShowing")&&timer.Elapsed<TimeSpan.FromSeconds(25)){Application.DoEvents();Thread.Sleep(40);}
   if(!Field<bool>(form,"rollPreviewShowing"))throw new Exception("No apareció la vista doble durante Roll Edit");
   var monitor=Field<PreviewCanvas>(form,"monitor");
   if(monitor.Image==null||monitor.Image.Width!=640||monitor.Image.Height!=360)throw new Exception("La vista doble tiene un tamaño incorrecto");
   Console.WriteLine("PASS monitor muestra dos fotogramas del corte en "+timer.Elapsed.TotalSeconds.ToString("F1")+" s");
   timeline.RollPreviewChanged(null,null,0,false);
  }
 }
}
