using System;
using System.Linq;
using System.Windows.Forms;
using Prometheus;

class ExportDialogTests0320 {
 static void Check(bool result,string label){if(!result)throw new Exception(label);Console.WriteLine("PASS "+label);}
 static Button FindButton(Control root,string text){return root.Controls.OfType<Button>().First(x=>x.Text==text);}
 [STAThread]
 static void Main(){
  using(var dialog=new ExportDialog(Store.New())){
   dialog.Show();Application.DoEvents();
   Check(dialog.Visible&&dialog.ClientSize.Height==550,"ventana principal de exportación abre");
   FindButton(dialog,"▸ Avanzado").PerformClick();Application.DoEvents();
   Check(dialog.ClientSize.Height==750,"ajustes avanzados se expanden");
   FindButton(dialog,"▾ Avanzado").PerformClick();Application.DoEvents();
   Check(dialog.ClientSize.Height==550,"ajustes avanzados se contraen");
   var format=dialog.Controls.OfType<ComboBox>().First(x=>x.Items.Count==2);
   format.SelectedIndex=1;Application.DoEvents();
   Check(dialog.Controls.OfType<CheckBox>().Single().Enabled==false,"WAV desactiva la opción de vistas ligeras");
   FindButton(dialog,"Iniciar exportación").PerformClick();Application.DoEvents();
   Check(dialog.Options!=null&&!dialog.Options.Video&&dialog.OutputPath.EndsWith(".wav"),"WAV conserva el formato seleccionado");
   dialog.Close();
  }
 }
}
