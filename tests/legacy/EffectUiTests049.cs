using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class EffectUiTests049 {
 static object Field(object owner,string name){return owner.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(owner);}
 static void Call(object owner,string name,params object[] args){owner.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(owner,args);Application.DoEvents();}
 static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);}
 static Panel Card(EffectControlsPanelView view,string id){return view.Controls.OfType<Panel>().FirstOrDefault(p=>Equals(p.Tag,id));}
 static FlowLayoutPanel Body(Panel card){return card.Controls.OfType<FlowLayoutPanel>().First(p=>p.Dock!=DockStyle.Top);}
 static void Click(Control control,string name,MouseEventArgs args){typeof(Control).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(control,new object[]{args});Application.DoEvents();}
 [STAThread]static void Main(string[] args){
  Application.EnableVisualStyles();typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Public|BindingFlags.Static).SetValue(null,true);
  using(var form=new MainForm()){
   form.Show();form.Size=new Size(1600,900);Application.DoEvents();
   var editor=(Editor)Field(form,"editor");var timeline=(Timeline)Field(form,"timeline");var controls=(EffectControlsPanelView)Field(form,"effectControlsView");
   var asset=GeneratedMedia.CreateSolid("Prueba","224466",8);
   editor.Commit(p=>{p.assets.Add(asset);Media.Insert(p,asset.id,p.Active.tracks.First(t=>t.kind=="video").id,0);});Call(form,"RefreshProject");
   var clip=editor.Project.Active.clips.Single();timeline.Selected=clip.id;timeline.Selection.Add(clip.id);Call(form,"ShowClip");
   Call(form,"ApplyCatalogEffectToClip",EffectContracts.BasicColorId,clip.id);
   Call(form,"ApplyCatalogEffectToClip",EffectContracts.CropId,clip.id);
   var basic=editor.Project.Active.clips.Single().effects.First();var crop=editor.Project.Active.clips.Single().effects.Last();
   var card=Card(controls,basic.id);var second=Card(controls,crop.id);
   var head=card.Controls.OfType<FlowLayoutPanel>().First(p=>p.Dock==DockStyle.Top);
   Check(head.Controls.OfType<Button>().Count()==3,"la cabecera ya no tiene reinicio completo");
   head.Controls.OfType<Button>().First().PerformClick();Application.DoEvents();
   var rows=Body(card).Controls.OfType<ScrubNumber>().ToArray();int collapsed=card.Height;
   rows[0].Controls.OfType<Button>().First(b=>b.Text=="▾").PerformClick();Application.DoEvents();
   Check(card.Height>=collapsed+35,"el primer deslizador aumenta la altura de la tarjeta");
   rows[1].Controls.OfType<Button>().First(b=>b.Text=="▾").PerformClick();Application.DoEvents();
   Check(card.Bottom<=second.Top,"varios deslizadores no se superponen al efecto siguiente");
   Check(card.Height>=Body(card).Top+Body(card).Controls.Cast<Control>().Sum(r=>r.Height+r.Margin.Vertical),"la tarjeta contiene todos los controles abiertos");
   Call(form,"SetEffectNumber",false,clip.id,basic.id,"brightness",0.5);
   Call(form,"SetEffectNumber",false,clip.id,basic.id,"contrast",1.5);Call(form,"ShowClip");
   card=Card(controls,basic.id);rows=Body(card).Controls.OfType<ScrubNumber>().ToArray();
   rows[0].Controls.OfType<Button>().First(b=>b.Text=="↶").PerformClick();Application.DoEvents();
   var current=editor.Project.Active.clips.Single().effects.First();
   Check(Math.Abs(current.parameters["brightness"].number.Value)<0.001&&Math.Abs(current.parameters["contrast"].number.Value-1.5)<0.001,"reiniciar un parámetro conserva los demás");
   card=Card(controls,basic.id);card.ContextMenuStrip.Items.Cast<ToolStripItem>().First(i=>i.Text=="Restablecer efecto completo").PerformClick();Application.DoEvents();
   current=editor.Project.Active.clips.Single().effects.First();
   Check(Math.Abs(current.parameters["contrast"].number.Value-1)<0.001,"el menú contextual reinicia el efecto completo");
   editor.Undo();Call(form,"RefreshProject");
   Check(Math.Abs(editor.Project.Active.clips.Single().effects.First().parameters["contrast"].number.Value-1.5)<0.001,"el reinicio completo permite deshacer");
   Call(form,"ApplyCatalogEffectToClip",EffectContracts.ChromaKeyId,clip.id);
   var chroma=editor.Project.Active.clips.Single().effects.Last();card=Card(controls,chroma.id);
   card.Controls.OfType<FlowLayoutPanel>().First(p=>p.Dock==DockStyle.Top).Controls.OfType<Button>().First().PerformClick();Application.DoEvents();
   var picker=Body(card).Controls.OfType<ColorPickerWidget>().Single();picker.SetColor(Color.Red,true);Application.DoEvents();
   Check(editor.Project.Active.clips.Single().effects.Last().parameters["color"].text=="#FF0000","selector compartido cambia color de croma");
   card=Card(controls,chroma.id);picker=Body(card).Controls.OfType<ColorPickerWidget>().Single();picker.Controls.OfType<Button>().First(b=>b.Text=="↶").PerformClick();Application.DoEvents();
   Check(editor.Project.Active.clips.Single().effects.Last().parameters["color"].text=="#00FF00","el color se reinicia como parámetro individual");
   card=Card(controls,crop.id);card.Controls.OfType<FlowLayoutPanel>().First(p=>p.Dock==DockStyle.Top).Controls.OfType<Button>().First().PerformClick();
   foreach(var row in Body(card).Controls.OfType<ScrubNumber>())row.Controls.OfType<Button>().First(b=>b.Text=="▾").PerformClick();
   Application.DoEvents();
   var nextCard=Card(controls,chroma.id);
   Check(card.Bottom<=nextCard.Top,"cuatro controles de recorte abiertos no tapan el croma");
   var overlays=form.Controls.Cast<Control>().SelectMany(c=>Descendants(c)).OfType<PanelScrollbarOverlay>().ToArray();
   var effectScroll=overlays.First(o=>Field(o,"target")==controls);Call(effectScroll,"UpdateState");
   Check(effectScroll.Visible,"la barra gris aparece al superar el alto del panel de efectos");
   int oldScroll=-controls.AutoScrollPosition.Y;
   Click(effectScroll,"OnMouseDown",new MouseEventArgs(MouseButtons.Left,1,effectScroll.Width/2,effectScroll.Height-6,0));
   Click(effectScroll,"OnMouseUp",new MouseEventArgs(MouseButtons.Left,1,effectScroll.Width/2,effectScroll.Height-6,0));
   Check(-controls.AutoScrollPosition.Y>oldScroll,"la barra gris desplaza los efectos");
   editor.Commit(project=>{for(int index=0;index<28;index++)project.assets.Add(GeneratedMedia.CreateSolid("Medio "+index,"334455",2));});Call(form,"RefreshProject");
   var media=(MediaPanelView)Field(form,"mediaView");var mediaScroll=overlays.First(o=>Field(o,"target")==media.Library);Call(mediaScroll,"UpdateState");
   Check(mediaScroll.Visible,"la barra gris también aparece en Medios");
   int oldTop=media.Library.TopIndex;
   Click(mediaScroll,"OnMouseDown",new MouseEventArgs(MouseButtons.Left,1,mediaScroll.Width/2,mediaScroll.Height-6,0));
   Click(mediaScroll,"OnMouseUp",new MouseEventArgs(MouseButtons.Left,1,mediaScroll.Width/2,mediaScroll.Height-6,0));
   Check(media.Library.TopIndex>oldTop,"la barra gris desplaza la lista de medios");
   var timelineView=(TimelinePanelView)Field(form,"timelineView");var navigator=timelineView.HorizontalNavigator;
   Check(navigator.Visible&&navigator.Bottom==timelineView.ClientSize.Height,"el desplazamiento horizontal ocupa la parte inferior");
   double before=timeline.Scroll;Click(navigator,"OnMouseDown",new MouseEventArgs(MouseButtons.Left,1,navigator.Width-12,7,0));
   Click(navigator,"OnMouseUp",new MouseEventArgs(MouseButtons.Left,1,navigator.Width-12,7,0));
   Check(timeline.Scroll>before,"la barra horizontal mueve la línea de tiempo");
   Console.WriteLine("OVERLAYS="+overlays.Length+" VISIBLE="+overlays.Count(o=>o.Visible));
   foreach(var overlay in overlays)Console.WriteLine("OVERLAY "+overlay.Visible+" "+overlay.Bounds+" SCREEN="+(overlay.IsHandleCreated?overlay.PointToScreen(Point.Empty).ToString():"n/a")+" TARGET="+Field(overlay,"target").GetType().Name);
   if(args.Length>0)using(var bitmap=new Bitmap(form.Width,form.Height)){using(var graphics=Graphics.FromImage(bitmap))graphics.CopyFromScreen(form.PointToScreen(Point.Empty),Point.Empty,bitmap.Size);bitmap.Save(args[0],System.Drawing.Imaging.ImageFormat.Png);}
  }
 }
 static System.Collections.Generic.IEnumerable<Control> Descendants(Control root){yield return root;foreach(Control child in root.Controls)foreach(var item in Descendants(child))yield return item;}
}
