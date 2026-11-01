using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class UiTests02511 {
 static int count;
 static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);count++;}
 static T Field<T>(object obj,string name){return (T)obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj);}
 static IEnumerable<string> Leaves(PanelWorkspace.LayoutNode node){if(node==null)yield break;if(node.Id!=null){yield return node.Id;yield break;}foreach(var id in Leaves(node.First))yield return id;foreach(var id in Leaves(node.Second))yield return id;}
 [STAThread]static void Main(string[] args){
  Application.EnableVisualStyles();Tools.Root=System.IO.Path.GetFullPath(args[0]);
  string path=System.IO.Path.Combine(Tools.Root,"layout-test-"+Guid.NewGuid().ToString("N")+".json");
  using(var form=new MainForm()){
   var workspace=Field<PanelWorkspace>(form,"workspace");workspace.PersistenceEnabled=false;form.Show();Application.DoEvents();
   string projectBefore=Store.Encode(Field<Editor>(form,"editor").Project);
   Check(workspace.PanelShelf.Controls.Cast<Control>().Any(x=>x.Text.Contains("Paneles"))&&workspace.PanelShelf.Controls.Cast<Control>().Any(x=>x.Text.Contains("Diseños")),"Paneles y Diseños son menús separados");
   foreach(var preset in new[]{"equilibrado","montaje","broll","revision"}){
    workspace.ApplyPreset(preset);
    Check(workspace.CurrentPreset==preset,"diseño "+preset+" queda activo");
    Check(Leaves(workspace.Root).OrderBy(x=>x).SequenceEqual(new[]{"medios","monitor","propiedades","timeline"}.OrderBy(x=>x)),"diseño "+preset+" conserva los cuatro paneles");
    Check(workspace.Entries.Where(x=>new[]{"medios","monitor","propiedades","timeline"}.Contains(x.Id)).All(x=>x.State=="docked"&&x.Content.Parent!=null),"diseño "+preset+" aloja controles existentes");
   }
   workspace.ApplyPreset("equilibrado");
   var separator=workspace.Root;
   workspace.DockBetween("propiedades",separator);
   Check(workspace.CurrentPreset=="personalizado","acoplamiento intermedio convierte el diseño en personalizado");
   Check(separator.Second!=null&&separator.Second.First!=null&&separator.Second.First.Id=="propiedades","panel se inserta entre los dos lados del separador");
   Check(Leaves(workspace.Root).Distinct().Count()==4,"acoplamiento intermedio no duplica ni pierde paneles");
   Check(Store.Encode(Field<Editor>(form,"editor").Project)==projectBefore,"cambiar diseño no altera el proyecto de vídeo");
   workspace.FloatPanel("medios");workspace.ApplyPreset("montaje");
   Check(workspace.Entries.Where(x=>new[]{"medios","monitor","propiedades","timeline"}.Contains(x.Id)).All(x=>x.State=="docked"&&x.Floating==null)&&Leaves(workspace.Root).Distinct().Count()==4,"un diseño recupera paneles flotantes");
   workspace.DockBetween("propiedades",workspace.Root);
   var anchor=new Button{Left=form.ClientSize.Width-32,Top=form.ClientSize.Height-32,Width=28,Height=26};form.Controls.Add(anchor);
   using(var menu=new ContextMenuStrip()){
    menu.Items.Add("Opción extensa de panel");menu.Items.Add("Otra opción");
    typeof(PanelWorkspace).GetMethod("ShowMenuInside",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(workspace,new object[]{menu,anchor});
    Application.DoEvents();var inside=form.RectangleToScreen(form.ClientRectangle);
    Check(menu.Bounds.Left>=inside.Left&&menu.Bounds.Right<=inside.Right&&menu.Bounds.Top>=inside.Top&&menu.Bounds.Bottom<=inside.Bottom,"menú contextual permanece dentro de la ventana");menu.Close();
   }
   workspace.LayoutPath=path;workspace.PersistenceEnabled=true;workspace.SaveLayout();
   Check(System.IO.File.Exists(path),"diseño personalizado se guarda");
  }
  using(var next=new MainForm()){
   var workspace=Field<PanelWorkspace>(next,"workspace");workspace.LayoutPath=path;workspace.LoadLayout();
   Check(workspace.CurrentPreset=="personalizado"&&Leaves(workspace.Root).Distinct().Count()==4,"diseño personalizado se restaura");
  }
  System.IO.File.Delete(path);Console.WriteLine("PASS TOTAL "+count);
 }
}
