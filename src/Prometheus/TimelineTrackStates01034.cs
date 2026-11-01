using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace Prometheus {
 public partial class Timeline {
  void DrawTrackStates01034(Graphics graphics,IEnumerable<Track> tracks){
   foreach(var track in tracks){
    if(track.enabled&&!track.locked)continue;
    var viewport=TrackViewport(track.kind);
    int top=TrackTop(track.id),height=TrackHeight(track.id);
    if(top+height<=viewport.Top||top>=viewport.Bottom)continue;
    var state=graphics.Save();graphics.SetClip(viewport,CombineMode.Intersect);
    var area=new Rectangle(headerWidth,top,System.Math.Max(1,Width-headerWidth),height);
    if(!track.enabled)using(var dim=new SolidBrush(Color.FromArgb(105,27,29,33)))graphics.FillRectangle(dim,area);
    if(track.locked)using(var stripes=new HatchBrush(HatchStyle.ForwardDiagonal,Color.FromArgb(76,210,215,222),Color.Transparent))graphics.FillRectangle(stripes,area);
    graphics.Restore(state);
   }
  }
  void DrawMicroCuts01034(Graphics graphics,IEnumerable<Track> tracks){
   foreach(var track in tracks.Where(t=>t.kind=="video")){
    var clips=Project.Active.clips.Where(c=>c.track_id==track.id).OrderBy(c=>c.timeline_start.Seconds).ToList();
    var state=graphics.Save();graphics.SetClip(TrackViewport(track.kind),CombineMode.Intersect);
    for(int i=0;i<clips.Count;i++){
     var clip=clips[i];bool tiny=clip.duration.Seconds<=3.0/60+0.000001;
     if(tiny){var bounds=ClipBounds(clip);DrawMicroArc01034(graphics,bounds.Left,bounds.Bottom);DrawMicroArc01034(graphics,bounds.Right,bounds.Bottom);}
     if(i+1<clips.Count){var next=clips[i+1];double gap=next.timeline_start.Seconds-clip.End;
      if(gap>0.000001&&gap<=3.0/60+0.000001){var left=ClipBounds(clip);var right=ClipBounds(next);DrawMicroArc01034(graphics,left.Right,left.Bottom);DrawMicroArc01034(graphics,right.Left,right.Bottom);}
     }
    }
    graphics.Restore(state);
   }
  }
  static void DrawMicroArc01034(Graphics graphics,float x,float bottom){
   const float radius=8;using(var fill=new SolidBrush(Color.FromArgb(250,222,57,52)))graphics.FillPie(fill,x-radius,bottom-radius,radius*2,radius*2,180,180);
   using(var outline=new Pen(Color.FromArgb(255,255,226,221),1.2f))graphics.DrawArc(outline,x-radius,bottom-radius,radius*2,radius*2,180,180);
  }
 }
}
