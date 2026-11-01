using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 public sealed class SequenceViewport {public double Cursor,Scroll,Zoom=55;}
 public static class ClipIdentity {
  public static void NewEffects(Clip clip){
   var remap=new Dictionary<string,string>();
   foreach(var effect in clip.effects){string old=effect.id;effect.id=Store.Id();remap[old]=effect.id;}
   foreach(var curve in clip.animation)foreach(var pair in remap){string prefix="effect:"+pair.Key+":";
    if(curve.target!=null&&curve.target.StartsWith(prefix,StringComparison.Ordinal))curve.target="effect:"+pair.Value+":"+curve.target.Substring(prefix.Length);
   }
  }
  public static void NewEffects(Track track){foreach(var effect in track.effects)effect.id=Store.Id();}
 }
 public static class Sequences {
  public const string ViewNamespace="prometheus.sequence-viewport";
  static double ReadNumber(Extension extension,string key,double fallback){object raw;if(extension==null||extension.payload==null||!extension.payload.TryGetValue(key,out raw))return fallback;double value;return Double.TryParse(Convert.ToString(raw,System.Globalization.CultureInfo.InvariantCulture),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out value)&&!Double.IsNaN(value)&&!Double.IsInfinity(value)?value:fallback;}
  public static SequenceViewport ReadView(Sequence sequence){var extension=sequence.extensions.FirstOrDefault(x=>x!=null&&x.@namespace==ViewNamespace);return new SequenceViewport{Cursor=Math.Max(0,ReadNumber(extension,"cursor",0)),Scroll=Math.Max(0,ReadNumber(extension,"scroll",0)),Zoom=Math.Max(1,Math.Min(1000,ReadNumber(extension,"zoom",55)))};}
  public static void WriteView(Sequence sequence,double cursor,double scroll,double zoom){var extension=sequence.extensions.FirstOrDefault(x=>x!=null&&x.@namespace==ViewNamespace);if(extension==null){extension=new Extension{@namespace=ViewNamespace,version="1",payload=new Dictionary<string,object>()};sequence.extensions.Add(extension);}extension.payload["cursor"]=Math.Max(0,cursor);extension.payload["scroll"]=Math.Max(0,scroll);extension.payload["zoom"]=Math.Max(1,Math.Min(1000,zoom));}
  public static string NextName(Project project,string basis="Secuencia") {
   for(int number=1;;number++){
    string candidate=basis+" "+number.ToString("00");
    if(!project.sequences.Any(s=>String.Equals(s.name,candidate,StringComparison.OrdinalIgnoreCase)))return candidate;
   }
  }
  static string ValidName(Project project,string name,string exceptId=null) {
   name=(name??"").Trim();
   if(name.Length==0||name.Length>80)throw new Exception("Escribe un nombre de secuencia de hasta 80 caracteres");
   if(project.sequences.Any(s=>s.id!=exceptId&&String.Equals(s.name,name,StringComparison.OrdinalIgnoreCase)))throw new Exception("Ya existe una secuencia con ese nombre");
   return name;
  }
  public static string Create(Project project,string name) {
   name=ValidName(project,name);
   var current=project.Active;
   var sequence=new Sequence{id=Store.Id(),name=name,width=current.width,height=current.height};
   foreach(var track in current.tracks.OrderBy(t=>t.kind).ThenBy(t=>t.order))Store.AddTrack(sequence,track.kind);
   if(!sequence.tracks.Any(t=>t.kind=="video"))Store.AddTrack(sequence,"video");
   if(!sequence.tracks.Any(t=>t.kind=="audio"))Store.AddTrack(sequence,"audio");
   project.sequences.Add(sequence);project.active_sequence_id=sequence.id;return sequence.id;
  }
  public static string Duplicate(Project project,string sourceId,string name) {
   var original=project.sequences.Find(s=>s.id==sourceId);
   if(original==null)throw new Exception("Secuencia inexistente");
   name=ValidName(project,name);
   var copy=Store.Json().Deserialize<Sequence>(Store.Json().Serialize(Store.Fields(original)));
   copy.id=Store.Id();copy.name=name;
   var trackIds=new Dictionary<string,string>();
   foreach(var track in copy.tracks){string old=track.id;track.id=Store.Id();trackIds[old]=track.id;ClipIdentity.NewEffects(track);}
   var clipIds=new Dictionary<string,string>();
   foreach(var clip in copy.clips){string old=clip.id;clip.id=Store.Id();clip.track_id=trackIds[clip.track_id];clipIds[old]=clip.id;ClipIdentity.NewEffects(clip);}
   foreach(var group in copy.link_groups){group.id=Store.Id();group.clip_ids=group.clip_ids.Select(id=>clipIds[id]).ToList();}
   foreach(var group in copy.edit_groups){group.id=Store.Id();group.clip_ids=group.clip_ids.Where(clipIds.ContainsKey).Select(id=>clipIds[id]).ToList();}
   copy.edit_groups.RemoveAll(group=>group.clip_ids.Count<2);
   var transitionGroups=new Dictionary<string,string>();
   foreach(var transition in copy.transitions){transition.id=Store.Id();transition.left_clip_id=clipIds[transition.left_clip_id];transition.right_clip_id=clipIds[transition.right_clip_id];
    if(!String.IsNullOrEmpty(transition.group_id)){string groupId;if(!transitionGroups.TryGetValue(transition.group_id,out groupId))transitionGroups[transition.group_id]=groupId=Store.Id();transition.group_id=groupId;}
   }
   foreach(var extension in copy.extensions)if(extension!=null&&extension.@namespace=="prometheus.sequence-marker"&&extension.payload!=null&&extension.payload.ContainsKey("id"))extension.payload["id"]=Store.Id();
   project.sequences.Add(copy);project.active_sequence_id=copy.id;return copy.id;
  }
  public static void Rename(Project project,string id,string name) {
   var sequence=project.sequences.Find(s=>s.id==id);
   if(sequence==null)throw new Exception("Secuencia inexistente");
   sequence.name=ValidName(project,name,id);
  }
  public static void Delete(Project project,string id) {
   if(project.sequences.Count<=1)throw new Exception("El proyecto debe conservar al menos una secuencia");
   int index=project.sequences.FindIndex(s=>s.id==id);
   if(index<0)throw new Exception("Secuencia inexistente");
   project.sequences.RemoveAt(index);
   if(project.active_sequence_id==id)project.active_sequence_id=project.sequences[Math.Min(index,project.sequences.Count-1)].id;
  }
 }
 public partial class Editor {
  public void SwitchSequence(string id) {
   if(!Project.sequences.Any(s=>s.id==id))throw new Exception("Secuencia inexistente");
   Project.active_sequence_id=id;
  }
  public void CreateSequence(string name){Commit(p=>Sequences.Create(p,name));}
  public void DuplicateSequence(string id,string name){Commit(p=>Sequences.Duplicate(p,id,name));}
  public void RenameSequence(string id,string name){Commit(p=>Sequences.Rename(p,id,name));}
  public void DeleteSequence(string id){Commit(p=>Sequences.Delete(p,id));}
 }
}
