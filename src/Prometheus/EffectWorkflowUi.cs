using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  void SelectEffectTrack(string id){var track=editor.Project.Active.tracks.Find(t=>t.id==id);if(track==null)return;timeline.ActiveTrack=id;timeline.Selected=null;timeline.Selection.Clear();selectedEffectTrackId=id;timeline.Invalidate();ShowClip();}
  void ManageTrackEffects(string id){SelectEffectTrack(id);var entry=workspace.Entries.FirstOrDefault(p=>p.Id=="propiedades");if(entry!=null&&entry.State!="docked")workspace.FloatPanel("propiedades");effectControlsView.Focus();}
  void ToggleTrackEffects(string id){var track=editor.Project.Active.tracks.Find(t=>t.id==id);if(track!=null&&track.effects.Count>0)SetTrackEffectsEnabled(id,!track.effects_enabled);}
  void SetTrackEffectsEnabled(string id,bool enabled){try{Edit(()=>editor.Commit(p=>{var t=p.Active.tracks.Find(x=>x.id==id);if(t==null||t.locked)throw new Exception("Pista no disponible o bloqueada");t.effects_enabled=enabled;}));status.Text=enabled?"Fx de pista activo":"Fx de pista desactivado en monitor y exportación";}catch(Exception ex){Error(ex);ShowClip();}}
  void SetClipTrackEffectsInheritance(string id,bool enabled){try{var ids=timeline.Selection.Contains(id)?timeline.Selection.ToList():new List<string>{id};Edit(()=>editor.Commit(p=>{foreach(string cid in ids){var c=p.Active.clips.Find(x=>x.id==cid);if(c==null)continue;if(p.Active.tracks.Find(t=>t.id==c.track_id).locked)throw new Exception("Hay una pista bloqueada");c.inherit_track_effects=enabled;}}));}catch(Exception ex){Error(ex);ShowClip();}}
  string PickEffectLut(){string examples=Path.Combine(Tools.Root,"assets","luts");using(var d=new OpenFileDialog{Title="Elegir LUT 3D",Filter="LUT 3D (*.cube)|*.cube",CheckFileExists=true,InitialDirectory=Directory.Exists(examples)?examples:null}){if(d.ShowDialog(this)!=DialogResult.OK)return null;FFmpegEffectAdapter.VerifyLutFile(d.FileName);return Path.GetFullPath(d.FileName);}}
  void ApplyCatalogEffectToSelection(string effectId){try{var contract=EffectContracts.All.FirstOrDefault(c=>c.id==effectId);if(contract==null)throw new Exception("Efecto no disponible");if(timeline.Selection.Count>0){var ids=timeline.Selection.Where(id=>editor.Project.Active.clips.Any(c=>c.id==id&&editor.Project.Active.tracks.Any(t=>t.id==c.track_id&&t.kind==contract.input_kind))).Distinct().ToList();if(ids.Count==0&&contract.input_kind=="audio")ids=editor.Project.Active.link_groups.Where(g=>g.clip_ids.Any(timeline.Selection.Contains)).SelectMany(g=>g.clip_ids).Where(id=>editor.Project.Active.clips.Any(c=>c.id==id&&editor.Project.Active.tracks.Any(t=>t.id==c.track_id&&t.kind=="audio"))).Distinct().ToList();if(ids.Count>0){ApplyCatalogEffect(effectId,ids,null);return;}}if(selectedEffectTrackId!=null){ApplyCatalogEffect(effectId,null,selectedEffectTrackId);return;}if(timeline.Selected!=null){string target=timeline.Selected;if(contract.input_kind=="audio"){var group=editor.Project.Active.link_groups.FirstOrDefault(g=>g.clip_ids.Contains(target));if(group!=null)target=group.clip_ids.FirstOrDefault(id=>editor.Project.Active.clips.Any(c=>c.id==id&&editor.Project.Active.tracks.Any(t=>t.id==c.track_id&&t.kind=="audio")))??target;}ApplyCatalogEffect(effectId,new List<string>{target},null);return;}status.Text="Selecciona un clip o pista compatible para aplicar el efecto";}catch(Exception ex){Error(ex);}}
  void ApplyCatalogEffectToClip(string effectId,string clipId){try{var contract=EffectContracts.All.FirstOrDefault(c=>c.id==effectId);var ids=timeline.Selection.Contains(clipId)&&contract!=null?timeline.Selection.Where(id=>editor.Project.Active.clips.Any(c=>c.id==id&&editor.Project.Active.tracks.Any(t=>t.id==c.track_id&&t.kind==contract.input_kind))).Distinct().ToList():new List<string>{clipId};ApplyCatalogEffect(effectId,ids.Count>0?ids:new List<string>{clipId},null);}catch(Exception ex){Error(ex);}}
  void ApplyCatalogEffectToTrack(string effectId,string trackId){try{ApplyCatalogEffect(effectId,null,trackId);}catch(Exception ex){Error(ex);}}
  void ApplyCatalogEffectToControls(bool onTrack,string ownerId,string effectId,int? insertionSlot){
   try{
    if(onTrack){ApplyCatalogEffect(effectId,null,ownerId,insertionSlot);return;}
    var contract=EffectContracts.All.FirstOrDefault(c=>c.id==effectId);
    if(contract==null)throw new Exception("Efecto no disponible");
    var ids=new List<string>{ownerId};
    foreach(string selected in timeline.Selection){
     var clip=editor.Project.Active.clips.Find(c=>c.id==selected);
     if(clip==null)continue;
     var track=editor.Project.Active.tracks.Find(t=>t.id==clip.track_id);
     if(track!=null&&track.kind==contract.input_kind){if(!ids.Contains(clip.id))ids.Add(clip.id);continue;}
     if(contract.input_kind!="audio")continue;
     var group=editor.Project.Active.link_groups.FirstOrDefault(g=>g.clip_ids.Contains(clip.id));
     if(group==null)continue;
     foreach(string related in group.clip_ids){
      var linked=editor.Project.Active.clips.Find(c=>c.id==related);
      if(linked!=null&&editor.Project.Active.tracks.Any(t=>t.id==linked.track_id&&t.kind=="audio")&&!ids.Contains(related))ids.Add(related);
     }
    }
    ApplyCatalogEffect(effectId,ids,null,insertionSlot);
   }catch(Exception ex){Error(ex);}
  }
  void ApplyCatalogEffect(string effectId,List<string> clips,string trackId,int? insertionSlot=null){
   var contract=EffectContracts.All.FirstOrDefault(c=>c.id==effectId);if(contract==null)throw new Exception("Efecto no disponible");
   if(trackId==null&&(clips==null||clips.Count==0))throw new Exception("Selecciona clips compatibles");
   string lut=effectId==EffectContracts.Lut3DId?PickEffectLut():null;if(effectId==EffectContracts.Lut3DId&&lut==null)return;
   Edit(()=>editor.Commit(p=>{EffectResource resource=null;if(lut!=null){resource=p.effect_resources.FirstOrDefault(r=>r.kind=="lut3d"&&String.Equals(r.uri,lut,StringComparison.OrdinalIgnoreCase));if(resource==null){resource=new EffectResource{id=Store.Id(),kind="lut3d",uri=lut};p.effect_resources.Add(resource);}}
    if(trackId!=null){var t=p.Active.tracks.Find(x=>x.id==trackId);if(t==null||t.locked||t.kind!=contract.input_kind)throw new Exception("Pista incompatible o bloqueada");t.effects.Insert(insertionSlot.HasValue?Math.Max(0,Math.Min(t.effects.Count,insertionSlot.Value)):t.effects.Count,resource==null?EffectContracts.New(effectId):EffectContracts.NewLut3D(resource.id));}
    else foreach(string id in clips){var c=p.Active.clips.Find(x=>x.id==id);var t=c==null?null:p.Active.tracks.Find(x=>x.id==c.track_id);if(t==null||t.locked||t.kind!=contract.input_kind)throw new Exception("Clip incompatible o bloqueado");c.effects.Insert(insertionSlot.HasValue?Math.Max(0,Math.Min(c.effects.Count,insertionSlot.Value)):c.effects.Count,resource==null?EffectContracts.New(effectId):EffectContracts.NewLut3D(resource.id));}
   }));
   if(trackId!=null){SelectEffectTrack(trackId);status.Text=editor.Project.Active.tracks.Find(t=>t.id==trackId).effects_enabled?"Efecto añadido a la pista":"Efecto añadido · Fx de pista sigue desactivado";}
   else{selectedEffectTrackId=null;timeline.Selected=clips[0];timeline.Selection=clips.ToList();ShowClip();status.Text="Efecto añadido a "+clips.Count+" clip(s)";}
  }
  List<EffectInstance> EffectsOf(Project p,bool onTrack,string id){if(onTrack){var t=p.Active.tracks.Find(x=>x.id==id);if(t==null||t.locked)throw new Exception("Pista no disponible o bloqueada");return t.effects;}var c=p.Active.clips.Find(x=>x.id==id);if(c==null||p.Active.tracks.Find(t=>t.id==c.track_id).locked)throw new Exception("Clip no disponible o bloqueado");return c.effects;}
  List<Tuple<List<EffectInstance>,EffectInstance>> MatchingEffects(Project p,bool onTrack,string ownerId,string effectId){var anchor=EffectsOf(p,onTrack,ownerId);var current=anchor.FirstOrDefault(e=>e.id==effectId);if(current==null)throw new Exception("Efecto no disponible");if(onTrack)return new List<Tuple<List<EffectInstance>,EffectInstance>>{Tuple.Create(anchor,current)};int ordinal=anchor.TakeWhile(e=>e.id!=effectId).Count(e=>e.effect_id==current.effect_id);var ids=timeline.Selection.Contains(ownerId)?timeline.Selection.Distinct().ToList():new List<string>{ownerId};var result=new List<Tuple<List<EffectInstance>,EffectInstance>>();foreach(string id in ids){var list=EffectsOf(p,false,id);var match=list.Where(e=>e.effect_id==current.effect_id).Skip(ordinal).FirstOrDefault();if(match!=null)result.Add(Tuple.Create(list,match));}return result;}
  void SetEffectEnabled(bool onTrack,string ownerId,string effectId,bool enabled){try{Edit(()=>editor.Commit(p=>{foreach(var pair in MatchingEffects(p,onTrack,ownerId,effectId))pair.Item2.enabled=enabled;}));}catch(Exception ex){Error(ex);ShowClip();}}
  void RemoveEffectFromTarget(bool onTrack,string ownerId,string effectId){try{Edit(()=>editor.Commit(p=>{foreach(var pair in MatchingEffects(p,onTrack,ownerId,effectId)){var clip=onTrack?null:p.Active.clips.FirstOrDefault(c=>Object.ReferenceEquals(c.effects,pair.Item1));if(clip!=null&&TemporalMasks0823.Target(clip)=="effect:"+pair.Item2.id){var sam2=Sam2MaskStore090.Get(clip);TemporalMasks0823.SetTarget(clip,"unassigned",sam2!=null&&sam2.enabled);}pair.Item1.Remove(pair.Item2);}CleanEffectResources(p);}));}catch(Exception ex){Error(ex);ShowClip();}}
  void MoveEffect(bool onTrack,string ownerId,string effectId,int direction){try{Edit(()=>editor.Commit(p=>{foreach(var pair in MatchingEffects(p,onTrack,ownerId,effectId)){int i=pair.Item1.IndexOf(pair.Item2),j=i+direction;if(j<0||j>=pair.Item1.Count)continue;pair.Item1.RemoveAt(i);pair.Item1.Insert(j,pair.Item2);}}));}catch(Exception ex){Error(ex);ShowClip();}}
  void ReorderEffect(bool onTrack,string ownerId,string effectId,int insertionSlot){try{var list=EffectsOf(editor.Project,onTrack,ownerId);int source=list.FindIndex(e=>e.id==effectId);int target=Math.Max(0,Math.Min(list.Count-1,insertionSlot-(source<insertionSlot?1:0)));if(source<0||source==target)return;Edit(()=>editor.Commit(p=>{foreach(var pair in MatchingEffects(p,onTrack,ownerId,effectId)){int index=pair.Item1.IndexOf(pair.Item2);if(index<0)continue;int destination=Math.Max(0,Math.Min(pair.Item1.Count-1,insertionSlot-(index<insertionSlot?1:0)));pair.Item1.RemoveAt(index);pair.Item1.Insert(destination,pair.Item2);}}));}catch(Exception ex){Error(ex);ShowClip();}}
  void ResetEffectParameters(bool onTrack,string ownerId,string effectId){try{Edit(()=>editor.Commit(p=>{foreach(var pair in MatchingEffects(p,onTrack,ownerId,effectId)){var contract=EffectContracts.Find(pair.Item2.effect_id,pair.Item2.contract_version);if(contract==null)throw new Exception("Contrato de efecto no disponible");var clip=onTrack?null:p.Active.clips.Find(c=>c.id==ownerId);foreach(var spec in contract.parameters){if(spec.kind=="resource"||spec.initial==null)continue;string target="effect:"+pair.Item2.id+":"+spec.id;if(clip!=null&&spec.initial.number.HasValue&&ClipKeyframes.Active(clip,target)){CheckAnimationPosition(clip,timeline.Cursor);SetAnimatedValue(clip,target,timeline.Cursor,spec.initial.number.Value);}else pair.Item2.parameters[spec.id]=new EffectValue{kind=spec.initial.kind,number=spec.initial.number,text=spec.initial.text,boolean=spec.initial.boolean,resource_id=spec.initial.resource_id};}}}));}catch(Exception ex){Error(ex);ShowClip();}}
  void ResetEffectParameter(bool onTrack,string ownerId,string effectId,string key){try{Edit(()=>editor.Commit(p=>{foreach(var pair in MatchingEffects(p,onTrack,ownerId,effectId)){
   var contract=EffectContracts.Find(pair.Item2.effect_id,pair.Item2.contract_version);
   var spec=contract==null?null:contract.parameters.FirstOrDefault(item=>item.id==key);
   if(spec==null||spec.initial==null)throw new Exception("El parámetro no tiene un valor predeterminado");
   var clip=onTrack?null:p.Active.clips.Find(c=>c.id==ownerId);string target="effect:"+pair.Item2.id+":"+key;
   if(clip!=null&&spec.initial.number.HasValue&&ClipKeyframes.Active(clip,target)){CheckAnimationPosition(clip,timeline.Cursor);SetAnimatedValue(clip,target,timeline.Cursor,spec.initial.number.Value);}
   else pair.Item2.parameters[key]=new EffectValue{kind=spec.initial.kind,number=spec.initial.number,text=spec.initial.text,boolean=spec.initial.boolean,resource_id=spec.initial.resource_id};
  }}));}catch(Exception ex){Error(ex);ShowClip();}}
  void SetEffectText(bool onTrack,string ownerId,string effectId,string key,string value){try{Edit(()=>editor.Commit(p=>{foreach(var pair in MatchingEffects(p,onTrack,ownerId,effectId)){EffectValue parameter;if(!pair.Item2.parameters.TryGetValue(key,out parameter)||parameter.kind!="text")throw new Exception("Parámetro de texto no disponible");parameter.text=value;}}));}catch(Exception ex){Error(ex);ShowClip();}}
  void SetEffectNumber(bool onTrack,string ownerId,string effectId,string key,double value){try{if(busy)return;double time=timeline.Cursor;Edit(()=>editor.Commit(p=>{foreach(var pair in MatchingEffects(p,onTrack,ownerId,effectId)){
   if(!pair.Item2.parameters.ContainsKey(key)||pair.Item2.parameters[key].kind!="number")throw new Exception("Parámetro no numérico");
   var clip=onTrack?null:p.Active.clips.FirstOrDefault(c=>c.effects.Contains(pair.Item2));string target="effect:"+pair.Item2.id+":"+key;
   if(clip!=null&&ClipKeyframes.Active(clip,target)){CheckAnimationPosition(clip,time);SetAnimatedValue(clip,target,time,value);}else pair.Item2.parameters[key].number=value;
  }}));}catch(Exception ex){Error(ex);ShowClip();}}
  void PreviewEffectNumber(bool onTrack,string ownerId,string effectId,string key,double value){double time=timeline.Cursor;PreviewProject(p=>{foreach(var pair in MatchingEffects(p,onTrack,ownerId,effectId)){
   var clip=onTrack?null:p.Active.clips.FirstOrDefault(c=>c.effects.Contains(pair.Item2));string target="effect:"+pair.Item2.id+":"+key;
   if(clip!=null&&ClipKeyframes.Active(clip,target)){CheckAnimationPosition(clip,time);SetAnimatedValue(clip,target,time,value);}else if(pair.Item2.parameters.ContainsKey(key))pair.Item2.parameters[key].number=value;
  }});}
  void ChangeLutForTarget(bool onTrack,string ownerId,string effectId){try{string path=PickEffectLut();if(path==null)return;Edit(()=>editor.Commit(p=>{var effect=EffectsOf(p,onTrack,ownerId).FirstOrDefault(e=>e.id==effectId);if(effect==null||effect.effect_id!=EffectContracts.Lut3DId)throw new Exception("LUT no disponible");var resource=p.effect_resources.FirstOrDefault(r=>r.kind=="lut3d"&&String.Equals(r.uri,path,StringComparison.OrdinalIgnoreCase));if(resource==null){resource=new EffectResource{id=Store.Id(),kind="lut3d",uri=path};p.effect_resources.Add(resource);}effect.parameters["lut"].resource_id=resource.id;CleanEffectResources(p);}));}catch(Exception ex){Error(ex);}}
  static void CleanEffectResources(Project p){var used=new HashSet<string>(p.sequences.SelectMany(s=>s.clips.SelectMany(c=>c.effects).Concat(s.tracks.SelectMany(t=>t.effects))).SelectMany(e=>e.parameters.Values).Where(v=>v.kind=="resource").Select(v=>v.resource_id));p.effect_resources.RemoveAll(r=>!used.Contains(r.id));}
 }
}





