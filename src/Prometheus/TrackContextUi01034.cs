using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  void SelectTrackClips01034(string trackId){
   var ids=editor.Project.Active.clips.Where(c=>c.track_id==trackId).Select(c=>c.id).ToList();
   timeline.Selection=ids;timeline.Selected=ids.FirstOrDefault();timeline.ActiveTrack=trackId;
   timeline.Invalidate();ShowClip();status.Text=ids.Count+" clip(s) seleccionados en la pista";
  }
  void DeleteTrack01034(string trackId){try{
   var sequence=editor.Project.Active;var track=sequence.tracks.Find(t=>t.id==trackId);
   if(track==null||sequence.tracks.Count(t=>t.kind==track.kind)<2)throw new Exception("Debe quedar al menos una pista de este tipo");
   int count=sequence.clips.Count(c=>c.track_id==trackId);
   if(count>0&&MessageBox.Show(this,"La pista contiene "+count+" medio(s). Se eliminarán junto con la pista. Puedes deshacerlo con Ctrl+Z.","Eliminar pista",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning)!=DialogResult.OK)return;
   Edit(()=>editor.Commit(p=>{
    var target=p.Active.tracks.Find(t=>t.id==trackId);if(target==null)throw new Exception("Pista no disponible");
    var removed=new HashSet<string>(p.Active.clips.Where(c=>c.track_id==trackId).Select(c=>c.id));
    p.Active.clips.RemoveAll(c=>removed.Contains(c.id));p.Active.tracks.Remove(target);
    p.Active.transitions.RemoveAll(t=>removed.Contains(t.left_clip_id)||removed.Contains(t.right_clip_id));
    foreach(var group in p.Active.link_groups)group.clip_ids.RemoveAll(removed.Contains);
    p.Active.link_groups.RemoveAll(g=>g.clip_ids.Count<2);
    foreach(var group in p.Active.edit_groups)group.clip_ids.RemoveAll(removed.Contains);
    p.Active.edit_groups.RemoveAll(g=>g.clip_ids.Count<2);
    int order=0;foreach(var lane in p.Active.tracks.Where(t=>t.kind==target.kind).OrderBy(t=>t.order))lane.order=order++;
   }));
   timeline.Selection.Clear();timeline.Selected=null;timeline.ActiveTrack=editor.Project.Active.tracks.First(t=>t.kind==track.kind).id;
   timeline.Invalidate();ShowClip();status.Text="Pista eliminada · Ctrl+Z para deshacer";
  }catch(Exception ex){Error(ex);}}
 }
}
