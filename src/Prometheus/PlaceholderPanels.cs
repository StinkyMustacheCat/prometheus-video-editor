using System;
using System.Drawing;
using System.Windows.Forms;

namespace Prometheus {
 // Future editing areas can be moved through the workspace without loading their tools yet.
 public static class PlaceholderPanels {
  static readonly string[][] panels={
   new[]{"mezclador","MEZCLADOR DE AUDIO","♫"},
   new[]{"navegador","NAVEGADOR","◎"}
  };
  public static void RegisterAll(PanelWorkspace workspace){
   foreach(var spec in panels)workspace.Register(spec[0],spec[1],spec[2],Create(spec[1]));
  }
  static Control Create(string title){
   var panel=new Panel{Dock=DockStyle.Fill,BackColor=Color.FromArgb(36,38,42)};
   var message=new Label{Dock=DockStyle.Top,Height=80,Padding=new Padding(16,18,12,0),
    ForeColor=Color.FromArgb(185,189,194),Font=new Font("Segoe UI",10),
    Text=title+"\nPanel reservado; sus funciones aún no están implementadas."};
   panel.Controls.Add(message);
   return panel;
  }
 }
}
