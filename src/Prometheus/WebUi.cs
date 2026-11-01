using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  CancellationTokenSource webImportToken,webPreviewToken,webResolveToken;
  readonly Queue<string> webPreviewQueue=new Queue<string>();
  readonly HashSet<string> webPreviewRefreshIds=new HashSet<string>();
  string activeWebPreviewAssetId;
  string webCacheDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","WebCache");
  string ChooseWebKind(){
   using(var dialog=new Form{Text="¿Qué contiene el enlace?",Width=365,Height=145,FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.Manual,MaximizeBox=false,MinimizeBox=false,ShowInTaskbar=false,TopMost=true,BackColor=panel,ForeColor=text}){
    var location=Control.MousePosition;dialog.Location=new Point(Math.Max(0,location.X-140),Math.Max(0,location.Y-55));
    dialog.Controls.Add(new Label{Text="No se pudo identificar el tipo automáticamente.",Left=14,Top=13,Width=335,Height=27});
    var image=new Button{Text="Imagen",Left=14,Top=50,Width=95,DialogResult=DialogResult.Yes};
    var video=new Button{Text="Vídeo",Left=119,Top=50,Width=95,DialogResult=DialogResult.No};
    var cancel=new Button{Text="Cancelar",Left=224,Top=50,Width=95,DialogResult=DialogResult.Cancel};
    dialog.Controls.AddRange(new Control[]{image,video,cancel});dialog.CancelButton=cancel;
    var result=dialog.ShowDialog();return result==DialogResult.Yes?"image":result==DialogResult.No?"video":null;
   }
  }
  Asset InspectWebLink(string url,string pageUrl,CancellationToken token){
   try{return WebMedia.InspectDirectLink(url,pageUrl,webCacheDir,null,token);}
   catch(Exception ex){
    if(!ex.Message.StartsWith("No se pudo identificar el contenido del enlace",StringComparison.Ordinal))throw;
    token.ThrowIfCancellationRequested();
    string kind=IsHandleCreated?(string)Invoke(new Func<string>(ChooseWebKind)):null;
    if(kind==null)throw new OperationCanceledException();
    return WebMedia.InspectDirectLink(url,pageUrl,webCacheDir,kind,token);
   }
  }
  void CancelWebWork(){CancelPlaceholderLink();webPreviewQueue.Clear();webPreviewRefreshIds.Clear();activeWebPreviewAssetId=null;if(webImportToken!=null){webImportToken.Cancel();webImportToken=null;}if(webPreviewToken!=null){webPreviewToken.Cancel();webPreviewToken=null;}if(webResolveToken!=null){webResolveToken.Cancel();webResolveToken=null;}}
  void ShowImportLink(){ShowImportLink(null,0,false);}
  void ShowImportLink(string trackId,double at,bool safePlacement){ShowImportLink(trackId,at,safePlacement,null);}
  void ShowImportLink(string trackId,double at,bool safePlacement,string prefill){ShowImportLink(trackId,at,safePlacement,prefill,null);}
  void ShowImportLink(string trackId,double at,bool safePlacement,string prefill,double? exactDuration){
   bool audioTarget=trackId!=null&&editor.Project.Active.tracks.Any(t=>t.id==trackId&&t.kind=="audio");
   using(var dialog=new LinkImportDialog(trackId!=null,audioTarget,panel,text)){
    dialog.PrefillUrl=prefill;
    if(dialog.ShowDialog(this)==DialogResult.OK){double? begin=exactDuration.HasValue?dialog.RangeIn??0:dialog.RangeIn;double? end=exactDuration.HasValue?begin.Value+exactDuration.Value:dialog.RangeOut;StartWebImport(new WebDropInfo{Url=dialog.Link,PageUrl=dialog.Link,IsYouTube=WebMedia.IsYouTubeUrl(dialog.Link)},trackId,at,safePlacement,begin,end);}
   }
  }
  static bool WebAuthChallenge(Exception ex){string message=ex==null?"":ex.Message;return message.IndexOf("not a bot",StringComparison.OrdinalIgnoreCase)>=0||message.IndexOf("sign in to confirm",StringComparison.OrdinalIgnoreCase)>=0||message.IndexOf("cookies-from-browser",StringComparison.OrdinalIgnoreCase)>=0;}
  void StartWebImport(WebDropInfo drop,string trackId,double at,bool safePlacement=false,double? requestedIn=null,double? requestedOut=null,bool authRetry=false){
   if(drop==null||String.IsNullOrWhiteSpace(drop.Url))return;
   if(webImportToken!=null){status.Text="Espera a que termine la importación web actual";return;}
   string platform=WebMedia.Platform(drop.Url);
   if(platform==null&&!WebMedia.IsImageUrl(drop.Url)){status.Text="Enlace no compatible";return;}
   bool audioOnly=false;
   if(trackId==Timeline.NewAudioTrackDrop)audioOnly=true;
   else if(trackId!=null&&trackId!=Timeline.NewVideoTrackDrop){var track=editor.Project.Active.tracks.Find(t=>t.id==trackId);if(track==null||track.locked){status.Text="Pista de destino no disponible";return;}audioOnly=track.kind=="audio";}
   if(audioOnly&&platform==null){status.Text="El enlace no contiene audio web compatible";return;}
   var token=new CancellationTokenSource();webImportToken=token;string projectId=editor.Project.id;
   status.Text=audioOnly?"Consultando audio de "+WebMedia.PlatformLabel(platform)+"…":"Analizando enlace web…";
   Task.Factory.StartNew(()=>{
    try{
     var asset=audioOnly?WebMedia.InspectAudio(drop.Url,token.Token):InspectWebLink(drop.Url,drop.PageUrl,token.Token);
     UI(()=>{
      if(webImportToken!=token||token.IsCancellationRequested)return;webImportToken=null;
      if(editor.Project.id!=projectId){status.Text="La importación web terminó después de cambiar de proyecto";return;}
      try{
       double? selectionIn=requestedIn,selectionOut=requestedOut;
       bool video=asset.streams.Any(s=>s.kind=="video");
       if(selectionIn.HasValue){if(trackId==null||asset.streams.All(s=>s.kind=="image"))throw new Exception("Solo puedes elegir tramos de vídeo o audio al importar en una pista");if(WebMedia.DurationProvisional(asset))WebMedia.EnsureProvisionalDuration(asset,selectionOut??Math.Min(48*3600,selectionIn.Value+5));else if((selectionOut??selectionIn.Value)>RangeUi.Duration(asset)+0.001)throw new Exception("El tramo elegido supera la duración del medio");}
       PlacementResult placed=null;
       editor.Commit(p=>{p.assets.Add(asset);if(trackId!=null){string destination=trackId==Timeline.NewVideoTrackDrop?Store.AddTrack(p.Active,"video").id:trackId==Timeline.NewAudioTrackDrop?Store.AddTrack(p.Active,"audio").id:trackId;placed=Media.PlaceInFreeTrack(p,asset.id,destination,at,selectionIn,selectionOut.HasValue?selectionOut.Value-selectionIn.Value:(double?)null);}});
       dirty=true;RefreshProject();mediaView.SelectLast();if(placed!=null)RevealPlacedClip(placed);RefreshPendingUi();RequestFrame();
       status.Text=audioOnly?"Audio virtual añadido · descargando audio en segundo plano…":video?(prepareWebPreviewAutomatically?"Vídeo virtual añadido · preparando vista ligera…":"Vídeo virtual añadido · vista ligera pendiente") :"Imagen web guardada localmente y añadida";
       if(audioOnly)BeginWebPreview(asset.id);
       else if(video){if(prepareWebPreviewAutomatically)BeginWebPreview(asset.id);Task.Factory.StartNew(()=>{WebMedia.DownloadThumbnail(asset,webCacheDir,CancellationToken.None);UI(()=>{mediaView.RefreshThumbnail(asset.id);if(mediaView.SelectedAssetId==asset.id)ShowAsset();});});}
      }catch(Exception ex){Error(ex);}
     });
    }catch(OperationCanceledException){UI(()=>{if(webImportToken==token){webImportToken=null;status.Text="Importación web cancelada";}});}
    catch(Exception ex){UI(()=>{if(webImportToken!=token)return;webImportToken=null;if(WebAuthChallenge(ex)){
      if(ShowWebAccessPrompt(new[]{drop.Url})){StartWebImport(drop,trackId,at,safePlacement,requestedIn,requestedOut,true);return;}
      status.Text="Enlace pendiente de acceso: "+drop.Url;return;
     }Error(ex);});}
   });
  }
  void BeginWebPreview(string assetId,bool refresh=false){
   if(String.IsNullOrWhiteSpace(assetId))return;if(refresh)webPreviewRefreshIds.Add(assetId);
   if(webPreviewQueue.Contains(assetId)||!refresh&&activeWebPreviewAssetId==assetId)return;
   webPreviewQueue.Enqueue(assetId);StartNextWebPreview();
  }
  void StartNextWebPreview(){
   if(webPreviewToken!=null)return;
   while(webPreviewQueue.Count>0){
    string assetId=webPreviewQueue.Dequeue();bool refresh=webPreviewRefreshIds.Remove(assetId);var currentAsset=editor.Project.assets.Find(a=>a.id==assetId);
    var asset=currentAsset==null?null:Store.Json().Deserialize<Asset>(Store.Json().Serialize(Store.Fields(currentAsset)));
    bool audioOnly=WebMedia.IsAudioOnly(asset);
    if(asset==null||asset.source==null||(!audioOnly&&asset.source.resolver_type!="youtube/yt-dlp"&&(asset.source.resolver_type==null||!asset.source.resolver_type.StartsWith("yt-dlp/",StringComparison.Ordinal)))||!refresh&&Media.SelectRepresentation(asset,true)!=null)continue;
    var token=new CancellationTokenSource();webPreviewToken=token;activeWebPreviewAssetId=assetId;string projectId=editor.Project.id;
    Task.Factory.StartNew(()=>{
     try{
      bool provisional=WebMedia.DurationProvisional(asset);
      var rep=audioOnly?WebMedia.ResolveAudio(asset,webCacheDir,token.Token,message=>UI(()=>{if(webPreviewToken==token)status.Text=message;})):
       refresh&&Media.SelectRepresentation(asset,false)!=null?WebMedia.PreviewFromFull(asset,webCacheDir,token.Token,message=>UI(()=>{if(webPreviewToken==token)status.Text=message;}),true):
       WebMedia.ResolveVideo(asset,webCacheDir,token.Token,message=>UI(()=>{if(webPreviewToken==token)status.Text=message;}),!provisional);
      double measured=provisional?WebMedia.MeasureDuration(rep.locator.path,token.Token):0;
      UI(()=>{
       if(webPreviewToken!=token)return;webPreviewToken=null;activeWebPreviewAssetId=null;
       if(!token.IsCancellationRequested&&editor.Project.id==projectId)try{
        bool visible=editor.UpdateDerivedAsset(assetId,asset.source.uri,p=>{
         var current=p.assets.Find(a=>a.id==assetId);
         if(provisional)WebMedia.ReconcileDuration(p,assetId,measured);
         current.representations.RemoveAll(x=>x.role==rep.role);
         current.representations.Add(rep);
        });
        if(visible){dirty=true;RefreshProject();if(!playing)RequestFrame();status.Text=audioOnly?"Audio web listo · medio final disponible":provisional?"Medio web completo y duración confirmada":"Vista ligera lista · el vídeo completo se resolverá antes de exportar";}
       }
       catch(Exception ex){Error(ex);}
       StartNextWebPreview();
      });
     }catch(OperationCanceledException){UI(()=>{if(webPreviewToken==token){webPreviewToken=null;activeWebPreviewAssetId=null;status.Text="Vista ligera cancelada";StartNextWebPreview();}});}
     catch(Exception ex){UI(()=>{if(webPreviewToken==token){webPreviewToken=null;activeWebPreviewAssetId=null;status.Text="Vista ligera no disponible: "+ex.Message;StartNextWebPreview();}});}
    });
    return;
   }
  }
  List<MediaRepresentation> ResolveUsedWeb(Project snapshot,CancellationToken token,Action<string> statusUpdate,Action<Asset,MediaRepresentation> resolvedOne=null,string onlyId=null,bool audioOnlyOutput=false,bool selective=false,Action<int,int,string> progress=null,string scope="timeline",bool force=false,IEnumerable<string> targetIds=null){
   var resolved=new List<MediaRepresentation>();
   var authUrls=new List<string>();
   var targets=targetIds==null?null:new HashSet<string>(targetIds);
   var renderSnapshot=NestedMedia0910.Flatten(snapshot);
   var candidates=snapshot.assets.Where(a=>(targets==null||targets.Contains(a.id))&&(onlyId!=null?a.id==onlyId:scope=="project"||renderSnapshot.Active.clips.Any(c=>c.asset_id==a.id&&c.enabled&&renderSnapshot.Active.tracks.Any(t=>t.id==c.track_id&&t.enabled&&(!audioOnlyOutput||t.kind=="audio"))))&&a.source!=null&&a.source.kind=="web"&&(force||Media.SelectRepresentation(a,false)==null)).ToList();
   if(progress!=null)progress(0,candidates.Count,candidates.Count>0?candidates[0].name:"");
   for(int i=0;i<candidates.Count;i++){
    var asset=candidates[i];token.ThrowIfCancellationRequested();MediaRepresentation rep=null;bool partial=false;
    try{
    bool extractor=asset.source.resolver_type=="youtube/yt-dlp"||asset.source.resolver_type!=null&&asset.source.resolver_type.StartsWith("yt-dlp/",StringComparison.Ordinal);
    if(selective&&!force&&RangeMedia.CoversUsed(renderSnapshot,asset))partial=true;
    else if(selective&&!force&&RangeMedia.CanResolveSelective(asset)){
     try{if(statusUpdate!=null)statusUpdate("Descargando solo tramos usados…");RangeMedia.ResolveUsed(renderSnapshot,asset,webCacheDir,token,statusUpdate);partial=true;}
     catch(OperationCanceledException){throw;}
     catch(Exception ex){if(statusUpdate!=null)statusUpdate("No se pudo usar tramos ("+ex.Message+"). Descargando medio completo…");}
    }
    if(!partial){
     if(audioOnlyOutput&&extractor)rep=WebMedia.ResolveAudioTrack(asset,webCacheDir,token,statusUpdate,force);
     else if(WebMedia.IsAudioOnly(asset))rep=WebMedia.ResolveAudio(asset,webCacheDir,token,statusUpdate,force);
     else if(extractor){if(statusUpdate!=null)statusUpdate("Resolviendo medio completo…");rep=WebMedia.ResolveVideo(asset,webCacheDir,token,statusUpdate,false,force);}
     else if(asset.source.resolver_type=="image-http"){
      if(statusUpdate!=null)statusUpdate("Recuperando imagen web…");var image=WebMedia.DownloadImage(asset.source.uri,asset.source.page_url,webCacheDir,token,force);
      rep=image.representations.First();rep.stream_ordinals=new Dictionary<string,int>{{asset.streams[0].id,image.streams[0].ordinal}};
     }else throw new Exception("No se sabe resolver el origen web de "+asset.name);
     if(WebMedia.DurationProvisional(asset))WebMedia.ReconcileDuration(snapshot,asset.id,WebMedia.MeasureDuration(rep.locator.path,token));
     asset.representations.RemoveAll(x=>x.role==rep.role);asset.representations.Add(rep);resolved.Add(rep);
    }
    if(resolvedOne!=null)resolvedOne(asset,rep);
    if(progress!=null)progress(i+1,candidates.Count,i+1<candidates.Count?candidates[i+1].name:"");
    }catch(OperationCanceledException){throw;}
     catch(Exception ex){if(!WebAuthChallenge(ex))throw;authUrls.Add(asset.source.page_url??asset.source.uri);if(statusUpdate!=null)statusUpdate("Acceso pendiente: "+asset.name);if(progress!=null)progress(i+1,candidates.Count,i+1<candidates.Count?candidates[i+1].name:"");}
   }
   if(authUrls.Count>0)throw new WebAuthBatchException(authUrls);
   return resolved;
  }
  void AttachResolvedWeb(Project snapshot){
   if(editor.Project.id!=snapshot.id)return;
   var additions=new Dictionary<string,MediaRepresentation>();var rangeAdditions=new Dictionary<string,Asset>();var measured=new Dictionary<string,double>();
   foreach(var asset in snapshot.assets.Where(a=>a.source!=null&&a.source.kind=="web")){
    var rep=asset.representations.FirstOrDefault(x=>x.role=="final"||x.role=="original");
    if(rep!=null&&editor.Project.assets.Any(a=>a.id==asset.id&&!a.representations.Any(r=>r.role==rep.role)))additions[asset.id]=rep;
    if(RangeMedia.Read(asset).Count>0&&editor.Project.assets.Any(a=>a.id==asset.id&&RangeMedia.Read(asset).Any(r=>!RangeMedia.Read(a).Any(current=>Math.Abs(current.start-r.start)<0.0001&&Math.Abs(current.end-r.end)<0.0001&&current.path==r.path&&current.size_bytes==r.size_bytes&&current.modified_utc==r.modified_utc))))rangeAdditions[asset.id]=asset;
    if(!WebMedia.DurationProvisional(asset)&&editor.Project.assets.Any(a=>a.id==asset.id&&WebMedia.DurationProvisional(a)))measured[asset.id]=asset.streams.Where(x=>x.duration!=null).Select(x=>x.duration.Seconds).DefaultIfEmpty(0).Max();
   }
   if(additions.Count==0&&rangeAdditions.Count==0&&measured.Count==0)return;
   editor.Commit(p=>{foreach(var pair in measured)WebMedia.ReconcileDuration(p,pair.Key,pair.Value);foreach(var pair in additions){var asset=p.assets.Find(a=>a.id==pair.Key);if(asset!=null){asset.representations.RemoveAll(r=>r.role==pair.Value.role);asset.representations.Add(pair.Value);}}foreach(var pair in rangeAdditions){var asset=p.assets.Find(a=>a.id==pair.Key);if(asset!=null)RangeMedia.Merge(asset,pair.Value);}});
   dirty=true;RefreshProject();RequestFrame();
   foreach(var assetId in additions.Keys)BeginWebPreview(assetId,true);
  }
  void ResolveUsedWebNow(){ResolveWebNow(null,false,"timeline");}
  void ResolveProjectWebNow(){ResolveWebNow(null,false,"project");}
  void RedownloadAllWebNow(){ResolveWebNow(null,false,"project",true);}
  void RedownloadAssetWebNow(string assetId){ResolveWebNow(assetId,false,"project",true);}
  void ResolveAssetWebNow(string assetId){ResolveWebNow(assetId);}
  void ResolveWebNow(string onlyId,bool authRetry=false,string scope="timeline",bool force=false,IEnumerable<string> targetIds=null){
   if(busy)return;
   if(webResolveToken!=null){status.Text="La resolución de medios ya está en curso";return;}
   var snapshot=Store.Copy(editor.Project);string projectId=snapshot.id;var targets=targetIds==null?null:new HashSet<string>(targetIds);var renderSnapshot=NestedMedia0910.Flatten(snapshot);
   bool pending=snapshot.assets.Any(a=>{if(a.source==null||a.source.kind!="web"||onlyId!=null&&a.id!=onlyId||targets!=null&&!targets.Contains(a.id)||!force&&Media.SelectRepresentation(a,false)!=null)return false;bool used=renderSnapshot.Active.clips.Any(c=>c.asset_id==a.id&&c.enabled&&renderSnapshot.Active.tracks.Any(t=>t.id==c.track_id&&t.enabled));return used||onlyId!=null||scope=="project";});
   if(!pending){status.Text=onlyId==null?"Todos los medios web utilizados ya están resueltos":"Este medio ya está resuelto";return;}
   var token=new CancellationTokenSource();webResolveToken=token;
   status.Text=onlyId==null?"Resolviendo medios web en segundo plano · puedes seguir editando":"Resolviendo solo este medio web · puedes seguir editando";
   Task.Factory.StartNew(()=>{
    try{
     ResolveUsedWeb(snapshot,token.Token,message=>UI(()=>{if(webResolveToken==token)status.Text=message+" · puedes seguir editando";}),(asset,rep)=>UI(()=>{
      Action connect=()=>{
       if(token.IsCancellationRequested||editor.Project.id!=projectId)return;
       try{
        editor.UpdateDerivedAsset(asset.id,asset.source.uri,p=>{var current=p.assets.Find(a=>a.id==asset.id);if(!WebMedia.DurationProvisional(asset)&&WebMedia.DurationProvisional(current))WebMedia.ReconcileDuration(p,asset.id,asset.streams.Where(x=>x.duration!=null).Select(x=>x.duration.Seconds).DefaultIfEmpty(0).Max());if(rep==null)RangeMedia.Merge(current,asset);else{current.representations.RemoveAll(x=>x.role==rep.role);current.representations.Add(rep);}});
        dirty=true;RefreshProject();RequestFrame();
        if(rep!=null&&rep.role=="final"&&!WebMedia.IsAudioOnly(asset))BeginWebPreview(asset.id,true);
       }catch(Exception ex){status.Text="Medio descargado, pero no se pudo conectar: "+ex.Message;}
      };
      if(playing){webUpdatesAfterPlayback.Add(connect);status.Text="Medio descargado · se conectará al pausar";}
      else connect();
     }),onlyId,false,false,null,scope,force,targets);
     UI(()=>{if(webResolveToken==token){webResolveToken=null;status.Text=onlyId==null?"Medios web utilizados resueltos · edición disponible":"Medio web resuelto · edición disponible";}});
    }catch(OperationCanceledException){UI(()=>{if(webResolveToken==token){webResolveToken=null;status.Text="Resolución en segundo plano cancelada";}});}
    catch(WebAuthBatchException ex){UI(()=>{if(webResolveToken!=token)return;webResolveToken=null;if(ShowWebAccessPrompt(ex.Urls))ResolveWebNow(onlyId,true,scope,force,targets);else status.Text="Quedaron "+ex.Urls.Count+" enlace(s) pendientes de acceso";});}
    catch(Exception ex){UI(()=>{if(webResolveToken==token){webResolveToken=null;status.Text="No se pudo resolver un medio web: "+ex.Message;}});}
   });
  }
  void OpenWebSource(string assetId){var asset=editor.Project.assets.Find(a=>a.id==assetId);if(asset==null||asset.source==null||asset.source.kind!="web")return;Process.Start(asset.source.page_url??asset.source.uri);}
  void CopyWebSource(string assetId){var asset=editor.Project.assets.Find(a=>a.id==assetId);if(asset==null||asset.source==null||asset.source.kind!="web")return;Clipboard.SetText(asset.source.uri);status.Text="URL de origen copiada";}
 }
}

