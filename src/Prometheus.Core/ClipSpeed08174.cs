using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 public static class ClipSpeed08174 {
  // Speed changes affect this medium and its linked streams; other clips never move.
  public static bool Change(Project project,string clipId,decimal percent,bool unusedRipple=false){
   if(percent<25||percent>400)throw new Exception("La velocidad debe estar entre 25 % y 400 %.");
   var sequence=project.Active;var primary=sequence.clips.Find(c=>c.id==clipId);
   if(primary==null)throw new Exception("Selecciona un clip para cambiar su velocidad.");
   var group=sequence.link_groups.FirstOrDefault(g=>g.clip_ids.Contains(clipId));
   var ids=new HashSet<string>(group==null?new List<string>{clipId}:group.clip_ids);
   var targets=sequence.clips.Where(c=>ids.Contains(c.id)).ToList();
   foreach(var clip in targets){
    var track=sequence.tracks.Find(t=>t.id==clip.track_id);var asset=project.assets.Find(a=>a.id==clip.asset_id);
    var stream=asset==null?null:asset.streams.Find(s=>s.id==clip.stream_id);
    if(track==null||track.locked)throw new Exception("La pista del clip está bloqueada.");
    if(stream==null||stream.kind=="image")throw new Exception("La velocidad requiere un medio temporal de vídeo o audio.");
   }
   double newSpeed=(double)percent/100.0;
   bool linkedVideo=targets.Any(c=>sequence.tracks.Find(t=>t.id==c.track_id).kind=="video");
   double grid=linkedVideo?60:48000;
   double requested=targets.Min(c=>Math.Floor(c.duration.Seconds*c.speed.Seconds/newSpeed*grid+0.000001)/grid);
   double available=Double.PositiveInfinity;
   foreach(var clip in targets){
    var next=sequence.clips.Where(other=>!ids.Contains(other.id)&&other.track_id==clip.track_id&&other.timeline_start.Seconds>=clip.End-0.000001).OrderBy(other=>other.timeline_start.Seconds).FirstOrDefault();
    if(next!=null)available=Math.Min(available,next.timeline_start.Seconds-clip.timeline_start.Seconds);
   }
   bool limited=requested>available+0.000001;
   double duration=Math.Floor(Math.Min(requested,available)*grid+0.000001)/grid;
   if(duration<1.0/grid)throw new Exception("No queda espacio para mostrar un fotograma del medio.");
   var speed=new Rational((long)Decimal.Round(percent*100,0,MidpointRounding.AwayFromZero),10000);
   foreach(var clip in targets){clip.speed=speed;clip.duration=linkedVideo?Rational.Frame(duration):Rational.Time(duration);}
   TransitionContracts.Normalize(project);
   return limited;
  }
 }
}