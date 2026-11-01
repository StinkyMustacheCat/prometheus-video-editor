using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Prometheus {
 public sealed class SequenceMarker {
  public string Id,Name;
  public long Frame;
  public int ColorArgb;
  public double Seconds {get{return Frame/60.0;}}
 }
 public static class Markers {
  const string Namespace="prometheus.sequence-marker";
  const string Version="1";
  public const int DefaultColor=unchecked((int)0xFFFFB95F);
  static bool IsMarker(Extension extension){return extension!=null&&extension.@namespace==Namespace&&extension.version==Version;}
  static SequenceMarker Parse(Extension extension){
   if(!IsMarker(extension)||extension.payload==null)return null;
   object id,name,frame,color;
   if(!extension.payload.TryGetValue("id",out id)||!extension.payload.TryGetValue("name",out name)||!extension.payload.TryGetValue("frame",out frame)||!extension.payload.TryGetValue("color_argb",out color))return null;
   try{
    var marker=new SequenceMarker{Id=Convert.ToString(id,CultureInfo.InvariantCulture),Name=Convert.ToString(name,CultureInfo.InvariantCulture),Frame=Convert.ToInt64(frame,CultureInfo.InvariantCulture),ColorArgb=Convert.ToInt32(color,CultureInfo.InvariantCulture)};
    return String.IsNullOrWhiteSpace(marker.Id)||marker.Frame<0?null:marker;
   }catch{return null;}
  }
  public static List<SequenceMarker> Read(Sequence sequence){
   if(sequence==null||sequence.extensions==null)return new List<SequenceMarker>();
   return sequence.extensions.Select(Parse).Where(marker=>marker!=null).OrderBy(marker=>marker.Frame).ThenBy(marker=>marker.Id).ToList();
  }
  static Extension Find(Sequence sequence,string markerId){return sequence.extensions.FirstOrDefault(extension=>{var parsed=Parse(extension);return parsed!=null&&parsed.Id==markerId;});}
  static long FrameAt(double seconds){
   if(Double.IsNaN(seconds)||Double.IsInfinity(seconds)||seconds<0||seconds>360000000)throw new Exception("Posición de marcador inválida");
   return (long)Math.Round(seconds*60);
  }
  public static string Add(Sequence sequence,double seconds,string name=null){
   long frame=FrameAt(seconds);
   if(String.IsNullOrWhiteSpace(name))name="Marca "+(Read(sequence).Count+1);
   name=name.Trim();if(name.Length>100)throw new Exception("El nombre del marcador es demasiado largo");
   string id=Store.Id();
   sequence.extensions.Add(new Extension{@namespace=Namespace,version=Version,payload=new Dictionary<string,object>{{"id",id},{"name",name},{"frame",frame},{"color_argb",DefaultColor}}});
   return id;
  }
  public static void Move(Sequence sequence,string markerId,double seconds){var extension=Find(sequence,markerId);if(extension==null)throw new Exception("Marcador inexistente");extension.payload["frame"]=FrameAt(seconds);}
  public static void Rename(Sequence sequence,string markerId,string name){var extension=Find(sequence,markerId);if(extension==null)throw new Exception("Marcador inexistente");if(String.IsNullOrWhiteSpace(name)||name.Trim().Length>100)throw new Exception("Escribe un nombre de hasta 100 caracteres");extension.payload["name"]=name.Trim();}
  public static void SetColor(Sequence sequence,string markerId,int argb){var extension=Find(sequence,markerId);if(extension==null)throw new Exception("Marcador inexistente");extension.payload["color_argb"]=argb;}
  public static void Delete(Sequence sequence,string markerId){var extension=Find(sequence,markerId);if(extension==null)throw new Exception("Marcador inexistente");sequence.extensions.Remove(extension);}
 }
 public partial class Editor {
  public void AddMarker(double seconds,string name=null){Commit(p=>Markers.Add(p.Active,seconds,name));}
  public void MoveMarker(string markerId,double seconds){Commit(p=>Markers.Move(p.Active,markerId,seconds));}
  public void RenameMarker(string markerId,string name){Commit(p=>Markers.Rename(p.Active,markerId,name));}
  public void SetMarkerColor(string markerId,int argb){Commit(p=>Markers.SetColor(p.Active,markerId,argb));}
  public void DeleteMarker(string markerId){Commit(p=>Markers.Delete(p.Active,markerId));}
 }
}
