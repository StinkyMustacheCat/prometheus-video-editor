using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class Ui08178Tests {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static T Field<T>(object owner,string name) where T:class{return (T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);}
 static System.Collections.Generic.IEnumerable<Control> All(Control owner){foreach(Control child in owner.Controls){yield return child;foreach(var grand in All(child))yield return grand;}}
 [STAThread] static void Main(){Application.EnableVisualStyles();
  using(var dialog=new LinkImportDialog(true,false,Color.FromArgb(30,31,34),Color.White)){dialog.StartPosition=FormStartPosition.Manual;dialog.Left=-3000;dialog.Top=-3000;dialog.Show();Field<Button>(dialog,"toggle").PerformClick();Application.DoEvents();
   var from=Field<TextBox>(dialog,"from");var to=Field<TextBox>(dialog,"to");var mode=Field<ComboBox>(dialog,"rangeMode");
   Check(from.Visible&&!to.Visible,"desde un punto muestra solo la entrada necesaria");
   mode.SelectedIndex=1;Check(from.Visible&&to.Visible,"duración muestra inicio y duración");
   mode.SelectedIndex=3;Check(!from.Visible&&to.Visible&&to.Left==0,"hasta un punto muestra solo el final");dialog.Close();
  }
  var project=Store.New();var placeholder=GeneratedMedia.CreatePlaceholder("Prueba","",5);project.assets.Add(placeholder);Media.Insert(project,placeholder.id,project.Active.tracks.First(t=>t.kind=="video").id,0);
  using(var panel=new EffectControlsPanelView{Width=470,Height=500}){bool? linked=null;panel.ScaleLinkChanged=(id,value)=>linked=value;panel.ShowClip(project,project.Active.clips.First());var button=All(panel).OfType<Button>().FirstOrDefault(b=>b.AccessibleName=="Desenlazar escalas");Check(button!=null&&button.Width<=26,"candado de escala compacto");button.PerformClick();Check(linked==false,"candado permite desenlazar escalas");}
 }
}
