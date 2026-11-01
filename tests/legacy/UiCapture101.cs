using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;
class UiCapture101 {
 [STAThread]static int Main(string[] args){try{Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);var app=typeof(MainForm).Assembly.GetType("Prometheus.App");app.GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);using(var form=new MainForm()){form.Show();form.LoadFixture(Path.GetFullPath(args[0]));var project=Store.Load(Path.GetFullPath(args[0]));string id=project.Active.clips.First(c=>NestedMedia0910.IsNested(project,c)).id;typeof(MainForm).GetMethod("OpenNested",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{id});Application.DoEvents();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(Path.GetFullPath(args[1]));}form.Close();}return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
