using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 public static class MediaReferences08202 {
  public static string CopyText(Project project,IEnumerable<string> clipIds=null){
   if(project==null||project.Active==null)return "";
   var selected=clipIds==null?null:new HashSet<string>(clipIds);
   var lines=new List<string>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var clip in project.Active.clips.OrderBy(c=>c.timeline_start.Seconds)){
    if(selected!=null&&!selected.Contains(clip.id))continue;
    var asset=project.assets.FirstOrDefault(a=>a.id==clip.asset_id);
    if(asset==null||asset.source==null||asset.source.kind!="web")continue;
    string url=String.IsNullOrWhiteSpace(asset.source.page_url)?asset.source.uri:asset.source.page_url;
    if(String.IsNullOrWhiteSpace(url)||!seen.Add(url))continue;
    lines.Add((String.IsNullOrWhiteSpace(asset.name)?"Medio web":asset.name.Trim())+" — "+url);
   }
   return String.Join(Environment.NewLine,lines);
  }
 }
}
