using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Prometheus {
 public static class ProjectResourceTransfer {
  static bool Same(string a,string b){return String.Equals(Path.GetFullPath(a),Path.GetFullPath(b),StringComparison.OrdinalIgnoreCase);}
  static string ShortName(string value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").Substring(0,18).ToLowerInvariant();}
  static string ExtensionOf(string path){return MgtMedia.IsJsonFile(path)?".mgt.json":Path.GetExtension(path);}
  static string Copy(string source,string directory,string name){
   if(String.IsNullOrWhiteSpace(source)||!File.Exists(source))return source;
   Directory.CreateDirectory(directory);
   string extension=ExtensionOf(source);
   string destination=Path.Combine(directory,ShortName(name)+extension);
   if(Same(source,destination))return destination;
   if(File.Exists(destination))destination=Path.Combine(directory,ShortName(name+Store.Id())+extension);
   string partial=destination+".tmp-"+Store.Id().Substring(0,8);
   try{File.Copy(source,partial);File.Move(partial,destination);return destination;}
   catch(PathTooLongException){throw new IOException("La ruta de recursos del proyecto supera el límite de Windows: "+partial);}
   finally{if(File.Exists(partial))File.Delete(partial);}
  }
  static string CopyMapped(Dictionary<string,string> copied,string source,string directory,string name){
   if(String.IsNullOrWhiteSpace(source))return source;
   string result;if(copied.TryGetValue(source,out result))return result;
   result=Copy(source,directory,name);copied[source]=result;return result;
  }
  static void Update(MediaRepresentation representation,string destination){
   if(String.IsNullOrWhiteSpace(destination)||!File.Exists(destination))return;
   var info=new FileInfo(destination);
   representation.locator.path=info.FullName;representation.locator.relative=false;
   representation.size_bytes=info.Length;
   representation.modified_utc=info.LastWriteTimeUtc.ToString("o",CultureInfo.InvariantCulture);
  }
  // The project owns downloaded media and images. Local video/audio stays linked.
  public static void Materialize(Project project,string destinationFile){
   var copiedPaths=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   foreach(var asset in project.assets){
    if(asset.source==null||asset.source.kind=="generated")continue;
    bool image=asset.streams.Any(s=>s.kind=="image");
    bool owned=asset.source.kind=="web"||asset.source.kind=="mgt"||asset.source.kind=="local"&&image;
    if(!owned)continue;
    string root=asset.source.kind=="mgt"?ProjectStorage.MgtDirectory(destinationFile):asset.source.kind=="web"?ProjectStorage.WebDirectory(destinationFile):ProjectStorage.ImportDirectory(destinationFile);
    if(asset.source.kind=="mgt"||asset.source.kind=="local"&&image){
     string copied=CopyMapped(copiedPaths,asset.source.uri,root,asset.id+"-source");
     if(File.Exists(copied)){asset.source.uri=copied;asset.source.relative=false;}
    }
    int ordinal=0;
    foreach(var rep in asset.representations){
     if(rep==null||rep.locator==null)continue;
     string directory=rep.role=="preview"||rep.role=="proxy"?ProjectStorage.ProxyDirectory(destinationFile):root;
     string copied=CopyMapped(copiedPaths,rep.locator.path,directory,asset.id+"-"+rep.role+"-"+ordinal++);
     Update(rep,copied);
    }
    if(asset.source.kind=="web"){
     var metadata=asset.extensions.FirstOrDefault(x=>x!=null&&x.@namespace==WebMedia.MetadataNamespace&&x.payload!=null);
     object svgPath;
     if(metadata!=null&&metadata.payload.TryGetValue("svg_source_path",out svgPath)&&svgPath is string){
      string copied=CopyMapped(copiedPaths,(string)svgPath,root,asset.id+"-svg-source");
      if(File.Exists(copied))metadata.payload["svg_source_path"]=copied;
     }
     var ranges=RangeMedia.Read(asset);
     for(int i=0;i<ranges.Count;i++){
      string copied=CopyMapped(copiedPaths,ranges[i].path,root,asset.id+"-range-"+i);
      if(File.Exists(copied)){
       var info=new FileInfo(copied);ranges[i].path=info.FullName;ranges[i].size_bytes=info.Length;
       ranges[i].modified_utc=info.LastWriteTimeUtc.ToString("o",CultureInfo.InvariantCulture);
      }
     }
     if(ranges.Count>0)RangeMedia.Write(asset,ranges);
    }
    Store.NormalizeAsset(asset);
   }
  }
 }
}
