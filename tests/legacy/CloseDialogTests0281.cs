using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Prometheus;

class CloseDialogTests0281 {
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string className,string title);
 [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr window,uint command);
 [DllImport("user32.dll")]static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
 [STAThread]static void Main(){
  typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  using(var form=new MainForm()){
   form.Show();Application.DoEvents();
   typeof(MainForm).GetField("dirty",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,true);
   form.WindowState=FormWindowState.Minimized;
   bool found=false,owned=false;
   DateTime deadline=DateTime.UtcNow.AddSeconds(6);
   using(var timer=new Timer{Interval=50}){
    timer.Tick+=(s,e)=>{
     IntPtr dialog=FindWindow("#32770","Cambios sin guardar");
     if(dialog!=IntPtr.Zero){found=true;owned=GetWindow(dialog,4)==form.Handle;PostMessage(dialog,0x111,new IntPtr(2),IntPtr.Zero);timer.Stop();}
     else if(DateTime.UtcNow>deadline)Environment.Exit(2);
    };
    timer.Start();form.Close();
   }
   if(!found||!owned||form.IsDisposed||form.WindowState!=FormWindowState.Normal)throw new Exception("La confirmación de cierre no quedó visible y asociada a la ventana principal");
   Console.WriteLine("PASS confirmación visible, modal y asociada a la ventana restaurada");
   typeof(MainForm).GetField("dirty",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,false);
   form.Close();
  }
 }
}
