using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Prometheus {
 // A small dark window keeps floating panels visually consistent with the workspace.
 public sealed class FloatingPanelWindow:Form {
  const int WmNcHitTest=0x84;
  const int HtClient=1,HtLeft=10,HtRight=11,HtTop=12,HtTopLeft=13,HtTopRight=14,HtBottom=15,HtBottomLeft=16,HtBottomRight=17;
  public Panel ContentHost{get;private set;}
  public FloatingPanelWindow(string title){
   Text="Prometheus · "+title;FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;
   Size=new Size(530,360);MinimumSize=new Size(300,200);ShowInTaskbar=false;
   BackColor=Color.FromArgb(83,86,92);Padding=new Padding(1);
   ContentHost=new Panel{Dock=DockStyle.Fill,BackColor=Color.FromArgb(27,28,31)};
   Controls.Add(ContentHost);
  }
  protected override void WndProc(ref Message message){
   base.WndProc(ref message);
   if(message.Msg!=WmNcHitTest||message.Result!=(IntPtr)HtClient)return;
   Point point=PointToClient(Cursor.Position);const int grip=7;
   bool left=point.X<grip,right=point.X>=Width-grip,top=point.Y<grip,bottom=point.Y>=Height-grip;
   if(left&&top)message.Result=(IntPtr)HtTopLeft;
   else if(right&&top)message.Result=(IntPtr)HtTopRight;
   else if(left&&bottom)message.Result=(IntPtr)HtBottomLeft;
   else if(right&&bottom)message.Result=(IntPtr)HtBottomRight;
   else if(left)message.Result=(IntPtr)HtLeft;
   else if(right)message.Result=(IntPtr)HtRight;
   else if(top)message.Result=(IntPtr)HtTop;
   else if(bottom)message.Result=(IntPtr)HtBottom;
  }
 }
}
