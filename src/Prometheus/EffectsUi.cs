using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  List<string> VisualSelection(string anchor){
   var ids=timeline.Selection.Contains(anchor)?timeline.Selection.ToList():new List<string>{anchor};
   return ids.Distinct().Where(id=>{
    var clip=editor.Project.Active.clips.Find(c=>c.id==id);
    var track=clip==null?null:editor.Project.Active.tracks.Find(t=>t.id==clip.track_id);
    return track!=null&&track.kind=="video";
   }).ToList();
  }
  void ChooseLut(string clipId){
   string examples=Path.Combine(Tools.Root,"assets","luts");
   using(var dialog=new OpenFileDialog{Title="Elegir LUT 3D",Filter="LUT 3D (*.cube)|*.cube",CheckFileExists=true,InitialDirectory=Directory.Exists(examples)?examples:null}){
    if(dialog.ShowDialog(this)!=DialogResult.OK)return;
    try{ApplyLut(clipId,dialog.FileName);}catch(Exception ex){Error(ex);}
   }
  }
  void ApplyLut(string clipId,string path){
   FFmpegEffectAdapter.VerifyLutFile(path);
   path=Path.GetFullPath(path);
   var selected=VisualSelection(clipId);
   if(selected.Count==0)throw new Exception("Selecciona un clip visual para aplicar el LUT");
   Edit(()=>editor.Commit(project=>{
    var resource=project.effect_resources.FirstOrDefault(r=>r.kind=="lut3d"&&String.Equals(r.uri,path,StringComparison.OrdinalIgnoreCase));
    if(resource==null){resource=new EffectResource{id=Store.Id(),kind="lut3d",uri=path};project.effect_resources.Add(resource);}
    foreach(string id in selected){
     var clip=project.Active.clips.Find(c=>c.id==id);
     if(project.Active.tracks.Find(t=>t.id==clip.track_id).locked)throw new Exception("Hay clips seleccionados en una pista bloqueada");
     var current=clip.effects.FirstOrDefault(e=>e.effect_id==EffectContracts.Lut3DId);
     if(current==null)clip.effects.Add(EffectContracts.NewLut3D(resource.id));
     else{current.parameters["lut"]=new EffectValue{kind="resource",resource_id=resource.id};current.enabled=true;}
    }
   }));
   status.Text="LUT aplicado a "+selected.Count+" clip(s) visuales";
  }
  void RelinkLut(string resourceId){
   var resource=editor.Project.effect_resources.FirstOrDefault(r=>r.id==resourceId);
   if(resource==null)return;
   using(var dialog=new OpenFileDialog{Title="Localizar LUT",Filter="LUT 3D (*.cube)|*.cube",CheckFileExists=true}){
    if(dialog.ShowDialog(this)!=DialogResult.OK)return;
    try{ApplyLutRelink(resourceId,dialog.FileName);}catch(Exception ex){Error(ex);}
   }
  }
  void ApplyLutRelink(string resourceId,string file){
   FFmpegEffectAdapter.VerifyLutFile(file);
   string path=Path.GetFullPath(file);
   Edit(()=>editor.Commit(p=>{var target=p.effect_resources.Find(r=>r.id==resourceId);if(target==null)throw new Exception("El recurso LUT ya no existe");target.uri=path;target.relative=false;}));
   status.Text="LUT reconectado en todos los clips que lo utilizan";
  }
  void SetLutEnabled(string clipId,string effectId,bool enabled){
   try{
    var selected=VisualSelection(clipId);
    Edit(()=>editor.Commit(project=>{
     foreach(string id in selected){
      var clip=project.Active.clips.Find(c=>c.id==id);
      if(project.Active.tracks.Find(t=>t.id==clip.track_id).locked)throw new Exception("Hay clips seleccionados en una pista bloqueada");
      var effect=clip.effects.FirstOrDefault(e=>e.id==effectId)??clip.effects.FirstOrDefault(e=>e.effect_id==EffectContracts.Lut3DId);
      if(effect!=null&&effect.effect_id==EffectContracts.Lut3DId)effect.enabled=enabled;
     }
    }));
   }catch(Exception ex){Error(ex);ShowClip();}
  }
  void RemoveEffect(string clipId,string effectId){
   try{
    var anchor=editor.Project.Active.clips.Find(c=>c.id==clipId);
    var target=anchor==null?null:anchor.effects.FirstOrDefault(e=>e.id==effectId);
    if(target==null)return;
    bool lut=target.effect_id==EffectContracts.Lut3DId;
    var selected=lut?VisualSelection(clipId):new List<string>{clipId};
    Edit(()=>editor.Commit(project=>{
     foreach(string id in selected){
      var clip=project.Active.clips.Find(c=>c.id==id);
      if(project.Active.tracks.Find(t=>t.id==clip.track_id).locked)throw new Exception("Hay clips seleccionados en una pista bloqueada");
      clip.effects.RemoveAll(e=>e.id==effectId||lut&&e.effect_id==EffectContracts.Lut3DId);
     }
     CleanEffectResources(project);
    }));
    status.Text="Efecto quitado de "+selected.Count+" clip(s) visuales";
   }catch(Exception ex){Error(ex);ShowClip();}
  }
 }
}
