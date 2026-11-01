using System;
using System.Linq;
using System.Windows.Forms;
using Prometheus;

class ExportUiTests0321 {
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 [STAThread]
 static void Main(){
  var project=Store.New();var solid=GeneratedMedia.CreateSolid("Prueba","FF5500",3);project.assets.Add(solid);Media.Insert(project,solid.id,project.Active.tracks.First(t=>t.kind=="video").id,0);SequenceExportRange.SetIn(project.Active,1);SequenceExportRange.SetOut(project.Active,2);
  using(var dialog=new ExportDialog(project)){
   dialog.Show();Application.DoEvents();var range=dialog.Controls.OfType<ComboBox>().First(x=>x.Items.Count==2&&x.Items[0].ToString()=="Secuencia completa");
   Check(range.SelectedIndex==1,"el diálogo propone rango I/O válido");
   dialog.Controls.OfType<Button>().First(x=>x.Text=="Iniciar exportación").PerformClick();
   Check(dialog.Options!=null&&dialog.Options.StartFrame==60&&dialog.Options.EndFrame==120,"opciones transmiten rango exacto");
  }
  using(var progress=new ExportProgressWindow("prueba.mp4")){
   progress.Show();Application.DoEvents();progress.MediaStatus("Preparando medio");progress.BeginRender();progress.Render(35);progress.Complete("Exportación terminada");
   Check(progress.Controls.OfType<Button>().Any(x=>x.Text=="Cerrar"),"ventana móvil muestra cierre tras terminar");progress.Close();
  }
 }
}
