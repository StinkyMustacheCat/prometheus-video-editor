using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class EffectDropUiTests0492 {
 static object Field(object target,string name){return target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
 static void Call(object target,string name,params object[] args){target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);Application.DoEvents();}
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static DragEventArgs Drag(Control target,string effect,int y){var data=new DataObject();data.SetData("Prometheus.EffectId",effect);var point=target.PointToScreen(new Point(Math.Min(12,target.Width-1),y));return new DragEventArgs(data,0,point.X,point.Y,DragDropEffects.Copy,DragDropEffects.None);}
 static void Fire(Control target,string eventName,DragEventArgs args){typeof(Control).GetMethod(eventName,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,new object[]{args});Application.DoEvents();}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  using(var form=new MainForm()){
   form.Show();form.Size=new Size(1600,900);Application.DoEvents();
   var editor=(Editor)Field(form,"editor");var timeline=(Timeline)Field(form,"timeline");var controls=(EffectControlsPanelView)Field(form,"effectControlsView");
   var video=GeneratedMedia.CreateSolid("Color","336699",5);var audio=GeneratedMedia.CreateAudioPlaceholder("Audio","",5);
   editor.Commit(p=>{p.assets.Add(video);p.assets.Add(audio);Media.Insert(p,video.id,p.Active.tracks.First(t=>t.kind=="video").id,0);Media.Insert(p,audio.id,p.Active.tracks.First(t=>t.kind=="audio").id,0);});
   var videoClip=editor.Project.Active.clips.Single(c=>c.asset_id==video.id);var audioClip=editor.Project.Active.clips.Single(c=>c.asset_id==audio.id);
   editor.Link(new System.Collections.Generic.List<string>{videoClip.id,audioClip.id});Call(form,"RefreshProject");timeline.Selected=videoClip.id;timeline.Selection.Add(videoClip.id);Call(form,"ShowClip");
   var addColor=Drag(controls,EffectContracts.BasicColorId,controls.Height/2);Fire(controls,"OnDragEnter",addColor);
   Check(addColor.Effect==DragDropEffects.Copy,"efecto compatible entra en Controles de efectos");Fire(controls,"OnDragDrop",addColor);
   Check(editor.Project.Active.clips.Single(c=>c.id==videoClip.id).effects.Single().effect_id==EffectContracts.BasicColorId,"soltar en espacio libre añade el efecto visual");
   var first=editor.Project.Active.clips.Single(c=>c.id==videoClip.id).effects.Single();
   var card=controls.Controls.OfType<Panel>().Single(c=>Equals(c.Tag,first.id));
   var insert=Drag(card,EffectContracts.FlipId,2);Fire(card,"OnDragEnter",insert);Fire(card,"OnDragOver",insert);
   Check(insert.Effect==DragDropEffects.Copy,"la tarjeta acepta un efecto compatible antes del actual");Fire(card,"OnDragDrop",insert);
   Check(editor.Project.Active.clips.Single(c=>c.id==videoClip.id).effects.First().effect_id==EffectContracts.FlipId,"soltar sobre tarjeta inserta en la posición señalada");
   editor.Undo();Call(form,"RefreshProject");
   Check(editor.Project.Active.clips.Single(c=>c.id==videoClip.id).effects.Single().effect_id==EffectContracts.BasicColorId,"arrastre de efecto ocupa un solo paso de Deshacer");
   var addAudio=Drag(controls,EffectContracts.HighPassId,controls.Height/2);Fire(controls,"OnDragEnter",addAudio);Fire(controls,"OnDragDrop",addAudio);
   Check(editor.Project.Active.clips.Single(c=>c.id==audioClip.id).effects.Single().effect_id==EffectContracts.HighPassId,"audio soltado en clip visual se aplica al audio vinculado");
   string audioTrack=editor.Project.Active.tracks.First(t=>t.kind=="audio").id;Call(form,"SelectEffectTrack",audioTrack);
   var invalid=Drag(controls,EffectContracts.BasicColorId,controls.Height/2);Fire(controls,"OnDragEnter",invalid);
   Check(invalid.Effect==DragDropEffects.None,"una pista de audio rechaza efectos visuales");
   var trackAudio=Drag(controls,EffectContracts.HighPassId,controls.Height/2);Fire(controls,"OnDragEnter",trackAudio);Fire(controls,"OnDragDrop",trackAudio);
   Check(editor.Project.Active.tracks.Single(t=>t.id==audioTrack).effects.Single().effect_id==EffectContracts.HighPassId,"el panel de pista recibe un efecto compatible");
  }
 }
}
