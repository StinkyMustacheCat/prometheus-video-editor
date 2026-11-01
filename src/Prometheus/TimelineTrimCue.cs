using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Prometheus {
 public partial class Timeline {
  string hoverTrimId;
  int hoverTrimSide;
  void DrawTrimHover(Graphics graphics){
   if(Project==null||hoverTrimId==null||hoverTrimSide==0||resizingTransitionId!=null||dragging!=null)return;
   var clip=Project.Active.clips.Find(c=>c.id==hoverTrimId);
   if(clip==null||!TrackVisible(clip.track_id))return;
   var track=Project.Active.tracks.Find(t=>t.id==clip.track_id);
   if(track==null||track.locked)return;
   var bounds=ClipBounds(clip);
   var state=graphics.Save();graphics.SetClip(TrackViewport(track.kind),CombineMode.Intersect);
   DrawTrimCue(graphics,bounds,hoverTrimSide);
   graphics.Restore(state);
  }
  void DrawTrimCue(Graphics graphics,RectangleF bounds,int side){
   if(side!=1&&side!=2||bounds.Width<=0||bounds.Height<=0)return;
   float width=Math.Min(16,Math.Max(1,bounds.Width));
   float x=side==1?bounds.Left:bounds.Right;
   var fade=new RectangleF(side==1?bounds.Left:bounds.Right-width,bounds.Top,width,bounds.Height);
   Color strong=Color.FromArgb(112,255,215,170),clear=Color.FromArgb(0,255,215,170);
   using(var wash=new LinearGradientBrush(fade,side==1?strong:clear,side==1?clear:strong,LinearGradientMode.Horizontal))graphics.FillRectangle(wash,fade);
   using(var glow=new Pen(Color.FromArgb(105,255,244,225),4))graphics.DrawLine(glow,x,bounds.Top+2,x,bounds.Bottom-2);
   using(var edge=new Pen(Color.FromArgb(245,255,247,239),1.5f))graphics.DrawLine(edge,x,bounds.Top+2,x,bounds.Bottom-2);
  }
 }
}
