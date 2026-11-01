using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class UiTests02512 {
 static int count;
 static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);count++;}
 static T Field<T>(object obj,string name){return (T)obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj);}
 static IEnumerable<PanelWorkspace.LayoutNode> Splits(PanelWorkspace.LayoutNode n){if(n==null||n.Id!=null)yield break;yield return n;foreach(var s in Splits(n.First))yield return s;foreach(var s in Splits(n.Second))yield return s;}
 static IEnumerable<string> Leaves(PanelWorkspace.LayoutNode n){if(n==null)yield break;if(n.Id!=null){yield return n.Id;yield break;}foreach(var id in Leaves(n.First))yield return id;foreach(var id in Leaves(n.Second))yield return id;}
 static IEnumerable<Control> Descendants(Control control){yield return control;foreach(Control child in control.Controls)foreach(var descendant in Descendants(child))yield return descendant;}
 [STAThread]static void Main(string[] args){
  Application.EnableVisualStyles();Tools.Root=System.IO.Path.GetFullPath(args[0]);
  var project=Store.New();var track=project.Active.tracks.First();
  var early=new Clip{id=Store.Id(),track_id=track.id,timeline_start=Rational.Time(1),duration=Rational.Time(2),source_in=Rational.Time(0)};
  var late=new Clip{id=Store.Id(),track_id=track.id,timeline_start=Rational.Time(6),duration=Rational.Time(2),source_in=Rational.Time(0)};
  project.Active.clips.Add(early);project.Active.clips.Add(late);
  using(var timeline=new Timeline{Project=project,Zoom=55,Snap=true}){
   Check(Math.Abs(timeline.AdjustMove(3.95,early.id)-4)<0.000001,"imán encaja el borde final al mover hacia adelante");
   Check(Math.Abs(timeline.AdjustMove(3.05,late.id)-3)<0.000001,"imán encaja el borde inicial al mover hacia atrás");
   float before=timeline.RollHandleRadius(track);early.duration=Rational.Time(0.25);
   Check(Math.Abs(timeline.RollHandleRadius(track)-before)<0.000001,"tirador mantiene tamaño al acortar un clip");
   timeline.SetTrackHeight(track.id,90);
   Check(timeline.RollHandleRadius(track)>before,"tirador crece con la altura de la pista");
  }
  using(var form=new MainForm()){
   var workspace=Field<PanelWorkspace>(form,"workspace");workspace.PersistenceEnabled=false;form.Show();Application.DoEvents();
   var rng=new Random(2512);var ids=new[]{"medios","monitor","propiedades","timeline"};var presets=new[]{"equilibrado","montaje","broll","revision"};var edges=new[]{"izquierda","derecha","arriba","abajo"};
   for(int i=0;i<180;i++){
    string id=ids[rng.Next(ids.Length)];int action=rng.Next(7);
    try{
     if(action==0)workspace.ApplyPreset(presets[rng.Next(presets.Length)]);
     else if(action==1)workspace.FloatPanel(id);
     else if(action==2)workspace.Minimize(id);
     else if(action==3)workspace.ClosePanel(id);
     else if(action==4){var targets=Leaves(workspace.Root).Where(x=>x!=id).ToList();if(targets.Count>0)workspace.DockPanel(id,targets[rng.Next(targets.Count)],edges[rng.Next(edges.Length)]);}
     else if(action==5){var targets=Leaves(workspace.Root).Where(x=>x!=id).ToList();if(targets.Count>0)workspace.ReplacePanel(targets[rng.Next(targets.Count)],id);}
     else {var splits=Splits(workspace.Root).ToList();if(splits.Count>0)workspace.DockBetween(id,splits[rng.Next(splits.Count)]);}
     Application.DoEvents();
     if(workspace.Entries.SelectMany(x=>Descendants(x.Content)).Any(x=>x.IsDisposed))throw new Exception("Contenido de panel descartado");
     var leaves=Leaves(workspace.Root).ToList();if(leaves.Distinct().Count()!=leaves.Count)throw new Exception("Panel duplicado en árbol");
    }catch(Exception ex){throw new Exception("Fallo tras operación "+i+", acción "+action+", panel "+id+": "+ex,ex);}
   }
   foreach(string preset in presets){workspace.ApplyPreset(preset);Check(workspace.Entries.Where(x=>ids.Contains(x.Id)).All(x=>!x.Content.IsDisposed&&x.Content.Parent!=null),"diseño "+preset+" recupera paneles tras disposiciones irregulares");}
   var designButton=(Button)workspace.PanelShelf.Controls.Cast<Control>().First(x=>x.Text.Contains("Diseños"));
   workspace.DockBetween("propiedades",workspace.Root);workspace.DockAtEdge("medios","derecha");
   foreach(string preset in presets){
    designButton.PerformClick();var menu=Field<ContextMenuStrip>(workspace,"designMenu");
    Check(menu!=null&&menu.Items.Cast<ToolStripMenuItem>().All(x=>x.Image!=null),"diseños muestran esquemas de paneles");
    if(args.Length>1&&preset=="equilibrado"){Application.DoEvents();using(var bitmap=new Bitmap(Math.Max(1,menu.Width),Math.Max(1,menu.Height))){menu.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(args[1]);}}
    menu.Items[presets.ToList().IndexOf(preset)].PerformClick();menu.Close();Application.DoEvents();
    Check(!designButton.IsDisposed&&workspace.Entries.All(x=>!x.Content.IsDisposed),"cambiar a "+preset+" desde el menú conserva los controles");
   }
   foreach(string edge in edges){workspace.DockAtEdge("medios",edge);Check(edge=="izquierda"||edge=="arriba"?workspace.Root.First.Id=="medios":workspace.Root.Second.Id=="medios","panel acopla al borde "+edge);}
  }
  Console.WriteLine("PASS TOTAL "+count);
 }
}
