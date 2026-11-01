using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Prometheus;

class SplitTimeline03167 : Timeline {
 protected override Keys CurrentModifiers {get{return Modifiers;}}
 public Keys Modifiers {get;set;}
 public void Down(int x,int y){OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,x,y,0));}
 public void DragTo(int x,int y){OnMouseMove(new MouseEventArgs(MouseButtons.Left,0,x,y,0));}
 public void Up(int x,int y){OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,x,y,0));}
 public void Spin(int x,int y,int delta){OnMouseWheel(new MouseEventArgs(MouseButtons.None,0,x,y,delta));}
 public void SimulateDragOver(DragEventArgs args){OnDragOver(args);}
 public void Drop(DragEventArgs args){OnDragDrop(args);}
}
class Tests03167 {
 static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);}
 [STAThread]static void Main(string[] args){
  Check(WebMedia.Platform("https://www.facebook.com/share/v/abc123/")=="facebook","Facebook share/v se reconoce como vídeo");
  Check(WebMedia.Platform("https://www.facebook.com/share/r/abc123/")=="facebook","Facebook share/r se reconoce como vídeo");
  Check(WebMedia.ProbeUrlKind("https://cdn.discordapp.com/attachments/123/456/picture.png?ex=deadbeef",System.Threading.CancellationToken.None)=="image","imagen CDN con URL temporal se clasifica sin descargarla");
  var instagram=WebMedia.FromMetadata("https://www.instagram.com/reel/ABC123/","{\"id\":\"ABC123\",\"title\":\"Reel sin duración\",\"formats\":[{\"width\":720,\"height\":1280,\"vcodec\":\"h264\"}]}");
  Check(WebMedia.DurationProvisional(instagram)&&instagram.streams[0].duration.Seconds==5,"Instagram sin duración produce medio virtual provisional");
  var project=Store.New();project.assets.Add(instagram);Media.Insert(project,instagram.id,project.Active.tracks.First(t=>t.kind=="video").id,0);Store.Validate(project);
  WebMedia.ReconcileDuration(project,instagram.id,2.5);Store.Validate(project);
  Check(!WebMedia.DurationProvisional(instagram)&&Math.Abs(instagram.streams[0].duration.Seconds-2.5)<0.02,"duración real se confirma sin romper proyecto");
  Check(ShortTail.MissingStart(project,project.Active.clips.First())!=null,"fuente corta deja cola rayada");
  var longer=WebMedia.FromMetadata("https://www.instagram.com/reel/DEF456/","{\"id\":\"DEF456\",\"title\":\"Reel largo\",\"vcodec\":\"h264\"}");
  project=Store.New();project.assets.Add(longer);Media.Insert(project,longer.id,project.Active.tracks.First(t=>t.kind=="video").id,0);WebMedia.ReconcileDuration(project,longer.id,8);Store.Validate(project);
  Check(Math.Abs(project.Active.clips.First().duration.Seconds-8)<0.02,"fuente larga extiende un clip intacto si hay espacio");
  var generated=Store.New();var solid=GeneratedMedia.CreateSolid("Rojo","FF0000",2);generated.assets.Add(solid);Media.Insert(generated,solid.id,generated.Active.tracks.First(t=>t.kind=="video").id,0);
  string normal=Rendering.Build(generated,true,true,320,180,60,"sample.mp4",0,false);
  string faster=Rendering.Build(generated,true,true,320,180,60,"audio-raw",0,true,2.5);
  Check(!normal.Contains("atempo=2")&&faster.Contains("atempo=2")&&faster.Contains("atempo=1.25"),"velocidad modifica la previsualización y deja exportación normal");
  if(args.Length>0){
   Tools.Root=Path.GetFullPath(args[0]);string output=Path.Combine(Path.GetTempPath(),"prometheus-speed-"+Guid.NewGuid().ToString("N")+".wav");
   try{Tools.Run(Tools.FFmpeg,Rendering.Build(generated,false,true,320,180,60,output,0,true,2.5),CancellationToken.None);var measured=Media.Inspect(output,CancellationToken.None).streams.First(s=>s.kind=="audio").duration.Seconds;Check(Math.Abs(measured-0.8)<0.08,"audio acelerado dura lo esperado en FFmpeg");}
   finally{if(File.Exists(output))File.Delete(output);}
   string webp=Path.Combine(Path.GetTempPath(),"prometheus-thumb-"+Guid.NewGuid().ToString("N")+".webp");string png=webp+".png";
   try{Tools.Run(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -y -f lavfi -i color=c=orange:s=32x32:d=1 -frames:v 1 -c:v libwebp "+Tools.Q(webp),CancellationToken.None);typeof(WebMedia).GetMethod("SavePng",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{File.ReadAllBytes(webp),png,CancellationToken.None});using(var thumb=Image.FromFile(png))Check(thumb.Width==32&&thumb.Height==32,"miniatura WebP se convierte a PNG");}
   finally{if(File.Exists(webp))File.Delete(webp);if(File.Exists(png))File.Delete(png);}
  }
  using(var timeline=new SplitTimeline03167{Project=Store.New(),Width=900,Height=380}){
   var video=timeline.Project.Active.tracks.First(t=>t.kind=="video");var audio=timeline.Project.Active.tracks.First(t=>t.kind=="audio");
   int videoTop=timeline.TrackTop(video.id),audioTop=timeline.TrackTop(audio.id),videoHeight=timeline.TrackHeight(video.id),audioHeight=timeline.TrackHeight(audio.id),separator=audioTop-7;
   timeline.Down(70,separator);timeline.DragTo(70,separator+40);timeline.Up(70,separator+40);
   Check(timeline.TrackTop(video.id)==videoTop+40&&timeline.TrackTop(audio.id)==audioTop+40,"separador desplaza ambos grupos sin dejar hueco entre ellos");
   Check(timeline.TrackHeight(video.id)==videoHeight&&timeline.TrackHeight(audio.id)==audioHeight,"separador no cambia alturas de pistas");
   Check(timeline.TrackAt(separator+43)==null,"la franja divisora no recibe medios");
  }
  Application.EnableVisualStyles();
  using(var host=new Form{Width=930,Height=470})using(var timeline=new SplitTimeline03167{Project=Store.New(),Dock=DockStyle.Fill}){
   host.Controls.Add(timeline);host.Show();Application.DoEvents();
   string video=timeline.Project.Active.tracks.First(t=>t.kind=="video").id;
   string dropped=null,track=null;double at=-1;timeline.CanDropShelf=(id,t,time)=>id=="shelf-03167"&&t==video;
   timeline.DropShelf=(id,t,time)=>{dropped=id;track=t;at=time;};
   var data=new DataObject();data.SetData("Prometheus.ShelfId","shelf-03167");var screen=timeline.PointToScreen(new Point(210,timeline.TrackTop(video)+12));
   var drag=new DragEventArgs(data,0,screen.X,screen.Y,DragDropEffects.Copy,DragDropEffects.None);
   timeline.SimulateDragOver(drag);Check(drag.Effect==DragDropEffects.Copy,"la Bandeja se admite como origen de arrastre en una pista compatible");
   timeline.Drop(drag);Check(dropped=="shelf-03167"&&track==video&&at>=0,"soltar desde Bandeja conserva medio, pista y tiempo");
  }
  using(var shelf=new ShelfWindow()){
   var buttons=(System.Collections.Generic.List<Button>)typeof(ShelfWindow).GetField("selectedButtons",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(shelf);
   Check(buttons.All(button=>!button.Enabled),"acciones de Bandeja sin selección empiezan desactivadas");
   var items=Store.New();ShelfStore.Add(items,GeneratedMedia.CreateSolid("Prueba","224466",2));shelf.SetItems(ShelfStore.Read(items));
   Check(buttons.All(button=>!button.Enabled),"añadir un elemento no activa importar sin seleccionarlo");
  }
 }
}
