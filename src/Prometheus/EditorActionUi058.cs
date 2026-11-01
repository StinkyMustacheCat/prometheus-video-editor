using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 public partial class MainForm {
  string CurrentActionTime(){double time=timeline.Cursor;return time.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);}
  // A future voice adapter can submit these validated requests without interpreting free text in the editor.
  // Transport, navigation and selection are immediate and do not touch the project revision or undo history.
  public string ExecuteEditorAction(EditorActionRequest request){
   var project=editor.Project;EditorActionCatalog.Validate(project,request);
   var sequence=project.Active;string id=request.id;
   if(id=="transport.play"){if(!playing)PlayPause();return playing?"Reproduciendo":"Sin reproducción";}
   if(id=="transport.pause"){if(playing){Stop();status.Text="Pausa";}return "Pausado";}
   if(id=="transport.toggle"){PlayPause();return playing?"Reproduciendo":"Pausado";}
   if(id=="playhead.seek"){Seek(request.seconds.Value);return CurrentActionTime();}
   if(id=="playhead.step_frames"){Seek(timeline.Cursor+request.frames.Value/sequence.fps.Seconds);return CurrentActionTime();}
   if(id=="playhead.step_seconds"){Seek(timeline.Cursor+request.seconds.Value);return CurrentActionTime();}
   if(id=="playhead.start"||id=="playhead.end"){Seek(id.EndsWith("start")?0:sequence.Length);return CurrentActionTime();}
   if(id.StartsWith("playhead.boundary.")||id.StartsWith("playhead.junction.")){
    IEnumerable<double> positions;
    var navigable=sequence.clips.Where(c=>timelineView.SubtitleStrip.Expanded||!SubtitleTrack0817.IsCue(editor.Project,c)).ToList();
    if(id.StartsWith("playhead.boundary."))positions=navigable.SelectMany(c=>new[]{c.timeline_start.Seconds,c.End});
    else positions=sequence.tracks.SelectMany(t=>{var clips=navigable.Where(c=>c.track_id==t.id).OrderBy(c=>c.timeline_start.Seconds).ToList();return clips.Take(Math.Max(0,clips.Count-1)).Select((c,i)=>new{c,i}).Where(x=>Math.Abs(x.c.End-clips[x.i+1].timeline_start.Seconds)<0.00001).Select(x=>x.c.End);});
    var ordered=positions.Distinct().OrderBy(x=>x).ToList();bool forward=id.EndsWith("next");double? target=forward?ordered.Where(x=>x>timeline.Cursor+0.000001).Select(x=>(double?)x).FirstOrDefault():ordered.Where(x=>x<timeline.Cursor-0.000001).Select(x=>(double?)x).LastOrDefault();if(target.HasValue)Seek(target.Value);return target.HasValue?CurrentActionTime():"Sin otro límite";
   }
   if(id=="playhead.marker.next"||id=="playhead.marker.previous"){NavigateMarker(id.EndsWith("next"));return CurrentActionTime();}
   if(id=="playhead.marker.seek"){var marker=Markers.Read(sequence).FirstOrDefault(x=>x.Id==request.marker_id);if(marker==null)throw new Exception("Marcador inexistente");Seek(marker.Seconds);return CurrentActionTime();}
   if(id=="selection.clip.at_playhead"||id=="selection.clip.by_id"){
    var clip=id.EndsWith("by_id")?sequence.clips.FirstOrDefault(c=>c.id==request.clip_id):sequence.clips.Where(c=>c.timeline_start.Seconds<=timeline.Cursor+0.000001&&c.End>timeline.Cursor+0.000001).OrderBy(c=>sequence.tracks.Find(t=>t.id==c.track_id).kind=="video"?0:1).ThenByDescending(c=>sequence.tracks.Find(t=>t.id==c.track_id).order).FirstOrDefault();
    if(clip==null)throw new Exception("No hay clip en esa posición");timeline.Selection=new List<string>{clip.id};timeline.Selected=clip.id;selectedTransitionId=null;timeline.SelectedTransitionId=null;instructionView.SetContext(project,timeline.Selection,null);ShowClip();timeline.Invalidate();return clip.id;
   }
   if(id=="selection.linked"){
    var ids=new HashSet<string>(timeline.Selection);foreach(var group in sequence.link_groups)if(group.clip_ids.Any(ids.Contains))foreach(var clipId in group.clip_ids)ids.Add(clipId);
    timeline.Selection=ids.ToList();timeline.Selected=timeline.Selection.FirstOrDefault();instructionView.SetContext(project,timeline.Selection,null);ShowClip();timeline.Invalidate();return String.Join(",",timeline.Selection);
   }
   if(id=="selection.clear"){timeline.Selection.Clear();timeline.Selected=null;selectedTransitionId=null;timeline.SelectedTransitionId=null;instructionView.SetContext(project,timeline.Selection,null);ShowClip();timeline.Invalidate();return "Selección vacía";}
   if(id=="state.current_time")return CurrentActionTime();
   if(id=="state.selected_clip")return timeline.Selected??"";
   if(id=="state.active_sequence")return sequence.id;
   throw new Exception("Acción sin ejecutor: "+id);
  }
 }
}
