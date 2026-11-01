using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Prometheus {
 public sealed class PendingImportJob {
  public string Id,Path,TrackId,Batch,State;
  public bool SafePlacement;
  public int Order;
  public double Start;
 }
 public partial class MainForm {
  const string PendingImportNamespace="prometheus.import.pending";
  CancellationTokenSource importToken;
  int importGeneration;
  static string Value(Extension x,string key){object v;return x.payload.TryGetValue(key,out v)?Convert.ToString(v,CultureInfo.InvariantCulture):"";}
  static PendingImportJob ParseJob(Extension x){double start;int order;if(!Double.TryParse(Value(x,"start"),NumberStyles.Float,CultureInfo.InvariantCulture,out start))start=0;if(!Int32.TryParse(Value(x,"order"),out order))order=0;return new PendingImportJob{Id=Value(x,"id"),Path=Value(x,"path"),TrackId=Value(x,"track"),Batch=Value(x,"batch"),State=Value(x,"state"),Order=order,Start=start,SafePlacement=Value(x,"safe")=="true"};}
  static List<PendingImportJob> PendingJobs(Project p){return p.extensions.Where(x=>x!=null&&x.@namespace==PendingImportNamespace&&x.version=="1"&&x.payload!=null).Select(ParseJob).Where(x=>!String.IsNullOrEmpty(x.Id)&&!String.IsNullOrEmpty(x.Path)).OrderBy(x=>x.Batch).ThenBy(x=>x.Order).ToList();}
  static Extension MakeJob(PendingImportJob job){return new Extension{@namespace=PendingImportNamespace,version="1",payload=new Dictionary<string,object>{{"id",job.Id},{"path",job.Path},{"track",job.TrackId??""},{"start",job.Start},{"batch",job.Batch},{"order",job.Order},{"state",job.State},{"safe",job.SafePlacement?"true":"false"}}};}
  static void UpdateJob(Extension ext,PendingImportJob job){ext.payload["start"]=job.Start;ext.payload["state"]=job.State;}
  void RefreshPendingUi(){
   var jobs=PendingJobs(editor.Project);
   timeline.PendingImports.Clear();
   foreach(var job in jobs.Where(x=>!String.IsNullOrEmpty(x.TrackId)&&x.TrackId!=Timeline.NewVideoTrackDrop&&x.TrackId!=Timeline.NewAudioTrackDrop))timeline.PendingImports.Add(new PendingPlacement{Name=Path.GetFileName(job.Path),TrackId=job.TrackId,Start=job.Start,Duration=5,State=importToken!=null&&job.State!="missing"?"preparing":job.State});
   timeline.Invalidate();
   mediaView.SetPendingImports(jobs,importToken!=null);
  }
  void RetryPending(string id){if(importToken!=null||busy)return;var all=PendingJobs(editor.Project);var job=all.FirstOrDefault(x=>x.Id==id);if(job!=null)RunImportJobs(all.Where(x=>x.Batch==job.Batch&&x.Order>=job.Order).ToList());}
  void DiscardPending(string id){if(importToken!=null)CancelImport();editor.Project.extensions.RemoveAll(x=>x!=null&&x.@namespace==PendingImportNamespace&&Value(x,"id")==id);dirty=true;RefreshProject();RefreshPendingUi();status.Text="Importación pendiente descartada";}
  void CancelImport(){
   importGeneration++;
   if(importToken!=null){importToken.Cancel();importToken=null;}
   RefreshPendingUi();
  }
  void StartImport(string[] files,string trackId,double at,bool safePlacement=false){
   if(files==null||files.Length==0)return;
   if(busy||importToken!=null){status.Text="Espera a que termine la importación actual";return;}
   var track=trackId==null?null:editor.Project.Active.tracks.Find(t=>t.id==trackId);
   if(trackId!=null&&trackId!=Timeline.NewVideoTrackDrop&&trackId!=Timeline.NewAudioTrackDrop&&(track==null||track.locked)){status.Text="Pista no disponible para importar";return;}
   if((trackId==Timeline.NewVideoTrackDrop||trackId==Timeline.NewAudioTrackDrop)&&files.Length!=1){status.Text="Suelta un medio cada vez para crear una pista";return;}
   string batch=Store.Id();var jobs=new List<PendingImportJob>();
   for(int i=0;i<files.Length;i++)jobs.Add(new PendingImportJob{Id=Store.Id(),Path=Path.GetFullPath(files[i]),TrackId=trackId??"",Batch=batch,Order=i,Start=at+i*5,State="pending",SafePlacement=safePlacement});
   // The pending badge is transient UI state. It must not become an undo step.
   editor.Project.extensions.AddRange(jobs.Select(MakeJob));dirty=true;RefreshProject();
   RunImportJobs(jobs);
  }
  void ResumePendingImports(){if(importToken==null&&!busy){var jobs=PendingJobs(editor.Project);RefreshPendingUi();if(jobs.Count>0)RunImportJobs(jobs);}}
  void RunImportJobs(List<PendingImportJob> jobs){
   if(jobs.Count==0)return;
   var token=new CancellationTokenSource();importToken=token;int generation=++importGeneration;RefreshPendingUi();
   status.Text="Preparando "+jobs.Count+" medio(s)… puedes seguir editando · Esc cancela";
   Task.Factory.StartNew(()=>{
    int imported=0,staticGifs=0;var errors=new List<string>();
    foreach(var job in jobs){
     if(token.IsCancellationRequested)break;
     try{
      if(Directory.Exists(job.Path))throw new Exception("Arrastra archivos, no carpetas");
      if(!File.Exists(job.Path)){job.State="missing";throw new FileNotFoundException("Archivo no disponible");}
      var asset=Media.Inspect(job.Path,token.Token);
      if(job.TrackId==Timeline.NewVideoTrackDrop&&asset.streams.All(s=>s.kind!="video"&&s.kind!="image"))throw new Exception("El medio necesita una pista de vídeo");
      if(job.TrackId==Timeline.NewAudioTrackDrop&&asset.streams.All(s=>s.kind!="audio"))throw new Exception("El medio necesita una pista de audio");
      if(IsDisposed||closing)break;
      Invoke((Action)(()=>{
       if(token.IsCancellationRequested||importGeneration!=generation||closing)return;
       if(!String.IsNullOrEmpty(job.TrackId)&&job.TrackId!=Timeline.NewVideoTrackDrop&&job.TrackId!=Timeline.NewAudioTrackDrop){
        var track=editor.Project.Active.tracks.Find(t=>t.id==job.TrackId);
        if(track==null||track.locked)throw new Exception("Pista no disponible");
        bool audio=asset.streams.All(s=>s.kind=="audio");if(track.kind=="audio"&&!audio||track.kind=="video"&&audio)throw new Exception("Medio incompatible con la pista");
       }
       var pending=editor.Project.extensions.FirstOrDefault(x=>x!=null&&x.@namespace==PendingImportNamespace&&Value(x,"id")==job.Id);
       if(pending==null)return; // Undo, discard or project change removed this request.
       double next=job.Start;PlacementResult placed=null;
       editor.Project.extensions.Remove(pending);
       try{editor.Commit(p=>{
        if(String.IsNullOrEmpty(job.TrackId))p.assets.Add(asset);
        else{
         string destination=job.TrackId==Timeline.NewVideoTrackDrop?Store.AddTrack(p.Active,"video").id:job.TrackId==Timeline.NewAudioTrackDrop?Store.AddTrack(p.Active,"audio").id:job.TrackId;
         if(job.SafePlacement){p.assets.Add(asset);placed=Media.PlaceInFreeTrack(p,asset.id,destination,job.Start);}
         else Media.InsertTimelineBatch(p,new List<Asset>{asset},destination,job.Start);
         next=Math.Ceiling((p.Active.clips.Where(c=>c.asset_id==asset.id).Max(c=>c.End)-0.000001)*60)/60;
         int remaining=0;
         foreach(var later in jobs.Where(x=>x.Batch==job.Batch&&x.Order>job.Order)){
          later.Start=next+remaining++*5;
          var ext=p.extensions.FirstOrDefault(x=>x.@namespace==PendingImportNamespace&&Value(x,"id")==later.Id);if(ext!=null)UpdateJob(ext,later);
         }
        }
       });}catch{editor.Project.extensions.Add(pending);throw;}
       imported++;if(Path.GetExtension(job.Path).Equals(".gif",StringComparison.OrdinalIgnoreCase))staticGifs++;dirty=true;RefreshProject();mediaView.SelectLast();if(placed!=null)RevealPlacedClip(placed);RefreshPendingUi();if(MgtMedia.IsMgt(asset))QueueMgtPreview(asset.id);
       status.Text="Listos "+imported+" de "+jobs.Count+" medios · preparando los restantes…";
      }));
     }catch(OperationCanceledException){break;}
     catch(Exception ex){errors.Add(Path.GetFileName(job.Path)+": "+ex.Message);try{if(!IsDisposed&&!closing)Invoke((Action)(()=>{
      if(importGeneration!=generation)return;
      var p=editor.Project;
      {
       var ext=p.extensions.FirstOrDefault(x=>x.@namespace==PendingImportNamespace&&Value(x,"id")==job.Id);
       if(ext==null)return;
       if(job.State=="missing")UpdateJob(ext,job);else p.extensions.Remove(ext);
       int remaining=0;
       foreach(var later in jobs.Where(x=>x.Batch==job.Batch&&x.Order>job.Order)){
        later.Start=job.Start+(job.State=="missing"?5:0)+remaining++*5;
        var next=p.extensions.FirstOrDefault(x=>x.@namespace==PendingImportNamespace&&Value(x,"id")==later.Id);if(next!=null)UpdateJob(next,later);
       }
      }dirty=true;RefreshProject();RefreshPendingUi();
     }));}catch{}}
    }
    UI(()=>{
     if(importGeneration!=generation)return;
     importToken=null;RefreshPendingUi();
     status.Text=token.IsCancellationRequested?"Importación cancelada · "+imported+" medio(s) ya añadidos":"Importados "+imported+" de "+jobs.Count+" medios"+(staticGifs>0?" · "+staticGifs+" GIF importado(s) como imagen fija (primer fotograma)":"");
     if(errors.Count>0)MessageBox.Show("No compatibles o no añadidos:\n\n"+String.Join("\n\n",errors),"Importación de medios",MessageBoxButtons.OK,MessageBoxIcon.Information);
    });
   });
  }
 }
}
