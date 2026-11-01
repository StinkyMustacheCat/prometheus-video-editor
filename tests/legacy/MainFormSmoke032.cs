using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class MainFormSmoke032 {
 [STAThread]static void Main(){
  Application.EnableVisualStyles();
  typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  using(var form=new MainForm()){
   form.Show();Application.DoEvents();
   if(!form.Visible)throw new Exception("La ventana no abrió");
   using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"main-032.png"));}
   Console.WriteLine("PASS ventana principal 0.3.2 abre");form.Close();
  }
 }
}
