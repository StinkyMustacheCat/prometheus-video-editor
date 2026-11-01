using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Prometheus;

class EffectUiTests0464 {
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr wparam,IntPtr lparam);
 static object Field(object owner,string name){return owner.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(owner);}
 static void Call(object owner,string method,params object[] args){var target=owner.GetType().GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance);if(target==null)throw new Exception("Falta "+method);target.Invoke(owner,args);Application.DoEvents();}
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Mouse(Timeline timeline,MouseButtons button,int x,int y){Call(timeline,"OnMouseDown",new MouseEventArgs(button,1,x,y,0));}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Public|BindingFlags.Static).SetValue(null,true);
  using(var form=new MainForm()){
   form.Show();Application.DoEvents();var editor=(Editor)Field(form,"editor");var timeline=(Timeline)Field(form,"timeline");
   var workspace=(PanelWorkspace)Field(form,"workspace");
   Check(workspace.Entries.Any(e=>e.Id=="efectos"&&e.Content is EffectsCatalogView)&&workspace.Entries.Any(e=>e.Id=="propiedades"&&e.Title=="CONTROLES DE EFECTOS"&&e.Content is EffectControlsPanelView),"paneles Efectos y Controles de efectos separados");
   var asset=GeneratedMedia.CreateSolid("Prueba","FF0000",2);editor.Commit(p=>{p.assets.Add(asset);Media.Insert(p,asset.id,p.Active.tracks.First(t=>t.kind=="video").id,0);});Call(form,"RefreshProject");
   var clip=editor.Project.Active.clips.Single();var track=editor.Project.Active.tracks.First(t=>t.id==clip.track_id);timeline.Selected=clip.id;timeline.Selection.Clear();timeline.Selection.Add(clip.id);Call(form,"ShowClip");
   Call(form,"ApplyCatalogEffectToClip",EffectContracts.FlipId,clip.id);
   Check(editor.Project.Active.clips.Single().effects.Count==1&&editor.Project.Active.clips.Single().effects[0].effect_id==EffectContracts.FlipId,"soltar efecto en clip aplica solo al clip");
   Call(form,"ApplyCatalogEffectToTrack",EffectContracts.BasicColorId,track.id);
   Check(editor.Project.Active.tracks.First(t=>t.id==track.id).effects.Count==1&&editor.Project.Active.clips.Single().effects.Count==1,"efecto de pista no se copia en el clip");
   var controls=(EffectControlsPanelView)Field(form,"effectControlsView");
   Check(controls.Controls.OfType<Label>().Any(l=>l.Text.StartsWith("PISTA ·")),"seleccionar pista muestra su pila en Controles de efectos");
   var fx=(Rectangle)typeof(Timeline).GetMethod("EffectsButton",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(timeline,new object[]{editor.Project.Active.tracks.First(t=>t.id==track.id)});
   Mouse(timeline,MouseButtons.Left,fx.X+fx.Width/2,fx.Y+fx.Height/2);
   Check(!editor.Project.Active.tracks.First(t=>t.id==track.id).effects_enabled,"botón Fx apaga efectos de pista");
   editor.Undo();Call(form,"RefreshProject");Check(editor.Project.Active.tracks.First(t=>t.id==track.id).effects_enabled,"deshacer restaura Fx");
   int y=timeline.TrackTop(track.id)+10;Mouse(timeline,MouseButtons.Right,10,y);
   var menu=(ContextMenuStrip)Field(timeline,"renameMenu");Check(menu.Items.OfType<ToolStripItem>().Any(i=>i.Text=="Gestionar efectos de pista"),"menú de pista ofrece Gestionar efectos de pista");menu.Close();
   Call(form,"ApplyCatalogEffectToClip",EffectContracts.BlurId,clip.id);
   var clipNow=editor.Project.Active.clips.Single();string blur=clipNow.effects.Last().id;
   Call(form,"MoveEffect",false,clip.id,blur,-1);
   Check(editor.Project.Active.clips.Single().effects[0].id==blur,"Controles de efectos reordena la pila");
   Call(form,"SetClipTrackEffectsInheritance",clip.id,false);
   Check(!editor.Project.Active.clips.Single().inherit_track_effects,"interruptor del clip excluye Fx de pista");
   Call(form,"SetEffectNumber",false,clip.id,blur,"sigma",3.0);
   Check(Math.Abs(editor.Project.Active.clips.Single().effects[0].parameters["sigma"].number.Value-3)<0.001,"parámetro numérico se guarda sin perder selección");
   var blurCard=controls.Controls.OfType<Panel>().FirstOrDefault(panel=>panel.Controls.OfType<FlowLayoutPanel>().Any(row=>row.Controls.OfType<Label>().Any(label=>label.Text=="Desenfoque gaussiano")));
   Check(blurCard!=null,"la tarjeta de desenfoque aparece en Controles de efectos");
   var blurHeader=blurCard.Controls.OfType<FlowLayoutPanel>().First(row=>row.Dock==DockStyle.Top);
   blurHeader.Controls.OfType<Button>().First().PerformClick();Application.DoEvents();
   Check(blurCard.Height>=65,"la tarjeta expandida muestra sus parámetros");
   editor.Commit(p=>Media.Insert(p,asset.id,track.id,3));Call(form,"RefreshProject");
   var second=editor.Project.Active.clips.First(c=>c.id!=clip.id);
   timeline.Selected=clip.id;timeline.Selection=new System.Collections.Generic.List<string>{clip.id,second.id};Call(form,"ShowClip");
   Call(form,"ApplyCatalogEffectToSelection",EffectContracts.SharpenId);
   Check(editor.Project.Active.clips.All(c=>c.effects.Any(e=>e.effect_id==EffectContracts.SharpenId)),"doble clic en catálogo aplica a selección múltiple");
   string sharpen=editor.Project.Active.clips.First(c=>c.id==clip.id).effects.Last().id;
   Call(form,"SetEffectNumber",false,clip.id,sharpen,"amount",1.2);
   Check(editor.Project.Active.clips.All(c=>Math.Abs(c.effects.Last().parameters["amount"].number.Value-1.2)<0.001),"parámetros se aplican a selección múltiple");
   Call(form,"RemoveEffectFromTarget",false,clip.id,sharpen);
   Check(editor.Project.Active.clips.All(c=>c.effects.All(e=>e.effect_id!=EffectContracts.SharpenId)),"quitar efecto respeta selección múltiple");
   timeline.Selected=clip.id;timeline.Selection.Clear();timeline.Selection.Add(clip.id);Call(form,"ShowClip");
   var mode=controls.Controls.OfType<ComboBox>().First();timeline.Cursor=0.5;mode.CreateControl();SendMessage(mode.Handle,0x20A,new IntPtr(120<<16),IntPtr.Zero);Application.DoEvents();
   Check(Math.Abs(timeline.Cursor-0.5)<0.000001,"rueda sobre Encajar/Rellenar no mueve el lector");
  }
 }
}
