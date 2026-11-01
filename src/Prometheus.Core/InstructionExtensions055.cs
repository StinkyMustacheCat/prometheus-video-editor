using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Prometheus {
 public interface IInstructionMediaAdapter {
  void Apply(Editor editor,InstructionAction action,InstructionPlan plan);
 }
 public static partial class Instructions {
  sealed class ActionSpec {
   public readonly string Required;public readonly HashSet<string> Accepted;
   public ActionSpec(string required,string optional){Required=required;Accepted=new HashSet<string>(("op,"+required+","+optional).Split(new[]{','},StringSplitOptions.RemoveEmptyEntries));}
  }
  static readonly Dictionary<string,ActionSpec> actionSpecs=new Dictionary<string,ActionSpec>{
   {"clip.delete",new ActionSpec("clip_id","sequence_id,ripple")},
   {"clip.cut",new ActionSpec("clip_id,at_seconds","sequence_id,include_linked,result")},
   {"gap.close",new ActionSpec("track_id,at_seconds","sequence_id")},
   {"clip.range_trim",new ActionSpec("clip_id,at_seconds,finish_seconds","sequence_id")},
   {"range.trim",new ActionSpec("at_seconds,finish_seconds","sequence_id,clip_ids")},
   {"clip.slip",new ActionSpec("clip_id,source_in_seconds","sequence_id")},
   {"clip.roll",new ActionSpec("left_clip_id,right_clip_id,at_seconds","sequence_id,include_linked")},
   {"clip.link",new ActionSpec("clip_ids","sequence_id")},
   {"clip.unlink",new ActionSpec("clip_id","sequence_id")},
   {"effect.set_parameter",new ActionSpec("effect_id,parameter_id","sequence_id,clip_id,track_id,number,text,value,resource_path")},
   {"effect.set_enabled",new ActionSpec("effect_id,enabled","sequence_id,clip_id,track_id")},
   {"effect.move",new ActionSpec("effect_id,index","sequence_id,clip_id,track_id")},
   {"effect.remove_instance",new ActionSpec("effect_id","sequence_id,clip_id,track_id")},
   {"transition.set_duration",new ActionSpec("left_clip_id,right_clip_id,duration_frames","sequence_id")},
   {"sequence.create",new ActionSpec("name","width,height,fps,result")},
   {"sequence.rename",new ActionSpec("sequence_id,name","")},
   {"sequence.activate",new ActionSpec("sequence_id","")},
   {"track.create",new ActionSpec("source_kind","sequence_id,name,result")},
   {"track.rename",new ActionSpec("track_id,name","sequence_id")},
   {"track.set_locked",new ActionSpec("track_id,enabled","sequence_id")},
   {"media.import_url",new ActionSpec("url,source_kind","sequence_id,track_id,at_seconds,placement,source_in_seconds,duration_seconds,result")},
   {"media.redownload_web",new ActionSpec("scope","asset_id,sequence_id")},
   {"media.import_shelf",new ActionSpec("shelf_id","sequence_id,track_id,at_seconds,placement,result")},
   {"placeholder.fill_url",new ActionSpec("clip_id,url","sequence_id,source_kind,source_in_seconds,include_linked,result")}
   ,{"clip.paste_attributes",new ActionSpec("attributes,scope","sequence_id,clip_id,clip_ids,track_id")}
   ,{"clip.set_speed",new ActionSpec("clip_id,number","sequence_id")}
   ,{"clip.group",new ActionSpec("clip_ids","sequence_id")}
   ,{"clip.ungroup",new ActionSpec("clip_ids","sequence_id")}
   ,{"clip.set_transform",new ActionSpec("clip_id,parameter_id","sequence_id,number,value")}
   ,{"text.create",new ActionSpec("text,track_id,at_seconds,duration_seconds","sequence_id,result,number,color")}
   ,{"text.set_content",new ActionSpec("clip_id,text","sequence_id,number,color")}
   ,{"subtitle.create",new ActionSpec("text,at_seconds,duration_seconds","sequence_id,result")}
   ,{"subtitle.set_content",new ActionSpec("clip_id,text","sequence_id")}
   ,{"subtitle.delete",new ActionSpec("clip_id","sequence_id")}
   ,{"marker.create",new ActionSpec("at_seconds","sequence_id,name,result")}
   ,{"marker.move",new ActionSpec("marker_id,at_seconds","sequence_id")}
   ,{"marker.rename",new ActionSpec("marker_id,name","sequence_id")}
   ,{"marker.delete",new ActionSpec("marker_id","sequence_id")}
   ,{"track.set_enabled",new ActionSpec("track_id,enabled","sequence_id")}
   ,{"track.set_effects_enabled",new ActionSpec("track_id,enabled","sequence_id")}
   ,{"sequence.set_export_in",new ActionSpec("at_seconds","sequence_id")}
   ,{"sequence.set_export_out",new ActionSpec("at_seconds","sequence_id")}
   ,{"sequence.clear_export_range",new ActionSpec("","sequence_id")}
   ,{"media.place_asset",new ActionSpec("asset_id,track_id,at_seconds","sequence_id,source_in_seconds,duration_seconds,placement,result")}
   ,{"media.import_local",new ActionSpec("resource_path","sequence_id,track_id,at_seconds,source_in_seconds,duration_seconds,placement,result")}
   ,{"mgt.set_parameter",new ActionSpec("clip_id,parameter_id","sequence_id,number,text,value")}
   ,{"curve.set_keyframe",new ActionSpec("clip_id,parameter_id,at_seconds,number","sequence_id")}
   ,{"curve.set_interpolation",new ActionSpec("clip_id,parameter_id,text","sequence_id")}
   ,{"curve.set_tangent",new ActionSpec("clip_id,parameter_id,at_seconds,number,value","sequence_id")}
   ,{"mask.set_keyframe",new ActionSpec("clip_id,at_seconds","sequence_id")}
   ,{"mask.set_interpolation",new ActionSpec("clip_id,text","sequence_id")}
   ,{"mask.set_enabled",new ActionSpec("clip_id,enabled","sequence_id")}
   ,{"audio.ducking.set",new ActionSpec("track_id,voice_track_id","sequence_id,number")}
   ,{"audio.ducking.remove",new ActionSpec("track_id","sequence_id")}
  };
  static bool IsKnownOperation(string op){return op!=null&&(operations.Contains(op)||actionSpecs.ContainsKey(op));}
  static bool IsExtended(string op){return op!=null&&actionSpecs.ContainsKey(op);}
  static void CheckActionKeys(Dictionary<string,object> row){
   object name;Require(row.TryGetValue("op",out name)&&name is string&&IsKnownOperation((string)name),"Acción desconocida");
   ActionSpec spec;if(!actionSpecs.TryGetValue((string)name,out spec))return;
   foreach(string field in spec.Required.Split(new[]{','},StringSplitOptions.RemoveEmptyEntries))Require(row.ContainsKey(field)&&row[field]!=null,"Falta «"+field+"» en "+name);
   foreach(var pair in row){if(spec.Accepted.Contains(pair.Key)||pair.Value==null)continue;var array=pair.Value as object[];if(array!=null&&array.Length==0)continue;throw new Exception("El campo «"+pair.Key+"» no corresponde a "+name);}
  }
  static InstructionAction ResolveReferences(InstructionAction original,Dictionary<string,string> aliases){
   var action=Store.Json().Deserialize<InstructionAction>(Store.Json().Serialize(Store.Fields(original)));
   Func<string,string> resolve=id=>{if(String.IsNullOrEmpty(id)||!id.StartsWith("$"))return id;string value;Require(aliases.TryGetValue(id.Substring(1),out value),"Referencia aún no creada: "+id);return value;};
   action.sequence_id=resolve(action.sequence_id);action.clip_id=resolve(action.clip_id);action.other_clip_id=resolve(action.other_clip_id);action.track_id=resolve(action.track_id);action.voice_track_id=resolve(action.voice_track_id);action.left_clip_id=resolve(action.left_clip_id);action.right_clip_id=resolve(action.right_clip_id);action.effect_id=resolve(action.effect_id);action.asset_id=resolve(action.asset_id);action.marker_id=resolve(action.marker_id);
   if(action.clip_ids!=null)action.clip_ids=action.clip_ids.Select(resolve).ToList();return action;
  }
  static void Bind(InstructionPlan plan,string alias,string id){
   if(String.IsNullOrWhiteSpace(alias))return;Require(alias.Length<=64&&alias.All(c=>Char.IsLetterOrDigit(c)||c=='_'||c=='.'||c=='-'),"Nombre de resultado inválido");
   Require(!plan.References.ContainsKey(alias),"Resultado duplicado: "+alias);plan.References[alias]=id;plan.Lines.Add("Resultado $"+alias+" disponible para pasos posteriores");
  }
  static HashSet<string> SnapshotIds(Project project){return new HashSet<string>(project.sequences.SelectMany(s=>s.clips.Select(c=>c.id).Concat(s.transitions.Select(t=>t.id)).Concat(s.clips.SelectMany(c=>c.effects.Select(e=>e.id))).Concat(s.tracks.SelectMany(t=>t.effects.Select(e=>e.id)))));}
  static void CaptureResult(Project project,InstructionAction a,InstructionPlan plan,HashSet<string> before){
   if(String.IsNullOrWhiteSpace(a.result))return;
   var sequence=SequenceOf(project,a);var added=new List<string>();
   if(a.op=="clip.duplicate")added=sequence.clips.Where(c=>!before.Contains(c.id)).Select(c=>c.id).ToList();
   else if(a.op=="effect.apply")added=sequence.clips.SelectMany(c=>c.effects).Concat(sequence.tracks.SelectMany(t=>t.effects)).Where(e=>!before.Contains(e.id)).Select(e=>e.id).ToList();
   else if(a.op=="transition.apply")added=sequence.transitions.Where(t=>!before.Contains(t.id)).Select(t=>t.id).ToList();
   else if(a.op=="clip.move"||a.op=="clip.trim"||a.op=="clip.inherit_track_effects")added.Add(a.clip_id);
   Require(added.Count>0,"La acción no produjo un resultado referenciable");
   if(added.Count==1)Bind(plan,a.result,added[0]);
   else{
    string primary=added[0];
    if(a.op=="clip.duplicate"){
     var source=sequence.clips.FirstOrDefault(c=>c.id==a.clip_id);
     var match=source==null?null:sequence.clips.FirstOrDefault(c=>added.Contains(c.id)&&c.asset_id==source.asset_id&&c.stream_id==source.stream_id);
     if(match!=null)primary=match.id;
    }
    Bind(plan,a.result,primary);
    for(int i=0;i<added.Count;i++)Bind(plan,a.result+"."+(i+1),added[i]);
   }
  }
  static void DescribeChanges(Project before,InstructionPlan plan){
   var after=plan.Result;var oldClips=new HashSet<string>(before.sequences.SelectMany(s=>s.clips).Select(c=>c.id));var newClips=new HashSet<string>(after.sequences.SelectMany(s=>s.clips).Select(c=>c.id));
   int added=newClips.Except(oldClips).Count(),removed=oldClips.Except(newClips).Count();
   int moved=before.sequences.SelectMany(s=>s.clips).Count(c=>newClips.Contains(c.id)&&after.sequences.SelectMany(s=>s.clips).Any(n=>n.id==c.id&&(n.track_id!=c.track_id||Math.Abs(n.timeline_start.Seconds-c.timeline_start.Seconds)>0.00001)));
   int trimmed=before.sequences.SelectMany(s=>s.clips).Count(c=>newClips.Contains(c.id)&&after.sequences.SelectMany(s=>s.clips).Any(n=>n.id==c.id&&Math.Abs(n.duration.Seconds-c.duration.Seconds)>0.00001));
   plan.Changes.Add("Clips nuevos: "+added+" · eliminados: "+removed+" · movidos: "+moved+" · duración modificada: "+trimmed);
   var oldMap=before.sequences.SelectMany(s=>s.clips).ToDictionary(c=>c.id);var newMap=after.sequences.SelectMany(s=>s.clips).ToDictionary(c=>c.id);
   int shown=0;
   foreach(var pair in oldMap){
    Clip now;if(!newMap.TryGetValue(pair.Key,out now)){if(shown++<60)plan.Changes.Add("Sale de la línea de tiempo: "+pair.Value.name+" ["+pair.Key.Substring(0,Math.Min(8,pair.Key.Length))+"]");continue;}
    bool position=now.track_id!=pair.Value.track_id||Math.Abs(now.timeline_start.Seconds-pair.Value.timeline_start.Seconds)>0.00001;
    bool length=Math.Abs(now.duration.Seconds-pair.Value.duration.Seconds)>0.00001;
    if((position||length)&&shown++<60)plan.Changes.Add("Cambia "+pair.Value.name+" ["+pair.Key.Substring(0,Math.Min(8,pair.Key.Length))+"]: "+pair.Value.timeline_start.Seconds.ToString("0.###")+" s / "+pair.Value.duration.Seconds.ToString("0.###")+" s → "+now.timeline_start.Seconds.ToString("0.###")+" s / "+now.duration.Seconds.ToString("0.###")+" s");
   }
   foreach(var pair in newMap)if(!oldMap.ContainsKey(pair.Key)&&shown++<60)plan.Changes.Add("Aparece: "+pair.Value.name+" ["+pair.Key.Substring(0,Math.Min(8,pair.Key.Length))+"] a "+pair.Value.timeline_start.Seconds.ToString("0.###")+" s");
   if(shown>60)plan.Changes.Add("… y "+(shown-60)+" cambio(s) de clip adicionales");
   int assets=after.assets.Count-before.assets.Count;if(assets>0)plan.Changes.Add("Medios añadidos: "+assets);
   int sequences=after.sequences.Count-before.sequences.Count;if(sequences>0)plan.Changes.Add("Secuencias nuevas: "+sequences);
  }
  static List<EffectInstance> EffectList(Project project,InstructionAction a){
   var sequence=SequenceOf(project,a);
   if(!String.IsNullOrEmpty(a.clip_id)){var clip=ClipOf(sequence,a.clip_id);Require(!TrackOf(sequence,clip.track_id).locked,"La pista del clip está bloqueada");return clip.effects;}
   if(!String.IsNullOrEmpty(a.track_id)){var track=TrackOf(sequence,a.track_id);Require(!track.locked,"La pista está bloqueada");return track.effects;}
   throw new Exception("Indica clip_id o track_id para la instancia de efecto");
  }
  static void ApplyExtended(Editor editor,InstructionAction a,InstructionPlan plan,IInstructionMediaAdapter media){
   if(IsAdded08181(a.op)){ApplyAdded08181(editor,a,plan,media);return;}
   if(a.op=="media.redownload_web"){
    Require(a.scope=="asset"||a.scope=="timeline"||a.scope=="project","Indica scope asset, timeline o project");
    var targetSequence=a.scope=="timeline"?SequenceOf(editor.Project,a):null;
    var ids=a.scope=="asset"?new List<string>{a.asset_id}:a.scope=="timeline"?targetSequence.clips.Select(c=>c.asset_id).Distinct().ToList():editor.Project.assets.Select(asset=>asset.id).ToList();
    var matches=editor.Project.assets.Where(asset=>ids.Contains(asset.id)&&asset.source!=null&&asset.source.kind=="web").Select(asset=>asset.id).ToList();
    Require(a.scope!="asset"||matches.Count==1,"asset_id no corresponde a un medio web");Require(matches.Count>0,"No hay medios web en el alcance indicado");
    foreach(string id in matches)if(!plan.RedownloadAssetIds.Contains(id))plan.RedownloadAssetIds.Add(id);
    Note(plan,"Redescargar medios web",matches.Count,0);return;
   }
   if(a.op=="media.import_url"||a.op=="media.import_shelf"||a.op=="placeholder.fill_url"||a.op=="media.import_local"){
    Require(media!=null,"La preparación de enlaces requiere abrir el panel de instrucciones");media.Apply(editor,a,plan);return;
   }
   if(a.op.StartsWith("sequence.")){
    if(a.op=="sequence.create"){
     string id=null;editor.Commit(p=>{id=Sequences.Create(p,a.name);var s=p.Active;if(a.width.HasValue||a.height.HasValue){Require(a.width.HasValue&&a.height.HasValue&&a.width>=64&&a.width<=7680&&a.height>=64&&a.height<=4320,"Lienzo inválido");s.width=a.width.Value;s.height=a.height.Value;}if(a.fps.HasValue)Require(a.fps.Value==60,"El núcleo de secuencias actual solo admite 60 FPS; la salida sí puede convertirse a otros FPS");});Bind(plan,a.result,id);Note(plan,"Crear secuencia "+a.name,1,0);return;
    }
    if(a.op=="sequence.rename"){editor.RenameSequence(a.sequence_id,a.name);Note(plan,"Renombrar secuencia",1,0);return;}
    editor.Commit(p=>{Require(p.sequences.Any(s=>s.id==a.sequence_id),"Secuencia inexistente");p.active_sequence_id=a.sequence_id;});Note(plan,"Activar secuencia",1,0);return;
   }
   var sequence=SequenceOf(editor.Project,a);editor.Project.active_sequence_id=sequence.id;
   if(a.op=="audio.ducking.set"){
    Require(TrackOf(sequence,a.track_id).kind=="audio"&&TrackOf(sequence,a.voice_track_id).kind=="audio","Auto-Ducking requiere dos pistas de audio");
    Require(!TrackOf(sequence,a.track_id).locked,"La pista de fondo está bloqueada");
    editor.Commit(p=>{var s=SequenceOf(p,a);var rule=Ducking01016.For(s,a.track_id)??new DuckingRule01016{target_track_id=a.track_id};rule.voice_track_id=a.voice_track_id;if(a.number.HasValue)rule.amount_db=a.number.Value;Ducking01016.Set(s,rule);});
    Note(plan,"Activar Auto-Ducking",1,0);return;
   }
   if(a.op=="audio.ducking.remove"){
    Require(Ducking01016.For(sequence,a.track_id)!=null,"La pista no tiene Auto-Ducking");
    editor.Commit(p=>Ducking01016.Remove(SequenceOf(p,a),a.track_id));
    Note(plan,"Quitar Auto-Ducking",1,0);return;
   }
   if(a.op=="clip.paste_attributes"){
    Require(a.attributes!=null,"Faltan los atributos copiados");
    var snapshot=ClipAttributes.Parse(ClipAttributes.Encode(a.attributes));
    var ids=Targets(sequence,a).Select(c=>c.id).ToList();
    var result=new AttributeApplyResult();
    editor.Commit(p=>{result=ClipAttributes.Apply(p,snapshot,ids);if(result.applied==0)throw new Exception("No hay clips compatibles para estos atributos");});
    foreach(var reason in result.reasons)plan.Lines.Add("Omitido "+reason);
    Note(plan,"Pegar atributos",result.applied,result.skipped);return;
   }
   if(a.op.StartsWith("track.")){
    if(a.op=="track.create"){
     Require(a.source_kind=="video"||a.source_kind=="audio","La pista debe ser de vídeo o audio");string id=null;
     editor.Commit(p=>{var t=Store.AddTrack(p.Active,a.source_kind);if(!String.IsNullOrWhiteSpace(a.name))t.name=a.name.Trim();id=t.id;});Bind(plan,a.result,id);Note(plan,"Crear pista "+a.source_kind,1,0);return;
    }
    var track=TrackOf(sequence,a.track_id);
    if(a.op=="track.rename"){Require(!String.IsNullOrWhiteSpace(a.name)&&a.name.Trim().Length<=80,"Nombre de pista inválido");editor.Commit(p=>p.Active.tracks.Find(t=>t.id==track.id).name=a.name.Trim());Note(plan,"Renombrar pista",1,0);return;}
    Require(a.enabled.HasValue,"Indica enabled true o false");editor.Commit(p=>p.Active.tracks.Find(t=>t.id==track.id).locked=a.enabled.Value);Note(plan,"Cambiar bloqueo de pista",1,0);return;
   }
   if(a.op.StartsWith("effect.")){
    var list=EffectList(editor.Project,a);var instance=list.Find(e=>e.id==a.effect_id);Require(instance!=null,"Instancia de efecto inexistente: "+a.effect_id);
    if(a.op=="effect.remove_instance"){editor.Commit(p=>EffectList(p,a).RemoveAll(e=>e.id==a.effect_id));Note(plan,"Quitar instancia de efecto",1,0);return;}
    if(a.op=="effect.set_enabled"){Require(a.enabled.HasValue,"Indica enabled true o false");editor.Commit(p=>EffectList(p,a).Find(e=>e.id==a.effect_id).enabled=a.enabled.Value);Note(plan,"Activar o desactivar efecto",1,0);return;}
    if(a.op=="effect.move"){Require(a.index.HasValue&&a.index>=0&&a.index<list.Count,"Posición de efecto inválida");editor.Commit(p=>{var effects=EffectList(p,a);var effect=effects.Find(e=>e.id==a.effect_id);effects.Remove(effect);effects.Insert(a.index.Value,effect);});Note(plan,"Reordenar efecto",1,0);return;}
    var contract=EffectContracts.Find(instance.effect_id,instance.contract_version);Require(contract!=null,"Contrato de efecto no disponible");var spec=contract.parameters.FirstOrDefault(x=>x.id==a.parameter_id);Require(spec!=null,"Parámetro de efecto desconocido: "+a.parameter_id);
    EffectValue value=null;
    if(spec.kind=="number"){Require(a.number.HasValue,"Indica number para este parámetro");value=new EffectValue{kind="number",number=a.number.Value};}
    else if(spec.kind=="boolean"){Require(a.value.HasValue,"Indica value true o false");value=new EffectValue{kind="boolean",boolean=a.value.Value};}
    else if(spec.kind=="text"||spec.kind=="choice"){Require(a.text!=null,"Indica text para este parámetro");value=new EffectValue{kind=spec.kind,text=a.text};}
    else if(spec.kind=="resource"){
     Require(!String.IsNullOrWhiteSpace(a.resource_path)&&File.Exists(a.resource_path),"Indica un archivo de recurso existente");string path=Path.GetFullPath(a.resource_path);string resourceId=null;
     editor.Commit(p=>{var resource=p.effect_resources.FirstOrDefault(r=>r.kind=="lut3d"&&String.Equals(r.uri,path,StringComparison.OrdinalIgnoreCase));if(resource==null){resource=new EffectResource{id=Store.Id(),kind="lut3d",uri=path};p.effect_resources.Add(resource);}resourceId=resource.id;EffectList(p,a).Find(e=>e.id==a.effect_id).parameters[a.parameter_id]=new EffectValue{kind="resource",resource_id=resourceId};});Note(plan,"Cambiar recurso de efecto",1,0);return;
    }
    else throw new Exception("Tipo de parámetro no compatible");
    editor.Commit(p=>EffectList(p,a).Find(e=>e.id==a.effect_id).parameters[a.parameter_id]=value);Note(plan,"Cambiar "+a.parameter_id+" del efecto",1,0);return;
   }
   if(a.op=="transition.set_duration"){
    Require(a.duration_frames.HasValue,"Indica duración en fotogramas");var transition=sequence.transitions.FirstOrDefault(t=>t.left_clip_id==a.left_clip_id&&t.right_clip_id==a.right_clip_id);Require(transition!=null,"Transición inexistente en ese corte");editor.SetTransition(a.left_clip_id,a.right_clip_id,transition.mgt_resource_id??transition.transition_id,a.duration_frames.Value);Note(plan,"Cambiar duración de transición",1,0);return;
   }
   if(a.op=="gap.close"){RequirePosition(a);editor.RippleDeleteGap(a.track_id,a.at_seconds.Value);Note(plan,"Cerrar espacio",1,0);return;}
   if(a.op=="range.trim"){RequirePosition(a);Require(a.finish_seconds.HasValue,"Indica finish_seconds");Require(editor.RangeTrimSelection(a.clip_ids,a.at_seconds.Value,a.finish_seconds.Value),"El rango no afecta ningún clip");Note(plan,"Eliminar rango de la línea de tiempo",1,0);return;}
   if(a.op=="clip.link"){Require(a.clip_ids!=null&&a.clip_ids.Count>=2,"Indica al menos dos clips");foreach(string id in a.clip_ids)Require(!TrackOf(sequence,ClipOf(sequence,id).track_id).locked,"Una pista del vínculo está bloqueada");editor.Link(a.clip_ids);Note(plan,"Vincular clips",1,0);return;}
   if(a.op=="clip.roll"){RequirePosition(a);editor.RollEditLinked(a.left_clip_id,a.right_clip_id,a.at_seconds.Value,a.include_linked!=false);Note(plan,"Desplazar corte entre clips",1,0);return;}
   var clip=ClipOf(sequence,a.clip_id);
   if(a.op=="clip.unlink"){foreach(string id in editor.Related(clip.id))Require(!TrackOf(sequence,ClipOf(sequence,id).track_id).locked,"Una pista del vínculo está bloqueada");editor.Unlink(clip.id);Note(plan,"Desvincular clip",1,0);return;}
   if(a.op=="clip.delete"){if(a.ripple==true)editor.RippleDelete(clip.id);else editor.Delete(clip.id);Note(plan,a.ripple==true?"Eliminar con rizo":"Eliminar dejando hueco",1,0);return;}
   if(a.op=="clip.cut"){
    RequirePosition(a);var before=new HashSet<string>(sequence.clips.Select(c=>c.id));string track=clip.track_id;
    Require(editor.CutSelection(new List<string>{clip.id},a.at_seconds.Value,a.include_linked!=false),"El corte debe quedar dentro del clip");
    Bind(plan,a.result==null?null:a.result+".left",clip.id);
    var right=editor.Project.Active.clips.FirstOrDefault(c=>!before.Contains(c.id)&&c.track_id==track&&Math.Abs(c.timeline_start.Seconds-Rational.Frame(a.at_seconds.Value).Seconds)<0.00001);Require(right!=null,"No se creó el segmento derecho");Bind(plan,a.result==null?null:a.result+".right",right.id);Note(plan,"Dividir clip",1,0);return;
   }
   if(a.op=="clip.range_trim"){RequirePosition(a);Require(a.finish_seconds.HasValue,"Indica finish_seconds");double lo=Math.Min(a.at_seconds.Value,a.finish_seconds.Value),hi=Math.Max(a.at_seconds.Value,a.finish_seconds.Value);Require(hi>clip.timeline_start.Seconds+0.00001&&lo<clip.End-0.00001,"El rango no cruza el clip");Require(editor.RangeTrim(clip.id,a.at_seconds.Value,a.finish_seconds.Value,false),"El rango está vacío");Note(plan,"Eliminar segmento",1,0);return;}
   if(a.op=="clip.slip"){Require(a.source_in_seconds.HasValue,"Indica source_in_seconds");editor.Slip(clip.id,a.source_in_seconds.Value);Note(plan,"Mover contenido interno",1,0);return;}
   throw new Exception("Acción aún no implementada: "+a.op);
  }
 }
}
