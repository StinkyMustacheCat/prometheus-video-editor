using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Prometheus {
 public static class ProjectStorage {
  public const string Extension=".pthproj";
  public static string RecoveryDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","Recovery");
  public static bool IsProjectFile(string path){return path.EndsWith(Extension,StringComparison.OrdinalIgnoreCase);}
  static string Stem(string projectFile){
   string name=Path.GetFileName(projectFile);
   if(IsProjectFile(name))return name.Substring(0,name.Length-Extension.Length);
   const string legacy=".prometheus.json";
   if(name.EndsWith(legacy,StringComparison.OrdinalIgnoreCase))return name.Substring(0,name.Length-legacy.Length);
   return Path.GetFileNameWithoutExtension(name);
  }
  public static string DataDirectory(string projectFile){
   string full=Path.GetFullPath(projectFile);
   return Path.Combine(Path.GetDirectoryName(full),Stem(full)+(IsProjectFile(full)?".assets":".prometheus-data"));
  }
  public static string WebDirectory(string projectFile){return IsProjectFile(projectFile)?Path.Combine(DataDirectory(projectFile),"medios","web"):Path.Combine(DataDirectory(projectFile),"medios-web");}
  public static string ProxyDirectory(string projectFile){return IsProjectFile(projectFile)?Path.Combine(DataDirectory(projectFile),"cache","vistas-ligeras"):Path.Combine(DataDirectory(projectFile),"vistas-ligeras");}
  public static string ImportDirectory(string projectFile){return IsProjectFile(projectFile)?Path.Combine(DataDirectory(projectFile),"medios","importados"):Path.Combine(DataDirectory(projectFile),"medios-importados");}
  public static string MgtDirectory(string projectFile){return Path.Combine(DataDirectory(projectFile),"medios","mgt");}
  public static string LutDirectory(string projectFile){return IsProjectFile(projectFile)?Path.Combine(DataDirectory(projectFile),"recursos","luts"):Path.Combine(DataDirectory(projectFile),"luts");}
  public static string AutosaveFile(string projectFile){
   if(!IsProjectFile(projectFile))return Path.Combine(DataDirectory(projectFile),"recuperacion.prometheus.json");
   byte[] bytes=Encoding.UTF8.GetBytes(Path.GetFullPath(projectFile).ToUpperInvariant());
   using(var sha=SHA256.Create()){
    string key=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").Substring(0,24);
    return Path.Combine(RecoveryDirectory,key+Extension);
   }
  }
  public static void Ensure(string projectFile){Directory.CreateDirectory(DataDirectory(projectFile));}
 }
}
