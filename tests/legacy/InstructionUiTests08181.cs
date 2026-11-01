using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Prometheus;

class InstructionUiTests08181 {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static Control[] Descendants(Control root){return new[]{root}.Concat(root.Controls.Cast<Control>().SelectMany(Descendants)).ToArray();}
 [STAThread] static void Main(){
  var project=Store.New();
  using(var host=new Form{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-3000,-3000),Size=new Size(700,620)}){
   var panel=new InstructionPanelView{Dock=DockStyle.Fill};host.Controls.Add(panel);host.Show();Application.DoEvents();
   panel.SetContext(project,new string[0],null);
   Check(Descendants(panel).OfType<Button>().Any(b=>b.Text=="Añadir acción ▾"),"Catálogo de acciones visible");
   Check(Descendants(panel).OfType<Splitter>().Any(),"Detalle de revisión redimensionable");
   var choices=new[]{"io","selection","project"};var copy=Descendants(panel).OfType<Button>().First(b=>b.Text=="Copiar estado ▾");copy.PerformClick();Application.DoEvents();
   Check(Instructions.AvailableActions().Contains("subtitle.create")&&Instructions.AvailableActions().Contains("mgt.set_parameter"),"Catálogo unificado incluye subtítulos y MGT");
   using(var schema=new InstructionSchemaDialog08181(project,new InstructionAction{op="marker.create",sequence_id=project.active_sequence_id})){Check(Descendants(schema).OfType<TextBox>().Any(),"Formulario de marcador muestra sus campos");}
   host.Close();
  }
  var asset=GeneratedMedia.CreateSolid("Fondo","223344",5);project.assets.Add(asset);var track=project.Active.tracks.First(t=>t.kind=="video");var clip=new Clip{id=Store.Id(),name=asset.name,asset_id=asset.id,stream_id=asset.streams[0].id,track_id=track.id,timeline_start=Rational.Frame(0),duration=Rational.Frame(5),source_in=Rational.Time(0)};project.Active.clips.Add(clip);
  using(var controls=new EffectControlsPanelView()){controls.ShowClip(project,clip);var lockButton=Descendants(controls).OfType<Button>().First(b=>b.AccessibleName=="Desenlazar escalas");int count=Descendants(controls).Length;
   controls.ScaleLinkChanged=(id,linked)=>{ClipFraming.SetScaleLinked(clip,linked);controls.UpdateScaleLinkVisual(id,linked,ClipFraming.Read(clip).scaleY);};lockButton.PerformClick();
   Check(!ClipFraming.Read(clip).scaleLinked&&lockButton.AccessibleName=="Enlazar escalas"&&Descendants(controls).Length==count,"Candado actualiza su estado sin reconstruir controles");
  }
  Console.WriteLine("InstructionUiTests08181 OK");
 }
}
