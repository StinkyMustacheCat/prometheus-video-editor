using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class InstructionUiTests053 {
 [STAThread]static void Main(){
  Application.EnableVisualStyles();
  typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  using(var form=new MainForm()){
   form.Show();Application.DoEvents();
   var workspace=(PanelWorkspace)typeof(MainForm).GetField("workspace",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
   var view=(InstructionPanelView)typeof(MainForm).GetField("instructionView",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
   var editor=(Editor)typeof(MainForm).GetField("editor",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
   workspace.FloatPanel("instrucciones");Application.DoEvents();
   var doc=Instructions.New(editor.Project);doc.actions.Add(new InstructionAction{op="effect.apply",scope="sequence",catalog_id=EffectContracts.BasicColorId});
   view.LoadDocument(Instructions.Encode(doc));
   var floatWindow=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f!=form&&f.Text.Contains("INSTRUCCIONES"));
   if(floatWindow==null||!floatWindow.Visible)throw new Exception("El panel no abre");
   using(var image=new Bitmap(floatWindow.Width,floatWindow.Height)){floatWindow.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"instructions-053.png"));}
   using(var dialog=new InstructionActionDialog(editor.Project,new System.Collections.Generic.List<string>(),doc.actions[0])){
    dialog.Show(floatWindow);Application.DoEvents();
    using(var image=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"instruction-edit-053.png"));}
    typeof(InstructionActionDialog).GetMethod("Save",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(dialog,null);
    dialog.Close();
   }
   floatWindow.Close();form.Close();
  }
  Console.WriteLine("InstructionUiTests053 OK");
 }
}
