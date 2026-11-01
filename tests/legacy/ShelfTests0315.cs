using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class ShelfTests0315 {
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
 [STAThread]static void Main(){
  var project=Store.New();
  string metadata="{\"id\":\"abc123\",\"title\":\"Video de prueba\",\"duration\":42,\"fps\":30,\"width\":1280,\"height\":720,\"vcodec\":\"h264\",\"acodec\":\"aac\"}";
  var asset=WebMedia.FromMetadata("https://www.youtube.com/watch?v=abc123",metadata);
  ShelfStore.Add(project,asset);ShelfStore.Add(project,asset);
  Check(ShelfStore.Read(project).Count==1&&project.assets.Count==0,"Bandeja evita URL duplicada sin crear medio ni proxy");
  project=Store.Decode(Store.Encode(project));
  var item=ShelfStore.Read(project).Single();
  Check(item.asset.name=="Video de prueba"&&item.asset.streams[0].duration.Seconds==42,"metadatos y orden sobreviven al guardado");
  var promoted=ShelfStore.Promote(project,item);
  Check(project.assets.Count==1&&promoted.representations.Count==0,"promoción crea medio virtual sin descarga");
  Check(ShelfStore.Promote(project,item).id==promoted.id&&project.assets.Count==1,"promoción repetida reutiliza el medio existente");
  var track=project.Active.tracks.First(x=>x.kind=="video");
  var placed=Media.PlaceInFreeTrack(project,promoted.id,track.id,0);
  Rendering.GeneratedCacheRoot=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"shelf-test-cache");
  Check(Rendering.Build(project,true,true,640,360,60,"raw",0,true).Contains("filter_complex"),"Bandeja pendiente no bloquea la previsualización");
  ShelfStore.Remove(project,new[]{item.id});Store.Validate(project);
  Check(ShelfStore.Read(project).Count==0&&placed.ClipIds.Count==2,"importación retira la referencia y crea clips A/V vinculados");
  using(var window=new ShelfWindow()){
   window.SetItems(new[]{item});window.CaptureText("https://www.youtube.com/watch?v=abc123");
   Check(window.Controls.Count>0,"Bandeja construye su ventana flotante");
  }
 }
}
