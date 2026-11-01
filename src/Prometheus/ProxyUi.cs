using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Prometheus {
 public partial class MainForm {
  CancellationTokenSource proxyToken;
  string proxyCacheDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","ProxyCache");
  void CancelProxy(){if(proxyToken!=null){proxyToken.Cancel();proxyToken=null;}}
  void BeginProxy(string assetId){
   if(proxyToken!=null){status.Text="Ya se está generando un proxy";return;}
   var asset=Store.Copy(editor.Project).assets.Find(x=>x.id==assetId);
   if(asset==null)throw new Exception("Medio no encontrado");
   var original=asset.representations.FirstOrDefault(x=>x.role=="original");
   if(original==null||original.locator==null||!File.Exists(original.locator.path))throw new Exception("Reconecta el original antes de generar el proxy");
   var token=new CancellationTokenSource();proxyToken=token;string sourcePath=original.locator.path;
   status.Text="Generando proxy de "+asset.name+"… Esc cancela";
   Task.Factory.StartNew(()=>{
    try{
     var rep=Media.GenerateProxy(asset,proxyCacheDir,token.Token,percent=>UI(()=>{if(proxyToken==token)status.Text="Generando proxy de "+asset.name+" · "+(int)percent+" %";}));
     UI(()=>{
      if(proxyToken!=token||token.IsCancellationRequested)return;
      proxyToken=null;
      try{
       editor.Commit(p=>{
        var current=p.assets.Find(x=>x.id==assetId);if(current==null)throw new Exception("El medio ya no está en el proyecto");
        var originalNow=current.representations.FirstOrDefault(x=>x.role=="original");
        if(originalNow==null||originalNow.locator==null||!String.Equals(sourcePath,originalNow.locator.path,StringComparison.OrdinalIgnoreCase))throw new Exception("El original cambió durante la generación; repite el proxy");
        current.representations.RemoveAll(x=>x.role=="proxy");current.representations.Add(rep);
       });
       dirty=true;RefreshProject();timeline.Invalidate();
       if(!playing)RequestFrame();
       if(mediaView.SelectedAssetId==assetId)ShowAsset();
       status.Text="Proxy listo para vista previa · exportación desde el original";
      }catch(Exception ex){Error(ex);}
     });
    }catch(OperationCanceledException){UI(()=>{if(proxyToken==token){proxyToken=null;status.Text="Generación de proxy cancelada";}});}
    catch(Exception ex){UI(()=>{if(proxyToken==token){proxyToken=null;Error(ex);}});}
   });
  }
  void ChangeProxyPreference(string assetId,bool enabled){
   Stop();editor.Commit(p=>{var asset=p.assets.Find(x=>x.id==assetId);if(asset==null)throw new Exception("Medio no encontrado");Media.SetProxyPreference(asset,enabled);});
   dirty=true;RefreshProject();if(mediaView.SelectedAssetId==assetId)ShowAsset();RequestFrame();
   status.Text=enabled?"Vista previa con proxy":"Vista previa con original";
  }
 }
}
