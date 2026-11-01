using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 public partial class Editor {
  List<Tuple<Clip,Clip>> RollPairs(string leftId,string rightId,bool includeLinked){
   var clips=Project.Active.clips;
   var left=clips.Find(c=>c.id==leftId);
   var right=clips.Find(c=>c.id==rightId);
   if(left==null||right==null||left.track_id!=right.track_id||Math.Abs(left.End-right.timeline_start.Seconds)>0.000001)throw new Exception("Selecciona una unión válida");
   var pairs=new List<Tuple<Clip,Clip>>{Tuple.Create(left,right)};
   if(!includeLinked)return pairs;
   var leftGroup=Related(leftId).Select(id=>clips.Find(c=>c.id==id)).ToList();
   var rightGroup=Related(rightId).Select(id=>clips.Find(c=>c.id==id)).ToList();
   if(leftGroup.Any(c=>c==null)||rightGroup.Any(c=>c==null)||leftGroup.GroupBy(c=>c.track_id).Any(g=>g.Count()!=1)||rightGroup.GroupBy(c=>c.track_id).Any(g=>g.Count()!=1))throw new Exception("El vínculo no permite este recorte conjunto");
   var leftTracks=new HashSet<string>(leftGroup.Select(c=>c.track_id));
   var rightTracks=new HashSet<string>(rightGroup.Select(c=>c.track_id));
   if(!leftTracks.SetEquals(rightTracks))throw new Exception("El audio y el vídeo vinculados no tienen un corte equivalente. Mantén Alt para ajustar solo esta pista.");
   foreach(var linkedLeft in leftGroup.Where(c=>c.id!=leftId)){
    var linkedRight=rightGroup.Single(c=>c.track_id==linkedLeft.track_id);
    if(Math.Abs(linkedLeft.End-left.End)>0.000001||Math.Abs(linkedRight.timeline_start.Seconds-right.timeline_start.Seconds)>0.000001||Math.Abs(linkedLeft.End-linkedRight.timeline_start.Seconds)>0.000001)throw new Exception("Los componentes vinculados no comparten el punto de corte. Mantén Alt para ajustar solo esta pista.");
    pairs.Add(Tuple.Create(linkedLeft,linkedRight));
   }
   var leftEdit=Project.Active.edit_groups.Find(g=>g.clip_ids.Contains(leftId));
   var rightEdit=Project.Active.edit_groups.Find(g=>g.clip_ids.Contains(rightId));
   if(leftEdit!=null&&rightEdit!=null){
    double seam=left.End;
    var editLeft=clips.Where(c=>leftEdit.clip_ids.Contains(c.id)&&Math.Abs(c.End-seam)<0.000001).ToList();
    var editRight=clips.Where(c=>rightEdit.clip_ids.Contains(c.id)&&Math.Abs(c.timeline_start.Seconds-seam)<0.000001).ToList();
    foreach(var linkedLeft in editLeft){
     if(pairs.Any(pair=>pair.Item1.id==linkedLeft.id))continue;
     var linkedRight=editRight.FirstOrDefault(c=>c.track_id==linkedLeft.track_id);
     if(linkedRight!=null)pairs.Add(Tuple.Create(linkedLeft,linkedRight));
    }
    if(editRight.Any(c=>!pairs.Any(pair=>pair.Item2.id==c.id)))throw new Exception("El grupo no tiene una unión equivalente en todas sus pistas. Mantén Alt para ajustar solo esta pista.");
   }
   return pairs;
  }

  public double LimitRoll(string leftId,string rightId,double cut,bool includeLinked){
   var pairs=RollPairs(leftId,rightId,includeLinked);
   double low=Double.NegativeInfinity,high=Double.PositiveInfinity;
   bool video=false;
   foreach(var pair in pairs){
    var left=pair.Item1;var right=pair.Item2;
    var track=Project.Active.tracks.Find(t=>t.id==left.track_id);
    if(track==null||track.locked)throw new Exception("Una pista vinculada está bloqueada");
    var leftAsset=Project.assets.Find(a=>a.id==left.asset_id);
    var rightAsset=Project.assets.Find(a=>a.id==right.asset_id);
    var leftStream=leftAsset==null?null:leftAsset.streams.Find(s=>s.id==left.stream_id);
    var rightStream=rightAsset==null?null:rightAsset.streams.Find(s=>s.id==right.stream_id);
    if(leftStream==null||rightStream==null)throw new Exception("Falta un medio del punto de corte");
    double grid=track.kind=="audio"?48000:60,min=1/grid;
    if(track.kind=="video")video=true;
    low=Math.Max(low,Math.Max(min-left.duration.Seconds,rightStream.kind=="image"?Double.NegativeInfinity:-right.source_in.Seconds/right.speed.Seconds));
    high=Math.Min(high,right.duration.Seconds-min);
    if(leftStream.duration!=null)high=Math.Min(high,(leftStream.duration.Seconds-left.source_in.Seconds-left.duration.Seconds*left.speed.Seconds)/left.speed.Seconds);
   }
   double commonGrid=video?60:48000;
   low=Math.Ceiling(low*commonGrid-0.000001)/commonGrid;
   high=Math.Floor(high*commonGrid+0.000001)/commonGrid;
   if(high<low)throw new Exception("No hay contenido disponible para mover este corte en todos los medios vinculados");
   double delta=Math.Round((cut-pairs[0].Item1.End)*commonGrid)/commonGrid;
   return pairs[0].Item1.End+Math.Max(low,Math.Min(high,delta));
  }

  public void RollEditLinked(string leftId,string rightId,double cut,bool includeLinked){
   var pairs=RollPairs(leftId,rightId,includeLinked);
   double delta=LimitRoll(leftId,rightId,cut,includeLinked)-pairs[0].Item1.End;
   if(Math.Abs(delta)<0.000001)return;
   var ids=pairs.Select(pair=>Tuple.Create(pair.Item1.id,pair.Item2.id)).ToList();
   Commit(p=>{
    foreach(var pair in ids){
     var left=p.Active.clips.Find(c=>c.id==pair.Item1);
     var right=p.Active.clips.Find(c=>c.id==pair.Item2);
     if(p.Active.tracks.Find(t=>t.id==left.track_id).locked)throw new Exception("Una pista vinculada está bloqueada");
     double next=left.End+delta;
     left.duration=Rational.Time(left.duration.Seconds+delta);
     right.timeline_start=Rational.Time(next);
     var rightStream=p.assets.Find(a=>a.id==right.asset_id).streams.Find(s=>s.id==right.stream_id);
     if(rightStream.kind!="image")right.source_in=Rational.Time(right.source_in.Seconds+delta*right.speed.Seconds);
     right.duration=Rational.Time(right.duration.Seconds-delta);
    }
   });
  }
 }
}
