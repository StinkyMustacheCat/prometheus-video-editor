using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 public sealed class PlacementResult {
  public readonly List<string> ClipIds=new List<string>();
  public string PrimaryClipId,PrimaryTrackId;
 }
 public static partial class Media {
  // Timeline context insertion never overwrites an existing clip. Each component
  // of an A/V asset searches its own compatible track without assuming Vn/An pairs.
  public static PlacementResult PlaceInFreeTrack(Project project,string assetId,string preferredTrackId,double at){return PlaceInFreeTrack(project,assetId,preferredTrackId,at,null,null);}
  public static PlacementResult PlaceInFreeTrack(Project project,string assetId,string preferredTrackId,double at,double? sourceIn,double? selectedDuration){
   var asset=project.assets.Find(a=>a.id==assetId);
   var preferred=project.Active.tracks.Find(t=>t.id==preferredTrackId);
   if(asset==null||preferred==null)throw new Exception("Selecciona un medio y una pista disponibles");
   if(preferred.locked)throw new Exception("La pista está bloqueada");
   var main=asset.streams.FirstOrDefault(s=>preferred.kind=="audio"?s.kind=="audio":s.kind=="video"||s.kind=="image");
   if(main==null)throw new Exception("El medio no corresponde a esta pista");
   double start=Rational.Frame(Math.Max(0,at)).Seconds;
   var result=new PlacementResult();
   Action<StreamInfo,string,bool> place=(stream,kind,primary)=>{
    double offset=stream.pts_origin.Seconds-asset.presentation_origin.Seconds;
    double begin=(kind=="audio"?Rational.Time(start+Math.Max(0,offset)):Rational.Frame(start+Math.Max(0,offset))).Seconds;
    Rational duration=stream.duration==null?(asset.recommended_duration??new Rational(5,1)):
     kind=="audio"?new Rational((long)Math.Floor(stream.duration.Seconds*48000+0.000001),48000):
     new Rational((long)Math.Floor(stream.duration.Seconds*60),60);
    if(sourceIn.HasValue&&!selectedDuration.HasValue)duration=kind=="audio"?Rational.Time(duration.Seconds-sourceIn.Value):Rational.Frame(duration.Seconds-sourceIn.Value);
    if(selectedDuration.HasValue)duration=kind=="audio"?Rational.Time(selectedDuration.Value):Rational.Frame(selectedDuration.Value);
    if(duration.n<=0)throw new Exception("El medio es demasiado corto");
    int firstOrder=primary?preferred.order:0;
    var candidates=project.Active.tracks.Where(t=>t.kind==kind&&t.id!=SubtitleTrack0817.TrackId(project.Active)&&t.order>=firstOrder&&!t.locked).OrderBy(t=>t.order).ToList();
    Track target=candidates.FirstOrDefault(t=>!project.Active.clips.Any(c=>c.track_id==t.id&&c.timeline_start.Seconds<begin+duration.Seconds-0.000001&&c.End>begin+0.000001));
    if(target==null)target=Store.AddTrack(project.Active,kind);
    var clip=new Clip{id=Store.Id(),name=asset.name,asset_id=asset.id,stream_id=stream.id,track_id=target.id,
     timeline_start=Rational.Time(begin),source_in=Rational.Time(sourceIn??0),duration=duration};
    project.Active.clips.Add(clip);result.ClipIds.Add(clip.id);
    if(primary){result.PrimaryClipId=clip.id;result.PrimaryTrackId=target.id;}
   };
   place(main,preferred.kind,true);
   if(main.kind=="video"){
    var audio=asset.streams.FirstOrDefault(s=>s.kind=="audio");
    if(audio!=null)place(audio,"audio",false);
   }
   if(result.ClipIds.Count>1)project.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=result.ClipIds.ToList()});
   return result;
  }
 }
}
