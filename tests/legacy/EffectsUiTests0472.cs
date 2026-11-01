using System;
using System.Drawing;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class EffectsUiTests0472 {
 static object Field(object owner,string name){return owner.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(owner);}
 static void Call(object owner,string name,params object[] args){owner.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(owner,args);Application.DoEvents();}
 static void Check(bool result,string name){if(!result)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static IEnumerable<T> Descendants<T>(Control root) where T:Control {foreach(Control child in root.Controls){var target=child as T;if(target!=null)yield return target;foreach(var nested in Descendants<T>(child))yield return nested;}}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Public|BindingFlags.Static).SetValue(null,true);
  using(var form=new MainForm()){
   form.Show();Application.DoEvents();var editor=(Editor)Field(form,"editor");var timeline=(Timeline)Field(form,"timeline");var controls=(EffectControlsPanelView)Field(form,"effectControlsView");
   var asset=GeneratedMedia.CreateSolid("Prueba","FF0000",2);editor.Commit(p=>{p.assets.Add(asset);Media.Insert(p,asset.id,p.Active.tracks.First(t=>t.kind=="video").id,0);});Call(form,"RefreshProject");
   var clip=editor.Project.Active.clips.Single();timeline.Selected=clip.id;timeline.Selection.Add(clip.id);Call(form,"ShowClip");
   var header=controls.Controls.OfType<FlowLayoutPanel>().First();var buttons=header.Controls.OfType<Button>().ToArray();
   Check(buttons.Length==3&&buttons[0].BackColor.R>200&&buttons[1].Text=="Fx"&&buttons[2].Text=="▣","cabecera compacta con ojo, Fx y Ajustar al lienzo");
   buttons[2].PerformClick();Application.DoEvents();Check(ClipFraming.Read(editor.Project.Active.clips.Single()).mode=="native"&&ClipFraming.Read(editor.Project.Active.clips.Single()).scale==1,"interruptor cambia base sin alterar escala 100 %");
   editor.Undo();Call(form,"RefreshProject");Check(ClipFraming.Read(editor.Project.Active.clips.Single()).mode=="fit","deshacer restaura ajuste al lienzo");
   header=controls.Controls.OfType<FlowLayoutPanel>().First();buttons=header.Controls.OfType<Button>().ToArray();buttons[0].PerformClick();Application.DoEvents();Check(!editor.Project.Active.clips.Single().enabled,"ojo oculta solo el clip");
   editor.Undo();Call(form,"RefreshProject");header=controls.Controls.OfType<FlowLayoutPanel>().First();buttons=header.Controls.OfType<Button>().ToArray();buttons[1].PerformClick();Application.DoEvents();Check(!editor.Project.Active.clips.Single().inherit_track_effects,"Fx del clip controla herencia");
   editor.Undo();Call(form,"RefreshProject");
   var scale=Descendants<ScrubNumber>(controls).First();Check(Math.Abs(scale.Value-100)<0.001,"la escala visible inicia en 100 %");
   int revision=editor.Project.revision;
   var setter=typeof(ScrubNumber).GetMethod("SetValue",BindingFlags.NonPublic|BindingFlags.Instance);var commit=typeof(ScrubNumber).GetMethod("Commit",BindingFlags.NonPublic|BindingFlags.Instance);
   setter.Invoke(scale,new object[]{115.0,true});setter.Invoke(scale,new object[]{120.0,true});Check(editor.Project.revision==revision,"el arrastre solo previsualiza sin crear historial");
   commit.Invoke(scale,null);Application.DoEvents();Check(editor.Project.revision==revision+1&&Math.Abs(ClipFraming.Read(editor.Project.Active.clips.Single()).scale-1.2)<0.001,"un gesto de escala crea un solo deshacer");
   editor.Undo();Call(form,"RefreshProject");Check(Math.Abs(ClipFraming.Read(editor.Project.Active.clips.Single()).scale-1)<0.001,"deshacer restaura escala anterior");
   scale=Descendants<ScrubNumber>(controls).First();var valueLabel=scale.Controls.OfType<Label>().Last();
   var down=typeof(Label).GetMethod("OnMouseDown",BindingFlags.NonPublic|BindingFlags.Instance);var move=typeof(Label).GetMethod("OnMouseMove",BindingFlags.NonPublic|BindingFlags.Instance);var up=typeof(Label).GetMethod("OnMouseUp",BindingFlags.NonPublic|BindingFlags.Instance);
   var position=valueLabel.PointToScreen(new Point(20,12));Cursor.Position=position;
   int beforeDrag=editor.Project.revision;down.Invoke(valueLabel,new object[]{new MouseEventArgs(MouseButtons.Left,1,20,12,0)});
   Cursor.Position=new Point(position.X+30,position.Y);move.Invoke(valueLabel,new object[]{new MouseEventArgs(MouseButtons.Left,0,50,12,0)});
   up.Invoke(valueLabel,new object[]{new MouseEventArgs(MouseButtons.Left,1,50,12,0)});Application.DoEvents();
   Check(editor.Project.revision==beforeDrag+1&&ClipFraming.Read(editor.Project.Active.clips.Single()).scale>1,"arrastrar la cifra cambia escala con un solo registro de deshacer");
   scale=Descendants<ScrubNumber>(controls).First();scale.Controls.OfType<Button>().First().PerformClick();Application.DoEvents();Check(scale.Height>30,"la flecha despliega un deslizador sin perder el valor");
   valueLabel=scale.Controls.OfType<Label>().Last();down.Invoke(valueLabel,new object[]{new MouseEventArgs(MouseButtons.Left,1,20,12,0)});up.Invoke(valueLabel,new object[]{new MouseEventArgs(MouseButtons.Left,1,20,12,0)});Application.DoEvents();
   var editorField=scale.Controls.OfType<TextBox>().First();Check(editorField.Visible,"clic en el número permite escribir un valor exacto");
   editorField.Text="135";int beforeText=editor.Project.revision;typeof(ScrubNumber).GetMethod("ApplyText",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(scale,null);Application.DoEvents();
   Check(editor.Project.revision==beforeText+1&&Math.Abs(ClipFraming.Read(editor.Project.Active.clips.Single()).scale-1.35)<0.001,"valor escrito guarda una sola edición");
   Call(form,"ApplyCatalogEffectToClip",EffectContracts.CropId,clip.id);
   Check(editor.Project.Active.clips.Single().effects.Any(e=>e.effect_id==EffectContracts.CropId),"recorte entra desde catálogo al clip");
   Call(form,"ApplyCatalogEffectToTrack",EffectContracts.HueId,editor.Project.Active.tracks.First(t=>t.kind=="video").id);
   Check(editor.Project.Active.tracks.First(t=>t.kind=="video").effects.Any(e=>e.effect_id==EffectContracts.HueId),"tono entra desde catálogo a pista");
  }
 }
}
