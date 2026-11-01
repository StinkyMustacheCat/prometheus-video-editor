using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;

namespace Prometheus {
 // A junction is the owner of a transition. No FFmpeg expression is stored in a project.
 public sealed class TransitionInstance {
  public string id,transition_id,contract_version,left_clip_id,right_clip_id,group_id,mgt_resource_id;
  public bool enabled=true;
  public int cut_frame,duration_frames=30;
  public Dictionary<string,EffectValue> parameters=new Dictionary<string,EffectValue>();
  // Used only on the in-memory copy prepared for a preview window.
  public bool preview_expanded;
 }
 public sealed class MgtTransitionResource {
  public string id,name,document,external_id;
  public bool catalog_hidden;
  public double minimum_seconds,maximum_seconds,recommended_seconds;
  public List<Dictionary<string,object>> ParameterDefinitions(){
   var raw=Store.Json().Deserialize<Dictionary<string,object>>(document);object value;
   return raw.TryGetValue("parameters",out value)?(value as object[]??new object[0]).OfType<Dictionary<string,object>>().ToList():new List<Dictionary<string,object>>();
  }
  public static MgtTransitionResource FromJson(string json){
   var raw=Store.Json().Deserialize<Dictionary<string,object>>(json);
   if(raw==null||Convert.ToString(raw.ContainsKey("kind")?raw["kind"]:null)!="transition"||Convert.ToInt32(raw.ContainsKey("contractVersion")?raw["contractVersion"]:0)!=1)throw new Exception("Selecciona una transición MGT de contrato v1.");
   var duration=raw.ContainsKey("duration")?raw["duration"] as Dictionary<string,object>:null;
   var recommended=raw.ContainsKey("recommended")?raw["recommended"] as Dictionary<string,object>:null;
   if(duration==null||recommended==null)throw new Exception("La transición MGT no declara su duración.");
   var resource=new MgtTransitionResource{id=Store.Id(),name=Convert.ToString(raw["name"]),external_id=Convert.ToString(raw["id"]),document=json,
    minimum_seconds=Convert.ToDouble(duration["min"]),maximum_seconds=Convert.ToDouble(duration["max"]),recommended_seconds=Convert.ToDouble(recommended["duration"])};
   Validate(resource);return resource;
  }
  public static void Validate(MgtTransitionResource resource){
   if(String.IsNullOrWhiteSpace(resource.name)||String.IsNullOrWhiteSpace(resource.external_id)||String.IsNullOrWhiteSpace(resource.document)||
    resource.minimum_seconds<=0||resource.maximum_seconds<resource.minimum_seconds||resource.recommended_seconds<resource.minimum_seconds||resource.recommended_seconds>resource.maximum_seconds||resource.maximum_seconds>60)
    throw new Exception("Contrato de transición MGT inválido.");
   var raw=Store.Json().Deserialize<Dictionary<string,object>>(resource.document);
   if(raw==null||Convert.ToString(raw.ContainsKey("kind")?raw["kind"]:null)!="transition"||Convert.ToInt32(raw.ContainsKey("contractVersion")?raw["contractVersion"]:0)!=1||Convert.ToString(raw["id"])!=resource.external_id)throw new Exception("La transición MGT no coincide con su manifiesto.");
  }
 }
 public sealed class TransitionContract {
  public string id,version,name,category,input_kind,output_kind;
  public string[] capabilities=new string[0];
  public EffectParameterSpec[] parameters=new EffectParameterSpec[0];
  public bool needs_incoming_handle;
 }
 public static class TransitionContracts {
  public const string Mgt="prometheus.transition.mgt";
  public const string Dissolve="prometheus.transition.dissolve";
  public const string DipBlack="prometheus.transition.dip-black";
  public const string DipWhite="prometheus.transition.dip-white";
  static readonly TransitionContract[] contracts={
   new TransitionContract{id=Dissolve,version="1.0.0",name="Disolución cruzada",category="Básicas",input_kind="video/audio",output_kind="video/audio",capabilities=new[]{"ffmpeg","preview","export","audio","handles"},needs_incoming_handle=true},
   new TransitionContract{id=DipBlack,version="1.0.0",name="Fundido por negro",category="Básicas",input_kind="video/audio",output_kind="video/audio",capabilities=new[]{"ffmpeg","preview","export","audio"}},
   new TransitionContract{id=DipWhite,version="1.0.0",name="Fundido por blanco",category="Básicas",input_kind="video",output_kind="video",capabilities=new[]{"ffmpeg","preview","export"}}
  };
  public static IEnumerable<TransitionContract> All {get{return contracts;}}
  public static TransitionContract Find(string id,string version){return contracts.FirstOrDefault(c=>c.id==id&&c.version==version);}
  public static TransitionInstance At(Sequence sequence,string left,string right){return sequence.transitions.FirstOrDefault(t=>t.left_clip_id==left&&t.right_clip_id==right);}
  public static Clip LinkedAudio(Sequence sequence,Clip clip){
   var link=sequence.link_groups.FirstOrDefault(g=>g.clip_ids.Contains(clip.id));if(link==null)return null;
   return sequence.clips.FirstOrDefault(c=>link.clip_ids.Contains(c.id)&&sequence.tracks.Any(t=>t.id==c.track_id&&t.kind=="audio")&&Math.Abs(c.timeline_start.Seconds-clip.timeline_start.Seconds)<0.00001&&Math.Abs(c.End-clip.End)<0.00001);
  }
  public static int MaxLinkedFrames(Project project,Sequence sequence,Clip left,Clip right,string transitionId){
   int max=MaxFrames(project,sequence,left,right,transitionId);
   if(project.mgt_transitions.Any(r=>r.id==transitionId))return max;
   if(max<2||left==null||right==null)return max;
   var audioLeft=LinkedAudio(sequence,left);var audioRight=LinkedAudio(sequence,right);
   if(audioLeft!=null&&audioRight!=null&&audioLeft.track_id==audioRight.track_id&&Math.Abs(audioLeft.End-audioRight.timeline_start.Seconds)<0.00001)
    max=Math.Min(max,MaxFrames(project,sequence,audioLeft,audioRight,transitionId==DipWhite?DipBlack:transitionId));
   return max;
  }
  public static int MaxFrames(Project project,Sequence sequence,Clip left,Clip right,string transitionId){
   var mgt=project.mgt_transitions.FirstOrDefault(r=>r.id==transitionId);
   if(mgt!=null){
    if(left==null||right==null||left.track_id!=right.track_id||Math.Abs(left.End-right.timeline_start.Seconds)>0.00001)return 0;
    var mgtTrack=sequence.tracks.Find(t=>t.id==left.track_id);if(mgtTrack==null||mgtTrack.kind!="video")return 0;
    var la=project.assets.Find(a=>a.id==left.asset_id);var ra=project.assets.Find(a=>a.id==right.asset_id);
    var ls=la==null?null:la.streams.Find(s=>s.id==left.stream_id);var rs=ra==null?null:ra.streams.Find(s=>s.id==right.stream_id);
    if(ls==null||rs==null)return 0;
    int mgtMax=Math.Min((int)Math.Floor(mgt.maximum_seconds*60+0.00001),Math.Min((int)Math.Floor(left.duration.Seconds*120+0.00001),(int)Math.Floor(right.duration.Seconds*120+0.00001)));
    return Math.Max(0,mgtMax);
   }
   var contract=contracts.FirstOrDefault(c=>c.id==transitionId);if(contract==null||left==null||right==null||left.track_id!=right.track_id||Math.Abs(left.End-right.timeline_start.Seconds)>0.00001)return 0;
   var track=sequence.tracks.Find(t=>t.id==left.track_id);if(track==null||track.kind=="audio"&&contract.input_kind=="video")return 0;
   int max=Math.Min(600,(int)Math.Floor(left.duration.Seconds*60+0.00001));
   if(contract.needs_incoming_handle){
    var asset=project.assets.Find(a=>a.id==right.asset_id);var stream=asset==null?null:asset.streams.Find(s=>s.id==right.stream_id);
    if(stream==null)return 0;
    if(stream.kind!="image"&&!GeneratedMedia.IsUntimedPlaceholder(asset))max=Math.Min(max,(int)Math.Floor(right.source_in.Seconds/right.speed.Seconds*60+0.00001));
   }else max=Math.Min(max,(int)Math.Floor(right.duration.Seconds*120+0.00001));
   return Math.Max(0,max);
  }
  public static TransitionInstance New(Project project,Clip left,Clip right,string transitionId,int requestedFrames){
   var mgt=project.mgt_transitions.FirstOrDefault(r=>r.id==transitionId);
   if(mgt!=null){int maxMgt=MaxFrames(project,project.Active,left,right,transitionId);int minMgt=(int)Math.Ceiling(mgt.minimum_seconds*60-0.00001);if(maxMgt<Math.Max(2,minMgt))throw new Exception("Los medios no tienen fotogramas disponibles para la duración mínima de esta transición MGT.");int count=Math.Max(minMgt,Math.Min(maxMgt,requestedFrames));return new TransitionInstance{id=Store.Id(),transition_id=Mgt,contract_version="1.0.0",mgt_resource_id=mgt.id,left_clip_id=left.id,right_clip_id=right.id,cut_frame=(int)Math.Round(left.End*60),duration_frames=count};}
   var contract=contracts.FirstOrDefault(c=>c.id==transitionId);if(contract==null)throw new Exception("Transición desconocida");
   int max=MaxFrames(project,project.Active,left,right,transitionId);
   if(max<2)throw new Exception(contract.needs_incoming_handle?"El clip entrante no tiene material anterior al corte para esta transición. Prueba Fundido por negro o deja más margen de entrada.":"Este corte no tiene duración suficiente para la transición.");
   int frames=Math.Min(max,Math.Max(2,requestedFrames));
   return new TransitionInstance{id=Store.Id(),transition_id=contract.id,contract_version=contract.version,left_clip_id=left.id,right_clip_id=right.id,cut_frame=(int)Math.Round(left.End*60),duration_frames=frames};
  }
  public static void Validate(Project project,Sequence sequence,Action<string> registerId){
   if(sequence.transitions==null)throw new Exception("Falta la lista de transiciones");
   var occupied=new HashSet<string>();
   foreach(var transition in sequence.transitions){
    if(transition==null)throw new Exception("Transición inválida");registerId(transition.id);
    var contract=Find(transition.transition_id,transition.contract_version);
    bool isMgt=transition.transition_id==Mgt&&transition.contract_version=="1.0.0"&&project.mgt_transitions.Any(r=>r.id==transition.mgt_resource_id);
    if(contract==null&&!isMgt)throw new Exception("Contrato de transición no disponible");
    if(transition.parameters==null||!isMgt&&transition.parameters.Count!=0)throw new Exception("Parámetros de transición incompatibles");
    if(isMgt){var definitions=project.mgt_transitions.First(r=>r.id==transition.mgt_resource_id).ParameterDefinitions();
     foreach(var pair in transition.parameters){var definition=definitions.FirstOrDefault(d=>Convert.ToString(d["id"])==pair.Key);if(definition==null||pair.Value==null)throw new Exception("Parámetro MGT desconocido");
      string kind=Convert.ToString(definition["type"]),expected=kind=="number"?"number":kind=="boolean"?"boolean":kind=="select"?"choice":"text";
      if(pair.Value.kind!=expected||expected=="number"&&(!pair.Value.number.HasValue||Double.IsNaN(pair.Value.number.Value)||Double.IsInfinity(pair.Value.number.Value))||expected=="boolean"&&!pair.Value.boolean.HasValue||expected=="text"&&pair.Value.text==null||expected=="choice"&&pair.Value.text==null)throw new Exception("Tipo de parámetro MGT inválido: "+pair.Key);
      if(expected=="number"){double number=pair.Value.number.Value;object low,high;if(definition.TryGetValue("min",out low)&&number<Convert.ToDouble(low)||definition.TryGetValue("max",out high)&&number>Convert.ToDouble(high))throw new Exception("Parámetro MGT fuera de límites: "+pair.Key);}
     }
    }
    var left=sequence.clips.Find(c=>c.id==transition.left_clip_id);var right=sequence.clips.Find(c=>c.id==transition.right_clip_id);
    if(left==null||right==null||left.id==right.id||!occupied.Add(left.id+":"+right.id)||left.track_id!=right.track_id||Math.Abs(left.End-right.timeline_start.Seconds)>0.00001||transition.cut_frame!=(int)Math.Round(left.End*60))throw new Exception("La transición ya no corresponde a un corte contiguo");
    if(transition.preview_expanded)throw new Exception("Una transición temporal de vista previa no se puede guardar");
    var resource=isMgt?project.mgt_transitions.First(r=>r.id==transition.mgt_resource_id):null;
    if(transition.duration_frames<Math.Max(2,resource==null?2:(int)Math.Ceiling(resource.minimum_seconds*60-0.00001))||transition.duration_frames>MaxFrames(project,sequence,left,right,resource==null?transition.transition_id:resource.id))throw new Exception("Duración o margen de transición insuficiente");
   }
  }
  public static void Normalize(Project project){
   foreach(var sequence in project.sequences){
    if(sequence.transitions==null){sequence.transitions=new List<TransitionInstance>();continue;}
    sequence.transitions.RemoveAll(t=>t==null||(Find(t.transition_id,t.contract_version)==null&&!(t.transition_id==Mgt&&project.mgt_transitions.Any(r=>r.id==t.mgt_resource_id)))||!sequence.clips.Any(c=>c.id==t.left_clip_id)||!sequence.clips.Any(c=>c.id==t.right_clip_id));
    foreach(var t in sequence.transitions.ToList()){
     var left=sequence.clips.Find(c=>c.id==t.left_clip_id);var right=sequence.clips.Find(c=>c.id==t.right_clip_id);
     if(left.track_id!=right.track_id||Math.Abs(left.End-right.timeline_start.Seconds)>0.00001){sequence.transitions.Remove(t);continue;}
     t.cut_frame=(int)Math.Round(left.End*60);var resource=project.mgt_transitions.FirstOrDefault(r=>r.id==t.mgt_resource_id);t.duration_frames=Math.Min(t.duration_frames,MaxFrames(project,sequence,left,right,resource==null?t.transition_id:resource.id));
     if(t.duration_frames<Math.Max(2,resource==null?2:(int)Math.Ceiling(resource.minimum_seconds*60-0.00001)))sequence.transitions.Remove(t);
    }
   }
  }
 }
 public partial class Editor {
  public void SetTransition(string leftId,string rightId,string contractId,int durationFrames){
   Commit(p=>{
    var left=p.Active.clips.Find(c=>c.id==leftId);var right=p.Active.clips.Find(c=>c.id==rightId);if(left==null||right==null)throw new Exception("Selecciona un corte entre dos clips");
    if(p.Active.tracks.Find(t=>t.id==left.track_id).locked)throw new Exception("La pista está bloqueada");
    bool isMgt=p.mgt_transitions.Any(r=>r.id==contractId);
    var companionLeft=isMgt?null:TransitionContracts.LinkedAudio(p.Active,left);var companionRight=isMgt?null:TransitionContracts.LinkedAudio(p.Active,right);
    bool companion=!isMgt&&companionLeft!=null&&companionRight!=null&&companionLeft.track_id==companionRight.track_id&&Math.Abs(companionLeft.End-companionRight.timeline_start.Seconds)<0.00001;
    if(companion&&p.Active.tracks.Find(t=>t.id==companionLeft.track_id).locked)throw new Exception("La pista de audio vinculada está bloqueada");
    string audioContract=contractId==TransitionContracts.DipWhite?TransitionContracts.DipBlack:contractId;
    int max=TransitionContracts.MaxFrames(p,p.Active,left,right,contractId);
    if(companion){int audioMax=TransitionContracts.MaxFrames(p,p.Active,companionLeft,companionRight,audioContract);if(audioMax<2)throw new Exception("El audio vinculado no tiene suficiente margen para esta transición");max=Math.Min(max,audioMax);}
    if(max<2)throw new Exception("Este corte no tiene margen suficiente para la transición");
    int frames=Math.Min(Math.Max(2,durationFrames),max);string group=Store.Id();
    var old=TransitionContracts.At(p.Active,leftId,rightId);if(old!=null)p.Active.transitions.RemoveAll(x=>x.id==old.id||old.group_id!=null&&x.group_id==old.group_id);
    var primary=TransitionContracts.New(p,left,right,contractId,frames);primary.group_id=group;p.Active.transitions.Add(primary);
    if(companion){var previous=TransitionContracts.At(p.Active,companionLeft.id,companionRight.id);if(previous!=null)p.Active.transitions.Remove(previous);var audio=TransitionContracts.New(p,companionLeft,companionRight,audioContract,frames);audio.group_id=group;p.Active.transitions.Add(audio);}
   });
  }
  public void RemoveTransition(string transitionId){Commit(p=>{var t=p.Active.transitions.Find(x=>x.id==transitionId);if(t==null)return;var items=p.Active.transitions.Where(x=>x.id==transitionId||t.group_id!=null&&x.group_id==t.group_id).ToList();if(items.Any(x=>p.Active.tracks.Find(track=>track.id==p.Active.clips.Find(c=>c.id==x.left_clip_id).track_id).locked))throw new Exception("La pista está bloqueada");foreach(var item in items)p.Active.transitions.Remove(item);});}
  public void ResizeTransition(string transitionId,int frames){Commit(p=>{var t=p.Active.transitions.Find(x=>x.id==transitionId);if(t==null)throw new Exception("Transición inexistente");var items=p.Active.transitions.Where(x=>x.id==t.id||t.group_id!=null&&x.group_id==t.group_id).ToList();if(items.Any(x=>p.Active.tracks.Find(track=>track.id==p.Active.clips.Find(c=>c.id==x.left_clip_id).track_id).locked))throw new Exception("La pista está bloqueada");foreach(var item in items)item.duration_frames=Math.Max(2,frames);});}
 }
}
