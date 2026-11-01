using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Prometheus;

class InstructionRevisionUiTests0561 {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void WaitUntil(Func<bool> ready){for(int i=0;i<250&&!ready();i++){Application.DoEvents();Thread.Sleep(20);}if(!ready())throw new Exception("La revisión no terminó");}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  using(var form=new MainForm()){
   form.Show();Application.DoEvents();
   var view=(InstructionPanelView)typeof(MainForm).GetField("instructionView",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
   var editor=(Editor)typeof(MainForm).GetField("editor",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
   var apply=(Button)typeof(InstructionPanelView).GetField("apply",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
   var status=(Label)typeof(InstructionPanelView).GetField("reviewStatus",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
   var notice=(Label)typeof(InstructionPanelView).GetField("contextNotice",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
   var document=Instructions.New(editor.Project);document.actions.Add(new InstructionAction{op="sequence.create",name="Prueba"});
   view.LoadDocument(Instructions.Encode(document));WaitUntil(()=>apply.Enabled);
   editor.Commit(p=>p.name="Nuevo nombre");typeof(MainForm).GetMethod("RefreshProject",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);
   Check(!apply.Enabled,"Aplicar se bloquea mientras se vuelve a revisar");
   WaitUntil(()=>apply.Enabled&&status.Text.Contains("completas"));
   Check(notice.Visible&&notice.Text.Contains("se revisaron las coincidencias"),"El aviso queda dentro del panel y la revisión se recupera sola");
   var foreign=Instructions.New(Store.New());foreign.actions.Add(new InstructionAction{op="sequence.create",name="Ajena"});
   view.LoadDocument(Instructions.Encode(foreign));
   Check(!apply.Enabled&&notice.Visible&&notice.Text.Contains("no coincide"),"Otro proyecto muestra aviso sin iniciar aplicación");
   typeof(MainForm).GetField("dirty",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,false);form.Close();
  }
  Console.WriteLine("InstructionRevisionUiTests0561 OK");
  Environment.Exit(0);
 }
}
