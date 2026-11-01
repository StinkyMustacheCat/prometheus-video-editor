using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 // Draws a subdued interactive thumb over a native vertical scrollbar.
 // It leaves the underlying control and its wheel/keyboard behavior intact.
 public sealed class PanelScrollbarOverlay:Control {
  readonly Control target,host;
  readonly Timer refresh=new Timer{Interval=140};
  bool dragging,hover;
  int grabOffset;
  int maximum,current,visibleUnits,totalUnits;
  List<TreeNode> treeRows=new List<TreeNode>();
  public PanelScrollbarOverlay(Control hostControl,Control scrollTarget){
   host=hostControl;target=scrollTarget;Width=SystemInformation.VerticalScrollBarWidth;Visible=false;BackColor=Color.FromArgb(44,46,50);DoubleBuffered=true;Cursor=Cursors.Hand;
   refresh.Tick+=(s,e)=>UpdateState();refresh.Start();
  }
  static List<TreeNode> VisibleTreeRows(TreeView tree){
   var rows=new List<TreeNode>();if(tree.Nodes.Count==0)return rows;
   for(var node=tree.Nodes[0];node!=null;node=node.NextVisibleNode)rows.Add(node);
   return rows;
  }
  void UpdateState(){
   if(IsDisposed||host.IsDisposed||target.IsDisposed||!host.IsHandleCreated||!target.IsHandleCreated||!target.Visible){Visible=false;return;}
   int nextTotal=0,nextView=0,nextCurrent=0;
   var list=target as ListBox;var tree=target as TreeView;var scroll=target as ScrollableControl;
   if(list!=null){nextTotal=list.Items.Count;nextView=Math.Max(1,list.ClientSize.Height/Math.Max(1,list.ItemHeight));nextCurrent=list.TopIndex;}
   else if(tree!=null){treeRows=VisibleTreeRows(tree);nextTotal=treeRows.Count;nextView=Math.Max(1,tree.ClientSize.Height/Math.Max(1,tree.ItemHeight));nextCurrent=Math.Max(0,treeRows.IndexOf(tree.TopNode));}
   else if(scroll!=null){nextTotal=Math.Max(scroll.DisplayRectangle.Height,scroll.ClientSize.Height);nextView=Math.Max(1,scroll.ClientSize.Height);nextCurrent=Math.Max(0,-scroll.AutoScrollPosition.Y);}
   int nextMaximum=Math.Max(0,nextTotal-nextView);
   if(nextMaximum==0){Visible=false;return;}
   Point origin=host.PointToClient(target.PointToScreen(Point.Empty));
   var bounds=new Rectangle(origin.X+target.Width-Width,origin.Y,Width,target.Height);
   bool changed=Bounds!=bounds||maximum!=nextMaximum||current!=nextCurrent||visibleUnits!=nextView||totalUnits!=nextTotal;
   if(Bounds!=bounds)Bounds=bounds;
   maximum=nextMaximum;current=Math.Max(0,Math.Min(maximum,nextCurrent));visibleUnits=nextView;totalUnits=nextTotal;
   if(!Visible){Visible=true;BringToFront();changed=true;}
   if(changed)Invalidate();
  }
  Rectangle Thumb(){
   int track=Math.Max(1,Height-4),length=Math.Max(28,Math.Min(track,(int)Math.Round(track*visibleUnits/(double)Math.Max(1,totalUnits))));
   int travel=Math.Max(0,track-length),top=2+(maximum==0?0:(int)Math.Round(travel*current/(double)maximum));
   return new Rectangle(2,top,Math.Max(2,Width-4),length);
  }
  protected override void OnPaint(PaintEventArgs e){
   base.OnPaint(e);e.Graphics.Clear(BackColor);
   using(var track=new SolidBrush(Color.FromArgb(54,56,61)))e.Graphics.FillRectangle(track,2,2,Math.Max(2,Width-4),Math.Max(2,Height-4));
   using(var thumb=new SolidBrush(dragging?Color.FromArgb(154,159,166):hover?Color.FromArgb(129,134,141):Color.FromArgb(105,110,117)))e.Graphics.FillRectangle(thumb,Thumb());
  }
  void SetOffset(int coordinate){
   int track=Math.Max(1,Height-4),length=Thumb().Height,travel=Math.Max(1,track-length);
   int value=(int)Math.Round(Math.Max(0,Math.Min(1,(coordinate-grabOffset-2)/(double)travel))*maximum);
   var list=target as ListBox;var tree=target as TreeView;var scroll=target as ScrollableControl;
   if(list!=null)list.TopIndex=Math.Min(Math.Max(0,list.Items.Count-1),value);
   else if(tree!=null&&treeRows.Count>0)tree.TopNode=treeRows[Math.Min(treeRows.Count-1,value)];
   else if(scroll!=null)scroll.AutoScrollPosition=new Point(-scroll.AutoScrollPosition.X,value);
   current=value;Invalidate();
  }
  protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;var thumb=Thumb();grabOffset=thumb.Contains(e.Location)?e.Y-thumb.Top:thumb.Height/2;dragging=true;Capture=true;SetOffset(e.Y);}
  protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging){SetOffset(e.Y);return;}bool next=Thumb().Contains(e.Location);if(next!=hover){hover=next;Invalidate();}}
  protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);if(!dragging){hover=false;Invalidate();}}
  protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button==MouseButtons.Left&&dragging){dragging=false;Capture=false;UpdateState();}}
  protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);grabOffset=Thumb().Height/2;SetOffset(Thumb().Top+(e.Delta>0?-1:1)*Math.Max(10,Thumb().Height/3));}
  protected override void Dispose(bool disposing){if(disposing){refresh.Stop();refresh.Dispose();}base.Dispose(disposing);}
  public static void AddFor(Control host,Control content){
   foreach(var target in ScrollTargets(content))host.Controls.Add(new PanelScrollbarOverlay(host,target));
  }
  static IEnumerable<Control> ScrollTargets(Control control){
   if(control is ListBox||control is TreeView||control is FlowLayoutPanel&&((FlowLayoutPanel)control).AutoScroll){yield return control;yield break;}
   foreach(Control child in control.Controls)foreach(var target in ScrollTargets(child))yield return target;
  }
 }
}
