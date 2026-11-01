using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public partial class Timeline {
  public Action<string,string,double,bool> RollPreviewChanged;

  static Clip PreviewCopy(Clip original){
   return new Clip{id=original.id,name=original.name,track_id=original.track_id,asset_id=original.asset_id,stream_id=original.stream_id,timeline_start=original.timeline_start,duration=original.duration,source_in=original.source_in,speed=original.speed,enabled=original.enabled,gain_db=original.gain_db,effects=original.effects,extensions=original.extensions};
  }

  List<Clip> TrimPreviewClips(){
   var result=new List<Clip>();
   if(Project==null||dragging==null||mode==0)return result;
   if(mode==3){
    double delta=dragTime-oldEnd;
    if(Math.Abs(delta)<0.000001)return result;
    var left=Project.Active.clips.Find(c=>c.id==rollLeft);
    var right=Project.Active.clips.Find(c=>c.id==rollRight);
    if(left==null||right==null)return result;
    var pairs=new List<Tuple<Clip,Clip>>{Tuple.Create(left,right)};
    if(!altDrag){
     var lg=Project.Active.link_groups.Find(g=>g.clip_ids.Contains(left.id));
     var rg=Project.Active.link_groups.Find(g=>g.clip_ids.Contains(right.id));
     if(lg!=null&&rg!=null){
      foreach(string id in lg.clip_ids.Where(id=>id!=left.id)){
       var l=Project.Active.clips.Find(c=>c.id==id);
       var r=l==null?null:Project.Active.clips.FirstOrDefault(c=>rg.clip_ids.Contains(c.id)&&c.track_id==l.track_id);
       if(l!=null&&r!=null)pairs.Add(Tuple.Create(l,r));
      }
     }
    }
    foreach(var pair in pairs){
     var l=PreviewCopy(pair.Item1);var r=PreviewCopy(pair.Item2);
     l.duration=Rational.Time(l.duration.Seconds+delta);
     r.timeline_start=Rational.Time(r.timeline_start.Seconds+delta);
     var rightAsset=Project.assets.Find(a=>a.id==r.asset_id);
     var rightStream=rightAsset==null?null:rightAsset.streams.Find(s=>s.id==r.stream_id);
     if(rightStream==null||rightStream.kind!="image")r.source_in=Rational.Time(r.source_in.Seconds+delta*r.speed.Seconds);
     r.duration=Rational.Time(r.duration.Seconds-delta);
     if(l.duration.Seconds>0&&r.duration.Seconds>0){result.Add(l);result.Add(r);}
    }
    return result;
   }
   if(mode!=1&&mode!=2)return result;
   double trimDelta=dragTime-(mode==1?oldStart:oldEnd);
   if(Math.Abs(trimDelta)<0.000001)return result;
   var link=Project.Active.link_groups.Find(g=>g.clip_ids.Contains(dragging));
   var ids=link==null?new[]{dragging}:link.clip_ids.ToArray();
   foreach(string id in ids){
    var original=Project.Active.clips.Find(c=>c.id==id);
    if(original==null)continue;
    var preview=PreviewCopy(original);
    if(mode==1){preview.timeline_start=Rational.Time(preview.timeline_start.Seconds+trimDelta);preview.source_in=Rational.Time(preview.source_in.Seconds+trimDelta*preview.speed.Seconds);preview.duration=Rational.Time(preview.duration.Seconds-trimDelta);}
    else preview.duration=Rational.Time(preview.duration.Seconds+trimDelta);
    if(preview.duration.Seconds>0)result.Add(preview);
   }
   return result;
  }

  void DrawTrimPreview(Graphics g,List<Track> tracks){
   var previews=TrimPreviewClips();
   if(previews.Count==0)return;
   foreach(var clip in previews){
    var original=Project.Active.clips.Find(c=>c.id==clip.id);
    if(original==null||!TrackVisible(original.track_id))continue;
    using(var clear=new SolidBrush(bg))g.FillRectangle(clear,ClipBounds(original));
   }
   foreach(var clip in previews){
    var track=tracks.Find(t=>t.id==clip.track_id);
    if(track==null||!TrackVisible(track.id))continue;
    var r=ClipBounds(clip);
    if(r.Right<headerWidth||r.Left>Width||r.Top>Height)continue;
    var asset=Project.assets.Find(a=>a.id==clip.asset_id);
    bool offline=asset==null||Media.SelectRepresentation(asset,true)==null;
    Color color=offline?Color.FromArgb(103,51,53):ClipLabels.ColorFor(clip,track.kind);
    if(!clip.enabled)color=Color.FromArgb(62,62,64);
    using(var fill=new SolidBrush(color))g.FillRectangle(fill,r);
    float tab=HandleWidth(clip);
    using(var shade=new SolidBrush(Color.FromArgb(80,0,0,0))){g.FillRectangle(shade,r.Left,r.Top,tab,r.Height);g.FillRectangle(shade,r.Right-tab,r.Top,tab,r.Height);}
    using(var edge=new Pen(Color.FromArgb(235,255,190,105),2))g.DrawRectangle(edge,r.X,r.Y,r.Width,r.Height);
    var state=g.Save();g.SetClip(r,CombineMode.Intersect);
    float labelX=Math.Max(r.Left+tab+4,headerWidth+8);
    if(TrackPresentation.ShowNames(Project.Active,track))using(var ink=new SolidBrush(offline?Color.WhiteSmoke:Color.FromArgb(27,31,35)))g.DrawString(clip.name,Font,ink,labelX,r.Top+3);
    g.Restore(state);
    if(ShowWaveforms&&track.kind=="audio"){
     WaveformData peaks;
     if(Waveforms.TryGetValue(clip.stream_id,out peaks))DrawWaveform(g,clip,r,peaks,clip.enabled&&track.enabled);
    }
    if(clip.id==dragging&&(mode==1||mode==2))DrawTrimCue(g,r,mode);
   }
   if(mode==3){using(var pen=new Pen(Color.FromArgb(245,255,196,100),2))g.DrawLine(pen,X(dragTime),32,X(dragTime),Height);}
  }

  void DrawSplitCutIndicators(Graphics g,List<Track> tracks){
   foreach(var track in tracks.Where(t=>t.kind=="video")){
    var ordered=Project.Active.clips.Where(c=>c.track_id==track.id).OrderBy(c=>c.timeline_start.Seconds).ToList();
    for(int i=0;i+1<ordered.Count;i++){
     var left=ordered[i];var right=ordered[i+1];
     if(Math.Abs(left.End-right.timeline_start.Seconds)>0.000001)continue;
     var leftGroup=Project.Active.link_groups.Find(group=>group.clip_ids.Contains(left.id));
     var rightGroup=Project.Active.link_groups.Find(group=>group.clip_ids.Contains(right.id));
     if(leftGroup==null||rightGroup==null)continue;
     foreach(var audioTrack in tracks.Where(t=>t.kind=="audio")){
      var audioLeft=Project.Active.clips.FirstOrDefault(c=>c.track_id==audioTrack.id&&leftGroup.clip_ids.Contains(c.id));
      var audioRight=Project.Active.clips.FirstOrDefault(c=>c.track_id==audioTrack.id&&rightGroup.clip_ids.Contains(c.id));
      if(audioLeft==null||audioRight==null||Math.Abs(audioLeft.End-audioRight.timeline_start.Seconds)>0.000001||Math.Abs(audioLeft.End-left.End)<1.0/60-0.000001)continue;
      foreach(var item in new[]{Tuple.Create(track.id,left.End),Tuple.Create(audioTrack.id,audioLeft.End)}){
       if(!TrackVisible(item.Item1))continue;
       float x=X(item.Item2);int y=TrackTop(item.Item1)+5;
       if(x<headerWidth+8||x>Width-8||y<32||y>Height-16)continue;
       var badge=new Rectangle((int)x-12,y,24,14);
       using(var fill=new SolidBrush(Color.FromArgb(240,246,167,130)))g.FillRectangle(fill,badge);
       TextRenderer.DrawText(g,"J/L",Font,badge,Color.FromArgb(50,31,28),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
      }
     }
    }
   }
  }
 }
}
