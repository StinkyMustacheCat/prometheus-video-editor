using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class UiTests02517 {
 static int count;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);count++;}
 static T Field<T>(object target,string name){return (T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
 static void Invoke(object target,string name){target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,new object[0]);}
 [STAThread]static void Main(string[] args){
  Application.EnableVisualStyles();Tools.Root=Path.GetFullPath(args[0]);
  string fixture=Path.Combine(Tools.Root,"demo","Demostracion.prometheus.json");
  using(var form=new MainForm()){
   var workspace=Field<PanelWorkspace>(form,"workspace");workspace.PersistenceEnabled=false;
   form.Show();Application.DoEvents();
   var host=Field<Panel>(workspace,"contentHost");var view=Field<Control>(workspace,"view");
   var targets=Field<Dictionary<string,Panel>>(workspace,"edgeTargets");
   Check(view.Parent==host&&host.Bounds.Left==8&&host.Bounds.Right==workspace.ClientSize.Width-8,"paneles parten de un espacio interior propio");
   Check(targets["izquierda"].Right<=host.Left&&targets["derecha"].Left>=host.Right,"zonas laterales no cubren los paneles");
   Check(targets["arriba"].Bottom<=host.Top&&targets["abajo"].Top>=host.Bottom,"zonas superior e inferior tampoco cubren los paneles");
   form.LoadFixture(fixture);Application.DoEvents();
   var media=Field<MediaPanelView>(form,"mediaView");var inspector=Field<InspectorPanelView>(form,"inspectorView");
   var editor=Field<Editor>(form,"editor");var timeline=Field<Timeline>(form,"timeline");
   Check(workspace.Entries.Single(x=>x.Id=="medios").Content==media&&workspace.Entries.Single(x=>x.Id=="propiedades").Content==inspector,"Medios y Propiedades son controles autónomos");
   Check(media.Library.Items.Count==editor.Project.assets.Count&&media.Library.AllowDrop,"biblioteca mantiene medios y arrastre de archivos");
   media.Library.SelectedIndex=0;Application.DoEvents();
   Check(media.SelectedAssetId==editor.Project.assets[0].id&&inspector.Controls.OfType<Label>().Any(x=>x.Text==editor.Project.assets[0].name),"seleccionar medio actualiza Propiedades");
   string assetId=media.SelectedAssetId;int before=editor.Project.Active.clips.Count;
   timeline.Cursor=8;Invoke(form,"Insert");Application.DoEvents();
   Check(editor.Project.Active.clips.Count>before&&editor.Project.Active.clips.Any(x=>x.asset_id==assetId&&x.timeline_start.Seconds==8),"medio seleccionado se inserta en la timeline");
   var audio=editor.Project.Active.clips.First(x=>editor.Project.Active.tracks.Find(t=>t.id==x.track_id).kind=="audio");
   timeline.Selected=audio.id;Invoke(form,"ShowClip");Application.DoEvents();
   var mute=inspector.Controls.OfType<CheckBox>().Single();bool oldEnabled=audio.enabled;
   mute.Checked=!mute.Checked;Application.DoEvents();
   Check(editor.Project.Active.clips.Find(x=>x.id==audio.id).enabled!=oldEnabled,"silenciar desde Propiedades edita el clip");
   var volume=inspector.Controls.OfType<NumericUpDown>().Single();volume.Value=-6;
   inspector.Controls.OfType<Button>().Single(x=>x.Text=="Aplicar volumen").PerformClick();Application.DoEvents();
   Check(Math.Abs(editor.Project.Active.clips.Find(x=>x.id==audio.id).gain_db+6)<0.001,"volumen se aplica desde el panel separado");
   editor.Undo();Check(Math.Abs(editor.Project.Active.clips.Find(x=>x.id==audio.id).gain_db)<0.001,"cambio del inspector conserva Deshacer");
   workspace.FloatPanel("propiedades");Application.DoEvents();
   Check(workspace.Entries.Single(x=>x.Id=="propiedades").Content==inspector&&!inspector.IsDisposed,"Propiedades conserva su instancia al flotar");
   workspace.ApplyPreset("montaje");Application.DoEvents();
   Check(workspace.Entries.Single(x=>x.Id=="propiedades").Content==inspector&&inspector.Parent!=null,"Propiedades vuelve al diseño sin recrearse");
  }
  Console.WriteLine("PASS TOTAL "+count);
 }
}
