using System;
using System.Linq;

namespace Prometheus {
 public partial class MainForm {
  readonly ColorPanelView colorPanel=new ColorPanelView();
  void SetupColor(){
   colorPanel.ValueChanged=(clip,effect,parameter,value)=>{try{double time=timeline.Cursor;Edit(()=>editor.Commit(p=>ApplyColor(p,clip,effect,parameter,value,time)));}catch(Exception ex){Error(ex);}};
   colorPanel.ValuePreview=(clip,effect,parameter,value)=>{double time=timeline.Cursor;PreviewProject(p=>ApplyColor(p,clip,effect,parameter,value,time));};
  }
  static void ApplyColor(Project project,string clipId,string effectId,string parameter,double value,double time){
   var clip=project.Active.clips.Find(c=>c.id==clipId);if(clip==null)throw new Exception("Clip no disponible");
   var track=project.Active.tracks.Find(t=>t.id==clip.track_id);if(track==null||track.kind!="video"||track.locked)throw new Exception("Selecciona un clip visual desbloqueado");
   var effect=clip.effects.FirstOrDefault(e=>e.effect_id==effectId);
   if(effect==null){effect=EffectContracts.New(effectId);clip.effects.Add(effect);}
   var target="effect:"+effect.id+":"+parameter;
   if(ClipKeyframes.Active(clip,target)){CheckAnimationPosition(clip,time);SetAnimatedValue(clip,target,time,value);}
   else effect.parameters[parameter].number=value;
  }
 }
}
