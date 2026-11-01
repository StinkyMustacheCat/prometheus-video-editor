using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class UiTests02516 {
 static int count;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);count++;}
 static T Field<T>(object target,string name){return (T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
 static System.Collections.Generic.IEnumerable<string> Leaves(PanelWorkspace.LayoutNode node){
  if(node==null)yield break;
  if(node.Id!=null){yield return node.Id;yield break;}
  foreach(var id in Leaves(node.First))yield return id;
  foreach(var id in Leaves(node.Second))yield return id;
 }
 [STAThread]static void Main(string[] args){
  Application.EnableVisualStyles();Tools.Root=Path.GetFullPath(args[0]);
  string layout=Path.Combine(Tools.Root,"layout-test-02516-"+Guid.NewGuid().ToString("N")+".json");
  var modes=new[]{Timeline.ToolMode.TrackForward,Timeline.ToolMode.TrackBackward,Timeline.ToolMode.TrackUp,Timeline.ToolMode.TrackDown,Timeline.ToolMode.TrackAll};
  try{
   using(var form=new MainForm()){
    var workspace=Field<PanelWorkspace>(form,"workspace");workspace.PersistenceEnabled=false;
    form.Show();Application.DoEvents();
    var timeline=Field<Timeline>(form,"timeline");var arrow=Field<Button>(form,"directionDropButton");
    var editor=Field<Editor>(form,"editor");string project=Store.Encode(editor.Project);
    foreach(var mode in modes){
     arrow.PerformClick();Application.DoEvents();
     var menu=Field<ContextMenuStrip>(form,"directionMenu");
     Check(menu!=null&&!menu.IsDisposed&&menu.Items.Count==5,"menú de dirección abre para "+mode);
     menu.Items[Array.IndexOf(modes,mode)].PerformClick();
     Check(timeline.ActiveTool==mode,"selección "+mode+" se activa desde el menú");
     if(!menu.IsDisposed)menu.Close();Application.DoEvents();
     Check(menu.IsDisposed,"menú de "+mode+" se libera después de procesar el clic");
    }
    var place=workspace.Entries.Where(x=>!new[]{"medios","monitor","propiedades","timeline"}.Contains(x.Id)).ToList();
    Check(place.Count==8&&place.All(x=>x.State=="closed"&&x.Content.Parent==null),"ocho paneles futuros se registran cerrados");
    Check(Leaves(workspace.Root).Count()==4,"diseño inicial conserva solo los cuatro paneles funcionales");
    var host=Field<Panel>(workspace,"contentHost");
    Check(host.Bounds.Left==8&&host.Bounds.Right==workspace.ClientSize.Width-8,"bordes reservan margen sin tapar controles");
    var view=Field<Control>(workspace,"view");
    Check(view.Parent==host&&view.Bounds.Left==0,"contenido parte del espacio interior independiente");
    workspace.FloatPanel("color");Application.DoEvents();
    var floating=workspace.Entries.First(x=>x.Id=="color").Floating;
    Check(workspace.Entries.First(x=>x.Id=="color").State=="floating","Color abre como panel flotante");
    var content=((FloatingPanelWindow)floating).ContentHost;
    var leaf=(Panel)content.Controls[0];
    var bar=leaf.Controls.OfType<Panel>().Single(x=>x.Dock==DockStyle.Top);
    Check(floating.FormBorderStyle==FormBorderStyle.None&&floating.Controls.Count==1&&bar.Controls.OfType<Panel>().SelectMany(x=>x.Controls.OfType<Button>()).Any(b=>b.Text=="×"),"ventana flotante usa una cabecera integrada con cierre");
    if(args.Length>1)using(var image=new Bitmap(floating.Width,floating.Height)){floating.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save(args[1]);}
    workspace.DockPanel("color","monitor","derecha");Application.DoEvents();
    Check(workspace.Entries.First(x=>x.Id=="color").State=="docked"&&Leaves(workspace.Root).Contains("color"),"Color se acopla sin alterar otros paneles");
    workspace.Minimize("color");Application.DoEvents();
    Check(workspace.Entries.First(x=>x.Id=="color").State=="minimized","Color se minimiza en la bandeja superior");
    workspace.DockAtEdge("subtitulos","izquierda");Application.DoEvents();
    Check(Leaves(workspace.Root).Contains("subtitulos"),"Subtítulos se inserta en el borde");
    workspace.LayoutPath=layout;workspace.PersistenceEnabled=true;workspace.SaveLayout();
    Check(File.Exists(layout)&&workspace.LastLayoutError==null,"diseño con panel futuro se guarda");
    Check(Store.Encode(editor.Project)==project,"operaciones de paneles no alteran el montaje");
   }
   using(var form=new MainForm()){
    var workspace=Field<PanelWorkspace>(form,"workspace");workspace.LayoutPath=layout;workspace.LoadLayout();
    Check(workspace.Entries.First(x=>x.Id=="subtitulos").State=="docked"&&workspace.Entries.First(x=>x.Id=="color").State=="minimized","paneles futuros recuperan su disposición");
    workspace.PersistenceEnabled=false;workspace.ApplyPreset("montaje");
    Check(Leaves(workspace.Root).Count()==4&&workspace.Entries.Where(x=>x.Id=="color"||x.Id=="subtitulos").All(x=>x.State=="closed"),"diseño predefinido cierra los paneles opcionales");
   }
   Console.WriteLine("PASS TOTAL "+count);
  }finally{if(File.Exists(layout))File.Delete(layout);}
 }
}
