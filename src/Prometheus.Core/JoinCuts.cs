using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 public partial class Editor {
  static string EffectSignature(Clip clip){return Store.Json().Serialize(clip.effects.Select(e=>new{e.effect_id,e.contract_version,e.enabled,e.parameters}).ToList());}
  static bool SameEdit(Clip left,Clip right){
   if(left==null||right==null||left.track_id!=right.track_id||left.asset_id!=right.asset_id||left.stream_id!=right.stream_id)return false;
   if(Math.Abs(left.End-right.timeline_start.Seconds)>0.00001)return false;
   if(Math.Abs(left.source_in.Seconds+left.duration.Seconds*left.speed.Seconds-right.source_in.Seconds)>0.00001)return false;
   if(Math.Abs(left.speed.Seconds-right.speed.Seconds)>0.000001||left.enabled!=right.enabled||left.inherit_track_effects!=right.inherit_track_effects)return false;
   if(Math.Abs(left.gain_db-right.gain_db)>0.000001||Math.Abs(left.balance-right.balance)>0.000001||left.name!=right.name)return false;
   return EffectSignature(left)==EffectSignature(right)
    &&Store.Json().Serialize(left.extensions)==Store.Json().Serialize(right.extensions);
  }
  List<Tuple<Clip,Clip>> JoinPairs(string leftId,string rightId){
   var sequence=Project.Active;
   var left=sequence.clips.Find(c=>c.id==leftId);var right=sequence.clips.Find(c=>c.id==rightId);
   if(!SameEdit(left,right))return null;
   var leftGroup=sequence.link_groups.FirstOrDefault(g=>g.clip_ids.Contains(leftId));
   var rightGroup=sequence.link_groups.FirstOrDefault(g=>g.clip_ids.Contains(rightId));
   if((leftGroup==null)!=(rightGroup==null))return null;
   var pairs=new List<Tuple<Clip,Clip>>{Tuple.Create(left,right)};
   if(leftGroup!=null){
    if(leftGroup.id==rightGroup.id||leftGroup.clip_ids.Count!=rightGroup.clip_ids.Count)return null;
    foreach(var memberId in leftGroup.clip_ids.Where(id=>id!=leftId)){
     var a=sequence.clips.Find(c=>c.id==memberId);
     var b=rightGroup.clip_ids.Select(id=>sequence.clips.Find(c=>c.id==id)).FirstOrDefault(c=>c!=null&&a!=null&&c.track_id==a.track_id);
     if(!SameEdit(a,b))return null;
     pairs.Add(Tuple.Create(a,b));
    }
   }
   foreach(var pair in pairs){
    var track=sequence.tracks.Find(t=>t.id==pair.Item1.track_id);
    if(track==null||track.locked)return null;
    // A transition beyond the right-hand clip would lose its owner when the clips merge.
    if(sequence.transitions.Any(t=>t.left_clip_id==pair.Item2.id||t.right_clip_id==pair.Item2.id&&t.left_clip_id!=pair.Item1.id))return null;
   }
   return pairs;
  }
  public bool CanJoinCut(string leftId,string rightId){return JoinPairs(leftId,rightId)!=null;}
  public void JoinCut(string leftId,string rightId){
   var pairs=JoinPairs(leftId,rightId);
   if(pairs==null)throw new Exception("Estos clips no se pueden unir: deben ser partes contiguas del mismo medio y tener los mismos ajustes.");
   var lefts=pairs.Select(pair=>pair.Item1.id).ToList();var rights=pairs.Select(pair=>pair.Item2.id).ToList();
   Commit(project=>{
    var sequence=project.Active;
    sequence.transitions.RemoveAll(t=>lefts.Contains(t.left_clip_id)&&rights.Contains(t.right_clip_id));
    sequence.link_groups.RemoveAll(group=>group.clip_ids.Any(id=>lefts.Contains(id)||rights.Contains(id)));
    foreach(var pair in pairs){
     var left=sequence.clips.Find(c=>c.id==pair.Item1.id);var right=sequence.clips.Find(c=>c.id==pair.Item2.id);
     left.duration=Rational.Time(left.duration.Seconds+right.duration.Seconds);
     sequence.clips.Remove(right);
    }
    if(lefts.Count>1)sequence.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=lefts});
   });
  }
 }
}
