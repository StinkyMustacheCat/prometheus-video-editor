using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

public static class ProjectUiTests0592 {
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static void Call(MainForm form,string name,params object[] args){form.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,args);}
 static object Field(MainForm form,string name){return form.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);}
 [STAThread]public static void Main(){
  Application.EnableVisualStyles();
  typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Public|BindingFlags.Static).SetValue(null,true);
  string package=AppDomain.CurrentDomain.BaseDirectory;
  string root=Path.Combine(package,"ui-test-"+Guid.NewGuid().ToString("N").Substring(0,8));Directory.CreateDirectory(root);
  using(var form=new MainForm()){
   form.Show();Application.DoEvents();
   form.LoadFixture(Path.Combine(package,"demo","Demostracion.pthproj"));
   string saved=Path.Combine(root,"Edicion.pthproj");Call(form,"SaveProjectToPath",saved);
   Check(File.Exists(saved),"Guardar como escribe .pthproj");
   Check(Directory.Exists(ProjectStorage.DataDirectory(saved)),"Guardar como traslada imagen gestionada");
   Call(form,"NewProject");Call(form,"OpenProjectPath",saved);
   var editor=(Editor)Field(form,"editor");Check(editor.Project.name=="Edicion"&&editor.Project.assets.Count==3,"reapertura desde interfaz");
   string orphan=Path.Combine(root,"SinRecursos.pthproj");File.Copy(saved,orphan);
   string currentId=editor.Project.id;
   var timer=new Timer{Interval=350};timer.Tick+=(s,e)=>{
    var recovery=Application.OpenForms.Cast<Form>().OfType<ProjectMediaRecovery>().FirstOrDefault();
    if(recovery!=null){timer.Stop();recovery.DialogResult=DialogResult.Cancel;recovery.Close();}
   };timer.Start();Call(form,"OpenProjectPath",orphan);timer.Dispose();
   Check(editor.Project.id==currentId,"cancelar resolución deja el proyecto anterior abierto");
   form.Close();
  }
  Console.WriteLine("OK interfaz de guardado, reapertura y cancelación de recuperación");
 }
}
