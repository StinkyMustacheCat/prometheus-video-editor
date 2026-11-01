using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class InstructionShelfUiTests058 {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();var project=Store.New();var asset=GeneratedMedia.CreateSolid("Prueba UI","112233",6);ShelfStore.Add(project,asset);var item=ShelfStore.Read(project).Single();
  using(var view=new InstructionPanelView()){
   view.SetContext(project,new string[0],null);
   typeof(InstructionPanelView).GetMethod("AddShelfCard",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,new object[]{item.id});
   Check(view.Document!=null&&view.Document.actions.Count==1&&view.Document.actions[0].op=="media.import_shelf","Bandeja crea tarjeta sin importar inmediatamente");
   using(var dialog=new InstructionCommonDialog(project,view.Document.actions[0])){
    var rows=(TableLayoutPanel)typeof(InstructionCommonDialog).GetField("rows",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dialog);
    Check(rows.Controls.Cast<Control>().Any(x=>x is ComboBox&&((ComboBox)x).Items.Count>0),"Editor ofrece elemento de Bandeja");
   }
  }
  Console.WriteLine("InstructionShelfUiTests058 OK");
 }
}
