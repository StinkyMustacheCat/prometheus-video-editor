using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Prometheus {
 public static partial class Instructions {
  static readonly HashSet<string> added08181=new HashSet<string>{"clip.set_speed","clip.group","clip.ungroup","clip.set_transform","text.create","text.set_content","subtitle.create","subtitle.set_content","subtitle.delete","marker.create","marker.move","marker.rename","marker.delete","track.set_enabled","track.set_effects_enabled","sequence.set_export_in","sequence.set_export_out","sequence.clear_export_range","media.place_asset","mgt.set_parameter","curve.set_keyframe","curve.set_interpolation","curve.set_tangent","mask.set_keyframe","mask.set_interpolation","mask.set_enabled"};
  static bool IsAdded08181(string op){return added08181.Contains(op);}
  public static IEnumerable<string> AvailableActions(){return operations.Concat(actionSpecs.Keys).OrderBy(x=>x,StringComparer.Ordinal).ToArray();}
  static readonly Dictionary<string,string> basicFields=new Dictionary<string,string>{
   {"effect.apply","scope,catalog_id,sequence_id,clip_id,clip_ids,track_id,resource_path,result"},
   {"effect.remove","scope,catalog_id,sequence_id,clip_id,clip_ids,track_id"},
   {"transition.apply","scope,catalog_id,duration_frames,sequence_id,left_clip_id,right_clip_id,result"},
   {"transition.remove","scope,catalog_id,sequence_id,left_clip_id,right_clip_id"},
   {"clip.move","clip_id,at_seconds,track_id,placement,sequence_id,result"},
   {"clip.duplicate","clip_id,at_seconds,track_id,placement,sequence_id,result"},
   {"clip.trim","clip_id,edge,duration_seconds,ripple,sequence_id,result"},
   {"clip.inherit_track_effects","clip_id,value,sequence_id,result"}
  };
  public static string RequiredFields(string op){ActionSpec spec;if(actionSpecs.TryGetValue(op,out spec))return spec.Required;switch(op){case "effect.apply":case "effect.remove":case "transition.apply":case "transition.remove":return "scope,catalog_id";case "clip.move":case "clip.duplicate":return "clip_id,at_seconds,track_id,placement";case "clip.trim":return "clip_id,edge,duration_seconds,ripple";case "clip.inherit_track_effects":return "clip_id,value";default:return "";}}
  public static string[] AcceptedFields(string op){ActionSpec spec;if(actionSpecs.TryGetValue(op,out spec))return spec.Accepted.Where(f=>f!="op").OrderBy(f=>f).ToArray();string names;return basicFields.TryGetValue(op,out names)?names.Split(','):new string[0];}
  static void AddTitle(Editor editor,InstructionAction a,InstructionPlan plan,bool subtitle){
   Require(!String.IsNullOrWhiteSpace(a.text)&&a.duration_seconds.HasValue&&a.at_seconds.HasValue,"Indica texto, posición y duración");
   string id=null;
   editor.Commit(p=>{
    double at=Rational.Frame(a.at_seconds.Value).Seconds,duration=Rational.Frame(a.duration_seconds.Value).Seconds;
    Require(at>=0&&duration>=1.0/60&&duration<=600,"Intervalo de texto inválido");
    if(subtitle){id=SubtitleTrack0817.Add(p,a.text,at,duration).id;return;}
    var track=TrackOf(p.Active,a.track_id);Require(track.kind=="video"&&!track.locked&&track.id!=SubtitleTrack0817.TrackId(p.Active),"Selecciona una pista de vídeo desbloqueada");
    Require(!p.Active.clips.Any(c=>c.track_id==track.id&&c.timeline_start.Seconds<at+duration-0.000001&&c.End>at+0.000001),"El destino del texto está ocupado");
    var asset=GeneratedMedia.CreateText(a.text,duration);if(a.number.HasValue||a.color!=null)GeneratedMedia.SetText(asset,a.text,a.number.HasValue?(int)a.number.Value:GeneratedMedia.TextSize(asset),a.color??GeneratedMedia.TextColor(asset));p.assets.Add(asset);
    var clip=new Clip{id=Store.Id(),name=asset.name,asset_id=asset.id,stream_id=asset.streams[0].id,track_id=track.id,timeline_start=Rational.Frame(at),duration=Rational.Frame(duration),source_in=Rational.Time(0)};
    p.Active.clips.Add(clip);id=clip.id;
   });Bind(plan,a.result,id);Note(plan,subtitle?"Crear subtítulo":"Crear texto",1,0);
  }
  static void ChangeText(Editor editor,InstructionAction a,InstructionPlan plan,bool subtitle){
   Require(!String.IsNullOrWhiteSpace(a.text),"El texto no puede estar vacío");
   editor.Commit(p=>{
    var clip=ClipOf(p.Active,a.clip_id);var track=TrackOf(p.Active,clip.track_id);Require(!track.locked,"La pista está bloqueada");
    var asset=p.assets.Find(x=>x.id==clip.asset_id);Require(asset!=null&&GeneratedMedia.IsText(asset)&&(!subtitle||SubtitleTrack0817.IsCue(p,clip)),"El clip no corresponde al tipo de texto solicitado");
    var followers=p.sequences.SelectMany(s=>s.clips).Where(c=>c.asset_id==asset.id).ToList();
    if(followers.Count>1){var copy=Store.Json().Deserialize<Asset>(Store.Json().Serialize(Store.Fields(asset)));copy.id=Store.Id();foreach(var stream in copy.streams){string old=stream.id;stream.id=Store.Id();if(clip.stream_id==old)clip.stream_id=stream.id;}p.assets.Add(copy);clip.asset_id=copy.id;asset=copy;}
    GeneratedMedia.SetText(asset,a.text,a.number.HasValue?(int)a.number.Value:GeneratedMedia.TextSize(asset),a.color??GeneratedMedia.TextColor(asset));
    asset.name=(subtitle?"Subtítulo · ":"Texto · ")+(a.text.Length>34?a.text.Substring(0,34)+"…":a.text);clip.name=asset.name;
   });Note(plan,subtitle?"Editar subtítulo":"Editar texto",1,0);
  }
  static void ApplyAdded08181(Editor editor,InstructionAction a,InstructionPlan plan,IInstructionMediaAdapter media){
   if(a.op=="media.place_asset"||a.op=="mgt.set_parameter"){Require(media!=null,"Esta acción requiere el adaptador de medios");media.Apply(editor,a,plan);return;}
   var sequence=SequenceOf(editor.Project,a);editor.Project.active_sequence_id=sequence.id;
   if(a.op=="text.create"||a.op=="subtitle.create"){AddTitle(editor,a,plan,a.op=="subtitle.create");return;}
   if(a.op=="text.set_content"||a.op=="subtitle.set_content"){ChangeText(editor,a,plan,a.op=="subtitle.set_content");return;}
   if(a.op=="subtitle.delete"){editor.Commit(p=>SubtitleTrack0817.Remove(p,a.clip_id));Note(plan,"Eliminar subtítulo",1,0);return;}
   if(a.op.StartsWith("marker.")){
    string id=null;
    editor.Commit(p=>{if(a.op=="marker.create"){Require(a.at_seconds.HasValue,"Indica la posición");id=Markers.Add(p.Active,a.at_seconds.Value,a.name);}
     else if(a.op=="marker.move"){Require(a.at_seconds.HasValue,"Indica la posición");Markers.Move(p.Active,a.marker_id,a.at_seconds.Value);}
     else if(a.op=="marker.rename")Markers.Rename(p.Active,a.marker_id,a.name);
     else Markers.Delete(p.Active,a.marker_id);});
    if(id!=null)Bind(plan,a.result,id);Note(plan,"Cambiar marcador",1,0);return;
   }
   if(a.op=="sequence.set_export_in"||a.op=="sequence.set_export_out"||a.op=="sequence.clear_export_range"){
    editor.Commit(p=>{if(a.op=="sequence.clear_export_range")SequenceExportRange.Clear(p.Active);else{Require(a.at_seconds.HasValue,"Indica la posición");if(a.op=="sequence.set_export_in")SequenceExportRange.SetIn(p.Active,a.at_seconds.Value);else SequenceExportRange.SetOut(p.Active,a.at_seconds.Value);}});Note(plan,"Actualizar marcas In/Out",1,0);return;
   }
   if(a.op=="track.set_enabled"||a.op=="track.set_effects_enabled"){
    Require(a.enabled.HasValue,"Indica enabled true o false");var track=TrackOf(sequence,a.track_id);
    editor.Commit(p=>{var t=TrackOf(p.Active,track.id);if(a.op=="track.set_enabled")t.enabled=a.enabled.Value;else t.effects_enabled=a.enabled.Value;});Note(plan,"Cambiar estado de pista",1,0);return;
   }
   if(a.op=="clip.group"||a.op=="clip.ungroup"){
    Require(a.clip_ids!=null&&a.clip_ids.Count>0,"Indica los clips");if(a.op=="clip.group")editor.Group(a.clip_ids);else editor.UngroupMany(a.clip_ids);Note(plan,a.op=="clip.group"?"Agrupar clips":"Desagrupar clips",1,0);return;
   }
   var clipTarget=ClipOf(sequence,a.clip_id);Require(!TrackOf(sequence,clipTarget.track_id).locked,"La pista está bloqueada");
   if(a.op=="curve.set_keyframe"||a.op=="curve.set_interpolation"||a.op=="curve.set_tangent"){
    Require(!String.IsNullOrWhiteSpace(a.parameter_id),"Indica una propiedad animable");
    editor.Commit(p=>{var clip=ClipOf(p.Active,a.clip_id);if(a.op=="curve.set_keyframe"){Require(a.at_seconds.HasValue&&a.number.HasValue,"Indica tiempo y valor");ClipKeyframes.Set(clip,a.parameter_id,a.at_seconds.Value,a.number.Value);}else{var curve=ClipKeyframes.Find(clip,a.parameter_id);Require(curve!=null,"La pista de fotogramas clave no existe");if(a.op=="curve.set_interpolation"){Require(new[]{"linear","hold","smooth","bezier"}.Contains(a.text),"Interpolación desconocida");curve.interpolation=a.text;}else{Require(a.at_seconds.HasValue&&a.number.HasValue&&a.value.HasValue&&a.number.Value>=0&&a.number.Value<=3,"Indica tiempo, tangente (0–3) y lado");double source=ClipKeyframes.SourceAt(clip,a.at_seconds.Value);var point=curve.points.FirstOrDefault(q=>Math.Abs(q.source_seconds-source)<1.0/48000);Require(point!=null,"No hay un fotograma en ese tiempo");if(a.value.Value)point.in_tangent=a.number.Value;else point.out_tangent=a.number.Value;}}});Note(plan,"Actualizar curva de fotogramas clave",1,0);return;
   }
   if(a.op=="mask.set_keyframe"||a.op=="mask.set_interpolation"||a.op=="mask.set_enabled"){
    editor.Commit(p=>{var clip=ClipOf(p.Active,a.clip_id);Require(clip.mask!=null,"El clip no tiene máscara manual");if(a.op=="mask.set_keyframe"){Require(a.at_seconds.HasValue,"Indica el tiempo");TemporalMasks0823.Set(clip,a.at_seconds.Value,TemporalMasks0823.At(clip,a.at_seconds.Value));}else{var mask=TemporalMasks0823.Get(clip);Require(mask!=null,"Activa primero la animación de máscara");if(a.op=="mask.set_interpolation"){Require(new[]{"linear","bezier","hold"}.Contains(a.text),"Interpolación de máscara desconocida");mask.interpolation=a.text;}else{Require(a.enabled.HasValue,"Indica enabled");mask.enabled=a.enabled.Value;}TemporalMasks0823.Put(clip,mask);}});Note(plan,"Actualizar máscara temporal",1,0);return;
   }
   if(a.op=="clip.set_speed"){
    Require(a.number.HasValue&&a.number.Value>=25&&a.number.Value<=400,"La velocidad debe estar entre 25 y 400 %");bool limited=false;
    editor.Commit(p=>limited=ClipSpeed08174.Change(p,a.clip_id,(decimal)a.number.Value));Note(plan,limited?"Cambiar velocidad (limitada por el clip siguiente)":"Cambiar velocidad",1,0);return;
   }
   if(a.op=="clip.set_transform"){
    Require(!String.IsNullOrEmpty(a.parameter_id),"Indica la propiedad de transformación");
    editor.Commit(p=>{var clip=ClipOf(p.Active,a.clip_id);var frame=ClipFraming.Read(clip);
     if(a.parameter_id=="scale_linked"){Require(a.value.HasValue,"Indica value true o false");ClipFraming.SetScaleLinked(clip,a.value.Value);return;}
     Require(a.number.HasValue,"Indica number para la transformación");double n=a.number.Value;
     if(a.parameter_id=="scale")ClipFraming.Set(clip,frame.mode,n,frame.x,frame.y);
     else if(a.parameter_id=="scale_y")ClipFraming.SetScaleY(clip,n);
     else if(a.parameter_id=="x")ClipFraming.Set(clip,frame.mode,frame.scale,n,frame.y);
     else if(a.parameter_id=="y")ClipFraming.Set(clip,frame.mode,frame.scale,frame.x,n);
     else if(a.parameter_id=="opacity")ClipFraming.SetOpacity(clip,n);
     else throw new Exception("Propiedad de transformación desconocida: "+a.parameter_id);
    });Note(plan,"Cambiar transformación",1,0);return;
   }
   throw new Exception("Acción no implementada: "+a.op);
  }
 }
}
