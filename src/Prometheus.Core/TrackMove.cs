using System;
using System.Linq;

namespace Prometheus {
 public partial class Editor {
  public void MoveTrackContents(string sourceId,string targetId){
   if(sourceId==targetId)throw new Exception("Elige otra pista de destino");
   var source=Project.Active.tracks.Find(t=>t.id==sourceId);
   var target=Project.Active.tracks.Find(t=>t.id==targetId);
   if(source==null||target==null||source.kind!=target.kind)throw new Exception("No hay una pista compatible en esa dirección");
   if(source.locked||target.locked)throw new Exception("La pista de origen o destino está bloqueada");
   var incoming=Project.Active.clips.Where(c=>c.track_id==sourceId).ToList();
   if(incoming.Count==0)return;
   var existing=Project.Active.clips.Where(c=>c.track_id==targetId).ToList();
   if(incoming.Any(a=>existing.Any(b=>a.timeline_start.Seconds<b.End-0.000001&&a.End>b.timeline_start.Seconds+0.000001)))throw new Exception("La pista de destino tiene medios en uno de los tramos ocupados");
   Commit(p=>{
    foreach(var clip in p.Active.clips.Where(c=>c.track_id==sourceId))clip.track_id=targetId;
   });
  }
 }
}
