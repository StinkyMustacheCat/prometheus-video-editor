using System;
using System.IO;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  string UnsavedRecoveryFile {get{return Path.Combine(recoveryDir,"recovery.prometheus.json");}}
  string CurrentRecoveryFile {get{return projectFile==null?UnsavedRecoveryFile:ProjectStorage.AutosaveFile(projectFile);}}
  void ConfigureProjectStorage(){
   if(projectFile==null){
    webCacheDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","WebCache");
    proxyCacheDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","ProxyCache");
   }else{
    webCacheDir=ProjectStorage.WebDirectory(projectFile);
    proxyCacheDir=ProjectStorage.ProxyDirectory(projectFile);
   }
  }
  void AutosaveTick(){
   if(!dirty||busy||App.Capture)return;
   try{RememberSequenceView();string path=CurrentRecoveryFile;Directory.CreateDirectory(Path.GetDirectoryName(path));Store.Save(editor.Project,path);}
   catch(Exception ex){status.Text="No se pudo guardar la recuperación automática: "+ex.Message;}
  }
  void RemoveRecovery(string path){try{if(File.Exists(path))File.Delete(path);if(File.Exists(path+".bak"))File.Delete(path+".bak");}catch{}}
  Project OpenWithRecovery(string path,out bool recovered){
   recovered=false;var saved=Store.Load(path);string candidate=ProjectStorage.AutosaveFile(path);
   if(!File.Exists(candidate)||File.GetLastWriteTimeUtc(candidate)<=File.GetLastWriteTimeUtc(path))return saved;
   Project backup;
   try{backup=Store.Load(candidate);}catch(Exception ex){status.Text="La copia de recuperación no se puede abrir: "+ex.Message;return saved;}
   if(backup.id!=saved.id)return saved;
   var answer=MessageBox.Show(this,"Hay una recuperación automática más reciente que el proyecto guardado. ¿Quieres abrirla?","Recuperar proyecto",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button1);
   if(answer==DialogResult.Yes){recovered=true;return backup;}
   return saved;
  }
 }
}
