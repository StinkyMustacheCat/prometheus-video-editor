using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 public static class ClipboardCore1020 {
  public static List<string> Paste(Project project,IEnumerable<Clip> copied,IEnumerable<Track> sourceTracks,string sourceProjectId,string sourceSequenceId,IEnumerable<string> links,IEnumerable<string> groups,double cursor){
   if(project.id!=sourceProjectId)throw new Exception("Los medios copiados pertenecen a otro proyecto");
   var clips=copied.ToList();var tracks=sourceTracks.ToList();if(clips.Count==0)return new List<string>();
   double delta=Rational.Frame(cursor).Seconds-clips.Min(c=>c.timeline_start.Seconds);
   var ids=new Dictionary<string,string>();var trackMap=new Dictionary<string,string>();
   foreach(var original in clips){var clip=Store.Json().Deserialize<Clip>(Store.Json().Serialize(Store.Fields(original)));string destination;
    if(!trackMap.TryGetValue(clip.track_id,out destination)){
     var source=tracks.FirstOrDefault(t=>t.id==clip.track_id);if(source==null)throw new Exception("Falta la pista original del clip copiado");
     var target=project.Active.id==sourceSequenceId?project.Active.tracks.FirstOrDefault(t=>t.id==source.id):null;
     if(target==null)target=project.Active.tracks.FirstOrDefault(t=>t.kind==source.kind&&t.order==source.order);
     if(target==null){target=new Track{id=Store.Id(),name=source.name,kind=source.kind,order=source.order};project.Active.tracks.Add(target);}
     if(target.locked)throw new Exception("Pista de destino bloqueada: "+target.name);
     trackMap[clip.track_id]=destination=target.id;
    }
    ids[clip.id]=Store.Id();clip.id=ids[clip.id];clip.track_id=destination;clip.timeline_start=Rational.Time(clip.timeline_start.Seconds+delta);
    ClipIdentity.NewEffects(clip);
    var originalAsset=project.assets.Find(a=>a.id==clip.asset_id);
    if(originalAsset!=null&&GeneratedMedia.IsText(originalAsset)){
     var asset=Store.Json().Deserialize<Asset>(Store.Json().Serialize(Store.Fields(originalAsset)));asset.id=Store.Id();asset.source.uri="generated:"+asset.id;asset.source.imported_at=DateTime.UtcNow.ToString("o");
     foreach(var stream in asset.streams){string old=stream.id;stream.id=Store.Id();if(clip.stream_id==old)clip.stream_id=stream.id;}
     project.assets.Add(asset);clip.asset_id=asset.id;
    }
    project.Active.clips.Add(clip);
   }
   foreach(var encoded in links??Enumerable.Empty<string>())project.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=encoded.Split('|').Select(id=>ids[id]).ToList()});
   foreach(var encoded in groups??Enumerable.Empty<string>())project.Active.edit_groups.Add(new EditGroup{id=Store.Id(),number=project.Active.edit_groups.Select(g=>g.number).DefaultIfEmpty(0).Max()+1,clip_ids=encoded.Split('|').Select(id=>ids[id]).ToList()});
   Editor.Overwrite(project,ids.Values);return ids.Values.ToList();
  }
 }
}
