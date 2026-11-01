using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Prometheus {
 public sealed class InstructionDocument {
  public string format="prometheus-instructions";
  public int version=1;
  public string project_id;
  public int revision;
  public string context_sequence_id;
  public long? context_in_frame,context_out_frame;
  public List<string> context_track_ids;
  public bool? allow_outside_context;
  public List<InstructionAction> actions=new List<InstructionAction>();
 }
 public sealed class InstructionAction {
  public string op,scope,sequence_id,clip_id,track_id,voice_track_id,left_clip_id,right_clip_id,catalog_id,edge,placement,resource_path;
  public string result,other_clip_id,effect_id,parameter_id,name,url,asset_id,source_kind,shelf_id,marker_id,color;
  public List<string> clip_ids=new List<string>();
  public double? at_seconds,duration_seconds,finish_seconds,source_in_seconds,number;
  public int? duration_frames;
  public int? width,height,fps;
  public int? index;
  public bool? ripple,value,enabled,include_linked;
  public string text;
  public ClipAttributeSnapshot attributes;
 }
 public sealed class InstructionCardReview {
  public string status,summary;
  public int changed,skipped;
  public readonly List<string> details=new List<string>();
 }
 public sealed class InstructionPlan {
  public Project Result;
  public string DocumentJson;
  public string ReviewedProjectId;
  public int ReviewedRevision;
  public readonly List<string> Lines=new List<string>();
  public int ChangedCount,SkippedCount;
  public readonly Dictionary<string,string> References=new Dictionary<string,string>(StringComparer.Ordinal);
  public readonly List<string> Changes=new List<string>();
  public readonly List<InstructionCardReview> Cards=new List<InstructionCardReview>();
  public readonly List<string> RedownloadAssetIds=new List<string>();
  public readonly List<string> MgtPreviewAssetIds=new List<string>();
 }
 public static partial class Instructions {
  static readonly HashSet<string> operations=new HashSet<string>{"effect.apply","effect.remove","transition.apply","transition.remove","clip.move","clip.trim","clip.duplicate","clip.inherit_track_effects"};
  static readonly HashSet<string> fields=new HashSet<string>{"op","scope","sequence_id","clip_id","track_id","voice_track_id","left_clip_id","right_clip_id","catalog_id","edge","placement","resource_path","clip_ids","at_seconds","duration_seconds","duration_frames","ripple","value","result","other_clip_id","effect_id","parameter_id","name","url","asset_id","source_kind","shelf_id","marker_id","color","finish_seconds","source_in_seconds","number","width","height","fps","index","enabled","include_linked","text","attributes"};
  static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
  static void CheckKeys(Dictionary<string,object> node,IEnumerable<string> expected,string where){Require(node!=null,where+" debe ser un objeto");var allowed=new HashSet<string>(expected);foreach(var key in node.Keys)Require(allowed.Contains(key),where+": campo desconocido «"+key+"»");}
  public static InstructionDocument Parse(string text){
   Require(!String.IsNullOrWhiteSpace(text)&&text.Length<=1024*1024,"Pega un documento de instrucciones de hasta 1 MB");
   Dictionary<string,object> raw;
   try{raw=Store.Json().DeserializeObject(text) as Dictionary<string,object>;}catch(Exception ex){throw new Exception("Documento de instrucciones inválido: "+ex.Message);}
   CheckKeys(raw,new[]{"format","version","project_id","revision","actions","context_sequence_id","context_in_frame","context_out_frame","context_track_ids","allow_outside_context"},"Documento");
   Require(raw.ContainsKey("format")&&raw.ContainsKey("version")&&raw.ContainsKey("project_id")&&raw.ContainsKey("revision")&&raw.ContainsKey("actions"),"Faltan campos del documento");
   var rows=raw["actions"] as object[];Require(rows!=null&&rows.Length>0&&rows.Length<=200,"El documento debe contener entre 1 y 200 acciones");
   foreach(var row in rows){CheckKeys(row as Dictionary<string,object>,fields,"Acción");CheckActionKeys(row as Dictionary<string,object>);}
   InstructionDocument doc;
   try{doc=Store.Json().Deserialize<InstructionDocument>(text);}catch(Exception ex){throw new Exception("Campos de instrucciones inválidos: "+ex.Message);}
   Require(doc.format=="prometheus-instructions"&&doc.version==1,"Versión de instrucciones no compatible");
   Require(!String.IsNullOrWhiteSpace(doc.project_id)&&doc.revision>=0,"Proyecto o revisión inválidos");
   Require(doc.actions!=null&&doc.actions.Count==rows.Length,"Lista de acciones inválida");
   Require(doc.context_in_frame.HasValue==doc.context_out_frame.HasValue,"Indica ambas marcas del contexto In/Out");
   if(doc.context_in_frame.HasValue)Require(!String.IsNullOrWhiteSpace(doc.context_sequence_id)&&doc.context_in_frame.Value>=0&&doc.context_out_frame.Value>doc.context_in_frame.Value,"Tramo In/Out inválido");
   if(doc.context_track_ids!=null)Require(!String.IsNullOrWhiteSpace(doc.context_sequence_id)&&doc.context_track_ids.Count>0&&doc.context_track_ids.Count<=64&&doc.context_track_ids.All(id=>!String.IsNullOrWhiteSpace(id))&&doc.context_track_ids.Distinct().Count()==doc.context_track_ids.Count,"Pistas del contexto inválidas");
   foreach(var action in doc.actions)Require(action!=null&&IsKnownOperation(action.op),"Acción desconocida: "+(action==null?"(vacía)":action.op));
   return doc;
  }
  static object CanonicalFields(object node){
   var map=node as System.Collections.IDictionary;
   if(map!=null){var ordered=new SortedDictionary<string,object>(StringComparer.Ordinal);foreach(System.Collections.DictionaryEntry entry in map)ordered[Convert.ToString(entry.Key)]=CanonicalFields(entry.Value);return ordered;}
   var items=node as System.Collections.IEnumerable;
   if(items!=null&&!(node is string)){var list=new List<object>();foreach(var item in items)list.Add(CanonicalFields(item));return list;}
   return node;
  }
  public static string Encode(InstructionDocument doc){return Store.Json().Serialize(CanonicalFields(Store.Fields(doc)));}
  public static InstructionAction ParseAction(string text){
   Dictionary<string,object> raw;try{raw=Store.Json().DeserializeObject(text) as Dictionary<string,object>;}catch(Exception ex){throw new Exception("Tarjeta JSON inválida: "+ex.Message);}
   CheckKeys(raw,fields,"Tarjeta");CheckActionKeys(raw);
   return Store.Json().Deserialize<InstructionAction>(text);
  }
  public static InstructionDocument New(Project project){return new InstructionDocument{project_id=project.id,revision=project.revision};}
  static object EffectState(EffectInstance effect){return new{id=effect.id,contract_id=effect.effect_id,enabled=effect.enabled,parameters=effect.parameters};}
  public static string CopyState(Project project,bool allSequences){return CopyState(project,allSequences?"project":"sequence",null);}
  public static string CopyState(Project project,string scope,IEnumerable<string> selectedIds){
   Require(project!=null&&project.Active!=null,"Abre una secuencia antes de copiar el contexto");
   Require(scope=="project"||scope=="sequence"||scope=="io"||scope=="selection"||scope=="tracks","Alcance de contexto desconocido");
   var marks=SequenceExportRange.Read(project.Active);
   if(scope=="io")Require(marks.Complete,"Define primero las marcas de entrada y salida de la secuencia");
   var selected=new HashSet<string>(selectedIds??new string[0],StringComparer.Ordinal);
   if(scope=="selection")Require(selected.Count>0,"Selecciona al menos un clip para copiar su contexto");
   if(scope=="tracks")Require(selected.Count>0&&selected.All(id=>project.Active.tracks.Any(t=>t.id==id)),"Selecciona una o más pistas válidas");
   double from=scope=="io"?marks.InFrame.Value/60.0:0,to=scope=="io"?marks.OutFrame.Value/60.0:Double.MaxValue;
   var included=(scope=="project"?project.sequences:new List<Sequence>{project.Active});
   var edgeIds=new HashSet<string>(scope=="io"?project.Active.transitions.Where(t=>t.cut_frame/60.0>=from&&t.cut_frame/60.0<to).SelectMany(t=>new[]{t.left_clip_id,t.right_clip_id}):new string[0]);
   Func<Sequence,Clip,bool> include=(s,c)=>scope=="project"||scope=="sequence"||scope=="io"&&(c.timeline_start.Seconds<to&&c.End>from||edgeIds.Contains(c.id))||scope=="selection"&&selected.Contains(c.id)||scope=="tracks"&&selected.Contains(c.track_id);
   var usedAssets=new HashSet<string>(included.SelectMany(s=>s.clips.Where(c=>include(s,c))).Select(c=>c.asset_id),StringComparer.Ordinal);
   var usedClips=new HashSet<string>(included.SelectMany(s=>s.clips.Where(c=>include(s,c))).Select(c=>c.id),StringComparer.Ordinal);
   if(scope=="selection")Require(usedClips.Count>0,"La selección ya no contiene clips de la secuencia activa");
   var sequences=included.Select(s=>new{
    id=s.id,name=s.name,active=s.id==project.active_sequence_id,width=s.width,height=s.height,fps=new{s.fps.n,s.fps.d},color_space=s.color_space,
    tracks=s.tracks.OrderBy(t=>t.kind).ThenBy(t=>t.order).Select(t=>new{id=t.id,name=t.name,kind=t.kind,order=t.order,locked=t.locked,enabled=t.enabled,effects_enabled=t.effects_enabled,effects=t.effects.Select(EffectState).ToArray()}).ToArray(),
    clips=s.clips.Where(c=>include(s,c)).OrderBy(c=>c.timeline_start.Seconds).ThenBy(c=>c.track_id).Select(c=>new{id=c.id,name=c.name,asset_id=c.asset_id,track_id=c.track_id,start_seconds=c.timeline_start.Seconds,duration_seconds=c.duration.Seconds,source_in_seconds=c.source_in.Seconds,speed=c.speed.Seconds,enabled=c.enabled,inherit_track_effects=c.inherit_track_effects,linked_group=s.link_groups.FirstOrDefault(g=>g.clip_ids.Contains(c.id))==null?null:s.link_groups.First(g=>g.clip_ids.Contains(c.id)).id,edit_group=s.edit_groups.FirstOrDefault(g=>g.clip_ids.Contains(c.id))==null?(int?)null:s.edit_groups.First(g=>g.clip_ids.Contains(c.id)).number,editable_in_context=scope!="io"||c.timeline_start.Seconds>=from-0.000001&&c.End<=to+0.000001,framing=ClipFraming.Read(c),keyframes=c.animation,effects=c.effects.Select(EffectState).ToArray()}).ToArray(),
    transitions=s.transitions.Where(t=>usedClips.Contains(t.left_clip_id)&&usedClips.Contains(t.right_clip_id)).Select(t=>new{id=t.id,contract_id=t.transition_id,left_clip_id=t.left_clip_id,right_clip_id=t.right_clip_id,cut_frame=t.cut_frame,duration_frames=t.duration_frames,group_id=t.group_id}).ToArray(),
    audio_ducking=Ducking01016.All(s).ToArray(),
    markers=Markers.Read(s).Where(m=>scope!="io"||m.Seconds>=from&&m.Seconds<to).Select(m=>new{id=m.Id,name=m.Name,at_seconds=m.Seconds,color_argb=m.ColorArgb}).ToArray()
   }).ToArray();
   var state=new{format="prometheus-state",version=1,project_id=project.id,revision=project.revision,project_name=project.name,active_sequence_id=project.active_sequence_id,
    context=new{scope=scope,sequence_id=project.active_sequence_id,in_frame=scope=="io"?marks.InFrame:null,out_frame=scope=="io"?marks.OutFrame:null,track_ids=scope=="tracks"?selected.ToArray():null,times_are_absolute=true,edits_outside_context_require_allow_outside_context=true},
    assets=project.assets.Where(a=>scope=="project"||scope=="sequence"||usedAssets.Contains(a.id)).Select(a=>new{id=a.id,name=a.name,kind=a.source==null?null:a.source.kind,source_uri=a.source==null?null:a.source.uri,generated_kind=GeneratedMedia.Kind(a),is_placeholder=GeneratedMedia.IsPlaceholder(a),text=GeneratedMedia.IsText(a)?GeneratedMedia.Text(a):null,text_size=GeneratedMedia.IsText(a)?(int?)GeneratedMedia.TextSize(a):null,text_color=GeneratedMedia.IsText(a)?GeneratedMedia.TextColor(a):null,mgt_contract=a.extensions.FirstOrDefault(e=>e.@namespace=="prometheus.mgt.asset"),recommended_duration_seconds=a.recommended_duration==null?(double?)null:a.recommended_duration.Seconds,streams=a.streams.Select(st=>new{id=st.id,kind=st.kind,duration_seconds=st.duration==null?(double?)null:st.duration.Seconds}).ToArray()}).ToArray(),
    shelf_items=ShelfStore.Read(project).Select(x=>new{id=x.id,name=x.asset.name,url=x.url,kind=x.asset.source.kind,range_in_seconds=x.range_in,range_out_seconds=x.range_out}).ToArray(),
    sequences=sequences,
    available_effects=EffectContracts.All.Select(c=>new{id=c.id,name=c.name,kind=c.input_kind,version=c.version,requires_file=c.id==EffectContracts.Lut3DId}).ToArray(),
    available_transitions=TransitionContracts.All.Select(c=>new{id=c.id,name=c.name,kind=c.input_kind,version=c.version}).ToArray(),
    instruction_format="prometheus-instructions",instruction_version=1,
    instruction_example=new{format="prometheus-instructions",version=1,project_id=project.id,revision=project.revision,context_sequence_id=scope=="io"||scope=="tracks"?project.active_sequence_id:null,context_in_frame=scope=="io"?marks.InFrame:null,context_out_frame=scope=="io"?marks.OutFrame:null,context_track_ids=scope=="tracks"?selected.ToArray():null,actions=new[]{new{op="effect.apply",scope="clip",clip_id=project.Active.clips.FirstOrDefault(c=>usedClips.Contains(c.id))==null?"ID_DEL_CLIP":project.Active.clips.First(c=>usedClips.Contains(c.id)).id,catalog_id=EffectContracts.BasicColorId,sequence_id=project.active_sequence_id}}},
    supported_actions=AvailableActions().Select(op=>new{op=op,required_fields=RequiredFields(op),accepted_fields=AcceptedFields(op)}).ToArray(),
    usage_notes=new[]{
     "Los tiempos del contexto y de las instrucciones son absolutos; no restes el punto In. Si copiaste In/Out, incluye context_sequence_id, context_in_frame y context_out_frame en la respuesta. Solo se aplicarán cambios dentro del tramo; usa allow_outside_context true de forma explícita si realmente necesitas afectar otras partes.",
     "effect.apply/remove: scope selection|clip|track|sequence, catalog_id; optional result; LUT 3D requires resource_path",
     "effect.set_parameter: effect_id, parameter_id; number for numeric, text for text/choice, value for boolean, resource_path for LUT; effect.set_enabled: effect_id, enabled; effect.move: effect_id, index (zero-based); effect.remove_instance: effect_id; target clip_id or track_id",
     "transition.apply/remove: scope junction|sequence, catalog_id, duration_frames; junction uses left_clip_id/right_clip_id; transition.set_duration: left_clip_id,right_clip_id,duration_frames",
     "clip.move: clip_id, at_seconds, track_id, placement empty|overwrite|insert; clip.duplicate: same except no insert; optional result binds new clip",
     "clip.trim: clip_id, edge left|right, duration_seconds, ripple true|false; clip.delete: clip_id, ripple true|false; clip.cut: clip_id, at_seconds, optional result exposing $result.left and $result.right",
     "gap.close: track_id, at_seconds within gap; clip.range_trim: clip_id, at_seconds, finish_seconds; range.trim: at_seconds,finish_seconds, optional clip_ids (empty means all tracks); clip.slip: clip_id, source_in_seconds; clip.roll: left_clip_id,right_clip_id,at_seconds; clip.link: clip_ids; clip.unlink: clip_id",
     "clip.inherit_track_effects: clip_id, value true|false",
     "sequence.create: name, optional width,height,fps=60,result; sequence.rename/activate: sequence_id; track.create: source_kind video|audio, optional name,result,sequence_id; track.rename/set_locked: track_id and name or enabled",
     "media.import_url: url, source_kind video|image|audio; video acepta enlaces de imagen directos; optional track_id, at_seconds, placement empty|overwrite|insert, source_in_seconds,duration_seconds,result; without track_id adds to project only",
     "media.redownload_web: scope asset|timeline|project; asset requiere asset_id. Actualiza las fuentes web después de aplicar el lote.",
     "media.import_shelf: shelf_id from shelf_items; optional track_id, at_seconds, placement empty|overwrite|insert, result; without track_id adds to project only; removes item from Bandeja after application",
     "placeholder.fill_url: clip_id,url; optional source_kind image|video|audio (verified against the link),source_in_seconds,include_linked (default true),result; preserves clip position and duration",
     "clip.paste_attributes: attributes, scope selection|clip|track|sequence; clip_ids for selection, clip_id for clip, track_id for track; copies framing or audio controls and the ordered effect stack only to compatible unlocked clips",
     "Limpieza de voz: effect.apply con catalog_id prometheus.audio.voice-clean sobre un clip de audio; audio.ducking.set: track_id de fondo y voice_track_id de voz, number opcional de reducción entre -30 y -1 dB; audio.ducking.remove: track_id. Auto-Ducking afecta la pista completa y requiere allow_outside_context true si copiaste In/Out.",
     "Within one document, result names are referenced with $name; cut exposes $name.left and $name.right. Review fetches link metadata without downloading the media."
    }
   };
   return Store.Json().Serialize(state);
  }
  static Sequence SequenceOf(Project project,InstructionAction action){var id=action.sequence_id??project.active_sequence_id;var sequence=project.sequences.Find(s=>s.id==id);Require(sequence!=null,"Secuencia inexistente: "+id);return sequence;}
  static Clip ClipOf(Sequence sequence,string id){var clip=sequence.clips.Find(c=>c.id==id);Require(clip!=null,"Clip inexistente: "+id);return clip;}
  static Track TrackOf(Sequence sequence,string id){var track=sequence.tracks.Find(t=>t.id==id);Require(track!=null,"Pista inexistente: "+id);return track;}
  static string NameOf(EffectContract contract){return contract==null?"efecto":contract.name;}
  static IEnumerable<Clip> Targets(Sequence sequence,InstructionAction action){
   if(action.scope=="sequence")return sequence.clips.ToList();
   if(action.scope=="track"){Require(!String.IsNullOrWhiteSpace(action.track_id),"Falta la pista");return sequence.clips.Where(c=>c.track_id==action.track_id).ToList();}
   if(action.scope=="clip")return new[]{ClipOf(sequence,action.clip_id)};
   if(action.scope=="selection"){Require(action.clip_ids!=null&&action.clip_ids.Count>0,"La selección de la instrucción está vacía");return action.clip_ids.Distinct().Select(id=>ClipOf(sequence,id)).ToList();}
   throw new Exception("Alcance no compatible: "+action.scope);
  }
  static void Note(InstructionPlan plan,string title,int count,int skipped){plan.ChangedCount+=count;plan.SkippedCount+=skipped;plan.Lines.Add(title+": "+count+" destino(s)"+(skipped>0?"; "+skipped+" omitido(s) por incompatibilidad o bloqueo":""));}
  static void Effect(Editor editor,InstructionAction action,InstructionPlan plan,bool apply){
   var sequence=SequenceOf(editor.Project,action);editor.Project.active_sequence_id=sequence.id;
   var contract=EffectContracts.All.FirstOrDefault(c=>c.id==action.catalog_id);Require(contract!=null,"Efecto desconocido: "+action.catalog_id);
   string lut=null;if(apply&&contract.id==EffectContracts.Lut3DId){Require(!String.IsNullOrWhiteSpace(action.resource_path),"LUT 3D requiere un archivo .cube");lut=Path.GetFullPath(action.resource_path);Require(File.Exists(lut)&&String.Equals(Path.GetExtension(lut),".cube",StringComparison.OrdinalIgnoreCase),"No se encuentra el archivo LUT 3D");}
   Func<Project,EffectInstance> create=p=>{if(lut==null)return EffectContracts.New(contract.id);var resource=p.effect_resources.FirstOrDefault(r=>r.kind=="lut3d"&&String.Equals(r.uri,lut,StringComparison.OrdinalIgnoreCase));if(resource==null){resource=new EffectResource{id=Store.Id(),kind="lut3d",uri=lut};p.effect_resources.Add(resource);}return EffectContracts.NewLut3D(resource.id);};
   if(action.scope=="track"){
    var track=TrackOf(sequence,action.track_id);Require(!track.locked,"La pista "+track.name+" está bloqueada");
    Require(track.kind==contract.input_kind,"El efecto no es compatible con esta pista");
    int changed=apply?1:track.effects.Count(e=>e.effect_id==contract.id);
    editor.Commit(p=>{var target=p.Active.tracks.Find(t=>t.id==track.id);if(apply)target.effects.Add(create(p));else target.effects.RemoveAll(e=>e.effect_id==contract.id);});
    Note(plan,(apply?"Aplicar ":"Quitar ")+NameOf(contract)+" en "+track.name,changed,0);return;
   }
   var candidates=Targets(sequence,action).Where(c=>TrackOf(sequence,c.track_id).kind==contract.input_kind).ToList();
   string kind=contract.input_kind=="audio"?"audio":"vídeo";
   Require(candidates.Count>0,"No hay clips de "+kind+" compatibles para "+contract.name);
   var eligible=new List<string>();int skipped=0;
   foreach(var clip in candidates){var track=TrackOf(sequence,clip.track_id);if(track.locked){skipped++;plan.Lines.Add("Omitido «"+clip.name+"» ["+clip.id.Substring(0,Math.Min(8,clip.id.Length))+"]: pista bloqueada");continue;}eligible.Add(clip.id);}
   Require(eligible.Count>0,"Todos los clips de "+kind+" compatibles están en pistas bloqueadas");
   int count=apply?eligible.Count:sequence.clips.Where(c=>eligible.Contains(c.id)).Sum(c=>c.effects.Count(e=>e.effect_id==contract.id));
   editor.Commit(p=>{foreach(var clip in p.Active.clips.Where(c=>eligible.Contains(c.id))){if(apply)clip.effects.Add(create(p));else clip.effects.RemoveAll(e=>e.effect_id==contract.id);}});
   Note(plan,(apply?"Aplicar ":"Quitar ")+NameOf(contract),count,skipped);
  }
  static List<Tuple<string,string>> Junctions(Sequence sequence){
   var cuts=new List<Tuple<string,string>>();
   foreach(var track in sequence.tracks.OrderBy(t=>t.kind=="video"?0:1).ThenBy(t=>t.order)){
    var clips=sequence.clips.Where(c=>c.track_id==track.id).OrderBy(c=>c.timeline_start.Seconds).ToList();
    for(int i=0;i+1<clips.Count;i++)if(Math.Abs(clips[i].End-clips[i+1].timeline_start.Seconds)<0.00001)cuts.Add(Tuple.Create(clips[i].id,clips[i+1].id));
   }
   return cuts;
  }
  static void Transition(Editor editor,InstructionAction action,InstructionPlan plan,bool apply){
   var sequence=SequenceOf(editor.Project,action);editor.Project.active_sequence_id=sequence.id;
   Require(action.scope=="sequence"||action.scope=="junction","La transición requiere alcance de secuencia o corte");
   if(apply){
    var contract=TransitionContracts.All.FirstOrDefault(c=>c.id==action.catalog_id);Require(contract!=null,"Transición desconocida: "+action.catalog_id);
    int frames=action.duration_frames??30;Require(frames>=2&&frames<=600,"Duración de transición fuera de rango");
    var cuts=action.scope=="junction"?new List<Tuple<string,string>>{Tuple.Create(action.left_clip_id,action.right_clip_id)}:Junctions(sequence);
    int done=0,skipped=0;var handled=new HashSet<string>();
    foreach(var cut in cuts){
     if(handled.Contains(cut.Item1+":"+cut.Item2))continue;
     var left=editor.Project.Active.clips.Find(c=>c.id==cut.Item1);var right=editor.Project.Active.clips.Find(c=>c.id==cut.Item2);
     if(left==null||right==null){if(action.scope=="junction")throw new Exception("El corte indicado no existe");skipped++;plan.Lines.Add("Omitido corte: uno de los clips ya no existe");continue;}
     var track=TrackOf(editor.Project.Active,left.track_id);
     var linkedLeft=TransitionContracts.LinkedAudio(editor.Project.Active,left);var linkedRight=TransitionContracts.LinkedAudio(editor.Project.Active,right);
     bool linked=track.kind=="video"&&linkedLeft!=null&&linkedRight!=null&&linkedLeft.track_id==linkedRight.track_id&&Math.Abs(linkedLeft.End-linkedRight.timeline_start.Seconds)<0.00001;
     int max=TransitionContracts.MaxLinkedFrames(editor.Project,editor.Project.Active,left,right,contract.id);
     if(track.locked||track.kind=="audio"&&contract.input_kind=="video"||max<frames||linked&&TrackOf(editor.Project.Active,linkedLeft.track_id).locked){if(action.scope=="junction")throw new Exception("El corte no admite esa transición o duración");skipped++;plan.Lines.Add("Omitido corte «"+left.name+" → "+right.name+"»: "+(track.locked||linked&&TrackOf(editor.Project.Active,linkedLeft.track_id).locked?"pista bloqueada":track.kind=="audio"&&contract.input_kind=="video"?"transición solo visual":"margen insuficiente"));continue;}
     editor.SetTransition(left.id,right.id,contract.id,frames);done++;
     handled.Add(left.id+":"+right.id);if(linked)handled.Add(linkedLeft.id+":"+linkedRight.id);
    }
    Require(done>0,"No hay cortes compatibles para "+contract.name);Note(plan,"Aplicar "+contract.name,done,skipped);return;
   }
   Require(!String.IsNullOrWhiteSpace(action.catalog_id),"Indica la transición que quieres quitar o * para todas");
   var matches=sequence.transitions.Where(t=>(action.catalog_id=="*"||t.transition_id==action.catalog_id)&&(action.scope=="sequence"||t.left_clip_id==action.left_clip_id&&t.right_clip_id==action.right_clip_id)).ToList();
   Require(matches.Count>0,"No hay transiciones coincidentes para quitar");
   var groups=new HashSet<string>();int removed=0;
   foreach(var t in matches){string group=t.group_id??t.id;if(!groups.Add(group)||!editor.Project.Active.transitions.Any(x=>x.id==t.id))continue;var left=editor.Project.Active.clips.Find(c=>c.id==t.left_clip_id);Require(left!=null&&!TrackOf(editor.Project.Active,left.track_id).locked,"Hay una transición en pista bloqueada");editor.RemoveTransition(t.id);removed++;}
   Note(plan,"Quitar transiciones",removed,0);
  }
  static void RequirePosition(InstructionAction action){Require(action.at_seconds.HasValue&&!Double.IsNaN(action.at_seconds.Value)&&!Double.IsInfinity(action.at_seconds.Value)&&action.at_seconds.Value>=0,"Falta una posición válida en segundos");}
  static void RequireEmpty(Sequence sequence,List<Clip> incoming,IEnumerable<string> exclude){var ids=new HashSet<string>(exclude);foreach(var moved in incoming)if(sequence.clips.Any(c=>!ids.Contains(c.id)&&c.track_id==moved.track_id&&c.timeline_start.Seconds<moved.End-0.000001&&c.End>moved.timeline_start.Seconds+0.000001))throw new Exception("El destino está ocupado; elige sobrescribir o insertar");}
  static void ClipAction(Editor editor,InstructionAction action,InstructionPlan plan){
   var sequence=SequenceOf(editor.Project,action);editor.Project.active_sequence_id=sequence.id;
   var clip=ClipOf(sequence,action.clip_id);var track=TrackOf(sequence,clip.track_id);Require(!track.locked,"La pista de origen está bloqueada");
   if(action.op=="clip.inherit_track_effects"){
    Require(action.value.HasValue,"Indica activar o desactivar la herencia");Require(!track.locked,"La pista está bloqueada");editor.Commit(p=>p.Active.clips.Find(c=>c.id==clip.id).inherit_track_effects=action.value.Value);Note(plan,"Cambiar efectos de pista de "+clip.name,1,0);return;
   }
   if(action.op=="clip.trim"){
    Require(action.edge=="left"||action.edge=="right","Indica borde izquierdo o derecho");Require(action.ripple.HasValue,"Indica si el recorte hace rizo o deja hueco");Require(action.duration_seconds.HasValue&&action.duration_seconds.Value>0&&!Double.IsNaN(action.duration_seconds.Value)&&!Double.IsInfinity(action.duration_seconds.Value),"Indica una duración positiva");
    bool left=action.edge=="left";double edge=left?clip.End-action.duration_seconds.Value:clip.timeline_start.Seconds+action.duration_seconds.Value;
    double limit=editor.TrimEdge(clip.id,left,edge);Require(Math.Abs(limit-edge)<1.0/48000,"El medio no tiene material suficiente para esa duración");
    if(action.ripple==true)editor.RippleTrim(clip.id,left,edge);else editor.Trim(clip.id,left,edge);
    Note(plan,"Recortar "+clip.name,1,0);return;
   }
   RequirePosition(action);Require(!String.IsNullOrWhiteSpace(action.track_id),"Indica la pista de destino");var destination=TrackOf(sequence,action.track_id);
   Require(!destination.locked&&destination.kind==track.kind,"La pista de destino está bloqueada o es incompatible");
   if(action.op=="clip.move"){
    Require(action.placement=="empty"||action.placement=="overwrite"||action.placement=="insert","Indica empty, overwrite o insert para el destino");
    if(action.placement=="empty"){
     var related=editor.Related(clip.id);double delta=action.at_seconds.Value-clip.timeline_start.Seconds;
     var incoming=sequence.clips.Where(c=>related.Contains(c.id)).Select(c=>new Clip{track_id=c.id==clip.id?destination.id:c.track_id,timeline_start=Rational.Time(c.timeline_start.Seconds+delta),duration=c.duration}).ToList();RequireEmpty(sequence,incoming,related);
    }
    if(action.placement=="insert")editor.InsertSelection(new List<string>{clip.id},clip.id,action.at_seconds.Value,destination.id);
    else editor.MoveSelection(new List<string>{clip.id},clip.id,action.at_seconds.Value,destination.id);
    Note(plan,"Mover "+clip.name,1,0);return;
   }
   Require(action.placement=="empty"||action.placement=="overwrite","Duplicar admite empty u overwrite");
   var ids=editor.Related(clip.id);var originals=sequence.clips.Where(c=>ids.Contains(c.id)).ToList();double offset=action.at_seconds.Value-clip.timeline_start.Seconds;
   var copies=originals.Select(c=>Store.Json().Deserialize<Clip>(Store.Json().Serialize(Store.Fields(c)))).ToList();
   foreach(var copy in copies){copy.id=Store.Id();copy.timeline_start=Rational.Time(copy.timeline_start.Seconds+offset);if(copy.track_id==clip.track_id)copy.track_id=destination.id;Require(copy.timeline_start.Seconds>=0,"El duplicado quedaría antes del inicio");}
   if(action.placement=="empty")RequireEmpty(sequence,copies,new string[0]);
   editor.Commit(p=>{foreach(var c in copies)p.Active.clips.Add(c);if(copies.Count>1)p.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=copies.Select(c=>c.id).ToList()});if(action.placement=="overwrite")Editor.Overwrite(p,copies.Select(c=>c.id));});
   Note(plan,"Duplicar "+clip.name,copies.Count,0);
  }
  public static InstructionPlan Preview(Project project,InstructionDocument document){return Preview(project,document,null);}
  static bool IsPositionSensitive(InstructionAction action){
   if(action==null)return false;
   switch(action.op){
     case "clip.move":case "clip.trim":case "clip.duplicate":case "clip.cut":case "clip.range_trim":case "range.trim":case "clip.roll":case "gap.close":case "transition.apply":case "transition.remove":case "transition.set_duration":case "clip.set_speed":case "text.create":case "subtitle.create":case "marker.create":case "marker.move":case "media.place_asset":case "media.import_local":case "sequence.set_export_in":case "sequence.set_export_out":case "curve.set_keyframe":case "curve.set_tangent":case "mask.set_keyframe":return true;
    case "clip.delete":return action.ripple==true;
    case "media.import_url":return action.at_seconds.HasValue;
    default:return false;
   }
  }
  static void GuardContext(InstructionDocument document,Project before,Project after,InstructionAction action){
   if(document.allow_outside_context==true)return;
   if(document.context_track_ids!=null){
    var oldSequence=before.sequences.Find(s=>s.id==document.context_sequence_id);var newSequence=after.sequences.Find(s=>s.id==document.context_sequence_id);
    Require(oldSequence!=null&&newSequence!=null&&before.sequences.Count==after.sequences.Count&&before.active_sequence_id==after.active_sequence_id,"El contexto de pistas no permite cambiar de secuencia");
    Require(oldSequence.name==newSequence.name&&oldSequence.width==newSequence.width&&oldSequence.height==newSequence.height&&oldSequence.fps.n==newSequence.fps.n&&oldSequence.fps.d==newSequence.fps.d&&oldSequence.color_space==newSequence.color_space,"La acción modifica ajustes de toda la secuencia");
    Require(EncodeComparable(Markers.Read(oldSequence))==EncodeComparable(Markers.Read(newSequence)),"Los marcadores son globales a la secuencia");
    var allowed=new HashSet<string>(document.context_track_ids,StringComparer.Ordinal);Require(allowed.All(id=>oldSequence.tracks.Any(t=>t.id==id)),"Una pista del contexto ya no existe");
    foreach(var old in oldSequence.tracks.Where(t=>!allowed.Contains(t.id))){var now=newSequence.tracks.Find(t=>t.id==old.id);Require(now!=null&&EncodeComparable(old)==EncodeComparable(now),"La acción modifica una pista fuera del contexto: "+old.name);}
    foreach(var now in newSequence.tracks.Where(t=>!oldSequence.tracks.Any(old=>old.id==t.id)))Require(allowed.Contains(now.id),"Crear pistas requiere un contexto más amplio");
    foreach(var old in oldSequence.clips.Where(c=>!allowed.Contains(c.track_id))){var now=newSequence.clips.Find(c=>c.id==old.id);Require(now!=null&&EncodeComparable(old)==EncodeComparable(now),"La acción modifica un clip fuera de las pistas elegidas: "+old.name);}
    foreach(var now in newSequence.clips.Where(c=>!oldSequence.clips.Any(old=>old.id==c.id)))Require(allowed.Contains(now.track_id),"El clip nuevo queda fuera de las pistas elegidas");
    foreach(var now in newSequence.clips.Where(c=>oldSequence.clips.Any(old=>old.id==c.id)))if(!allowed.Contains(now.track_id))Require(false,"La acción mueve un clip fuera de las pistas elegidas");
    foreach(var old in oldSequence.transitions.Where(t=>oldSequence.clips.Any(c=>c.id==t.left_clip_id&&!allowed.Contains(c.track_id))||oldSequence.clips.Any(c=>c.id==t.right_clip_id&&!allowed.Contains(c.track_id)))){var now=newSequence.transitions.Find(t=>t.id==old.id);Require(now!=null&&EncodeComparable(old)==EncodeComparable(now),"La acción modifica una transición fuera de las pistas elegidas");}
    foreach(var now in newSequence.transitions.Where(t=>!oldSequence.transitions.Any(old=>old.id==t.id)))Require(newSequence.clips.Any(c=>c.id==now.left_clip_id&&allowed.Contains(c.track_id))&&newSequence.clips.Any(c=>c.id==now.right_clip_id&&allowed.Contains(c.track_id)),"La transición nueva afecta otra pista");
    foreach(var old in Ducking01016.All(oldSequence).Where(r=>!allowed.Contains(r.target_track_id))){var now=Ducking01016.For(newSequence,old.target_track_id);Require(now!=null&&EncodeComparable(now)==EncodeComparable(old),"La acción modifica mezcla de audio fuera de las pistas elegidas");}
    var protectedTrackAssets=new HashSet<string>(oldSequence.clips.Where(c=>!allowed.Contains(c.track_id)).Select(c=>c.asset_id));foreach(var asset in before.assets.Where(a=>protectedTrackAssets.Contains(a.id))){var now=after.assets.Find(a=>a.id==asset.id);Require(now!=null&&EncodeComparable(asset)==EncodeComparable(now),"La acción modifica un medio compartido con otra pista");}
   }
   if(!document.context_in_frame.HasValue)return;
   Require(document.context_sequence_id!=null,"Falta la secuencia del contexto");
   var original=before.sequences.Find(s=>s.id==document.context_sequence_id);
   var updated=after.sequences.Find(s=>s.id==document.context_sequence_id);
   Require(original!=null&&updated!=null,"La secuencia del contexto ya no existe");
   Require(after.sequences.Count==before.sequences.Count&&after.active_sequence_id==before.active_sequence_id,"El contexto In/Out no permite cambiar secuencias sin allow_outside_context");
   double lo=document.context_in_frame.Value/60.0,hi=document.context_out_frame.Value/60.0;
   Func<Clip,bool> inside=c=>c.timeline_start.Seconds>=lo-0.000001&&c.End<=hi+0.000001;
   var oldClips=original.clips.ToDictionary(c=>c.id);
   var newClips=updated.clips.ToDictionary(c=>c.id);
   foreach(var old in oldClips.Values){
    Clip now;bool exists=newClips.TryGetValue(old.id,out now);
    if(!inside(old)){Require(exists&&EncodeComparable(old)==EncodeComparable(now),"La acción modifica un clip fuera del tramo In/Out: "+old.name);}
    else if(exists)Require(inside(now),"El clip quedaría fuera del tramo In/Out: "+old.name);
   }
   foreach(var clip in newClips.Values.Where(c=>!oldClips.ContainsKey(c.id)))Require(inside(clip),"El nuevo clip quedaría fuera del tramo In/Out");
   foreach(var track in original.tracks){var now=updated.tracks.Find(t=>t.id==track.id);Require(now!=null&&EncodeComparable(track)==EncodeComparable(now),"Los cambios de pista requieren allow_outside_context");}
   var addedTracks=updated.tracks.Where(t=>!original.tracks.Any(old=>old.id==t.id)).ToList();
   Require(addedTracks.Count==0||action.op=="subtitle.create"&&addedTracks.Count==1&&addedTracks[0].id==SubtitleTrack0817.TrackId(updated),"Crear una pista requiere allow_outside_context");
   foreach(var transition in original.transitions){var now=updated.transitions.Find(t=>t.id==transition.id);if(transition.cut_frame<document.context_in_frame.Value||transition.cut_frame>=document.context_out_frame.Value)Require(now!=null&&EncodeComparable(transition)==EncodeComparable(now),"La acción afecta una transición fuera del tramo In/Out");}
   foreach(var transition in updated.transitions.Where(t=>!original.transitions.Any(x=>x.id==t.id)))Require(transition.cut_frame>=document.context_in_frame.Value&&transition.cut_frame<document.context_out_frame.Value,"La transición quedaría fuera del tramo In/Out");
   var oldMarkers=Markers.Read(original);var newMarkers=Markers.Read(updated);
   foreach(var marker in oldMarkers.Where(m=>m.Frame<document.context_in_frame.Value||m.Frame>=document.context_out_frame.Value)){var now=newMarkers.Find(m=>m.Id==marker.Id);Require(now!=null&&now.Frame==marker.Frame&&now.Name==marker.Name&&now.ColorArgb==marker.ColorArgb,"La acción afecta un marcador fuera del tramo In/Out");}
   foreach(var marker in newMarkers.Where(m=>!oldMarkers.Any(x=>x.Id==m.Id)))Require(marker.Frame>=document.context_in_frame.Value&&marker.Frame<document.context_out_frame.Value,"El marcador quedaría fuera del tramo In/Out");
   var protectedAssets=new HashSet<string>(original.clips.Where(c=>!inside(c)).Select(c=>c.asset_id));
   foreach(var asset in before.assets.Where(a=>protectedAssets.Contains(a.id))){var now=after.assets.Find(a=>a.id==asset.id);Require(now!=null&&EncodeComparable(asset)==EncodeComparable(now),"La acción modifica un medio usado fuera del tramo In/Out");}
   Require(EncodeComparable(Ducking01016.All(original))==EncodeComparable(Ducking01016.All(updated)),"Cambiar Auto-Ducking de una pista completa requiere allow_outside_context");
  }
  static string EncodeComparable(object value){return Store.Json().Serialize(CanonicalFields(Store.Fields(value)));}
  public static InstructionPlan Preview(Project project,InstructionDocument document,IInstructionMediaAdapter media,Action<int,int> progress=null){
   Require(project!=null&&document!=null&&document.actions!=null&&document.actions.Count>0,"No hay instrucciones para revisar");
   Require(document.format=="prometheus-instructions"&&document.version==1,"Versión de instrucciones no compatible");
   Require(document.project_id==project.id,"El proyecto no coincide con el de las instrucciones");
   Require(document.actions.Count<=200,"Demasiadas instrucciones en un lote");
   var editor=new Editor();editor.Reset(Store.Copy(project));var plan=new InstructionPlan{DocumentJson=Encode(document),ReviewedProjectId=project.id,ReviewedRevision=project.revision};string finalActive=project.active_sequence_id;
   var failedLinks=new Dictionary<string,Dictionary<string,List<int>>>(StringComparer.Ordinal);
   for(int i=0;i<document.actions.Count;i++){
    if(progress!=null)progress(i,document.actions.Count);
    var a=document.actions[i];var card=new InstructionCardReview();plan.Cards.Add(card);
    var beforeProject=Store.Copy(editor.Project);
    int oldLines=plan.Lines.Count,oldChanged=plan.ChangedCount,oldSkipped=plan.SkippedCount,oldRedownload=plan.RedownloadAssetIds.Count,oldMgt=plan.MgtPreviewAssetIds.Count;
    var oldReferences=new Dictionary<string,string>(plan.References,StringComparer.Ordinal);
    try{
     Require(a!=null&&IsKnownOperation(a.op),"Acción desconocida");
     var resolved=ResolveReferences(a,plan.References);
     var beforeIds=SnapshotIds(editor.Project);
     if(IsExtended(resolved.op))ApplyExtended(editor,resolved,plan,media);
     else if(resolved.op=="effect.apply"||resolved.op=="effect.remove")Effect(editor,resolved,plan,resolved.op=="effect.apply");
     else if(resolved.op=="transition.apply"||resolved.op=="transition.remove")Transition(editor,resolved,plan,resolved.op=="transition.apply");
     else ClipAction(editor,resolved,plan);
     GuardContext(document,beforeProject,editor.Project,resolved);
     if(!IsExtended(resolved.op))CaptureResult(editor.Project,resolved,plan,beforeIds);
     if(resolved.op=="sequence.create"||resolved.op=="sequence.activate")finalActive=editor.Project.active_sequence_id;
     card.changed=plan.ChangedCount-oldChanged;card.skipped=plan.SkippedCount-oldSkipped;
     card.details.AddRange(plan.Lines.Skip(oldLines));
     if(card.changed==0)throw new Exception(card.details.Count>0?String.Join(" · ",card.details):"No produce cambios aplicables");
     bool stalePosition=document.revision!=project.revision&&IsPositionSensitive(resolved);
     if(stalePosition)card.details.Add("La posición se preparó en la revisión "+document.revision+" y se comprobó en la "+project.revision+". Comprueba los clips afectados antes de aplicar.");
     card.status=card.skipped>0||stalePosition?"partial":"valid";
     card.summary=card.skipped>0?card.changed+" destino(s) compatibles; "+card.skipped+" omitido(s)":stalePosition?card.changed+" cambio(s) aplicables; revisa la posición":card.changed+" cambio(s) aplicables";
    }catch(Exception ex){
     editor.Reset(beforeProject);plan.ChangedCount=oldChanged;plan.SkippedCount=oldSkipped;
     if(plan.RedownloadAssetIds.Count>oldRedownload)plan.RedownloadAssetIds.RemoveRange(oldRedownload,plan.RedownloadAssetIds.Count-oldRedownload);
     if(plan.MgtPreviewAssetIds.Count>oldMgt)plan.MgtPreviewAssetIds.RemoveRange(oldMgt,plan.MgtPreviewAssetIds.Count-oldMgt);
     plan.Lines.RemoveRange(oldLines,plan.Lines.Count-oldLines);plan.References.Clear();foreach(var pair in oldReferences)plan.References.Add(pair.Key,pair.Value);
     card.status="invalid";card.summary=ex.Message;card.changed=0;card.skipped=0;card.details.Clear();card.details.Add(ex.Message);
     if(a!=null&&(a.op=="media.import_url"||a.op=="placeholder.fill_url")&&!String.IsNullOrWhiteSpace(a.url)){
      Dictionary<string,List<int>> reasons;if(!failedLinks.TryGetValue(a.url,out reasons)){reasons=new Dictionary<string,List<int>>(StringComparer.Ordinal);failedLinks[a.url]=reasons;}
      List<int> positions;if(!reasons.TryGetValue(ex.Message,out positions)){positions=new List<int>();reasons[ex.Message]=positions;}positions.Add(i+1);
     }else plan.Lines.Add("Instrucción "+(i+1)+" omitida: "+ex.Message);
    }
   }
   foreach(var link in failedLinks)foreach(var reason in link.Value)plan.Lines.Add("Enlace "+link.Key+" · instrucción"+(reason.Value.Count==1?" ":"es ")+String.Join(", ",reason.Value)+" omitida"+(reason.Value.Count==1?"":"s")+": "+reason.Key);
   if(progress!=null)progress(document.actions.Count,document.actions.Count);
   editor.Project.active_sequence_id=finalActive;Store.Validate(editor.Project);plan.Result=editor.Project;DescribeChanges(project,plan);return plan;
  }
  public static void Apply(Editor editor,InstructionDocument document,InstructionPlan plan){Apply(editor,document,plan,null);}
  public static void Apply(Editor editor,InstructionDocument document,InstructionPlan plan,IInstructionMediaAdapter media,Action<int,int> progress=null){
   Require(editor!=null&&plan!=null&&plan.Result!=null,"Revisa primero las instrucciones");
   Require(plan.DocumentJson==Encode(document),"Las tarjetas cambiaron después de la revisión. Pulsa Revisar otra vez");
   Require(editor.Project.id==document.project_id&&editor.Project.id==plan.ReviewedProjectId,"El proyecto no coincide con el de las instrucciones");
   Require(editor.Project.revision==plan.ReviewedRevision,"El proyecto cambió después de la revisión. Revisa las tarjetas otra vez");
   // A background proxy or web resolution can change derived media without advancing revision.
   // Rebuild on the latest project so applying the reviewed actions cannot discard that media.
   var fresh=Preview(editor.Project,document,media,progress);
   Require(fresh.ChangedCount>0,"No hay instrucciones aplicables");
   Require(plan.Cards.Count==fresh.Cards.Count&&plan.Cards.Zip(fresh.Cards,(a,b)=>a.status==b.status&&a.changed==b.changed&&a.skipped==b.skipped).All(x=>x),"La revisión cambió. Revisa las instrucciones de nuevo");
   editor.Commit(p=>{p.name=fresh.Result.name;p.assets=fresh.Result.assets;p.effect_resources=fresh.Result.effect_resources;p.sequences=fresh.Result.sequences;p.active_sequence_id=fresh.Result.active_sequence_id;p.extensions=fresh.Result.extensions;});
  }
 }
}
