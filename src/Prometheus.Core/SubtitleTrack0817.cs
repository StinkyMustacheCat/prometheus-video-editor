using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 public static class SubtitleTrack0817 {
  public const string Namespace="prometheus.subtitles";
  static Extension Data(Sequence sequence){return sequence.extensions.FirstOrDefault(e=>e!=null&&e.@namespace==Namespace&&e.version=="1");}
  public static string TrackId(Sequence sequence){object value;var ext=Data(sequence);return ext!=null&&ext.payload!=null&&ext.payload.TryGetValue("track_id",out value)?Convert.ToString(value):null;}
  public static Track Track(Sequence sequence){string id=TrackId(sequence);return id==null?null:sequence.tracks.FirstOrDefault(t=>t.id==id);}
  public static Track Ensure(Sequence sequence){var existing=Track(sequence);if(existing!=null)return existing;
   var track=Store.AddTrack(sequence,"video");track.name="Subtítulos";
   sequence.extensions.Add(new Extension{@namespace=Namespace,version="1",payload=new Dictionary<string,object>{{"track_id",track.id}}});return track;
  }
  public static List<Clip> Cues(Project project){var track=Track(project.Active);return track==null?new List<Clip>():project.Active.clips.Where(c=>c.track_id==track.id).OrderBy(c=>c.timeline_start.Seconds).ToList();}
  public static bool IsCue(Project project,Clip clip){return clip!=null&&clip.track_id==TrackId(project.Active);}
  public static Clip Add(Project project,string text,double start,double duration){
   start=Rational.Frame(start).Seconds;duration=Rational.Frame(duration).Seconds;
   if(start<0||duration<1.0/60||duration>600)throw new Exception("El subtítulo necesita un intervalo válido");
   var track=Ensure(project.Active);if(track.locked)throw new Exception("La pista de subtítulos está bloqueada");
   if(project.Active.clips.Any(c=>c.track_id==track.id&&c.timeline_start.Seconds<start+duration-0.000001&&c.End>start+0.000001))throw new Exception("Ya hay un subtítulo en ese intervalo");
   var asset=GeneratedMedia.CreateText(text,duration,true);project.assets.Add(asset);
   var clip=new Clip{id=Store.Id(),name=asset.name,asset_id=asset.id,stream_id=asset.streams[0].id,track_id=track.id,timeline_start=Rational.Frame(start),duration=Rational.Frame(duration),source_in=Rational.Time(0)};
   project.Active.clips.Add(clip);return clip;
  }
  public static void Remove(Project project,string clipId){var cue=Cues(project).FirstOrDefault(c=>c.id==clipId);if(cue==null)throw new Exception("El subtítulo ya no existe");
   if(Track(project.Active).locked)throw new Exception("La pista de subtítulos está bloqueada");
   project.Active.clips.Remove(cue);project.Active.link_groups.RemoveAll(g=>{g.clip_ids.Remove(clipId);return g.clip_ids.Count<2;});
   if(!project.sequences.Any(s=>s.clips.Any(c=>c.asset_id==cue.asset_id)))project.assets.RemoveAll(a=>a.id==cue.asset_id);
  }
 }
}
