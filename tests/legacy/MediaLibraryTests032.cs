using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Prometheus;

class MediaLibraryTests032 {
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static T Field<T>(object target,string name){return (T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();
  Tools.Root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","..","dev-030","package","Prometheus-0.2.0-preview-win-x64"));
  string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"library-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   string file=Path.Combine(root,"Clip de Mario.mp4");Tools.Run(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -y -f lavfi -i color=c=red:s=320x180:r=60:d=1 -c:v libx264 -preset ultrafast -pix_fmt yuv420p "+Tools.Q(file),CancellationToken.None);
   var project=Store.New();var asset=Media.Inspect(file,CancellationToken.None);project.assets.Add(asset);Media.Insert(project,asset.id,project.Active.tracks.First(t=>t.kind=="video").id,0);
   string folder=MediaLibrary.CreateFolder(project,"B-roll");MediaLibrary.Move(project,asset.id,folder);
   Check(MediaLibrary.Folders(project).Count==1&&MediaLibrary.FolderOf(asset)==folder,"carpeta organiza medios sin mover archivos");
   string saved=Path.Combine(root,"project.prometheus.json");Store.Save(project,saved);project=Store.Load(saved);
   Check(MediaLibrary.FolderOf(project.assets[0])==folder,"organización persiste al guardar y reabrir");
   Check(Rendering.Build(project,true,true,320,180,60,Path.Combine(root,"out.mp4"),0).Contains(file),"metadata de biblioteca no bloquea render");
   MediaLibrary.RenameFolder(project,folder,"Referencias");Check(MediaLibrary.Folders(project)[0].name=="Referencias","renombrar conserva asignaciones");
   using(var form=new Form{Width=500,Height=400})using(var view=new MediaPanelView()){
    form.Controls.Add(view);view.SetProject(project);form.Show();Application.DoEvents();
    Check(view.Library.Items.Count==1,"biblioteca muestra el medio");
    Field<TextBox>(view,"search").Text="Maroi";Application.DoEvents();
    Check(view.Library.Items.Count==1,"búsqueda encuentra un título con error pequeño");
    Field<TextBox>(view,"search").Text="inexistente";Application.DoEvents();Check(view.Library.Items.Count==0,"búsqueda filtra resultados");
    Field<TextBox>(view,"search").Text="";Field<ComboBox>(view,"filter").SelectedIndex=2;Application.DoEvents();Check(view.Library.Items.Count==0,"filtro de audio excluye video");
    Field<ComboBox>(view,"filter").SelectedIndex=0;Application.DoEvents();Check(view.Library.Items.Count==1,"restaurar filtro muestra el medio");
    using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"media-panel-032.png"));}
    typeof(MediaPanelView).GetMethod("ToggleView",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);Application.DoEvents();
    Check(view.Library.MultiColumn&&view.Library.ItemHeight>30,"la cuadrícula cambia la presentación de medios");
    form.Close();
   }
   MediaLibrary.DeleteFolder(project,folder);Check(MediaLibrary.Folders(project).Count==0&&MediaLibrary.FolderOf(project.assets[0])==null,"eliminar carpeta conserva el medio");
  }finally{try{if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory),StringComparison.OrdinalIgnoreCase))Directory.Delete(root,true);}catch{}}
 }
}
