using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Prometheus {
 // Project-local UI state lives in an existing sequence extension, keeping the clip schema unchanged.
 public static class TrackPresentation {
  const string Namespace="prometheus.ui.track-presentation";
  const string Version="1";
  static Extension Find(Sequence sequence){return sequence.extensions.FirstOrDefault(e=>e!=null&&e.@namespace==Namespace&&e.version==Version);}
  static Dictionary<string,object> Payload(Sequence sequence){var extension=Find(sequence);return extension==null?null:extension.payload;}
  static object Value(Sequence sequence,string key){var payload=Payload(sequence);object value;return payload!=null&&payload.TryGetValue(key,out value)?value:null;}
  static void Set(Sequence sequence,string key,object value){
   var extension=Find(sequence);
   if(extension==null){extension=new Extension{@namespace=Namespace,version=Version,payload=new Dictionary<string,object>()};sequence.extensions.Add(extension);}
   if(extension.payload==null)extension.payload=new Dictionary<string,object>();
   extension.payload[key]=value;
  }
  public static bool ShowNames(Sequence sequence,Track track){
   if(sequence==null||track==null)return true;
   var value=Value(sequence,"names:"+track.id);
   try{return value==null?track.kind!="audio":Convert.ToBoolean(value,CultureInfo.InvariantCulture);}catch{return track.kind!="audio";}
  }
  public static int ExpandedHeight(Sequence sequence,string trackId){
   var value=sequence==null?null:Value(sequence,"height:"+trackId);
   try{return value==null?55:Math.Max(32,Math.Min(180,Convert.ToInt32(value,CultureInfo.InvariantCulture)));}catch{return 55;}
  }
  public static bool Compact(Sequence sequence,string trackId){
   var value=sequence==null?null:Value(sequence,"compact:"+trackId);
   try{return value!=null&&Convert.ToBoolean(value,CultureInfo.InvariantCulture);}catch{return false;}
  }
  public static int Height(Sequence sequence,string trackId){return Compact(sequence,trackId)?32:ExpandedHeight(sequence,trackId);}
  public static void SetShowNames(Sequence sequence,string trackId,bool visible){Set(sequence,"names:"+trackId,visible);}
  public static void SetHeight(Sequence sequence,string trackId,int height){Set(sequence,"height:"+trackId,Math.Max(32,Math.Min(180,height)));Set(sequence,"compact:"+trackId,false);}
  public static void SetCompact(Sequence sequence,string trackId,bool compact){Set(sequence,"compact:"+trackId,compact);}
 }
 public partial class Timeline {
  public Action<string,bool> ShowTrackNames;
  public Action<string> ToggleTrackCompact;
  public Action<string,int> TrackHeightChanged;
  public void ClearTrackHeightOverride(string trackId){trackHeights.Remove(trackId);Invalidate();}
  public void ClearAllTrackHeightOverrides(){trackHeights.Clear();Invalidate();}
 }
 public partial class MainForm {
  void CommitTrackPresentation(Action<Project> action){editor.Commit(action);dirty=true;RefreshProject();}
  void SetTrackNamesVisible(string trackId,bool visible){CommitTrackPresentation(p=>TrackPresentation.SetShowNames(p.Active,trackId,visible));}
  void SetTrackCompact(string trackId){
   bool compact=!TrackPresentation.Compact(editor.Project.Active,trackId);
   timeline.ClearTrackHeightOverride(trackId);
   CommitTrackPresentation(p=>TrackPresentation.SetCompact(p.Active,trackId,compact));
  }
  void SaveTrackHeight(string trackId,int height){CommitTrackPresentation(p=>TrackPresentation.SetHeight(p.Active,trackId,height));}
 }
}
