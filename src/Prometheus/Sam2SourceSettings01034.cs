using System;
using System.Windows.Forms;

namespace Prometheus {
 public static class Sam2SourceSettings01034 {
  // Smart masks use the full local source. MGT may need its prepared local representation.
  public static bool UseProxy { get { return false; } }
  public static bool For(Project isolated,string clipId){
   var clip=isolated.Active.clips.Find(c=>c.id==clipId);
   var asset=clip==null?null:isolated.assets.Find(a=>a.id==clip.asset_id);
   return asset!=null&&MgtMedia.IsMgt(asset);
  }
  public static bool Confirm(IWin32Window owner,Asset asset){
   if(asset==null)throw new Exception("No se encontró el medio de la máscara inteligente");
   if(Media.SelectRepresentation(asset,false)!=null)return true;
   if(MgtMedia.IsMgt(asset)&&Media.SelectRepresentation(asset,true)!=null)return true;
   throw new Exception("Descarga o reconecta el medio completo antes de usar la máscara inteligente");
  }
 }
}
