using System;
using System.Linq;

namespace Prometheus {
 public partial class MainForm {
  void SetBlendMode01034(string clipId,string mode){
   try{
    var selected=VisualSelection(clipId);
    if(selected.Count==0)selected.Add(clipId);
    Edit(()=>editor.Commit(project=>{
     foreach(string id in selected){var clip=project.Active.clips.Find(c=>c.id==id);if(clip==null)continue;
      var track=project.Active.tracks.Find(t=>t.id==clip.track_id);
      if(track==null||track.kind!="video"||track.locked)throw new Exception("Selecciona medios visuales en pistas desbloqueadas");
      BlendModes01034.Set(clip,mode);
     }
    }));
    status.Text="Modo de fusión aplicado a "+selected.Count+" clip(s)";
   }catch(Exception ex){Error(ex);ShowClip();}
  }
 }
}
