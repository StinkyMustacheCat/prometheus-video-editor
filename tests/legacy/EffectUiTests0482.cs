using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class EffectUiTests0482 {
 static object Field(object owner,string name){return owner.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(owner);}
 static void Call(object owner,string name,params object[] args){owner.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(owner,args);Application.DoEvents();}
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static Panel Card(EffectControlsPanelView view,string id){return view.Controls.OfType<Panel>().FirstOrDefault(p=>Equals(p.Tag,id));}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Public|BindingFlags.Static).SetValue(null,true);
  using(var form=new MainForm()){
   form.Show();form.Size=new Size(1600,930);Application.DoEvents();((PanelWorkspace)Field(form,"workspace")).ApplyPreset("edicion-completa");
   var editor=(Editor)Field(form,"editor");var timeline=(Timeline)Field(form,"timeline");var view=(EffectControlsPanelView)Field(form,"effectControlsView");
   var asset=Media.Inspect(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"demo","video.mp4"),System.Threading.CancellationToken.None);
   editor.Commit(p=>{p.assets.Add(asset);Media.Insert(p,asset.id,p.Active.tracks.First(t=>t.kind=="video").id,0);});Call(form,"RefreshProject");
   var video=editor.Project.Active.clips.First(c=>editor.Project.Active.tracks.First(t=>t.id==c.track_id).kind=="video");
   var audio=editor.Project.Active.clips.First(c=>editor.Project.Active.tracks.First(t=>t.id==c.track_id).kind=="audio");
   timeline.Selected=video.id;timeline.Selection.Add(video.id);Call(form,"ShowClip");
   Check(view.Controls.OfType<Button>().Any(b=>b.Text.Contains("TRANSFORMACIÓN"))&&view.Controls.OfType<Button>().Any(b=>b.Text.Contains("AUDIO")),"un vídeo vinculado muestra Transformación y Audio");
   var transform=view.Controls.OfType<Button>().First(b=>b.Text.Contains("TRANSFORMACIÓN"));transform.PerformClick();Application.DoEvents();
   Check(view.Controls.OfType<Button>().First(b=>b.Text.Contains("TRANSFORMACIÓN")).Text.StartsWith("▸"),"Transformación se puede plegar");
   Call(form,"ApplyCatalogEffectToClip",EffectContracts.FlipId,video.id);
   var flip=editor.Project.Active.clips.First(c=>c.id==video.id).effects.Single(e=>e.effect_id==EffectContracts.FlipId);var card=Card(view,flip.id);
   Check(card!=null&&card.Controls.OfType<FlowLayoutPanel>().First(p=>p.Dock==DockStyle.Top).Controls.OfType<Button>().Count()==2,"efecto sin parámetros ocupa solo una fila sin desplegable");
   Check(card.ContextMenuStrip.Items.Cast<ToolStripItem>().Any(i=>i.Text=="Eliminar efecto"),"eliminar efecto está en el menú contextual");
   var head=card.Controls.OfType<FlowLayoutPanel>().First(p=>p.Dock==DockStyle.Top);var name=head.Controls.OfType<Label>().First();
   typeof(Label).GetMethod("OnMouseDown",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(name,new object[]{new MouseEventArgs(MouseButtons.Left,1,4,5,0)});
   Check(view.ContainsFocus&&view.DeleteSelectedEffect()&&editor.Project.Active.clips.First(c=>c.id==video.id).effects.Count==0,"cabecera seleccionada se puede eliminar sin tocar el clip");
   Call(form,"ApplyCatalogEffectToClip",EffectContracts.BasicColorId,video.id);
   var basic=editor.Project.Active.clips.First(c=>c.id==video.id).effects.Single(e=>e.effect_id==EffectContracts.BasicColorId);
   Call(form,"SetEffectNumber",false,video.id,basic.id,"brightness",0.5);Check(Math.Abs(editor.Project.Active.clips.First(c=>c.id==video.id).effects.Single().parameters["brightness"].number.Value-0.5)<0.001,"valor del efecto modificable");
   card=Card(view,basic.id);var reset=card.Controls.OfType<FlowLayoutPanel>().First(p=>p.Dock==DockStyle.Top).Controls.OfType<Button>().Last();reset.PerformClick();Application.DoEvents();
   Check(Math.Abs(editor.Project.Active.clips.First(c=>c.id==video.id).effects.Single().parameters["brightness"].number.Value)<0.001,"flecha de retorno restaura parámetros sin eliminar efecto");
   Call(form,"ApplyCatalogEffectToClip",EffectContracts.CropId,video.id);var crop=editor.Project.Active.clips.First(c=>c.id==video.id).effects.Last();
   card=Card(view,basic.id);var data=new DataObject();data.SetData("Prometheus.EffectRow","0|"+video.id+"|"+crop.id);var target=card.PointToScreen(new Point(5,2));
   typeof(Control).GetMethod("OnDragDrop",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(card,new object[]{new DragEventArgs(data,0,target.X,target.Y,DragDropEffects.Move,DragDropEffects.Move)});Application.DoEvents();
   Check(editor.Project.Active.clips.First(c=>c.id==video.id).effects.First().id==crop.id,"soltar una fila reordena el efecto antes de la indicada");
   Call(form,"ApplyCatalogEffectToClip",EffectContracts.HighPassId,audio.id);Check(editor.Project.Active.clips.First(c=>c.id==audio.id).effects.Any(e=>e.effect_id==EffectContracts.HighPassId),"efecto de audio entra en pista de audio");
   timeline.Selected=video.id;Call(form,"ShowClip");Check(view.Controls.OfType<Button>().Any(b=>b.Text.Contains("AUDIO")),"controles de audio siguen visibles al seleccionar el vídeo vinculado");
   Call(form,"UpdateAudioNumber",false,audio.id,0.4,true);Check(Math.Abs(editor.Project.Active.clips.First(c=>c.id==audio.id).balance-0.4)<0.001,"balance se edita desde Audio");
   var audioTrack=editor.Project.Active.tracks.First(t=>t.id==audio.track_id);Call(form,"ApplyCatalogEffectToTrack",EffectContracts.HighPassId,audioTrack.id);
   Check(editor.Project.Active.tracks.First(t=>t.id==audio.track_id).effects.Any(e=>e.effect_id==EffectContracts.HighPassId)&&view.Controls.OfType<Button>().Any(b=>b.Text.Contains("AUDIO")),"pista de audio admite efectos y controles propios");
   Call(form,"UpdateAudioNumber",true,audioTrack.id,-3.0,false);Check(Math.Abs(editor.Project.Active.tracks.First(t=>t.id==audio.track_id).gain_db+3)<0.001,"volumen de pista se guarda desde Audio");
   timeline.Selected=video.id;Call(form,"ShowClip");
   int revision=editor.Project.revision;Call(form,"PreviewClipFraming",video.id,"fit",1.2,0.1,0);Check(editor.Project.revision==revision,"arrastre de transformación previsualiza sin guardar cada paso");
  }
 }
}
