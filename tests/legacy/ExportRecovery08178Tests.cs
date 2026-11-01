using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Prometheus;

class ExportRecovery08178Tests {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static void SetField(object owner,string name,object value){owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(owner,value);}
 static Asset WebImage(string file,string url){var asset=Media.Inspect(file,CancellationToken.None);asset.representations.Clear();asset.locator=null;asset.source=new MediaSource{kind="web",uri=url,page_url=url,resolver_type="image-http"};return asset;}
 [STAThread] static void Main(string[] args){
  Application.EnableVisualStyles();Tools.Root=args[0];
  string original=Path.Combine(args[0],"demo","grafico.png");
  var bad=WebImage(original,"https://example.invalid/bad.png");var good=WebImage(original,"https://example.invalid/good.png");
  var project=Store.New();project.assets.Add(bad);project.assets.Add(good);string track=project.Active.tracks.First(t=>t.kind=="video").id;Media.Insert(project,bad.id,track,0);Media.Insert(project,good.id,track,6);
  typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  using(var form=new MainForm()){form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Left=-3000;form.Top=-3000;form.Show();Application.DoEvents();
   using(var progress=new ExportProgressWindow(Path.Combine(args[0],"unused.mp4"))){
    progress.Show(form);Application.DoEvents();
    var method=typeof(MainForm).GetMethod("ResolveWebForExport",BindingFlags.Instance|BindingFlags.NonPublic);
    int badAttempts=0;Action<Project,Asset,CancellationToken> resolve=(p,a,t)=>{if(a.id==bad.id){badAttempts++;throw new IOException("Servidor rechazó el medio (503)");}a.representations.Add(new MediaRepresentation{role="final",locator=new Locator{path=original}});};
    Exception failure=null;var work=Task.Factory.StartNew(()=>{try{method.Invoke(form,new object[]{project,false,false,CancellationToken.None,progress,resolve});}catch(TargetInvocationException ex){failure=ex.InnerException;}});
    var deadline=DateTime.UtcNow.AddSeconds(15);bool continued=false;
    while(!work.IsCompleted&&DateTime.UtcNow<deadline){Application.DoEvents();var button=progress.Controls.OfType<Button>().FirstOrDefault(b=>b.Text=="Seguir con el resto"&&b.Visible);if(button!=null&&!continued){button.PerformClick();continued=true;}Thread.Sleep(15);}
    Check(work.IsCompleted&&continued,"un error web ofrece seguir con el resto sin cerrar progreso");
    Check(badAttempts==3,"dos reintentos automáticos antes de ofrecer opciones");
    Check(failure!=null&&failure.Message.Contains("1 medio"),"el lote termina con un solo medio pendiente");
    Check(project.assets.First(a=>a.id==good.id).representations.Any(r=>r.role=="final"),"el siguiente medio queda resuelto pese al fallo anterior");
    progress.FailedMedia(new[]{"Medio de prueba pendiente"});progress.Close();
   }
   using(var progress=new ExportProgressWindow(Path.Combine(args[0],"unused.mp4"))){
    progress.Show(form);Application.DoEvents();
    var method=typeof(MainForm).GetMethod("ResolveWebForExport",BindingFlags.Instance|BindingFlags.NonPublic);
    int attempts=0;Action<Project,Asset,CancellationToken> recover=(p,a,t)=>{attempts++;if(attempts<=3)throw new IOException("Fallo temporal");a.representations.Add(new MediaRepresentation{role="final",locator=new Locator{path=original}});};
    Exception failure=null;var work=Task.Factory.StartNew(()=>{try{method.Invoke(form,new object[]{project,false,false,CancellationToken.None,progress,recover});}catch(TargetInvocationException ex){failure=ex.InnerException;}});
    var deadline=DateTime.UtcNow.AddSeconds(15);bool retried=false;
    while(!work.IsCompleted&&DateTime.UtcNow<deadline){Application.DoEvents();var button=progress.Controls.OfType<Button>().FirstOrDefault(b=>b.Text=="Reintentar"&&b.Visible);if(button!=null&&!retried){button.PerformClick();retried=true;}Thread.Sleep(15);}
    Check(work.IsCompleted&&retried&&failure==null&&attempts==4,"reintento manual recupera el medio y permite continuar");
    progress.Close();
   }
   form.Close();
  }
 }
}
