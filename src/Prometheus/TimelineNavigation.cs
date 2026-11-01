using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Prometheus {
 public partial class Timeline {
  public double MinimumZoom(){
   double markerEnd=Project==null?0:Markers.Read(Project.Active).Select(marker=>marker.Seconds).DefaultIfEmpty(0).Max();
   double horizon=Project==null?60:Math.Max(60,Math.Max(Math.Max(Project.Active.Length,markerEnd),Cursor));
   return Math.Max(0.000001,Math.Min(5,Math.Max(1,Width-headerWidth-15)/(horizon*10)));
  }

  public void SetZoomAt(double requested,int screenX){
   double anchor=Math.Max(0,(screenX-headerWidth)/Zoom+Scroll);
   Zoom=Math.Max(MinimumZoom(),Math.Min(500,requested));
   if(screenX>=headerWidth)Scroll=Math.Max(0,anchor-(screenX-headerWidth)/Zoom);
   Invalidate();
  }

  public void FitContentLong(){
   if(Project==null)return;
   double duration=Math.Max(Project.Active.Length,Markers.Read(Project.Active).Select(marker=>marker.Seconds).DefaultIfEmpty(0).Max());
   Zoom=duration<=0?55:Math.Max(0.000001,Math.Min(500,Math.Max(1,Width-headerWidth-18)/(duration*1.08)));
   Scroll=0;
   Invalidate();
  }

  public void FitSelection(){
   if(Project==null||Selection.Count==0){FitContentLong();return;}
   var ids=new HashSet<string>(Selection);
   foreach(var link in Project.Active.link_groups.Where(g=>g.clip_ids.Any(ids.Contains)))foreach(string id in link.clip_ids)ids.Add(id);
   var clips=Project.Active.clips.Where(c=>ids.Contains(c.id)).ToList();
   if(clips.Count==0){FitContentLong();return;}
   double start=clips.Min(c=>c.timeline_start.Seconds),end=clips.Max(c=>c.End);
   double span=Math.Max(1.0/60,end-start),margin=Math.Max(0.25,span*0.05);
   Zoom=Math.Max(0.000001,Math.Min(500,Math.Max(1,Width-headerWidth-18)/(span+margin*2)));
   Scroll=Math.Max(0,start-margin);
   Invalidate();
  }

  public double RulerStep(Graphics graphics){
   double end=Scroll+Math.Max(1,Width-headerWidth)/Math.Max(Zoom,0.000001);
   double labelWidth=Math.Max(graphics.MeasureString(TimeLabel(Scroll),Font).Width,graphics.MeasureString(TimeLabel(end),Font).Width)+16;
   double wanted=labelWidth/Math.Max(Zoom,0.000001);
   if(wanted<=1.0/60)return 1.0/60;
   double[] seconds={1.0/30,1.0/15,0.1,0.2,0.5,1,2,5,10,15,30};
   foreach(double step in seconds)if(step>=wanted)return step;
   double[] minutes={1,2,5,10,15,30};
   foreach(double step in minutes)if(step*60>=wanted)return step*60;
   double hourStep=wanted/3600;
   double magnitude=Math.Pow(10,Math.Floor(Math.Log10(hourStep)));
   foreach(double multiple in new[]{1.0,2.0,5.0,10.0})if(multiple*magnitude>=hourStep)return multiple*magnitude*3600;
   return 10*magnitude*3600;
  }

  void DrawAdaptiveRuler(Graphics graphics){
   double step=RulerStep(graphics);
   double visibleStart=Scroll,visibleEnd=Scroll+Math.Max(1,Width-headerWidth)/Zoom;
   long first=(long)Math.Floor(visibleStart/step),last=(long)Math.Ceiling(visibleEnd/step);
   double minor=step/5;
   if(minor*Zoom>=18){
    using(var faint=new Pen(Color.FromArgb(42,44,48))){
     long a=(long)Math.Floor(visibleStart/minor),b=(long)Math.Ceiling(visibleEnd/minor);
     for(long i=a;i<=b&&i<a+500;i++)if(i%5!=0){float x=X(i*minor);graphics.DrawLine(faint,x,27,x,Height);}
    }
   }
   using(var major=new Pen(line))for(long i=first;i<=last&&i<first+500;i++){
    double time=i*step;float x=X(time);
    if(x<headerWidth-2||x>Width)continue;
    graphics.DrawString(TimeLabel(time),Font,Brushes.Gray,x+2,7);
    graphics.DrawLine(major,x,27,x,Height);
   }
  }
 }
}
