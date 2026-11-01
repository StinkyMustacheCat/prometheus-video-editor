using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 // Dark horizontal navigator. The timeline remains the single source of scroll state.
 public sealed class TimelineOverviewScrollBar:Control {
  readonly Timeline timeline;
  readonly ToolTip hint=new ToolTip();
  bool dragging,hover;
  double dragEnd,dragSpan;
  int dragOffset;
  public TimelineOverviewScrollBar(Timeline target){
   timeline=target;Height=16;Dock=DockStyle.Bottom;DoubleBuffered=true;BackColor=Color.FromArgb(32,34,38);Cursor=Cursors.Hand;
   hint.SetToolTip(this,"Desplazar horizontalmente la línea de tiempo");
   timeline.Invalidated+=(s,e)=>{if(!IsDisposed)Invalidate();};
  }
  double VisibleSpan {get{return Math.Max(1.0/60,Math.Max(1,timeline.Width-timeline.HeaderWidth)/Math.Max(0.000001,timeline.Zoom));}}
  double ContentEnd(double span){
   double end=timeline.Cursor;
   if(timeline.Project!=null){
    end=Math.Max(end,timeline.Project.Active.Length);
    end=Math.Max(end,Markers.Read(timeline.Project.Active).Select(marker=>marker.Seconds).DefaultIfEmpty(0).Max());
   }
   return Math.Max(60,end)+Math.Max(60,span*0.5);
  }
  Rectangle BarTrack {get{return new Rectangle(Math.Max(0,Math.Min(Width-2,timeline.HeaderWidth)),2,Math.Max(1,Width-timeline.HeaderWidth-5),12);}}
  Rectangle Thumb(double end,double span){
   var track=BarTrack;
   double total=Math.Max(span,end),maxScroll=Math.Max(0,total-span);
   int width=Math.Max(34,Math.Min(track.Width,(int)Math.Round(track.Width*span/total)));
   int travel=Math.Max(0,track.Width-width);
   int x=track.Left+(maxScroll==0?0:(int)Math.Round(travel*Math.Max(0,Math.Min(maxScroll,timeline.Scroll))/maxScroll));
   return new Rectangle(x,track.Top+1,width,track.Height-2);
  }
  protected override void OnPaint(PaintEventArgs e){
   base.OnPaint(e);var g=e.Graphics;g.Clear(BackColor);
   var track=BarTrack;using(var brush=new SolidBrush(Color.FromArgb(48,50,55)))g.FillRectangle(brush,track);
   double span=dragging?dragSpan:VisibleSpan,end=dragging?dragEnd:ContentEnd(span);
   var thumb=Thumb(end,span);using(var brush=new SolidBrush(dragging?Color.FromArgb(152,157,164):hover?Color.FromArgb(127,132,139):Color.FromArgb(104,109,116)))g.FillRectangle(brush,thumb);
   using(var pen=new Pen(Color.FromArgb(60,63,69)))g.DrawLine(pen,track.Left,0,track.Left,Height);
  }
  protected override void OnMouseDown(MouseEventArgs e){
   base.OnMouseDown(e);if(e.Button!=MouseButtons.Left||e.X<BarTrack.Left)return;
   dragSpan=VisibleSpan;dragEnd=ContentEnd(dragSpan);
   var thumb=Thumb(dragEnd,dragSpan);dragOffset=thumb.Contains(e.Location)?e.X-thumb.Left:thumb.Width/2;
   dragging=true;Capture=true;MoveTo(e.X);
  }
  void MoveTo(int x){
   var track=BarTrack;int thumbWidth=Thumb(dragEnd,dragSpan).Width,travel=Math.Max(1,track.Width-thumbWidth);
   double maximum=Math.Max(0,dragEnd-dragSpan);
   timeline.Scroll=Math.Max(0,Math.Min(maximum,(x-dragOffset-track.Left)/(double)travel*maximum));
   timeline.Invalidate();Invalidate();
  }
  protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging){MoveTo(e.X);return;}bool next=Thumb(ContentEnd(VisibleSpan),VisibleSpan).Contains(e.Location);if(next!=hover){hover=next;Invalidate();}}
  protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button==MouseButtons.Left&&dragging){dragging=false;Capture=false;Invalidate();}}
  protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);timeline.Scroll=Math.Max(0,timeline.Scroll+(e.Delta>0?-1:1)*Math.Max(1,200/Math.Max(0.000001,timeline.Zoom)));timeline.Invalidate();}
  protected override void Dispose(bool disposing){if(disposing)hint.Dispose();base.Dispose(disposing);}
 }
}
