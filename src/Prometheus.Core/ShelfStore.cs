using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 public sealed class ShelfItem {
  public string id,url,added_at;
  public Asset asset;
  public double? range_in,range_out;
 }
 public static class ShelfStore {
  public const string Namespace="prometheus.ui.media-shelf";
  static Extension Find(Project project){return project.extensions.FirstOrDefault(x=>x!=null&&x.@namespace==Namespace&&x.version=="1");}
  public static List<ShelfItem> Read(Project project){
   var ext=Find(project);object raw;
   if(ext==null||ext.payload==null||!ext.payload.TryGetValue("items_json",out raw)||raw==null)return new List<ShelfItem>();
   try{return Store.Json().Deserialize<List<ShelfItem>>(Convert.ToString(raw)).Where(x=>x!=null&&x.asset!=null&&x.asset.source!=null&&!String.IsNullOrWhiteSpace(x.url)).ToList();}
   catch{return new List<ShelfItem>();}
  }
  public static void Write(Project project,IList<ShelfItem> items){
   var ext=Find(project);
   if(ext==null){ext=new Extension{@namespace=Namespace,version="1",payload=new Dictionary<string,object>()};project.extensions.Add(ext);}
   ext.payload["items_json"]=Store.Json().Serialize(Store.Fields(items.ToList()));
  }
  public static void Add(Project project,Asset asset){
   if(asset==null||asset.source==null||String.IsNullOrWhiteSpace(asset.source.uri))throw new Exception("El enlace no produjo un medio válido");
   var items=Read(project);string url=asset.source.uri;
   if(items.Any(x=>String.Equals(x.url,url,StringComparison.OrdinalIgnoreCase)))return;
   items.Add(new ShelfItem{id=Store.Id(),url=url,added_at=DateTime.UtcNow.ToString("o"),asset=asset});Write(project,items);
  }
  public static void Remove(Project project,IEnumerable<string> ids){var set=new HashSet<string>(ids);Write(project,Read(project).Where(x=>!set.Contains(x.id)).ToList());}
  public static void SetRange(Project project,string id,double? begin,double? end){var items=Read(project);var item=items.FirstOrDefault(x=>x.id==id);if(item==null)throw new Exception("Elemento de Bandeja no encontrado");if(begin.HasValue!=end.HasValue||begin.HasValue&&(begin.Value<0||end.Value<=begin.Value||end.Value>item.asset.streams.Where(s=>s.duration!=null).Select(s=>s.duration.Seconds).DefaultIfEmpty(0).Max()+0.001))throw new Exception("Tramo fuera del medio");item.range_in=begin;item.range_out=end;Write(project,items);}
  public static Asset Promote(Project project,ShelfItem item){
   var existing=project.assets.FirstOrDefault(x=>x.source!=null&&String.Equals(x.source.uri,item.url,StringComparison.OrdinalIgnoreCase));
   if(existing!=null)return existing;
   var copy=Store.Json().Deserialize<Asset>(Store.Json().Serialize(Store.Fields(item.asset)));
   if(project.assets.Any(x=>x.id==copy.id)||String.IsNullOrWhiteSpace(copy.id))copy.id=Store.Id();
   foreach(var stream in copy.streams)if(project.assets.Any(a=>a.streams.Any(s=>s.id==stream.id)))stream.id=Store.Id();
   project.assets.Add(copy);return copy;
  }
 }
}
