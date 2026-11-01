using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  ShelfWindow shelfWindow;
  CancellationTokenSource shelfInspectToken;
  readonly Queue<string> shelfInspectQueue=new Queue<string>();
  void ShowShelf(){
   if(shelfWindow==null||shelfWindow.IsDisposed){
    shelfWindow=new ShelfWindow();shelfWindow.Location=new Point(Math.Max(0,Left+Width-shelfWindow.Width-30),Math.Max(0,Top+75));
    shelfWindow.AddRequested=AddShelfText;shelfWindow.AddFilesRequested=AddShelfFiles;shelfWindow.ImportRequested=PromoteShelfItems;shelfWindow.RemoveRequested=RemoveShelfItems;shelfWindow.MoveRequested=MoveShelfItem;shelfWindow.RangeRequested=ChooseShelfRange;
   }
   shelfWindow.SetItems(ShelfStore.Read(editor.Project));
   if(!shelfWindow.Visible)shelfWindow.Show(this);else shelfWindow.Activate();
  }
  void CaptureShelfText(string value){
   if(String.IsNullOrWhiteSpace(value))return;
   QueueShelfText(value,false);
  }
  void RefreshShelfUi(){
   var items=ShelfStore.Read(editor.Project);mediaView.SetShelfCount(items.Count);
   if(shelfWindow!=null&&!shelfWindow.IsDisposed)shelfWindow.SetItems(items);
  }
  void CancelShelfWork(){if(shelfInspectToken!=null){shelfInspectToken.Cancel();shelfInspectToken=null;}shelfInspectQueue.Clear();if(shelfWindow!=null&&!shelfWindow.IsDisposed)shelfWindow.ClearInput();}
  void AddShelfText(string text){QueueShelfText(text,true);}
  void QueueShelfText(string text,bool manual){
   var urls=new List<string>();
   string remainder=Regex.Replace(text,@"https?://[^\s<>""']+",match=>{string url=match.Value.TrimEnd('.',',',';',')');if(WebMedia.Platform(url)==null&&!WebMedia.IsImageUrl(url))return match.Value;if(!urls.Contains(url,StringComparer.OrdinalIgnoreCase))urls.Add(url);return match.Value.Substring(url.Length);});
   if(urls.Count==0){status.Text="Bandeja: no se encontraron enlaces compatibles; el texto sigue disponible";return;}
   if(manual&&shelfWindow!=null&&!shelfWindow.IsDisposed)shelfWindow.SetInput(remainder.Trim());
   foreach(string url in urls)if(!shelfInspectQueue.Contains(url))shelfInspectQueue.Enqueue(url);
   status.Text="Bandeja: "+shelfInspectQueue.Count+" enlace(s) en consulta…";
   StartNextShelfInspect();
  }
  void StartNextShelfInspect(){
   if(shelfInspectToken!=null||shelfInspectQueue.Count==0)return;
   string url=shelfInspectQueue.Dequeue();
   var token=new CancellationTokenSource();shelfInspectToken=token;string projectId=editor.Project.id;
   Task.Factory.StartNew(()=>{
    try{
      Asset asset=InspectWebLink(url,url,token.Token);
      UI(()=>{if(shelfInspectToken!=token||editor.Project.id!=projectId)return;try{editor.Commit(p=>ShelfStore.Add(p,asset));dirty=true;RefreshProject();status.Text="Bandeja: "+asset.name+" añadido";if(asset.streams.Any(s=>s.kind=="video"))Task.Factory.StartNew(()=>{WebMedia.DownloadThumbnail(asset,webCacheDir,CancellationToken.None);UI(()=>{if(editor.Project.id==projectId)RefreshShelfUi();});});}catch(Exception ex){Error(ex);}finally{shelfInspectToken=null;StartNextShelfInspect();}});
    }catch(OperationCanceledException){UI(()=>{if(shelfInspectToken==token){shelfInspectToken=null;StartNextShelfInspect();}});}
    catch(Exception ex){UI(()=>{if(shelfInspectToken==token){status.Text="Bandeja: "+ex.Message;shelfInspectToken=null;StartNextShelfInspect();}});}
   });
  }
  void AddShelfFiles(string[] files){
   if(shelfInspectToken!=null){status.Text="Bandeja: espera a que termine la consulta actual";return;}
   var token=new CancellationTokenSource();shelfInspectToken=token;string projectId=editor.Project.id;
   Task.Factory.StartNew(()=>{
    foreach(string file in files){
     if(token.IsCancellationRequested)break;
     try{var asset=Media.Inspect(file,token.Token);UI(()=>{if(shelfInspectToken!=token||editor.Project.id!=projectId)return;try{editor.Commit(p=>ShelfStore.Add(p,asset));dirty=true;RefreshProject();status.Text="Bandeja: "+asset.name+" añadido";}catch(Exception ex){Error(ex);}});}
     catch(OperationCanceledException){break;}
     catch(Exception ex){UI(()=>{if(shelfInspectToken==token)status.Text="Bandeja: "+ex.Message;});}
    }
    UI(()=>{if(shelfInspectToken==token)shelfInspectToken=null;});
   });
  }
  void RemoveShelfItems(List<string> ids){if(ids.Count==0)return;editor.Commit(p=>ShelfStore.Remove(p,ids));dirty=true;RefreshProject();}
  void MoveShelfItem(string id,int targetIndex){
   editor.Commit(p=>{var items=ShelfStore.Read(p);int old=items.FindIndex(x=>x.id==id);if(old<0||targetIndex<0||targetIndex>=items.Count)return;var item=items[old];items.RemoveAt(old);items.Insert(targetIndex,item);ShelfStore.Write(p,items);});
   dirty=true;RefreshProject();
  }
  void ChooseShelfRange(string id){
   var item=ShelfStore.Read(editor.Project).FirstOrDefault(x=>x.id==id);if(item==null)return;
   double? begin=item.range_in,end=item.range_out;
   if(!RangeUi.Choose(shelfWindow,item.asset,ref begin,ref end))return;
   editor.Commit(p=>ShelfStore.SetRange(p,id,begin,end));dirty=true;RefreshProject();
  }
  bool CanDropShelfItem(string id,string trackId,double at){
   if(busy)return false;
   var item=ShelfStore.Read(editor.Project).FirstOrDefault(x=>x.id==id);
   if(trackId==Timeline.NewVideoTrackDrop||trackId==Timeline.NewAudioTrackDrop){string kind=trackId==Timeline.NewVideoTrackDrop?"video":"audio";return item!=null&&item.asset.streams.Any(x=>kind=="audio"?x.kind=="audio":x.kind=="video"||x.kind=="image");}
   var track=editor.Project.Active.tracks.Find(x=>x.id==trackId);
   return item!=null&&track!=null&&!track.locked&&item.asset.streams.Any(x=>track.kind=="audio"?x.kind=="audio":x.kind=="video"||x.kind=="image");
  }
  void PromoteShelfDrop(string id,string trackId,double at){PromoteShelfItems(new List<string>{id},true,trackId,at);}
  void PromoteShelfItems(List<string> ids,bool toTimeline){PromoteShelfItems(ids,toTimeline,null,null);}
  void PromoteShelfItems(List<string> ids,bool toTimeline,string preferredTrackId,double? preferredTime){
   if(ids.Count==0)return;
   var current=ShelfStore.Read(editor.Project);var selected=current.Where(x=>ids.Contains(x.id)).ToList();
   if(selected.Count==0)return;
   var promoted=new List<string>();PlacementResult last=null;double start=preferredTime??timeline.Cursor;
   try{
    editor.Commit(p=>{
     foreach(var item in selected){
      var asset=ShelfStore.Promote(p,item);promoted.Add(asset.id);
      if(toTimeline){
       string kind=WebMedia.IsAudioOnly(asset)?"audio":"video";
       var track=preferredTrackId==Timeline.NewVideoTrackDrop?Store.AddTrack(p.Active,"video"):preferredTrackId==Timeline.NewAudioTrackDrop?Store.AddTrack(p.Active,"audio"):preferredTrackId==null?null:p.Active.tracks.Find(t=>t.id==preferredTrackId);
       if(track==null)track=p.Active.tracks.Where(t=>t.kind==kind&&!t.locked).OrderBy(t=>t.order).FirstOrDefault();
       if(track==null)track=Store.AddTrack(p.Active,kind);
       double? duration=item.range_in.HasValue?item.range_out.Value-item.range_in.Value:(double?)null;
       last=Media.PlaceInFreeTrack(p,asset.id,track.id,start,item.range_in,duration);
       double length=duration??asset.streams.Where(x=>x.duration!=null).Select(x=>x.duration.Seconds).DefaultIfEmpty(asset.recommended_duration==null?5:asset.recommended_duration.Seconds).Max();
       start=Rational.Frame(start+Math.Max(1.0/60,length)).Seconds;
      }
     }
     ShelfStore.Remove(p,selected.Select(x=>x.id));
    });
    dirty=true;RefreshProject();if(last!=null)RevealPlacedClip(last);RequestFrame();
    foreach(string id in promoted){var asset=editor.Project.assets.Find(x=>x.id==id);if(asset!=null&&asset.source!=null&&asset.source.kind=="web"&&(asset.source.resolver_type=="youtube/yt-dlp"||asset.source.resolver_type!=null&&asset.source.resolver_type.StartsWith("yt-dlp/",StringComparison.Ordinal))&&(prepareWebPreviewAutomatically||WebMedia.IsAudioOnly(asset)))BeginWebPreview(id);}
    status.Text=selected.Count+" medio(s) enviados a "+(toTimeline?"la línea de tiempo":"Medios")+" · Bandeja actualizada";
   }catch(Exception ex){Error(ex);}
  }
 }
}
