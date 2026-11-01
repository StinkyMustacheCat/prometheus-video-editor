using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Prometheus {
 public sealed class ExportMarks {
  public long? InFrame,OutFrame;
  public bool Complete {get{return InFrame.HasValue&&OutFrame.HasValue&&OutFrame.Value>InFrame.Value;}}
 }
 public static class SequenceExportRange {
  public const string Namespace="prometheus.sequence-export-range";
  static Extension Find(Sequence s){return s.extensions.FirstOrDefault(x=>x!=null&&x.@namespace==Namespace&&x.version=="1");}
  static long? Number(Extension extension,string key){object raw;long frame;if(extension==null||extension.payload==null||!extension.payload.TryGetValue(key,out raw)||raw==null||!long.TryParse(Convert.ToString(raw,CultureInfo.InvariantCulture),out frame)||frame<0)return null;return frame;}
  public static ExportMarks Read(Sequence s){var extension=Find(s);return new ExportMarks{InFrame=Number(extension,"in_frame"),OutFrame=Number(extension,"out_frame")};}
  static long Frame(double seconds){if(Double.IsNaN(seconds)||Double.IsInfinity(seconds)||seconds<0)throw new Exception("Posición de entrada o salida inválida");return (long)Math.Round(seconds*60);}
  static void Write(Sequence s,ExportMarks marks){
   var extension=Find(s);if(extension==null){extension=new Extension{@namespace=Namespace,version="1",payload=new Dictionary<string,object>()};s.extensions.Add(extension);}
   extension.payload["in_frame"]=marks.InFrame.HasValue?(object)marks.InFrame.Value:null;
   extension.payload["out_frame"]=marks.OutFrame.HasValue?(object)marks.OutFrame.Value:null;
  }
  public static void SetIn(Sequence s,double seconds){var marks=Read(s);marks.InFrame=Frame(seconds);if(marks.OutFrame.HasValue&&marks.OutFrame.Value<=marks.InFrame.Value)marks.OutFrame=null;Write(s,marks);}
  public static void SetOut(Sequence s,double seconds){var marks=Read(s);marks.OutFrame=Frame(seconds);if(marks.InFrame.HasValue&&marks.InFrame.Value>=marks.OutFrame.Value)marks.InFrame=null;Write(s,marks);}
  public static void Clear(Sequence s){s.extensions.RemoveAll(x=>x!=null&&x.@namespace==Namespace);}
  public static Project Window(Project original,long startFrame,long endFrame){
   if(startFrame<0||endFrame<=startFrame||endFrame/60.0>original.Active.Length+0.000001)throw new Exception("Intervalo de exportación inválido");
   foreach(var transition in original.Active.transitions.Where(t=>t.enabled)){
    int lead=transition.transition_id==TransitionContracts.Dissolve?transition.duration_frames:transition.transition_id==TransitionContracts.Mgt?(transition.duration_frames+1)/2:transition.duration_frames/2;
    int trail=transition.transition_id==TransitionContracts.Dissolve?0:transition.transition_id==TransitionContracts.Mgt?transition.duration_frames-lead:transition.duration_frames/2;
    int begin=transition.cut_frame-lead,finish=transition.cut_frame+trail;
    if(startFrame>begin&&startFrame<finish||endFrame>begin&&endFrame<finish)
     throw new Exception("La marca de entrada o salida atraviesa una transición. Sitúa la marca fuera de ella para conservarla en la exportación.");
   }
   var result=Store.Copy(original);var seq=result.Active;double from=startFrame/60.0,to=endFrame/60.0;
   foreach(var clip in seq.clips.ToList()){
    double begin=Math.Max(from,clip.timeline_start.Seconds),end=Math.Min(to,clip.End);
    if(end<=begin+0.000001){seq.clips.Remove(clip);continue;}
    var track=seq.tracks.Find(x=>x.id==clip.track_id);double grid=track.kind=="audio"?48000:60;
    clip.source_in=Rational.Time(clip.source_in.Seconds+(begin-clip.timeline_start.Seconds)*clip.speed.Seconds);
    clip.timeline_start=track.kind=="audio"?Rational.Time(begin-from):Rational.Frame(begin-from);
    clip.duration=new Rational((long)Math.Round((end-begin)*grid),(long)grid);
   }
   foreach(var group in seq.link_groups)group.clip_ids.RemoveAll(id=>!seq.clips.Any(c=>c.id==id));
   seq.link_groups.RemoveAll(group=>group.clip_ids.Count<2);
   foreach(var group in seq.edit_groups)group.clip_ids.RemoveAll(id=>!seq.clips.Any(c=>c.id==id));
   seq.edit_groups.RemoveAll(group=>group.clip_ids.Count<2);
   seq.transitions.RemoveAll(t=>{
    int lead=t.transition_id==TransitionContracts.Dissolve?t.duration_frames:t.transition_id==TransitionContracts.Mgt?(t.duration_frames+1)/2:t.duration_frames/2;
    int trail=t.transition_id==TransitionContracts.Dissolve?0:t.transition_id==TransitionContracts.Mgt?t.duration_frames-lead:t.duration_frames/2;
    return t.cut_frame-lead<startFrame||t.cut_frame+trail>endFrame||!seq.clips.Any(c=>c.id==t.left_clip_id)||!seq.clips.Any(c=>c.id==t.right_clip_id);
   });
   foreach(var transition in seq.transitions)transition.cut_frame-=(int)startFrame;
   TransitionContracts.Normalize(result);
   Store.Validate(result);return result;
  }
 }
 public partial class Editor {
  public void MarkExportIn(double time){Commit(p=>SequenceExportRange.SetIn(p.Active,time));}
  public void MarkExportOut(double time){Commit(p=>SequenceExportRange.SetOut(p.Active,time));}
  public void ClearExportMarks(){Commit(p=>SequenceExportRange.Clear(p.Active));}
 }
}
