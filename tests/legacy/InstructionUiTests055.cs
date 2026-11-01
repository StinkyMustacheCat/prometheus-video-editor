using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Prometheus;

class InstructionUiTests055 {
 static void WaitUntil(Func<bool> ready){for(int i=0;i<250&&!ready();i++){Application.DoEvents();Thread.Sleep(20);}if(!ready())throw new Exception("La revisión no terminó");}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  using(var form=new MainForm()){
   form.Show();Application.DoEvents();
   var view=(InstructionPanelView)typeof(MainForm).GetField("instructionView",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
   var editor=(Editor)typeof(MainForm).GetField("editor",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
   var preview=(TextBox)typeof(InstructionPanelView).GetField("preview",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
   var doc=Instructions.New(editor.Project);doc.actions.Add(new InstructionAction{op="sequence.create",name="Prueba vertical",width=1080,height=1920,result="seq"});doc.actions.Add(new InstructionAction{op="track.create",sequence_id="$seq",source_kind="video",name="Auxiliar"});
   view.LoadDocument(Instructions.Encode(doc));Console.WriteLine("loaded");WaitUntil(()=>preview.Text.Contains("Secuencias nuevas: 1"));
   view.ReviewRequested(view.Document);Console.WriteLine("reviewed");WaitUntil(()=>preview.Text.Contains("Secuencias nuevas: 1"));
   view.ApplyRequested(view.Document);Console.WriteLine("applied");if(editor.Project.sequences.Count!=2||!editor.Project.Active.tracks.Any(t=>t.name=="Auxiliar"))throw new Exception("Aplicación desde el panel falló");
   editor.Undo();if(editor.Project.sequences.Count!=1)throw new Exception("Undo del lote falló");typeof(MainForm).GetField("dirty",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,false);form.Close();
  }
  Console.WriteLine("InstructionUiTests055 OK");
 }
}
