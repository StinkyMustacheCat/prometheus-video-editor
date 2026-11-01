using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  static string RecentProjectsFile01042 { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","recent-projects.txt"); } }
  static List<string> ReadRecentProjects01042(){
   try{return File.ReadAllLines(RecentProjectsFile01042).Where(path=>!String.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();}
   catch{return new List<string>();}
  }
  void RememberRecentProject01042(string path){
   if(String.IsNullOrWhiteSpace(path))return;
   try{var list=ReadRecentProjects01042();list.RemoveAll(item=>String.Equals(item,path,StringComparison.OrdinalIgnoreCase));list.Insert(0,Path.GetFullPath(path));Directory.CreateDirectory(Path.GetDirectoryName(RecentProjectsFile01042));File.WriteAllLines(RecentProjectsFile01042,list.Take(10).ToArray());}
   catch{ /* A failed recent-list update must never prevent opening or saving a project. */ }
  }
  void ShowRecentProjects01042(Button anchor){
   var menu=new ContextMenuStrip();var paths=ReadRecentProjects01042();
   if(paths.Count==0){menu.Items.Add("Todavía no hay proyectos recientes").Enabled=false;}
   foreach(var path in paths){string selected=path;var item=menu.Items.Add(Path.GetFileNameWithoutExtension(path));item.ToolTipText=path;item.Enabled=File.Exists(path);if(item.Enabled)item.Click+=(s,e)=>{if(!MayDiscard())return;try{OpenProjectPath(selected);}catch(Exception ex){Error(ex);}};}
   menu.Closed+=(s,e)=>DisposePopupLater(menu);menu.Show(anchor,new Point(0,anchor.Height));
  }
  void ShowSaveContext01042(Button anchor,Point point){
   var menu=new ContextMenuStrip();menu.Items.Add("Guardar como…",null,(s,e)=>SaveProjectAs());menu.Closed+=(s,e)=>DisposePopupLater(menu);menu.Show(anchor,point);
  }
 }
}
