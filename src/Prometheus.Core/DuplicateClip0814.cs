using System;
using System.Collections.Generic;
using System.Linq;
namespace Prometheus {
 public partial class Editor {
  public List<string> DuplicateRelated(string anchorId,double start,string targetTrack){
   var anchor=Project.Active.clips.Find(c=>c.id==anchorId);if(anchor==null)throw new Exception("Clip de origen ausente");
   var related=Related(anchorId);double delta=Rational.Frame(start-anchor.timeline_start.Seconds).Seconds;
   if(Math.Abs(delta)<1.0/120&&(targetTrack==null||targetTrack==anchor.track_id))return new List<string>();
   var originals=Project.Active.clips.Where(c=>related.Contains(c.id)).ToList();
   if(originals.Any(c=>c.timeline_start.Seconds+delta<0))throw new Exception("La copia quedaría antes del inicio de la secuencia");
   var created=new List<string>();
   Commit(project=>{
    var source=project.Active.clips.Where(c=>related.Contains(c.id)).ToList();
    var destination=project.Active.tracks.Find(t=>t.id==(targetTrack??anchor.track_id));
    if(destination==null||destination.locked)throw new Exception("Pista de destino ausente o bloqueada");
    var anchorTrack=project.Active.tracks.Find(t=>t.id==anchor.track_id);
    if(anchorTrack==null||anchorTrack.kind!=destination.kind)throw new Exception("La pista de destino no admite este medio");
    var copied=new List<Clip>();
    foreach(var original in source){var track=project.Active.tracks.Find(t=>t.id==original.track_id);if(track==null||track.locked)throw new Exception("La pista de origen está bloqueada");
     var clone=Store.Json().Deserialize<Clip>(Store.Json().Serialize(Store.Fields(original)));clone.id=Store.Id();clone.timeline_start=Rational.Frame(original.timeline_start.Seconds+delta);
     var originalAsset=project.assets.Find(a=>a.id==original.asset_id);
     if(GeneratedMedia.IsText(originalAsset)){
      var copy=Store.Json().Deserialize<Asset>(Store.Json().Serialize(Store.Fields(originalAsset)));copy.id=Store.Id();copy.source.uri="generated:"+copy.id;copy.source.imported_at=DateTime.UtcNow.ToString("o");
      foreach(var stream in copy.streams){string old=stream.id;stream.id=Store.Id();if(clone.stream_id==old)clone.stream_id=stream.id;}
      project.assets.Add(copy);clone.asset_id=copy.id;
     }
     if(original.id==anchorId)clone.track_id=destination.id;
     var effectIds=new Dictionary<string,string>();foreach(var effect in clone.effects){string old=effect.id;effect.id=Store.Id();effectIds[old]=effect.id;}
     foreach(var curve in clone.animation)foreach(var mapping in effectIds)if(curve.target.StartsWith("effect:"+mapping.Key+":",StringComparison.Ordinal))curve.target="effect:"+mapping.Value+curve.target.Substring(("effect:"+mapping.Key).Length);
     project.Active.clips.Add(clone);copied.Add(clone);created.Add(clone.id);
    }
    if(copied.Count>1)project.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=copied.Select(c=>c.id).ToList()});
    Overwrite(project,created);
   });
   return created;
  }
 }
}
