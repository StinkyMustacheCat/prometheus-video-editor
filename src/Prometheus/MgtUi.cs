using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;

namespace Prometheus {
 public partial class MainForm {
  string SavePastedMgt(string document){
   if(!MgtMedia.LooksLikeDocument(document))throw new Exception("Pega un documento MGT JSON completo.");
   string directory=projectFile==null?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","MGT-imports"):ProjectStorage.MgtDirectory(projectFile);
   Directory.CreateDirectory(directory);
   string file=Path.Combine(directory,"pegado-"+Guid.NewGuid().ToString("N")+".mgt.json");
   File.WriteAllText(file,document,System.Text.Encoding.UTF8);
   return file;
  }
  void PasteMgtDialog(string placeholderClipId){
   using(var dialog=new Form{Text="Pegar MGT (JSON)",Width=680,Height=465,MinimumSize=new Size(460,320),StartPosition=FormStartPosition.CenterParent,BackColor=panel,ForeColor=text}){
    var intro=new Label{Text="Pega el documento MGT JSON completo; se comprobará antes de conectarlo.",Dock=DockStyle.Top,Height=36,Padding=new Padding(12,10,0,0)};
    var input=new TextBox{Multiline=true,ScrollBars=ScrollBars.Both,AcceptsReturn=true,AcceptsTab=true,Dock=DockStyle.Fill,BackColor=Color.FromArgb(30,32,36),ForeColor=text,WordWrap=false};
    var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=48,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(8)};
    var cancel=new Button{Text="Cancelar",DialogResult=DialogResult.Cancel,Width=88};
    var connect=new Button{Text="Conectar",DialogResult=DialogResult.OK,Width=88};
    buttons.Controls.Add(cancel);buttons.Controls.Add(connect);dialog.Controls.Add(input);dialog.Controls.Add(buttons);dialog.Controls.Add(intro);dialog.AcceptButton=connect;dialog.CancelButton=cancel;
    try{if(Clipboard.ContainsText()&&MgtMedia.LooksLikeDocument(Clipboard.GetText()))input.Text=Clipboard.GetText();}catch(System.Runtime.InteropServices.ExternalException){}
    if(dialog.ShowDialog(this)!=DialogResult.OK)return;
    string file=SavePastedMgt(input.Text);
    StartPlaceholderFileLink(placeholderClipId,file,0,false);
   }
  }
  readonly MgtControlsPanelView mgtControlsView=new MgtControlsPanelView();
  readonly HashSet<string> pendingMgtPreviews=new HashSet<string>();
  CancellationTokenSource mgtPreviewToken=new CancellationTokenSource();
  void SetupMgt(){mgtControlsView.ParameterChanged=SetMgtParameter;}
  void ShowMgtClip(){mgtControlsView.ShowClip(editor.Project,editor.Project.Active.clips.Find(c=>c.id==timeline.Selected));}
  void CancelMgtPreview(){mgtPreviewToken.Cancel();mgtPreviewToken=new CancellationTokenSource();pendingMgtPreviews.Clear();}
  void QueueMgtPreview(string assetId){
   var asset=editor.Project.assets.Find(a=>a.id==assetId);if(!MgtMedia.IsMgt(asset)||pendingMgtPreviews.Contains(assetId))return;
   if(Media.SelectRepresentation(asset,true)!=null&&asset.representations.Any(r=>r.role=="preview"))return;
   pendingMgtPreviews.Add(assetId);status.Text="Preparando vista ligera MGT: "+asset.name;var token=mgtPreviewToken.Token;string projectId=editor.Project.id;
   var copy=Store.Json().Deserialize<Asset>(Store.Json().Serialize(Store.Fields(asset)));
   string expected=Store.Json().Serialize(MgtMedia.Overrides(asset));
   Task.Factory.StartNew(()=>{
    try{MgtMedia.Resolve(copy,true,token,null);UI(()=>{
     pendingMgtPreviews.Remove(assetId);if(editor.Project.id!=projectId)return;
     var current=editor.Project.assets.Find(a=>a.id==assetId);if(current==null||Store.Json().Serialize(MgtMedia.Overrides(current))!=expected)return;
     AttachMgtRepresentation(current,copy,"preview");dirty=true;RefreshProject();RequestFrame();status.Text="Vista ligera MGT lista: "+current.name;
    });}catch(OperationCanceledException){UI(()=>pendingMgtPreviews.Remove(assetId));}
    catch(Exception ex){UI(()=>{pendingMgtPreviews.Remove(assetId);status.Text="MGT: "+ex.Message;});}
   });
  }
  void QueueMissingMgtPreviews(){foreach(var asset in editor.Project.assets.Where(MgtMedia.IsMgt).ToList())QueueMgtPreview(asset.id);}
  void AttachMgtRepresentation(Asset target,Asset rendered,string role){
   var rep=rendered.representations.FirstOrDefault(r=>r.role==role);if(rep==null)return;
   MgtMedia.CopyDuration(target,rendered);MgtMedia.ReconcileClipDurations(editor.Project,target);
   target.representations.RemoveAll(r=>r.role==role);target.representations.Add(rep);
   var current=MgtMedia.Metadata(target);var source=MgtMedia.Metadata(rendered);object fingerprint;
   if(current!=null&&source!=null&&source.payload.TryGetValue(role+"_fingerprint",out fingerprint))current.payload[role+"_fingerprint"]=fingerprint;
   Store.NormalizeAsset(target);
  }
  void SetMgtParameter(string clipId,string parameterId,object value){
   try{
    if(busy)return;Stop();string changedAsset=null;
    editor.Commit(project=>{
     var clip=project.Active.clips.Find(c=>c.id==clipId);if(clip==null)throw new Exception("El clip MGT ya no existe.");
     var original=project.assets.Find(a=>a.id==clip.asset_id);if(!MgtMedia.IsMgt(original))throw new Exception("El clip ya no es MGT.");
     var related=new HashSet<string>{clip.id};foreach(var group in project.Active.link_groups.Where(g=>g.clip_ids.Contains(clip.id)))foreach(var id in group.clip_ids)related.Add(id);
     var followers=project.sequences.SelectMany(s=>s.clips).Where(c=>c.asset_id==original.id).ToList();
     Asset target=original;
     if(!MgtMedia.IsInstance(original)||followers.Any(c=>!related.Contains(c.id))){
      target=Store.Json().Deserialize<Asset>(Store.Json().Serialize(Store.Fields(original)));target.id=Store.Id();
      var streams=new Dictionary<string,string>();foreach(var stream in target.streams){string old=stream.id;stream.id=Store.Id();streams[old]=stream.id;}
      foreach(var linked in project.Active.clips.Where(c=>related.Contains(c.id)&&c.asset_id==original.id)){linked.asset_id=target.id;linked.stream_id=streams[linked.stream_id];}
      target.representations.Clear();var metadata=MgtMedia.Metadata(target);metadata.payload["instance"]=true;project.assets.Add(target);
     }
     MgtMedia.SetOverride(target,parameterId,value);MgtMedia.InvalidateSam2(project,target.id);changedAsset=target.id;
    });
    dirty=true;RefreshProject();if(changedAsset!=null)QueueMgtPreview(changedAsset);status.Text="Parámetro MGT actualizado; preparando vista ligera";
   }catch(Exception ex){Error(ex);ShowMgtClip();}
  }
  void ResolveUsedMgt(Project snapshot,bool preview,CancellationToken cancel,Action<string> progress,bool audioOnly=false){
   var rendered=NestedMedia0910.Flatten(snapshot);var used=snapshot.assets.Where(a=>MgtMedia.IsMgt(a)&&rendered.Active.clips.Any(c=>c.asset_id==a.id&&c.enabled&&rendered.Active.tracks.Any(t=>t.id==c.track_id&&t.enabled&&(!audioOnly||t.kind=="audio")))).ToList();
   for(int i=0;i<used.Count;i++){cancel.ThrowIfCancellationRequested();var asset=used[i];if(progress!=null)progress("Resolviendo MGT "+(i+1)+"/"+used.Count+": "+asset.name);MgtMedia.Resolve(asset,preview,cancel,null);MgtMedia.ReconcileClipDurations(snapshot,asset);}
  }
  void AttachResolvedMgt(Project snapshot){
   if(editor.Project.id!=snapshot.id)return;
   bool any=false;
   foreach(var rendered in snapshot.assets.Where(MgtMedia.IsMgt)){
    var current=editor.Project.assets.Find(a=>a.id==rendered.id);if(current==null||Store.Json().Serialize(MgtMedia.Overrides(current))!=Store.Json().Serialize(MgtMedia.Overrides(rendered)))continue;
    foreach(var rep in rendered.representations.Where(r=>r.role=="preview"||r.role=="final")){
     if(current.representations.Any(r=>r.role==rep.role&&r.locator.path==rep.locator.path))continue;
     AttachMgtRepresentation(current,rendered,rep.role);any=true;
    }
   }
   if(any){dirty=true;RefreshProject();}
  }
 }
}

