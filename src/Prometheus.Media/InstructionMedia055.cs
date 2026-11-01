using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Prometheus {
 public sealed class InstructionMediaAdapter:IInstructionMediaAdapter {
  readonly Dictionary<string,Asset> prepared=new Dictionary<string,Asset>(StringComparer.Ordinal);
  readonly Dictionary<string,string> failures=new Dictionary<string,string>(StringComparer.Ordinal);
  readonly Dictionary<string,Asset> resolvedUrls=new Dictionary<string,Asset>(StringComparer.Ordinal);
  readonly Dictionary<string,Asset> preparedLocal=new Dictionary<string,Asset>(StringComparer.OrdinalIgnoreCase);
  readonly List<string> added=new List<string>();
  public IEnumerable<string> AddedAssetIds {get{return added.Distinct().ToList();}}
  static string Key(string url,string kind){return kind+"|"+url.Trim();}
  static string Kind(Project project,InstructionAction action){
   if(action.op=="media.import_url"){
    if(action.source_kind!="video"&&action.source_kind!="audio"&&action.source_kind!="image")throw new Exception("Importar enlace requiere source_kind video, image o audio");return action.source_kind;
   }
   if(action.source_kind!=null&&action.source_kind!="video"&&action.source_kind!="image"&&action.source_kind!="audio")throw new Exception("source_kind debe ser video, image o audio");
   var sequence=project.sequences.Find(s=>s.id==(action.sequence_id??project.active_sequence_id));
   var clip=sequence==null?null:sequence.clips.Find(c=>c.id==action.clip_id);
   if(clip==null)throw new Exception("Espacio reservado inexistente: "+action.clip_id);
   var track=sequence.tracks.Find(t=>t.id==clip.track_id);
   bool linked=action.include_linked!=false&&sequence.link_groups.Any(g=>g.clip_ids.Contains(clip.id)&&g.clip_ids.Any(id=>sequence.clips.Any(c=>c.id==id&&sequence.tracks.Any(t=>t.id==c.track_id&&t.kind=="video"))));
   if(action.source_kind!=null){
    if(action.source_kind=="audio"&&(track.kind!="audio"||linked))throw new Exception("Para conectar solo audio, elige un espacio de audio y desactiva incluir vinculados");
    if(action.source_kind!="audio"&&track.kind=="audio"&&!linked)throw new Exception("Un enlace visual no puede ocupar un espacio reservado de audio");
    return action.source_kind;
   }
   return track.kind=="audio"&&!linked?"audio":"video";
  }
  public void Prepare(Project project,InstructionDocument document,CancellationToken token){
   foreach(var a in document.actions.Where(x=>x.op=="media.import_local")){
    token.ThrowIfCancellationRequested();string path=a.resource_path??"";
    if(preparedLocal.ContainsKey(path)||failures.ContainsKey("local|"+path))continue;
    try{if(!File.Exists(path))throw new Exception("Archivo local inexistente: "+path);preparedLocal[path]=MgtMedia.IsFile(path)?MgtMedia.Inspect(path,token):Media.Inspect(path,token);}
    catch(OperationCanceledException){throw;}catch(Exception ex){failures["local|"+path]=ex.Message;}
   }
   foreach(var a in document.actions.Where(x=>x.op=="media.import_url"||x.op=="placeholder.fill_url")){
    token.ThrowIfCancellationRequested();string kind;
    try{kind=Kind(project,a);}catch{continue;}
    string key=Key(a.url??"",kind);if(prepared.ContainsKey(key)||failures.ContainsKey(key))continue;
    try{
     if(String.IsNullOrWhiteSpace(a.url)||!WebMedia.IsImageUrl(a.url))throw new Exception("Enlace web no compatible: "+a.url);
     var known=project.assets.FirstOrDefault(x=>x.source!=null&&x.source.kind=="web"&&x.source.uri==a.url&&WebMedia.IsAudioOnly(x)==(kind=="audio")&&(kind!="image"||x.streams.Any(s=>s.kind=="image"))&&!(a.op=="placeholder.fill_url"&&a.source_kind=="video"&&x.streams.Any(s=>s.kind=="image")));
     if(known!=null){prepared[key]=known;continue;}
     if(kind=="audio"){prepared[key]=WebMedia.InspectAudio(a.url,token);continue;}
     Asset resolved;
     if(!resolvedUrls.TryGetValue(a.url,out resolved)){
      string expected=kind=="image"?"image":a.op=="placeholder.fill_url"&&a.source_kind=="video"?"video":null;
      resolved=WebMedia.InspectDirectLink(a.url,a.url,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","WebCache"),expected,token);
      resolvedUrls[a.url]=resolved;
     }
     if(kind=="image"&&!resolved.streams.Any(s=>s.kind=="image"))throw new Exception("La instrucción declara imagen, pero el enlace devuelve vídeo");
     if(a.op=="placeholder.fill_url"&&a.source_kind=="video"&&resolved.streams.Any(s=>s.kind=="image"))throw new Exception("La instrucción declara vídeo, pero el enlace devuelve una imagen");
     prepared[key]=resolved;
    }catch(OperationCanceledException){throw;}catch(Exception ex){failures[key]=ex.Message;}
   }
  }
  public void Apply(Editor editor,InstructionAction a,InstructionPlan plan){
   if(a.op=="media.import_local"){ImportLocal(editor,a,plan);return;}
   if(a.op=="media.place_asset"){PlaceAsset(editor,a,plan);return;}
   if(a.op=="mgt.set_parameter"){SetMgt(editor,a,plan);return;}
   if(a.op=="media.import_shelf"){ApplyShelf(editor,a,plan);return;}
   var sequence=editor.Project.sequences.Find(s=>s.id==(a.sequence_id??editor.Project.active_sequence_id));
   if(sequence==null)throw new Exception("Secuencia inexistente para el enlace");editor.Project.active_sequence_id=sequence.id;
   string kind=a.op=="media.import_url"?a.source_kind:Kind(editor.Project,a);
   string failure;if(failures.TryGetValue(Key(a.url??"",kind),out failure))throw new Exception(failure);
   Asset inspected;if(!prepared.TryGetValue(Key(a.url,kind),out inspected))throw new Exception("Revisa el enlace antes de aplicar instrucciones");
   var existing=editor.Project.assets.FirstOrDefault(x=>x.source!=null&&x.source.kind=="web"&&x.source.uri==inspected.source.uri&&WebMedia.IsAudioOnly(x)==WebMedia.IsAudioOnly(inspected)&&x.streams.Any(s=>s.kind=="image")==inspected.streams.Any(s=>s.kind=="image"));
   string assetId=existing==null?inspected.id:existing.id;
   if(a.op=="placeholder.fill_url"){
    editor.Commit(p=>{
     if(existing==null)p.assets.Add(inspected);
     var target=p.Active.clips.Find(c=>c.id==a.clip_id);if(target==null)throw new Exception("Espacio reservado inexistente");
     var ids=new List<string>{target.id};
     if(a.include_linked!=false){var group=p.Active.link_groups.Find(g=>g.clip_ids.Contains(target.id));if(group!=null)ids=group.clip_ids.ToList();}
     if(inspected.streams.Any(s=>s.kind=="image"))ids.RemoveAll(id=>p.Active.tracks.Any(t=>t.id==p.Active.clips.Find(c=>c.id==id).track_id&&t.kind=="audio"));
     if(ids.Count==0)throw new Exception("Una imagen no puede ocupar un espacio reservado de audio");
     foreach(var id in ids)PlaceholderReplacement.Replace(p,id,assetId,a.source_in_seconds??0);
    });
    if(existing==null)added.Add(assetId);
    plan.ChangedCount++;plan.Lines.Add("Conectar enlace a "+(a.include_linked==false?"un espacio reservado":"espacio reservado y componentes vinculados")+": "+a.clip_id.Substring(0,Math.Min(8,a.clip_id.Length)));
    if(!String.IsNullOrWhiteSpace(a.result))BindResult(plan,a.result,assetId);return;
   }
   if(existing==null){editor.Commit(p=>p.assets.Add(inspected));added.Add(assetId);}
   if(!String.IsNullOrWhiteSpace(a.track_id)){
    var track=editor.Project.Active.tracks.Find(t=>t.id==a.track_id);
    if(track==null||track.locked||track.kind!=(kind=="audio"?"audio":"video"))throw new Exception("Pista incompatible o bloqueada para el enlace");
    double at=a.at_seconds??0;if(at<0)throw new Exception("La posición debe ser positiva");
    if(a.placement!=null&&a.placement!="empty"&&a.placement!="overwrite"&&a.placement!="insert")throw new Exception("Destino ocupado inválido");
    double? selectedDuration=a.duration_seconds;
    if(a.source_in_seconds.HasValue&&selectedDuration==null){var stream=inspected.streams.FirstOrDefault(s=>track.kind=="audio"?s.kind=="audio":s.kind=="video"||s.kind=="image");if(stream!=null&&stream.duration!=null)selectedDuration=stream.duration.Seconds-a.source_in_seconds.Value;}
    if(selectedDuration.HasValue&&selectedDuration.Value<=0)throw new Exception("El tramo solicitado queda fuera del medio");
    PlacementResult placement=null;
    editor.Commit(p=>placement=Media.PlaceInFreeTrack(p,assetId,a.track_id,at,a.source_in_seconds,selectedDuration));
    if(a.placement=="overwrite")editor.MoveSelection(new List<string>{placement.PrimaryClipId},placement.PrimaryClipId,at,a.track_id);
    else if(a.placement=="insert")editor.InsertSelection(new List<string>{placement.PrimaryClipId},placement.PrimaryClipId,at,a.track_id);
    if(!String.IsNullOrWhiteSpace(a.result))BindResult(plan,a.result,placement.PrimaryClipId);
    plan.ChangedCount++;plan.Lines.Add("Importar enlace y colocar en "+track.name+" a "+at.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+" s");
   }else{
    if(!String.IsNullOrWhiteSpace(a.result))BindResult(plan,a.result,assetId);
    if(existing==null){plan.ChangedCount++;plan.Lines.Add("Importar enlace a medios: "+inspected.name);}
    else{plan.SkippedCount++;plan.Lines.Add("El enlace ya figura en medios: "+existing.name);}
   }
  }
  void ImportLocal(Editor editor,InstructionAction a,InstructionPlan plan){
   string path=a.resource_path??"",failure;if(failures.TryGetValue("local|"+path,out failure))throw new Exception(failure);
   Asset inspected;if(!preparedLocal.TryGetValue(path,out inspected))throw new Exception("Revisa el archivo local antes de aplicar instrucciones");
   var sequence=editor.Project.sequences.Find(s=>s.id==(a.sequence_id??editor.Project.active_sequence_id));if(sequence==null)throw new Exception("Secuencia inexistente");editor.Project.active_sequence_id=sequence.id;
   var existing=editor.Project.assets.FirstOrDefault(x=>x.source!=null&&x.source.kind==inspected.source.kind&&String.Equals(x.source.uri,inspected.source.uri,StringComparison.OrdinalIgnoreCase));
   string id=existing==null?inspected.id:existing.id;if(existing==null){editor.Commit(p=>p.assets.Add(inspected));added.Add(id);}
   if(String.IsNullOrWhiteSpace(a.track_id)){if(!String.IsNullOrWhiteSpace(a.result))BindResult(plan,a.result,id);if(existing==null){plan.ChangedCount++;plan.Lines.Add("Importar archivo local: "+inspected.name);}else{plan.SkippedCount++;plan.Lines.Add("El archivo ya figura en Medios");}return;}
   var place=new InstructionAction{op="media.place_asset",sequence_id=sequence.id,asset_id=id,track_id=a.track_id,at_seconds=a.at_seconds,source_in_seconds=a.source_in_seconds,duration_seconds=a.duration_seconds,placement=a.placement,result=a.result};
   PlaceAsset(editor,place,plan);
  }
  void PlaceAsset(Editor editor,InstructionAction a,InstructionPlan plan){
   var sequence=editor.Project.sequences.Find(s=>s.id==(a.sequence_id??editor.Project.active_sequence_id));
   if(sequence==null)throw new Exception("Secuencia inexistente");editor.Project.active_sequence_id=sequence.id;
   var asset=editor.Project.assets.Find(x=>x.id==a.asset_id);if(asset==null)throw new Exception("Medio inexistente: "+a.asset_id);
   var track=sequence.tracks.Find(t=>t.id==a.track_id);if(track==null||track.locked||!asset.streams.Any(st=>track.kind=="audio"?st.kind=="audio":st.kind=="video"||st.kind=="image"))throw new Exception("Pista incompatible o bloqueada");
   if(a.placement!=null&&a.placement!="empty"&&a.placement!="overwrite"&&a.placement!="insert")throw new Exception("Destino ocupado inválido");
   if(!a.at_seconds.HasValue||a.at_seconds.Value<0)throw new Exception("Posición inválida");
   PlacementResult placed=null;editor.Commit(p=>placed=Media.PlaceInFreeTrack(p,a.asset_id,a.track_id,a.at_seconds.Value,a.source_in_seconds,a.duration_seconds));
   if(a.placement=="overwrite")editor.MoveSelection(new List<string>{placed.PrimaryClipId},placed.PrimaryClipId,a.at_seconds.Value,a.track_id);
   else if(a.placement=="insert")editor.InsertSelection(new List<string>{placed.PrimaryClipId},placed.PrimaryClipId,a.at_seconds.Value,a.track_id);
   if(!String.IsNullOrWhiteSpace(a.result))BindResult(plan,a.result,placed.PrimaryClipId);plan.ChangedCount++;plan.Lines.Add("Colocar medio en "+track.name);
  }
  void SetMgt(Editor editor,InstructionAction a,InstructionPlan plan){
   var sequence=editor.Project.sequences.Find(s=>s.id==(a.sequence_id??editor.Project.active_sequence_id));if(sequence==null)throw new Exception("Secuencia inexistente");editor.Project.active_sequence_id=sequence.id;
   var clip=sequence.clips.Find(c=>c.id==a.clip_id);if(clip==null||sequence.tracks.Find(t=>t.id==clip.track_id).locked)throw new Exception("Clip MGT inexistente o pista bloqueada");
   var original=editor.Project.assets.Find(x=>x.id==clip.asset_id);if(!MgtMedia.IsMgt(original))throw new Exception("El clip no es MGT");
   var spec=MgtMedia.Parameters(original).OfType<Dictionary<string,object>>().SelectMany(g=>{object raw;return g.TryGetValue("parameters",out raw)&&raw is System.Collections.IEnumerable?((System.Collections.IEnumerable)raw).Cast<object>().OfType<Dictionary<string,object>>():Enumerable.Empty<Dictionary<string,object>>();}).FirstOrDefault(p=>p.ContainsKey("id")&&Convert.ToString(p["id"])==a.parameter_id);
   if(spec==null||spec.ContainsKey("editable")&&spec["editable"] is bool&&!(bool)spec["editable"])throw new Exception("Parámetro MGT no editable");
   string type=spec.ContainsKey("type")?Convert.ToString(spec["type"]):"";object value;
   if(type=="boolean"){if(!a.value.HasValue)throw new Exception("Indica value true o false");value=a.value.Value;}
   else if(type=="number"){if(!a.number.HasValue)throw new Exception("Indica number");value=a.number.Value;}
   else {if(a.text==null)throw new Exception("Indica text");value=a.text;}
   string changed=null;editor.Commit(project=>{
    var targetClip=project.Active.clips.Find(c=>c.id==a.clip_id);var source=project.assets.Find(x=>x.id==targetClip.asset_id);
    var related=new HashSet<string>{targetClip.id};foreach(var group in project.Active.link_groups.Where(g=>g.clip_ids.Contains(targetClip.id)))foreach(string id in group.clip_ids)related.Add(id);
    var followers=project.sequences.SelectMany(s=>s.clips).Where(c=>c.asset_id==source.id).ToList();Asset target=source;
    if(!MgtMedia.IsInstance(source)||followers.Any(c=>!related.Contains(c.id))){
     target=Store.Json().Deserialize<Asset>(Store.Json().Serialize(Store.Fields(source)));target.id=Store.Id();var streams=new Dictionary<string,string>();foreach(var stream in target.streams){string old=stream.id;stream.id=Store.Id();streams[old]=stream.id;}
     foreach(var linked in project.Active.clips.Where(c=>related.Contains(c.id)&&c.asset_id==source.id)){linked.asset_id=target.id;linked.stream_id=streams[linked.stream_id];}
     target.representations.Clear();MgtMedia.Metadata(target).payload["instance"]=true;project.assets.Add(target);
    }
    MgtMedia.SetOverride(target,a.parameter_id,value);MgtMedia.InvalidateSam2(project,target.id);changed=target.id;
   });
   if(!plan.MgtPreviewAssetIds.Contains(changed))plan.MgtPreviewAssetIds.Add(changed);plan.ChangedCount++;plan.Lines.Add("Cambiar parámetro MGT "+a.parameter_id);
  }
  void ApplyShelf(Editor editor,InstructionAction a,InstructionPlan plan){
   var sequence=editor.Project.sequences.Find(s=>s.id==(a.sequence_id??editor.Project.active_sequence_id));
   if(sequence==null)throw new Exception("Secuencia inexistente para la Bandeja");
   var item=ShelfStore.Read(editor.Project).FirstOrDefault(x=>x.id==a.shelf_id);
   if(item==null)throw new Exception("El elemento ya no está en la Bandeja: "+a.shelf_id);
   var track=String.IsNullOrWhiteSpace(a.track_id)?null:sequence.tracks.Find(t=>t.id==a.track_id);
   if(a.track_id!=null&&(track==null||track.locked||!item.asset.streams.Any(s=>track.kind=="audio"?s.kind=="audio":s.kind!="audio")))throw new Exception("Pista incompatible o bloqueada para el elemento de Bandeja");
   if(a.placement!=null&&a.placement!="empty"&&a.placement!="overwrite"&&a.placement!="insert")throw new Exception("Destino ocupado inválido");
   double at=a.at_seconds??0;if(at<0)throw new Exception("La posición debe ser positiva");
   string assetId=null,clipId=null;bool wasNew=false;
   editor.Commit(p=>{
    p.active_sequence_id=sequence.id;
    wasNew=!p.assets.Any(x=>x.source!=null&&String.Equals(x.source.uri,item.url,StringComparison.OrdinalIgnoreCase));
    var asset=ShelfStore.Promote(p,item);assetId=asset.id;
    if(track!=null){var placed=Media.PlaceInFreeTrack(p,assetId,track.id,at,item.range_in,item.range_in.HasValue?item.range_out-item.range_in:null);clipId=placed.PrimaryClipId;}
    ShelfStore.Remove(p,new[]{item.id});
   });
   if(track!=null){
    if(a.placement=="overwrite")editor.MoveSelection(new List<string>{clipId},clipId,at,track.id);
    else if(a.placement=="insert")editor.InsertSelection(new List<string>{clipId},clipId,at,track.id);
   }
   if(wasNew)added.Add(assetId);
   if(!String.IsNullOrWhiteSpace(a.result))BindResult(plan,a.result,clipId??assetId);
   plan.ChangedCount++;plan.Lines.Add("Bandeja → "+(track==null?"Medios":track.name+" a "+at.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+" s")+": "+item.asset.name);
  }
  static void BindResult(InstructionPlan plan,string name,string id){
   if(name.Length>64||name.Length==0||name.Any(c=>!Char.IsLetterOrDigit(c)&&c!='_'&&c!='.'&&c!='-')||plan.References.ContainsKey(name))throw new Exception("Nombre de resultado inválido o duplicado: "+name);
   plan.References.Add(name,id);plan.Lines.Add("Resultado $"+name+" disponible para pasos posteriores");
  }
 }
}
