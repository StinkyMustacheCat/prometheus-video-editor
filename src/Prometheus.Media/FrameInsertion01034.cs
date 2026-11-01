using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 public static class FrameInsertion01034 {
  public static Clip Overlay(Project project,Asset asset,double at){
   var sequence=project.Active;double position=Rational.Frame(at).Seconds;
   double span=(asset.recommended_duration??new Rational(5,1)).Seconds;
   if(span<1.0/60||span>48*3600)throw new Exception("Duración del fotograma inválida");
   var target=sequence.tracks.Where(t=>t.kind=="video"&&!t.locked).OrderBy(t=>t.order).FirstOrDefault(t=>
    !sequence.clips.Any(c=>c.track_id==t.id&&c.timeline_start.Seconds<position+span-0.000001&&c.End>position+0.000001))
    ??Store.AddTrack(sequence,"video");
   project.assets.Add(asset);Media.Insert(project,asset.id,target.id,position);
   return sequence.clips.Last(c=>c.asset_id==asset.id);
  }
  public static Clip Insert(Project project,Asset asset,double at){
   var sequence=project.Active;double position=Rational.Frame(at).Seconds;
   double span=(asset.recommended_duration??new Rational(5,1)).Seconds;
   long shiftFrames=(long)Math.Round(span*60),atFrame=(long)Math.Round(position*60);
   if(span<1.0/60||span>48*3600)throw new Exception("Duración del fotograma inválida");
   foreach(var clip in sequence.clips.Where(c=>c.End>position+0.000001))
    if(sequence.tracks.Find(t=>t.id==clip.track_id).locked)throw new Exception("Desbloquea las pistas posteriores para insertar el fotograma sin perder sincronía");
   if(sequence.transitions.Any(t=>Math.Abs(t.cut_frame/60.0-position)<t.duration_frames/60.0+0.000001))
    throw new Exception("Sitúa el lector fuera de la transición antes de insertar el fotograma");
   var rightIds=new Dictionary<string,string>();
   foreach(var clip in sequence.clips.Where(c=>c.timeline_start.Seconds<position-0.000001&&c.End>position+0.000001).ToList()){
    var right=Store.Json().Deserialize<Clip>(Store.Json().Serialize(Store.Fields(clip)));
    double offset=position-clip.timeline_start.Seconds;right.id=Store.Id();ClipIdentity.NewEffects(right);right.timeline_start=Rational.Time(position);
    right.duration=Rational.Time(clip.End-position);right.source_in=Rational.Time(clip.source_in.Seconds+offset*clip.speed.Seconds);
    clip.duration=Rational.Time(offset);sequence.clips.Add(right);rightIds[clip.id]=right.id;
   }
   foreach(var group in sequence.link_groups.ToList()){
    if(!group.clip_ids.Any(rightIds.ContainsKey))continue;
    var left=new List<string>();var right=new List<string>();
    foreach(string id in group.clip_ids){var clip=sequence.clips.Find(c=>c.id==id);string copy;
     if(rightIds.TryGetValue(id,out copy)){left.Add(id);right.Add(copy);}
     else if(clip.timeline_start.Seconds>=position-0.000001)right.Add(id);else left.Add(id);
    }
    sequence.link_groups.Remove(group);
    if(left.Count>1)sequence.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=left});
    if(right.Count>1)sequence.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=right});
   }
   foreach(var group in sequence.edit_groups)foreach(var pair in rightIds)
    if(group.clip_ids.Contains(pair.Key)&&!group.clip_ids.Contains(pair.Value))group.clip_ids.Add(pair.Value);
   foreach(var transition in sequence.transitions){string right;if(rightIds.TryGetValue(transition.left_clip_id,out right))transition.left_clip_id=right;}
   foreach(var clip in sequence.clips.Where(c=>c.timeline_start.Seconds>=position-0.000001))clip.timeline_start=Rational.Time(clip.timeline_start.Seconds+span);
   foreach(var transition in sequence.transitions.Where(t=>t.cut_frame>=atFrame))transition.cut_frame+=(int)shiftFrames;
   foreach(var marker in Markers.Read(sequence).Where(m=>m.Frame>=atFrame))Markers.Move(sequence,marker.Id,(marker.Frame+shiftFrames)/60.0);
   var marks=SequenceExportRange.Read(sequence);
   if(marks.InFrame.HasValue&&marks.InFrame.Value>=atFrame)SequenceExportRange.SetIn(sequence,(marks.InFrame.Value+shiftFrames)/60.0);
   if(marks.OutFrame.HasValue&&marks.OutFrame.Value>=atFrame)SequenceExportRange.SetOut(sequence,(marks.OutFrame.Value+shiftFrames)/60.0);
   project.assets.Add(asset);
   var target=sequence.tracks.Where(t=>t.kind=="video"&&!t.locked).OrderBy(t=>t.order).FirstOrDefault()
    ??Store.AddTrack(sequence,"video");
   Media.Insert(project,asset.id,target.id,position);
   return sequence.clips.Last(c=>c.asset_id==asset.id);
  }
 }
}
