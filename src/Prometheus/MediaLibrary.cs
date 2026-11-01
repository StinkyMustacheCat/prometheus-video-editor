using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 public sealed class MediaFolder {public string id,name;}

 // Library organization is editorial metadata. It never changes media files or render output.
 public static class MediaLibrary {
  public const string Namespace="prometheus.ui.media-library";
  static Extension Find(List<Extension> extensions){return extensions==null?null:extensions.FirstOrDefault(x=>x!=null&&x.@namespace==Namespace&&x.version=="1");}
  static Extension Ensure(List<Extension> extensions){var ext=Find(extensions);if(ext!=null)return ext;ext=new Extension{@namespace=Namespace,version="1",payload=new Dictionary<string,object>()};extensions.Add(ext);return ext;}
  public static List<MediaFolder> Folders(Project project){
   var ext=Find(project.extensions);object value;
   if(ext==null||ext.payload==null||!ext.payload.TryGetValue("folders_json",out value)||value==null)return new List<MediaFolder>();
   try{return Store.Json().Deserialize<List<MediaFolder>>(Convert.ToString(value)).Where(x=>x!=null&&!String.IsNullOrWhiteSpace(x.id)&&!String.IsNullOrWhiteSpace(x.name)).ToList();}
   catch{return new List<MediaFolder>();}
  }
  static void SaveFolders(Project project,List<MediaFolder> folders){Ensure(project.extensions).payload["folders_json"]=Store.Json().Serialize(folders);}
  public static string CreateFolder(Project project,string name){
   name=(name??"").Trim();if(name.Length==0||name.Length>64)throw new Exception("Escribe un nombre de carpeta de 1 a 64 caracteres");
   var folders=Folders(project);if(folders.Any(x=>String.Equals(x.name,name,StringComparison.OrdinalIgnoreCase)))throw new Exception("Ya existe una carpeta con ese nombre");
   string id=Store.Id();folders.Add(new MediaFolder{id=id,name=name});SaveFolders(project,folders);return id;
  }
  public static void RenameFolder(Project project,string id,string name){
   name=(name??"").Trim();if(name.Length==0||name.Length>64)throw new Exception("Escribe un nombre de carpeta de 1 a 64 caracteres");
   var folders=Folders(project);var folder=folders.FirstOrDefault(x=>x.id==id);if(folder==null)throw new Exception("Carpeta no encontrada");
   if(folders.Any(x=>x.id!=id&&String.Equals(x.name,name,StringComparison.OrdinalIgnoreCase)))throw new Exception("Ya existe una carpeta con ese nombre");
   folder.name=name;SaveFolders(project,folders);
  }
  public static void DeleteFolder(Project project,string id){
   var folders=Folders(project);if(folders.RemoveAll(x=>x.id==id)!=1)throw new Exception("Carpeta no encontrada");
   SaveFolders(project,folders);foreach(var asset in project.assets)if(FolderOf(asset)==id)Move(project,asset.id,null);
  }
  public static string FolderOf(Asset asset){var ext=Find(asset.extensions);object value;return ext!=null&&ext.payload!=null&&ext.payload.TryGetValue("folder_id",out value)?Convert.ToString(value):null;}
  public static void Move(Project project,string assetId,string folderId){
   var asset=project.assets.FirstOrDefault(x=>x.id==assetId);if(asset==null)throw new Exception("Medio no encontrado");
   if(folderId!=null&&!Folders(project).Any(x=>x.id==folderId))throw new Exception("Carpeta no encontrada");
   asset.extensions.RemoveAll(x=>x!=null&&x.@namespace==Namespace);
   if(folderId!=null)Ensure(asset.extensions).payload["folder_id"]=folderId;
  }
 }
}
