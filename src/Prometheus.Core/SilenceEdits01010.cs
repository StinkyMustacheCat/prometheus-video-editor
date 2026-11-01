using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 public partial class Editor {
  static double TimeAfterSilences(double at,List<double[]> cuts){
   double removed=0;
   foreach(var cut in cuts){if(at>=cut[1]-0.000001)removed+=cut[1]-cut[0];else if(at>cut[0])return cut[0]-removed;else break;}
   return Math.Max(0,at-removed);
  }
  // Bounds are sequence times. A batch is committed once so Undo restores the whole cut.
  public void RemoveSilences(string clipId,IEnumerable<double[]> bounds,bool ripple){
   var anchor=Project.Active.clips.Find(c=>c.id==clipId);
   if(anchor==null)throw new Exception("Selecciona un clip con audio");
   var asset=Project.assets.Find(a=>a.id==anchor.asset_id);
   var stream=asset==null?null:asset.streams.Find(s=>s.id==anchor.stream_id);
   if(stream==null||stream.kind!="audio")throw new Exception("Selecciona el audio que se analizó");
   var cuts=(bounds??Enumerable.Empty<double[]>()).Where(x=>x!=null&&x.Length>=2)
    .Select(x=>new[]{Rational.Frame(x[0]).Seconds,Rational.Frame(x[1]).Seconds})
    .Where(x=>x[1]-x[0]>=1.0/60-0.000001).OrderBy(x=>x[0]).ToList();
   if(cuts.Count==0)throw new Exception("Selecciona al menos un silencio");
   for(int i=0;i<cuts.Count;i++){
    if(cuts[i][0]<anchor.timeline_start.Seconds-0.000001||cuts[i][1]>anchor.End+0.000001||
      i>0&&cuts[i][0]<cuts[i-1][1]-0.000001)throw new Exception("Los silencios seleccionados no pertenecen al clip o se solapan");
   }
   var related=new HashSet<string>(Related(clipId));
   foreach(var clip in Project.Active.clips.Where(c=>ripple||related.Contains(c.id))){
    bool affected=cuts.Any(cut=>clip.End>cut[0]+0.000001&&clip.timeline_start.Seconds<cut[1]-0.000001)||
      ripple&&clip.timeline_start.Seconds>=cuts[0][1]-0.000001;
    if(affected&&Project.Active.tracks.Find(t=>t.id==clip.track_id).locked)
     throw new Exception("Una pista bloqueada contiene material que tendría que cambiar");
   }
   Commit(p=>{
    var fragments=new Dictionary<string,List<Clip>>();
    foreach(var original in p.Active.clips.ToList()){
     if(!ripple&&!related.Contains(original.id))continue;
     double from=original.timeline_start.Seconds,to=original.End,cursor=from;
     var kept=new List<double[]>();
     foreach(var cut in cuts){
      if(cut[1]<=cursor+0.000001||cut[0]>=to-0.000001)continue;
      if(cut[0]>cursor+0.000001)kept.Add(new[]{cursor,Math.Min(to,cut[0])});
      cursor=Math.Max(cursor,Math.Min(to,cut[1]));
     }
     if(cursor<to-0.000001)kept.Add(new[]{cursor,to});
     if(kept.Count==1&&Math.Abs(kept[0][0]-from)<0.000001&&Math.Abs(kept[0][1]-to)<0.000001&&!ripple)continue;
     p.Active.clips.Remove(original);
     var made=new List<Clip>();
     foreach(var range in kept){
      var piece=Store.Json().Deserialize<Clip>(Store.Json().Serialize(Store.Fields(original)));
      piece.id=made.Count==0?original.id:Store.Id();
      if(made.Count>0)ClipIdentity.NewEffects(piece);
      double shifted=ripple?cuts.Where(cut=>cut[1]<=range[0]+0.000001).Sum(cut=>cut[1]-cut[0]):0;
      piece.timeline_start=Rational.Time(range[0]-shifted);
      piece.duration=Rational.Time(range[1]-range[0]);
      piece.source_in=Rational.Time(original.source_in.Seconds+(range[0]-from)*original.speed.Seconds);
      p.Active.clips.Add(piece);made.Add(piece);
     }
     fragments[original.id]=made;
    }
    if(ripple){
     foreach(var marker in Markers.Read(p.Active))Markers.Move(p.Active,marker.Id,TimeAfterSilences(marker.Seconds,cuts));
     var marks=SequenceExportRange.Read(p.Active);if(marks.InFrame.HasValue||marks.OutFrame.HasValue){
      double? markIn=marks.InFrame.HasValue?(double?)TimeAfterSilences(marks.InFrame.Value/60.0,cuts):null;
      double? markOut=marks.OutFrame.HasValue?(double?)TimeAfterSilences(marks.OutFrame.Value/60.0,cuts):null;
      SequenceExportRange.Clear(p.Active);
      if(markIn.HasValue)SequenceExportRange.SetIn(p.Active,markIn.Value);
      if(markOut.HasValue&&(!markIn.HasValue||markOut.Value>markIn.Value+0.000001))SequenceExportRange.SetOut(p.Active,markOut.Value);
     }
    }
    foreach(var group in p.Active.link_groups.ToList()){
     if(!group.clip_ids.Any(fragments.ContainsKey))continue;
     p.Active.link_groups.Remove(group);
     var members=group.clip_ids.Select(id=>fragments.ContainsKey(id)?fragments[id]:p.Active.clips.Where(c=>c.id==id).ToList()).ToList();
     int count=members.Count==0?0:members.Max(x=>x.Count);
     for(int index=0;index<count;index++){
      var ids=members.Where(x=>index<x.Count).Select(x=>x[index].id).Distinct().ToList();
      if(ids.Count>=2)p.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=ids});
     }
    }
    foreach(var group in p.Active.edit_groups){
     var members=new List<string>();
     foreach(string id in group.clip_ids){List<Clip> made;if(fragments.TryGetValue(id,out made))members.AddRange(made.Select(c=>c.id));else members.Add(id);}
     group.clip_ids=members.Distinct().ToList();
    }
    p.Active.edit_groups.RemoveAll(g=>g.clip_ids.Count<2);
   });
  }
 }
}
