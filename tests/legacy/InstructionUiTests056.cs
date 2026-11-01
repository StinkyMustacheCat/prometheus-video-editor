using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Prometheus;

class InstructionUiTests056 {
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 static void WaitUntil(Func<bool> ready){for(int i=0;i<250&&!ready();i++){Application.DoEvents();Thread.Sleep(20);}if(!ready())throw new Exception("La revisión no terminó");}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  using(var form=new MainForm()){
   form.Show();Application.DoEvents();
   var view=(InstructionPanelView)typeof(MainForm).GetField("instructionView",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
   var editor=(Editor)typeof(MainForm).GetField("editor",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
   var workspace=(PanelWorkspace)typeof(MainForm).GetField("workspace",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);workspace.FloatPanel("instrucciones");Application.DoEvents();
   var list=(ListBox)typeof(InstructionPanelView).GetField("cards",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
   var apply=(Button)typeof(InstructionPanelView).GetField("apply",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
   var status=(Label)typeof(InstructionPanelView).GetField("reviewStatus",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
   var doc=Instructions.New(editor.Project);doc.actions.Add(new InstructionAction{op="sequence.create",name="Nueva"});doc.actions.Add(new InstructionAction{op="clip.delete",clip_id="missing"});
   view.LoadDocument(Instructions.Encode(doc));WaitUntil(()=>status.Text.Contains("1 completas"));
   Check(status.Text.Contains("1 completas")&&status.Text.Contains("1 sin aplicación"),"Revisión automática y estado de las tarjetas");
   Check(apply.Enabled,"Un lote parcialmente válido puede aplicarse");
   list.SelectedIndex=0;typeof(InstructionPanelView).GetMethod("EditSelected",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);Application.DoEvents();
   var edit=(Form)typeof(InstructionPanelView).GetField("editForm",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
   Check(edit!=null&&edit.Visible&&form.Enabled,"La edición es una ventana no modal");
   view.SetContext(editor.Project,new string[0],null);Check(form.Enabled,"Cambiar la selección no bloquea el editor");edit.Close();Application.DoEvents();
   list.SelectedIndex=1;Check(view.DeleteSelectedCard()==false,"Supr requiere el foco del panel");
   list.Focus();Application.DoEvents();Check(view.DeleteSelectedCard(),"Supr elimina la tarjeta seleccionada");Check(view.Document.actions.Count==1,"Se eliminó una sola tarjeta");
   form.Close();
  }
  Console.WriteLine("InstructionUiTests056 OK");
 }
}
