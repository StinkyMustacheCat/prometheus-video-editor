using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Prometheus;

class UiTests0264 {
 static int count;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);count++;}
 static T Field<T>(object target,string name){return (T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
 static void Call(object target,string name,params object[] args){target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);}
 static Extension Job(string id,string file,string track,string state){return new Extension{@namespace="prometheus.import.pending",version="1",payload=new Dictionary<string,object>{{"id",id},{"path",file},{"track",track},{"start",3.0},{"batch","test"},{"order",0},{"state",state}}};}
 [STAThread]static void Main(string[] args){
  Application.EnableVisualStyles();Tools.Root=Path.GetFullPath(args[0]);
  typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  string image=Path.Combine(Tools.Root,"demo","grafico.png");string save=Path.Combine(Tools.Root,"test-pending-0264.prometheus.json");
  try{
   var p=Store.New();string track=p.Active.tracks.First(t=>t.kind=="video").id;var asset=Media.Inspect(image,CancellationToken.None);p.assets.Add(asset);Media.Insert(p,asset.id,track,0);
   p.extensions.Add(Job("missing-job",Path.Combine(Tools.Root,"demo","no-existe.png"),track,"missing"));Store.Save(p,save);
   var reopened=Store.Load(save);Check(reopened.extensions.Count==1&&reopened.Active.clips.Count==1,"guardar y reabrir conserva la cola aparte de los clips reales");
   string graph=Rendering.Build(reopened,true,false,640,360,15,"raw",0,true);Check(graph.Contains("grafico.png"),"la cola pendiente no impide previsualizar clips válidos");
   using(var form=new MainForm()){
    Field<PanelWorkspace>(form,"workspace").PersistenceEnabled=false;form.Show();Application.DoEvents();
    var editor=Field<Editor>(form,"editor");editor.Reset(reopened);Call(form,"RefreshProject");Call(form,"RefreshPendingUi");
    var timeline=Field<Timeline>(form,"timeline");Check(timeline.PendingImports.Count==1&&timeline.PendingImports[0].State=="missing","archivo faltante tiene estado distinto al de preparación");
    Call(form,"DiscardPending","missing-job");Check(editor.Project.extensions.Count==0&&timeline.PendingImports.Count==0,"se puede descartar el pendiente sin alterar el montaje");
    editor.Commit(x=>x.extensions.Add(Job("resume-job",image,track,"pending")));Call(form,"RefreshProject");Call(form,"ResumePendingImports");
    DateTime until=DateTime.UtcNow.AddSeconds(15);
    while(Field<CancellationTokenSource>(form,"importToken")!=null&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(15);}Application.DoEvents();
    Check(editor.Project.extensions.Count==0&&editor.Project.Active.clips.Count==2,"la cola guardada reanuda y añade el clip al proyecto");
    form.GetType().GetField("dirty",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,false);form.Close();
   }
   Console.WriteLine("PASS TOTAL "+count);
  }finally{foreach(string path in new[]{save,save+".bak"})if(File.Exists(path))File.Delete(path);}
 }
}
