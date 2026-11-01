using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class DirectionTimeline02513:Timeline {public void Down(int x,int y){OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,x,y,0));}}
class UiTests02513 {
 static int count;
 static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);count++;}
 static T Field<T>(object obj,string name){return (T)obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj);}
 [STAThread]static void Main(string[] args){
  Application.EnableVisualStyles();Tools.Root=Path.GetFullPath(args[0]);
  string file=Path.Combine(Tools.Root,"shortcut-test-"+Guid.NewGuid().ToString("N")+".json");
  try{
   var map=new ShortcutMap(file);
   Check(map.Get("edit.divide")== (Keys.Control|Keys.K),"Dividir usa Ctrl+K");
   Check(map.Get("tool.razor")==Keys.C&&map.Get("tool.select")==Keys.V&&map.Get("tool.zoom")==Keys.Z,"C, V y Z activan herramientas");
   Check(map.Get("tool.forward")==Keys.A&&map.Get("tool.backward")== (Keys.Shift|Keys.A),"A y Mayús+A seleccionan por dirección");
   Check(map.Conflict("edit.divide",Keys.C)=="tool.razor","detecta conflictos antes de reasignar");
   map.Assign("edit.divide",Keys.S,true);Check(map.CommandAt(Keys.S)=="edit.divide"&&map.CommandAt(Keys.Control|Keys.K)==null,"Dividir se reasigna a S");
   var loaded=new ShortcutMap(file);Check(loaded.Get("edit.divide")==Keys.S,"el atajo persiste al reabrir");
   loaded.Assign("tool.razor",Keys.S,true);Check(loaded.Get("edit.divide")==Keys.None&&loaded.Get("tool.razor")==Keys.S,"resolver conflicto libera la acción anterior");
   loaded.Reset("edit.divide");Check(loaded.Get("edit.divide")== (Keys.Control|Keys.K),"restaurar predeterminado");
   loaded.Assign("tool.razor",Keys.None,true);Check(loaded.Get("tool.razor")==Keys.None,"quitar atajo");
   var project=Store.New();var video=project.Active.tracks.First(t=>t.kind=="video");var audio=project.Active.tracks.First(t=>t.kind=="audio");
   var v1=new Clip{id=Store.Id(),track_id=video.id,timeline_start=Rational.Time(1),duration=Rational.Time(2)};
   var v2=new Clip{id=Store.Id(),track_id=video.id,timeline_start=Rational.Time(4),duration=Rational.Time(2)};
   var a1=new Clip{id=Store.Id(),track_id=audio.id,timeline_start=Rational.Time(1),duration=Rational.Time(2)};
   var a2=new Clip{id=Store.Id(),track_id=audio.id,timeline_start=Rational.Time(7),duration=Rational.Time(2)};
   project.Active.clips.AddRange(new[]{v1,v2,a1,a2});project.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=new System.Collections.Generic.List<string>{v1.id,a1.id}});
   using(var timeline=new Timeline{Project=project}){
    timeline.SelectDirection(video.id,3.5,true,false,false);Check(timeline.Selection.SequenceEqual(new[]{v2.id}),"adelante desde hueco solo selecciona clips posteriores");
    timeline.SelectDirection(video.id,2,true,false,false);Check(timeline.Selection.Contains(v1.id)&&timeline.Selection.Contains(v2.id)&&timeline.Selection.Contains(a1.id),"adelante incluye clip atravesado y vínculo");
    timeline.SelectDirection(video.id,2,true,false,true);Check(!timeline.Selection.Contains(a1.id),"Alt ignora vínculo");
    timeline.SelectDirection(video.id,3.5,false,false,false);Check(timeline.Selection.Contains(v1.id)&&timeline.Selection.Contains(a1.id)&&!timeline.Selection.Contains(v2.id),"atrás desde hueco");
    timeline.SelectDirection(video.id,3.5,true,true,false);Check(timeline.Selection.Contains(v2.id)&&timeline.Selection.Contains(a2.id),"Mayús incluye todas las pistas");
   }
   using(var timeline=new DirectionTimeline02513{Width=850,Height=300,Project=project,Zoom=55}){
    int y=timeline.TrackTop(video.id)+25,x=(int)timeline.X(3.5);
    timeline.ActiveTool=Timeline.ToolMode.TrackForward;timeline.Down(x,y);Check(timeline.Selection.SequenceEqual(new[]{v2.id}),"clic en hueco activa selección hacia adelante");
    timeline.ActiveTool=Timeline.ToolMode.TrackBackward;timeline.Down(x,y);Check(timeline.Selection.Contains(v1.id)&&!timeline.Selection.Contains(v2.id),"clic en hueco activa selección hacia atrás");
   }
   using(var form=new MainForm()){
    var workspace=Field<PanelWorkspace>(form,"workspace");workspace.PersistenceEnabled=false;
    var buttons=Field<System.Collections.Generic.Dictionary<Button,string>>(form,"shortcutButtons");
    Check(buttons.Values.Contains("edit.divide")&&buttons.Values.Contains("tool.forward"),"menú rápido está disponible en Dividir y la herramienta direccional");
    form.GetType().GetMethod("SetTool",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{Timeline.ToolMode.TrackBackward});
    Check(buttons.Values.Contains("tool.backward"),"el atajo rápido sigue la variante elegida en el botón agrupado");
    Check(buttons.All(x=>x.Key.ContextMenuStrip!=null),"cada herramienta registrada permite asignar atajo con clic derecho");
    Check(buttons.Single(x=>x.Value=="edit.divide").Key.AccessibleName.Contains("Ctrl+K"),"el tooltip muestra Ctrl+K");
    Check(form.Controls.Cast<Control>().SelectMany(x=>x.Controls.Cast<Control>()).SelectMany(x=>x.Controls.Cast<Control>()).Any(x=>x.Text=="Ajustes"),"Ajustes aparece después de Guardar");
    var run=form.GetType().GetMethod("ExecuteShortcut",BindingFlags.Instance|BindingFlags.NonPublic);
    var mainTimeline=Field<Timeline>(form,"timeline");
    Check((bool)run.Invoke(form,new object[]{Keys.C})&&mainTimeline.ActiveTool==Timeline.ToolMode.Razor,"C activa Tijeras y no ejecuta Dividir");
    Check((bool)run.Invoke(form,new object[]{Keys.V})&&mainTimeline.ActiveTool==Timeline.ToolMode.Select,"V vuelve a Seleccionar");
    Check((bool)run.Invoke(form,new object[]{Keys.Control|Keys.K})&&mainTimeline.ActiveTool==Timeline.ToolMode.Select,"Ctrl+K ejecuta Dividir sin cambiar de herramienta");
   }
   Console.WriteLine("PASS TOTAL "+count);
  }finally{if(File.Exists(file))File.Delete(file);if(File.Exists(file+".bak"))File.Delete(file+".bak");}
 }
}
