using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Prometheus {
 sealed class PendingWebExportException:Exception {
  public readonly List<string> Failures;
  public readonly List<string> AuthUrls;
  public PendingWebExportException(List<string> failures,List<string> authUrls):base("Quedaron "+failures.Count+" medio(s) web pendientes") {Failures=failures;AuthUrls=authUrls;}
 }
 public partial class MainForm {
  WebFailureChoice AskExportWebFailure(ExportProgressWindow progress,Asset asset,Exception error,CancellationToken token){
   WebFailureChoice choice=WebFailureChoice.Continue;
   int expired=0;
   using(var signal=new System.Threading.ManualResetEvent(false)){
    UI(()=>{if(progress.IsDisposed||Interlocked.CompareExchange(ref expired,0,0)!=0)return;progress.PromptMediaFailure(asset.name,error.Message,answer=>{
     choice=answer;try{signal.Set();}catch(ObjectDisposedException){}
    });});
    int outcome=WaitHandle.WaitAny(new[]{signal,token.WaitHandle},10000);
    Interlocked.Exchange(ref expired,1);
    token.ThrowIfCancellationRequested();
    if(outcome==WaitHandle.WaitTimeout)UI(()=>{if(!progress.IsDisposed)progress.ClearMediaFailure();});
   }
   if(choice==WebFailureChoice.Retry&&error is WebAuthBatchException){
    bool allowed=false;using(var signal=new System.Threading.ManualResetEvent(false)){
     UI(()=>{if(!progress.IsDisposed)allowed=ShowWebAccessPrompt(((WebAuthBatchException)error).Urls,progress);try{signal.Set();}catch(ObjectDisposedException){}});
     int outcome=WaitHandle.WaitAny(new[]{signal,token.WaitHandle});token.ThrowIfCancellationRequested();
     if(outcome!=0)allowed=false;
    }
    if(!allowed)choice=WebFailureChoice.Continue;
   }
   return choice;
  }
  void ResolveWebForExport(Project snapshot,bool audioOnly,bool selective,CancellationToken token,ExportProgressWindow progress,Action<Project,Asset,CancellationToken> resolveOne=null){
   var renderSnapshot=NestedMedia0910.Flatten(snapshot);var candidates=snapshot.assets.Where(a=>a.source!=null&&a.source.kind=="web"&&Media.SelectRepresentation(a,false)==null&&renderSnapshot.Active.clips.Any(c=>c.asset_id==a.id&&c.enabled&&renderSnapshot.Active.tracks.Any(t=>t.id==c.track_id&&t.enabled&&(!audioOnly||t.kind=="audio")))).ToList();
   var failures=new List<string>();var authUrls=new List<string>();UI(()=>{if(!progress.IsDisposed)progress.MediaProgress(0,candidates.Count,candidates.FirstOrDefault()==null?"":candidates[0].name);});
   for(int i=0;i<candidates.Count;i++){
    var asset=candidates[i];bool prepared=false;int automaticRetries=0;
    while(!prepared){
     token.ThrowIfCancellationRequested();
     try{if(resolveOne==null)ResolveUsedWeb(snapshot,token,message=>UI(()=>{if(!progress.IsDisposed)progress.MediaStatus(message);}),null,asset.id,audioOnly,selective);else resolveOne(snapshot,asset,token);prepared=true;}
     catch(OperationCanceledException){throw;}
     catch(Exception ex){
      if(!(ex is WebAuthBatchException)&&automaticRetries<2){automaticRetries++;UI(()=>{if(!progress.IsDisposed)progress.MediaStatus("Reintentando "+automaticRetries+"/2: "+asset.name);});if(token.WaitHandle.WaitOne(automaticRetries*1000))token.ThrowIfCancellationRequested();continue;}
      var decision=AskExportWebFailure(progress,asset,ex,token);
      if(decision==WebFailureChoice.Cancel)throw new OperationCanceledException(token);
      if(decision==WebFailureChoice.Retry){automaticRetries=0;continue;}
      if(ex is WebAuthBatchException)authUrls.AddRange(((WebAuthBatchException)ex).Urls);
      failures.Add(asset.name+" Â· "+(asset.source.page_url??asset.source.uri)+Environment.NewLine+ex.Message);break;
     }
    }
    int done=i+1;UI(()=>{if(!progress.IsDisposed)progress.MediaProgress(done,candidates.Count,done<candidates.Count?candidates[done].name:"");});
   }
   if(failures.Count>0)throw new PendingWebExportException(failures,authUrls.Distinct().ToList());
  }
  void ShowExportDialog(){
   if(busy){status.Text="Espera a que termine la tarea actual antes de exportar";return;}
   using(var dialog=new ExportDialog(Store.Copy(editor.Project)))
    if(dialog.ShowDialog(this)==DialogResult.OK)BeginExport(dialog.OutputPath,dialog.Options,dialog.OpenFolderAfter,dialog.PlayAfter);
  }
  static Project ExportWindow(Project project,ExportOptions options){return options.StartFrame.HasValue?SequenceExportRange.Window(project,options.StartFrame.Value,options.EndFrame.Value):project;}
  void BeginExport(string output,ExportOptions options,bool openFolderAfter,bool playAfter,bool authRetry=false){
   output=Path.GetFullPath(output);options.Validate(editor.Project);
   string projectId=editor.Project.id,sequenceName=editor.Project.Active.name;
   if(editor.Project.assets.Any(a=>AssetContainsPath(a,output)))throw new Exception("La exportaciÃ³n no puede sustituir un medio original");
   if(!ShowExportPreflight(options))return;
   Stop();if(webResolveToken!=null){webResolveToken.Cancel();webResolveToken=null;}
   if(webPreviewToken!=null){webPreviewToken.Cancel();webPreviewToken=null;}
   webPreviewQueue.Clear();activeWebPreviewAssetId=null;
   var snapshot=ExportWindow(Store.Copy(editor.Project),options);
   var renderOptions=options.StartFrame.HasValue?options.ForWindow((options.EndFrame.Value-options.StartFrame.Value)/60.0):options;
   busy=true;exportRunning=true;mediaToken=new CancellationTokenSource();var token=mediaToken;
   exportPanel.Visible=false;
   bool retryWebAccess=false;
   using(var progress=new ExportProgressWindow(output)){
    progress.CancelRequested+=()=>token.Cancel();
    status.Text="ExportaciÃ³n en curso";
    Task.Factory.StartNew(()=>{
     string subtitleFolder0820=null;
     try{
      ResolveUsedMgt(snapshot,options.UsePreview,token.Token,message=>UI(()=>{if(!progress.IsDisposed){progress.MediaStatus(message);status.Text=message;}}),!options.Video);
      if(!options.UsePreview)ResolveWebForExport(snapshot,!options.Video,options.SelectiveWeb,token.Token,progress);
      if(options.Video&&SubtitleAnimation0820.Template(snapshot.Active)!=null&&SubtitleTrack0817.Cues(snapshot).Count>0){subtitleFolder0820=Path.Combine(Path.GetTempPath(),"prometheus-subtitle-export-"+Guid.NewGuid().ToString("N"));UI(()=>{if(!progress.IsDisposed)progress.MediaStatus("Preparando subtÃ­tulos animados MGTâ€¦");});SubtitleAnimation0820.PrepareExport(snapshot,subtitleFolder0820,token.Token,percent=>UI(()=>{if(!progress.IsDisposed)progress.MediaStatus("SubtÃ­tulos MGT Â· "+percent+" %");}));}
      string encName = (renderOptions.Video && renderOptions.Acceleration == "nvenc" && Rendering.CanUseNvenc()) ? "GPU · NVIDIA NVENC" : (renderOptions.Video ? "CPU · libx264" : "WAV PCM"); UI(()=>{if(!progress.IsDisposed)progress.BeginRender(encName);});
      Rendering.Export(snapshot,output,token.Token,percent=>UI(()=>{if(!progress.IsDisposed)progress.Render(percent);}),renderOptions);
      UI(()=>{string warning=null;try{if(options.Video)AttachResolvedMgt(snapshot);if(!options.UsePreview)AttachResolvedWeb(snapshot);}catch(Exception ex){warning="No se pudo registrar un medio: "+ex.Message;}
       try{ExportHistory0814.Add(projectId,sequenceName,output,options);}catch(Exception ex){warning=(warning==null?"":warning+" Â· ")+"Historial no actualizado: "+ex.Message;}
       busy=false;exportRunning=false;mediaToken=null;if(!progress.IsDisposed)progress.Complete("ExportaciÃ³n terminada: "+output);status.Text="ExportaciÃ³n terminada: "+output+(warning==null?"":" Â· "+warning);
       try{if(openFolderAfter)System.Diagnostics.Process.Start("explorer.exe","/select,\""+output+"\"");if(playAfter)System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(output){UseShellExecute=true});}catch(Exception ex){status.Text="ExportaciÃ³n terminada; no se pudo abrir el resultado: "+ex.Message;}
      });
     }catch(OperationCanceledException){UI(()=>{try{if(options.Video)AttachResolvedMgt(snapshot);if(!options.UsePreview)AttachResolvedWeb(snapshot);}catch{}busy=false;exportRunning=false;mediaToken=null;if(!progress.IsDisposed)progress.Cancelled();status.Text="ExportaciÃ³n cancelada";});}
     catch(PendingWebExportException ex){UI(()=>{try{if(options.Video)AttachResolvedMgt(snapshot);if(!options.UsePreview)AttachResolvedWeb(snapshot);}catch{}busy=false;exportRunning=false;mediaToken=null;if(ex.AuthUrls.Count>0&&ShowWebAccessPrompt(ex.AuthUrls,progress)){retryWebAccess=true;progress.Close();}else if(!progress.IsDisposed)progress.FailedMedia(ex.Failures);status.Text="ExportaciÃ³n pendiente Â· "+ex.Failures.Count+" medio(s) web sin resolver; los demÃ¡s quedaron preparados";});}
     catch(WebAuthBatchException ex){UI(()=>{try{if(options.Video)AttachResolvedMgt(snapshot);if(options.Video&&!options.UsePreview)AttachResolvedWeb(snapshot);}catch{}busy=false;exportRunning=false;mediaToken=null;
      if(ShowWebAccessPrompt(ex.Urls,progress)){retryWebAccess=true;progress.Close();}
      else{if(!progress.IsDisposed)progress.Failed("Quedaron enlaces pendientes de acceso");status.Text="ExportaciÃ³n pendiente: confirma el acceso al sitio";}
     });}
     catch(Exception ex){UI(()=>{try{if(options.Video)AttachResolvedMgt(snapshot);if(!options.UsePreview)AttachResolvedWeb(snapshot);}catch{}busy=false;exportRunning=false;mediaToken=null;if(!progress.IsDisposed)progress.Failed(ex.Message);status.Text="ExportaciÃ³n fallida: "+ex.Message;});}
     finally{if(subtitleFolder0820!=null)try{Directory.Delete(subtitleFolder0820,true);}catch{}}
    });
    progress.ShowDialog(this);
   }
   if(retryWebAccess&&!closing)BeginExport(output,options,openFolderAfter,playAfter,true);
  }
 }
}

