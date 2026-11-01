using System;
using System.IO;
using System.Security.Cryptography;

namespace Prometheus {
 public static class EffectResourceStorage {
  static string Hash(string file){
   using(var stream=File.OpenRead(file))using(var sha=SHA256.Create())
    return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
  }
  public static void Materialize(Project project,string projectFile){
   string directory=ProjectStorage.LutDirectory(projectFile);
   foreach(var resource in project.effect_resources){
    if(resource.kind!="lut3d"||String.IsNullOrWhiteSpace(resource.uri)||!File.Exists(resource.uri))continue;
    string source=Path.GetFullPath(resource.uri);
    string target=Path.Combine(directory,Hash(source).Substring(0,24)+".cube");
    if(!String.Equals(source,target,StringComparison.OrdinalIgnoreCase)){
     Directory.CreateDirectory(directory);
     if(!File.Exists(target)){
      string temporary=target+".tmp-"+Store.Id().Substring(0,8);
      try{File.Copy(source,temporary);if(!File.Exists(target))File.Move(temporary,target);}
      finally{if(File.Exists(temporary))File.Delete(temporary);}
     }
    }
    resource.uri=target;resource.relative=false;
   }
  }
 }
}
