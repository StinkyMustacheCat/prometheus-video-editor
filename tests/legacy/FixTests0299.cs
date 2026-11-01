using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Prometheus;

class CaptureProbe0299:ShortcutCaptureForm {
 public bool DialogKey(Keys key){return ProcessDialogKey(key);}
}
class FixTests0299 {
 static int count;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);count++;}
 [STAThread]static void Main(){
  string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"prometheus-fix-0299-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   string image=Path.Combine(root,"still.png");using(var bitmap=new Bitmap(64,36)){using(var g=Graphics.FromImage(bitmap))g.Clear(Color.Orange);bitmap.Save(image,System.Drawing.Imaging.ImageFormat.Png);}
   var p=Store.New();var track=p.Active.tracks.First(t=>t.kind=="video");
   var asset=new Asset{id="still-asset",name="still",source=new MediaSource{kind="local",uri=image},presentation_origin=Rational.Time(0),streams={new StreamInfo{id="still-stream",ordinal=0,kind="image",codec="png",pts_origin=Rational.Time(0),time_base=new Rational(1,60)}}};
   asset.representations.Add(new MediaRepresentation{role="original",locator=new Locator{path=image}});p.assets.Add(asset);
   p.Active.clips.Add(new Clip{id="still-clip",name="still",asset_id=asset.id,stream_id="still-stream",track_id=track.id,timeline_start=Rational.Time(0),duration=Rational.Time(1),source_in=Rational.Time(0)});
   Markers.Add(p.Active,0.5,"Revisar");TrackPresentation.SetShowNames(p.Active,track.id,false);Store.Validate(p);
   Check(Rendering.Build(p,true,false,64,36,60,"raw",0).Contains("-filter_complex"),"marcadores y preferencias no bloquean el render");
   using(var frame=Rendering.Frame(p,0.5,CancellationToken.None))Check(frame.Width==640,"previsualización funciona con marcador");
   string output=Path.Combine(root,"export.mp4");Rendering.Export(p,output,CancellationToken.None);Check(File.Exists(output)&&new FileInfo(output).Length>0,"exportación funciona con marcador");
   p.Active.extensions.Add(new Extension{@namespace="future.render.effect",version="1",payload=new System.Collections.Generic.Dictionary<string,object>()});bool rejected=false;
   try{Rendering.Build(p,true,false,64,36,60,"raw",0);}catch(Exception){rejected=true;}Check(rejected,"extensión de render desconocida sigue protegida");
   using(var form=new CaptureProbe0299()){Keys captured=Keys.None;form.CaptureKey=key=>{captured=key;return true;};Check(form.DialogKey(Keys.Left)&&captured==Keys.Left,"flecha izquierda se captura en el diálogo");Check(form.DialogKey(Keys.Control|Keys.Right)&&captured==(Keys.Control|Keys.Right),"flecha con modificador se captura");}
   using(var panel=new TimelinePanelView()){panel.SetTime(65.5);var label=panel.Timecode.Controls.OfType<Label>().First();Check(label.Text=="00:01:05:30","posición del lector aparece en la línea de tiempo");}
   Console.WriteLine("PASS TOTAL "+count);
  }finally{try{Directory.Delete(root,true);}catch{}}
 }
}
