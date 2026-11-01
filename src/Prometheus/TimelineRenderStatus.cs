using System;
using System.Drawing;
using System.Linq;

namespace Prometheus {
 public partial class Timeline {
  void DrawRenderStatus(Graphics graphics){
   if(Project==null)return;
   var sequence=Project.Active;
   using(var yellow=new SolidBrush(Color.FromArgb(231,225,207,104)))
   using(var red=new SolidBrush(Color.FromArgb(235,217,75,72)))
   using(var green=new SolidBrush(Color.FromArgb(235,106,186,111))){
    foreach(var clip in sequence.clips.Where(c=>c.enabled&&sequence.tracks.Any(t=>t.id==c.track_id&&t.enabled&&t.kind=="video"))){
     float left=Math.Max(headerWidth+1,X(clip.timeline_start.Seconds)),right=Math.Min(Width,X(clip.End));
     if(right>left)graphics.FillRectangle(yellow,left,29,Math.Max(1,right-left),3);
    }
    foreach(var transition in sequence.transitions.Where(MgtTransitionCache.IsMgt).Where(t=>new[]{t.left_clip_id,t.right_clip_id}.All(id=>sequence.clips.Any(c=>c.id==id&&c.enabled&&sequence.tracks.Any(track=>track.id==c.track_id&&track.enabled))))) {
     float left=Math.Max(headerWidth+1,X(MgtTransitionCache.Start(transition,60))),right=Math.Min(Width,X(MgtTransitionCache.End(transition,60)));
     if(right>left)graphics.FillRectangle(MgtPreviewReady!=null&&MgtPreviewReady(transition)?green:red,left,29,Math.Max(1,right-left),3);
    }
   }
  }
 }
}
