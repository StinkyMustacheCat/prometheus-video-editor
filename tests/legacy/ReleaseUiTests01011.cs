using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class ReleaseUiTests01011 {
 static object Field(object target,string name){return target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
 static void Call(object target,string name,params object[] args){target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);}
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 [STAThread]static void Main(string[] args){
  Application.EnableVisualStyles();typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  using(var form=new MainForm()){
   form.Show();Application.DoEvents();var editor=(Editor)Field(form,"editor");var timeline=(Timeline)Field(form,"timeline");
   var project=Store.New();var image=Media.Inspect(Path.Combine(Path.GetFullPath(args[0]),"demo","grafico.png"),System.Threading.CancellationToken.None);project.assets.Add(image);editor.Reset(project);Call(form,"RefreshProject");
   var top=new Point((int)timeline.X(1),20);string videoZone=timeline.NewTrackDropAt(top,"video");
   Check(videoZone==Timeline.NewVideoTrackDrop&&timeline.CanDropAsset(image.id,videoZone,1),"zona superior acepta imagen para crear pista");
   timeline.DropAsset(image.id,videoZone,1);Check(editor.Project.Active.tracks.Count(t=>t.kind=="video")==3&&editor.Project.Active.clips.Any(c=>c.asset_id==image.id),"arrastre desde Medios crea pista y clip en una edición");
   editor.Undo();Call(form,"RefreshProject");Check(editor.Project.Active.tracks.Count(t=>t.kind=="video")==2&&editor.Project.Active.clips.Count==0,"deshacer retira la pista creada al soltar");
   var last=editor.Project.Active.tracks.Where(t=>t.kind=="audio").OrderBy(t=>t.order).Last();var bottom=new Point((int)timeline.X(1),Math.Min(timeline.Height,timeline.TrackTop(last.id)+timeline.TrackHeight(last.id))-6);
   Check(timeline.NewTrackDropAt(bottom,"audio")==Timeline.NewAudioTrackDrop,"zona inferior ofrece crear pista de audio");
   string file=Path.Combine(Path.GetFullPath(args[0]),"demo","grafico.png");
   Check(timeline.CanDropFiles(new[]{file},Timeline.NewVideoTrackDrop),"archivo de imagen local es compatible con pista nueva");
   var payload=new DataObject();payload.SetData(DataFormats.FileDrop,new[]{file});var position=timeline.PointToScreen(top);
   var drag=new DragEventArgs(payload,0,position.X,position.Y,DragDropEffects.Copy,DragDropEffects.None);
   typeof(Timeline).GetMethod("OnDragEnter",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(timeline,new object[]{drag});
   Check(drag.Effect==DragDropEffects.Copy,"soltar imagen local en zona libre muestra destino válido");
   typeof(Timeline).GetMethod("OnDragDrop",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(timeline,new object[]{drag});
   var wait=System.Diagnostics.Stopwatch.StartNew();while(wait.Elapsed.TotalSeconds<8&&editor.Project.Active.clips.Count==0){Application.DoEvents();System.Threading.Thread.Sleep(30);}
   Check(editor.Project.Active.clips.Count==1&&editor.Project.Active.tracks.Count(t=>t.kind=="video")==3,"arrastre real de archivo crea pista y medio");
   timeline.Selected=editor.Project.Active.clips[0].id;timeline.Selection=new System.Collections.Generic.List<string>{timeline.Selected};Call(form,"ShowClip");
   Call(form,"BeginMaskTool","sam2");var monitor=(PreviewCanvas)Field(form,"monitor");
   Check(monitor.ObjectPickActive&&((bool)Field(form,"maskToolActive")),"modo de máscara activa selección solo en monitor");
   Call(form,"BeginMaskTool","sam2");Check(!monitor.ObjectPickActive&&!((bool)Field(form,"maskToolActive")),"segundo clic sale del modo y limpia la selección");
   form.GetType().GetField("dirty",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,false);form.Close();
  }
 }
}
