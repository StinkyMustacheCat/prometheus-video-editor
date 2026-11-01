using System;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  void ShowMediaInfo(string assetId,string clipId){
   var asset=editor.Project.assets.FirstOrDefault(a=>a.id==assetId);
   if(asset==null)return;
   Clip clip=clipId==null?null:editor.Project.Active.clips.FirstOrDefault(c=>c.id==clipId);
   if(infoWindow==null||infoWindow.IsDisposed){
    infoWindow=new MediaInfoWindow();
    infoWindow.OpenFolderRequested=OpenLibraryAssetFolder;
    infoWindow.OpenPageRequested=OpenWebSource;
    infoWindow.FormClosed+=(s,e)=>infoWindow=null;
    infoWindow.Show(this);
   }
   infoWindow.ShowMedia(editor.Project,asset,clip);
   infoWindow.BringToFront();
  }
  void CloseMediaInfo(){if(infoWindow!=null&&!infoWindow.IsDisposed)infoWindow.Close();infoWindow=null;}
 }
}
