using System;
using System.Drawing;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  Button directionMainButton,directionDropButton;
  ContextMenuStrip directionMenu;
  Timeline.ToolMode directionMode=Timeline.ToolMode.TrackForward;
  static readonly Timeline.ToolMode[] directionModes={Timeline.ToolMode.TrackForward,Timeline.ToolMode.TrackBackward,Timeline.ToolMode.TrackUp,Timeline.ToolMode.TrackDown,Timeline.ToolMode.TrackAll};
  static string DirectionIcon(Timeline.ToolMode mode){switch(mode){case Timeline.ToolMode.TrackForward:return "⇥";case Timeline.ToolMode.TrackBackward:return "⇤";case Timeline.ToolMode.TrackUp:return "↑";case Timeline.ToolMode.TrackDown:return "↓";default:return "▦";}}
  void AddDirectionToolGroup(){
   directionMainButton=timelineView.DirectionButton;directionDropButton=timelineView.DirectionDropButton;
   RegisterShortcutButton(directionMainButton,ToolCommand(directionMode));UpdateDirectionButton();
  }
  void ShowDirectionMenu(){
   if(directionMenu!=null&&!directionMenu.IsDisposed)directionMenu.Close();
   var menu=new ContextMenuStrip();
   directionMenu=menu;
   foreach(var mode in directionModes){var selected=mode;string id=ToolCommand(mode);
    var entry=new ToolStripMenuItem(DirectionIcon(mode)+"  "+shortcutMap.Command(id).Name+"   "+ShortcutMap.Label(shortcutMap.Get(id))){Checked=directionMode==mode};
    entry.Click+=(s,e)=>SetTool(selected);menu.Items.Add(entry);
   }
   menu.Closed+=(s,e)=>{if(Object.ReferenceEquals(directionMenu,menu))directionMenu=null;DisposePopupLater(menu);};
   menu.Show(directionDropButton,new Point(0,directionDropButton.Height));
  }
  void UpdateDirectionButton(){
   if(directionMainButton==null)return;
   if(Timeline.IsDirection(timeline.ActiveTool))directionMode=timeline.ActiveTool;
   directionMainButton.Text=DirectionIcon(directionMode);
   shortcutButtons[directionMainButton]=ToolCommand(directionMode);RefreshShortcutTooltips();
   bool active=Timeline.IsDirection(timeline.ActiveTool);
   directionMainButton.BackColor=active?Color.FromArgb(255,156,44):Color.FromArgb(49,51,56);
   directionMainButton.ForeColor=active?Color.FromArgb(25,27,30):text;
   directionMainButton.FlatAppearance.BorderColor=active?Color.FromArgb(255,208,123):Color.FromArgb(49,51,56);
   directionMainButton.FlatAppearance.BorderSize=active?1:0;
   directionDropButton.BackColor=active?Color.FromArgb(94,68,39):Color.FromArgb(49,51,56);
   directionDropButton.ForeColor=text;
  }
 }
}
