using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class MediaInfoTests044 {
 static void Check(bool value,string description){if(!value)throw new Exception(description);Console.WriteLine("PASS "+description);}
 static object Field(object owner,string name){return owner.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(owner);}
 static void Info(MainForm form,string asset,string clip){form.GetType().GetMethod("ShowMediaInfo",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,new object[]{asset,clip});Application.DoEvents();}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Public|BindingFlags.Static).SetValue(null,true);
  using(var form=new MainForm()){
   form.Show();Application.DoEvents();
   var project=((Editor)Field(form,"editor")).Project;
   var first=GeneratedMedia.CreateSolid("Primero","FF0000",3);
   var second=GeneratedMedia.CreateSolid("Segundo","00FF00",4);
   project.assets.Add(first);project.assets.Add(second);
   Info(form,first.id,null);
   var window=(MediaInfoWindow)Field(form,"infoWindow");
   Check(window!=null&&window.Visible&&window.AssetId==first.id,"información abre en ventana temporal");
   var workspace=(PanelWorkspace)Field(form,"workspace");
   Check(!workspace.Entries.Any(e=>e.Content==window),"información no se acopla al layout");
   Info(form,second.id,null);
   Check(Object.ReferenceEquals(window,Field(form,"infoWindow"))&&window.AssetId==second.id,"otro medio reutiliza la misma ventana");
   window.Close();Application.DoEvents();
   Check(Field(form,"infoWindow")==null,"la ventana se cierra sin dejar instancia activa");
  }
 }
}
