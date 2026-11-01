using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class SplitTimeline031671:Timeline {
 public void Down(int y){OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,70,y,0));}
 public void DragTo(int y){OnMouseMove(new MouseEventArgs(MouseButtons.Left,0,70,y,0));}
 public void Up(int y){OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,70,y,0));}
}
class Tests031672 {
 static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();
  using(var timeline=new SplitTimeline031671{Project=Store.New(),Width=1000,Height=370}){
   var videos=timeline.Tracks().Where(x=>x.kind=="video").ToList();var audio=timeline.Project.Active.tracks.First(x=>x.kind=="audio");
   int original=timeline.TrackTop(audio.id)-7;int before=timeline.TrackHeight(videos[0].id);int target=original-35;
   timeline.Down(original);timeline.DragTo(target);timeline.Up(target);
   Check(timeline.TrackTop(videos.Last().id)+timeline.TrackHeight(videos.Last().id)==target,"al subir la división, la pista de vídeo inferior acompaña el borde");
   Check(timeline.TrackTop(audio.id)==target+7,"las pistas de audio empiezan después de la división");
   Check(timeline.TrackHeight(videos[0].id)==before,"el separador no cambia la altura individual");
   Check(timeline.TrackAt(target+3)==null,"la división no pertenece a ninguna pista");
   timeline.Down(target);timeline.DragTo(original+35);timeline.Up(original+35);
   Check(timeline.TrackTop(videos.Last().id)+timeline.TrackHeight(videos.Last().id)==original+35,"al bajar la división, el vídeo también acompaña el borde");
   Check(timeline.TrackTop(audio.id)==original+42,"el audio permanece debajo del separador");
   Check(timeline.TrackHeight(videos[0].id)==before,"ambas direcciones conservan la altura de las pistas");
  }
  var asset=WebMedia.FromMetadata("https://www.instagram.com/reel/test/","{\"id\":\"test\",\"title\":\"Sin duración\",\"vcodec\":\"h264\"}");
  WebMedia.EnsureProvisionalDuration(asset,42);
  Check(WebMedia.DurationProvisional(asset)&&asset.streams.First().duration.Seconds==42,"un tramo elegido cabe en un medio virtual de duración desconocida");
  var project=Store.New();project.assets.Add(asset);Media.PlaceInFreeTrack(project,asset.id,project.Active.tracks.First(x=>x.kind=="video").id,0,30,12);Store.Validate(project);
  WebMedia.ReconcileDuration(project,asset.id,35);Store.Validate(project);
  Check(ShortTail.MissingStart(project,project.Active.clips.First())!=null,"un tramo más largo que la fuente conserva su cola rayada");
  using(var dialog=new LinkImportDialog(true,false,Color.FromArgb(36,38,42),Color.White)){
   Check(!dialog.RangeExpanded&&dialog.RangeIn==null,"importar enlace empieza con medio completo");
   var toggle=(Button)typeof(LinkImportDialog).GetField("toggle",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(dialog);
   var address=(TextBox)typeof(LinkImportDialog).GetField("address",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(dialog);
   var to=(TextBox)typeof(LinkImportDialog).GetField("to",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(dialog);
   dialog.Show();Application.DoEvents();address.Text="https://www.youtube.com/watch?v=abc123";toggle.PerformClick();Check(to.Text=="00:00:00","Hasta muestra el contador al desplegar el tramo");to.Text="00:00:12";
   Check(dialog.RangeExpanded&&dialog.ClientSize.Height>200,"el tramo se despliega dentro de la misma ventana");
   Check(to.Text=="00:00:12","Hasta permite escribir la duración sin completar manualmente el formato");
   typeof(LinkImportDialog).GetMethod("Accept",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(dialog,null);
   Check(dialog.RangeIn==0&&dialog.RangeOut==12,"la importación conserva Desde/Hasta sin segunda pantalla");
  }
  typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  using(var form=new MainForm()){
   var workspace=(PanelWorkspace)typeof(MainForm).GetField("workspace",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
   workspace.PersistenceEnabled=false;form.Width=1540;form.Height=900;form.Show();Application.DoEvents();workspace.ApplyPreset("edicion-completa");Application.DoEvents();
   Check(workspace.CurrentPreset=="edicion-completa"&&new[]{"efectos","propiedades","monitor","color","medios","timeline","mezclador"}.All(id=>workspace.Entries.First(x=>x.Id==id).State=="docked"),"Edición completa acopla los siete paneles de la captura");
   var media=(MediaPanelView)workspace.Entries.First(x=>x.Id=="medios").Content;
   var filter=(Control)typeof(MediaPanelView).GetField("filterButton",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(media);
   Check(filter.ClientRectangle.Width>=25&&filter.Parent!=null,"el filtro conserva su botón dentro de Medios abajo");
   typeof(MediaPanelView).GetMethod("ShowFilterMenu",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(media,null);Application.DoEvents();
   var filterPopup=(Panel)typeof(MediaPanelView).GetField("filterPopup",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(media);
   Check(filterPopup.Visible&&media.ClientRectangle.Contains(filterPopup.Bounds),"el filtro desplegado queda dentro de Medios abajo");
   Rectangle buttonBounds=(Rectangle)typeof(MediaPanelView).GetMethod("VisibleFilterBounds",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(media,null);
   Check(filterPopup.Top==buttonBounds.Bottom||filterPopup.Bottom==buttonBounds.Top,"el menú queda pegado al botón sin flotar sobre la lista");
   using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"layout-031672.png"));}
   Rectangle capture=form.RectangleToScreen(form.ClientRectangle);
   using(var image=new Bitmap(capture.Width,capture.Height))using(var graphics=Graphics.FromImage(image)){graphics.CopyFromScreen(capture.Location,Point.Empty,capture.Size);image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"filter-screen-031672.png"));}
   workspace.ApplyPreset("montaje");Application.DoEvents();Check(!filterPopup.Visible,"cambiar de diseño cierra el filtro anterior");
   typeof(MediaPanelView).GetMethod("ShowFilterMenu",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(media,null);Application.DoEvents();
   Check(filterPopup.Visible&&media.ClientRectangle.Contains(filterPopup.Bounds),"el filtro también queda anclado en Montaje");
   foreach(string preset in new[]{"equilibrado","broll","revision","edicion-completa"}){
    workspace.ApplyPreset(preset);Application.DoEvents();
    typeof(MediaPanelView).GetMethod("ShowFilterMenu",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(media,null);Application.DoEvents();
    buttonBounds=(Rectangle)typeof(MediaPanelView).GetMethod("VisibleFilterBounds",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(media,null);
    Check(filterPopup.Visible&&media.ClientRectangle.Contains(filterPopup.Bounds)&&(filterPopup.Top==buttonBounds.Bottom||filterPopup.Bottom==buttonBounds.Top),"el filtro permanece anclado en "+preset);
   }
   form.Close();
  }
 }
}
