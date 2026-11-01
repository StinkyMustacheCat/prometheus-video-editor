using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class ShelfUiTests0315 {
 [STAThread]static void Main(){
  Application.EnableVisualStyles();
  typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  using(var main=new MainForm()){
   main.Show();Application.DoEvents();
   typeof(MainForm).GetMethod("ShowShelf",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(main,null);
   Application.DoEvents();
   var shelf=(ShelfWindow)typeof(MainForm).GetField("shelfWindow",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(main);
   if(!shelf.Visible||shelf.Owner!=main)throw new Exception("La Bandeja no abrió como ventana flotante");
   using(var image=new Bitmap(shelf.Width,shelf.Height)){shelf.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save("shelf-ui-0315.png");}
   main.Close();
  }
  Console.WriteLine("PASS ventana flotante de Bandeja");
 }
}
