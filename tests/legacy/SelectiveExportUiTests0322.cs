using System;
using System.Linq;
using System.Windows.Forms;
using Prometheus;

class SelectiveExportUiTests0322 {
 static void Check(bool yes,string name){if(!yes)throw new Exception(name);Console.WriteLine("PASS "+name);}
 [STAThread]
 static void Main(){
  var project=Store.New();var solid=GeneratedMedia.CreateSolid("Prueba","FF5500",3);project.assets.Add(solid);Media.Insert(project,solid.id,project.Active.tracks.First(t=>t.kind=="video").id,0);
  using(var dialog=new ExportDialog(project)){
   dialog.Show();Application.DoEvents();
   var selective=dialog.Controls.OfType<Panel>().SelectMany(p=>p.Controls.OfType<CheckBox>()).Single(x=>x.Text.StartsWith("Descargar solo tramos"));
   Check(!selective.Checked,"descarga completa es predeterminada");
   var preview=dialog.Controls.OfType<CheckBox>().Single();preview.Checked=true;
   Check(!selective.Enabled,"vista ligera excluye descarga selectiva");
   preview.Checked=false;selective.Checked=true;
   dialog.Controls.OfType<Button>().First(x=>x.Text=="Iniciar exportación").PerformClick();
   Check(dialog.Options!=null&&dialog.Options.SelectiveWeb&&!dialog.Options.UsePreview,"opción selectiva llega al motor");
  }
 }
}
