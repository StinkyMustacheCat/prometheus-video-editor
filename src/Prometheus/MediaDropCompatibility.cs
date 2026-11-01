using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Prometheus {
 public static class MediaDropCompatibility {
  static readonly Dictionary<string,Asset> cache=new Dictionary<string,Asset>(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> rejected=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  public static bool CanPlace(string[] files,string trackKind){
   if(files==null||files.Length==0||files.Length>32||(trackKind!="video"&&trackKind!="audio"))return false;
   foreach(string file in files){
    if(!File.Exists(file))return false;
    var info=new FileInfo(file);string key=Path.GetFullPath(file)+"|"+info.Length+"|"+info.LastWriteTimeUtc.Ticks;
    if(rejected.Contains(key))return false;
    if(MgtMedia.IsFile(file)){if(trackKind!="video")return false;continue;}
    if(Media.IsConvertibleStillExtension(file)){if(trackKind!="video")return false;continue;}
    Asset asset;if(!cache.TryGetValue(key,out asset)){
     try{asset=Media.Inspect(file,CancellationToken.None);cache[key]=asset;}catch{rejected.Add(key);return false;}
    }
    if(trackKind=="audio"?!asset.streams.All(s=>s.kind=="audio"):!asset.streams.Any(s=>s.kind=="video"||s.kind=="image"))return false;
   }
   return true;
  }
  public static bool CanFillPlaceholder(string[] files,Project project,Clip clip){
   if(files==null||files.Length!=1||project==null||clip==null)return false;
   var track=project.Active.tracks.Find(t=>t.id==clip.track_id);
   if(track==null||!CanPlace(files,track.kind))return false;
   if(MgtMedia.IsFile(files[0]))return track.kind=="video"&&(PlaceholderReplacement.ExpectedType(clip)=="any"||PlaceholderReplacement.ExpectedType(clip)=="video");
   var info=new FileInfo(files[0]);string key=Path.GetFullPath(files[0])+"|"+info.Length+"|"+info.LastWriteTimeUtc.Ticks;
   Asset asset;return cache.TryGetValue(key,out asset)&&PlaceholderReplacement.Accepts(project,clip,asset);
  }
 }
}
