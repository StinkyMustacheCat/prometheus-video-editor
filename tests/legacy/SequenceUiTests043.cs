using System;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using Prometheus;

class SequenceUiTests043 {
 static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static object Field(object obj,string name){return obj.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(obj);}
 static void Call(object obj,string name){obj.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(obj,null);}
 [STAThread]static void Main(string[] args){
  Application.EnableVisualStyles();typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Public|BindingFlags.Static).SetValue(null,true);
  using(var form=new MainForm()){
   form.Show();Application.DoEvents();
   var editor=(Editor)Field(form,"editor");var panel=(TimelinePanelView)Field(form,"timelineView");
   editor.CreateSequence("Segunda");Call(form,"RefreshProject");Application.DoEvents();
   var strip=panel.Controls.OfType<FlowLayoutPanel>().First(x=>x.Height==35);
   Check(strip.Controls.OfType<Button>().Count()==4,"pestañas de dos secuencias y controles de gestión");
   var first=strip.Controls.OfType<Button>().First();first.PerformClick();Application.DoEvents();
   Check(editor.Project.Active.name=="Secuencia 01","clic en pestaña cambia secuencia activa");
   panel.Timeline.Cursor=2;var second=strip.Controls.OfType<Button>().First(x=>x.Text=="Segunda");second.PerformClick();Application.DoEvents();
   Check(editor.Project.Active.name=="Segunda"&&panel.Timeline.Cursor==0,"al cambiar se restaura el lector de la otra secuencia");
   var currentStrip=panel.Controls.OfType<FlowLayoutPanel>().First(x=>x.Height==35);currentStrip.Controls.OfType<Button>().First(x=>x.Text=="Secuencia 01").PerformClick();Application.DoEvents();
   Check(Math.Abs(panel.Timeline.Cursor-2)<0.001,"cada secuencia conserva su lector");
   if(args.Length>0)using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(args[0],System.Drawing.Imaging.ImageFormat.Png);}
   string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"autosave-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   string previousRecoveryDirectory=ProjectStorage.RecoveryDirectory;ProjectStorage.RecoveryDirectory=Path.Combine(root,"recovery");
   try{
    string path=Path.Combine(root,"Proyecto.pthproj");Store.Save(editor.Project,path);
    form.GetType().GetField("projectFile",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(form,path);
    form.GetType().GetField("dirty",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(form,true);
    typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Public|BindingFlags.Static).SetValue(null,false);
    Call(form,"AutosaveTick");
    string backup=ProjectStorage.AutosaveFile(path);
    Check(File.Exists(backup)&&Store.Load(backup).sequences.Count==2,"autoguardado por proyecto conserva secuencias");
    Call(form,"SaveProject");
    Check(!File.Exists(backup)&&File.Exists(path)&&!Directory.Exists(ProjectStorage.ImportDirectory(path)),"guardar proyecto limpia recuperación sin carpetas vacías");
   }finally{ProjectStorage.RecoveryDirectory=previousRecoveryDirectory;typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Public|BindingFlags.Static).SetValue(null,true);Directory.Delete(root,true);}
   form.Dispose();
  }
 }
}
