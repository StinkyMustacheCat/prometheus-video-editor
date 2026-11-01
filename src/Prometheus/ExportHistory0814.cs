using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
namespace Prometheus {
 public sealed class ExportRecord0814 {
  public string project_id,sequence_name,path,exported_utc,format;
  public int width,height,fps;
 }
 public sealed class ExportPreferences0814 {public bool open_folder,play_result;}
 public sealed class ExportHistoryData0814 {
  public List<ExportRecord0814> records=new List<ExportRecord0814>();
  public ExportPreferences0814 preferences=new ExportPreferences0814();
 }
 public static class ExportHistory0814 {
  static readonly object gate=new object();
  public static string OverridePath;
  static string FilePath {get{return OverridePath??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","export-history.json");}}
  static ExportHistoryData0814 Read(){try{if(File.Exists(FilePath)){var data=new JavaScriptSerializer().Deserialize<ExportHistoryData0814>(File.ReadAllText(FilePath));if(data!=null){if(data.records==null)data.records=new List<ExportRecord0814>();if(data.preferences==null)data.preferences=new ExportPreferences0814();return data;}}}catch{}return new ExportHistoryData0814();}
  static void Write(ExportHistoryData0814 data){Directory.CreateDirectory(Path.GetDirectoryName(FilePath));string tmp=FilePath+".tmp";File.WriteAllText(tmp,new JavaScriptSerializer().Serialize(data));if(File.Exists(FilePath))File.Copy(tmp,FilePath,true);else File.Move(tmp,FilePath);if(File.Exists(tmp))File.Delete(tmp);}
  public static List<ExportRecord0814> For(string projectId){lock(gate)return Read().records.Where(r=>r.project_id==projectId).OrderByDescending(r=>r.exported_utc).Take(30).ToList();}
  public static ExportPreferences0814 Preferences(){lock(gate)return Read().preferences;}
  public static void SetPreferences(bool folder,bool play){lock(gate){var data=Read();data.preferences.open_folder=folder;data.preferences.play_result=play;Write(data);}}
  public static void Add(string projectId,string sequence,string path,ExportOptions options){lock(gate){var data=Read();data.records.RemoveAll(r=>r.project_id==projectId&&String.Equals(r.path,path,StringComparison.OrdinalIgnoreCase));data.records.Insert(0,new ExportRecord0814{project_id=projectId,sequence_name=sequence,path=path,exported_utc=DateTime.UtcNow.ToString("o"),format=options.Video?"MP4":"WAV",width=options.Video?options.Width:0,height=options.Video?options.Height:0,fps=options.Video?options.Fps:0});data.records=data.records.GroupBy(r=>r.project_id).SelectMany(g=>g.OrderByDescending(r=>r.exported_utc).Take(30)).ToList();Write(data);}}
 }
}
