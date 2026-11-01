using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  void BeginSmartCut091(string id){
   try{
    if(busy)throw new Exception("Espera a que termine la tarea actual");
    var clip=MaskClip(editor.Project,id);
    var asset=editor.Project.assets.First(a=>a.id==clip.asset_id);
    var stream=asset.streams.First(s=>s.id==clip.stream_id);
    if(stream.kind!="video")throw new Exception("Recorte inteligente necesita un clip de vídeo");
    if(Media.SelectRepresentation(asset,false)==null)throw new Exception("Descarga o reconecta el medio antes de recortar");
    Stop();monitor.TextToolActive=false;
    var prior=Sam2MaskStore090.Get(clip);
    var points=prior==null?new List<Sam2Prompt090>():prior.prompts.Select(p=>new Sam2Prompt090{frame=p.frame,x=p.x,y=p.y,positive=p.positive,object_id=p.object_id}).ToList();
    int frame=Math.Max(0,Math.Min((int)Math.Ceiling(clip.duration.Seconds*60)-1,(int)Math.Round((timeline.Cursor-clip.timeline_start.Seconds)*60)));
    int objectId=points.Count==0?1:Math.Max(1,points.Max(p=>p.object_id));
    var dialog=new Form{Text="Recorte inteligente · fotograma "+(frame+1),ClientSize=new Size(520,105),FormBorderStyle=FormBorderStyle.FixedToolWindow,StartPosition=FormStartPosition.CenterParent,BackColor=panel,ForeColor=text};
    var label=new Label{Left=12,Top=10,Width=495,Height=48,Text="Detectando objetos del fotograma…\nClic: seleccionar · Alt: añadir · Mayús: quitar · Ctrl: otra zona"};
    var process=new Button{Text="Seguir objeto",Left=290,Top=64,Width=126,Height=29};
    var cancel=new Button{Text="Cancelar",Left=422,Top=64,Width=88,Height=29};
    dialog.Controls.AddRange(new Control[]{label,process,cancel});
    var candidatesToken=new CancellationTokenSource();
    var previewToken=new CancellationTokenSource();
    Action refresh=()=>{label.Text="Fotograma "+(frame+1)+" · "+points.Count(p=>p.frame==frame)+" correcciones\nClic: seleccionar · Alt: añadir · Mayús: quitar · Ctrl: otra zona";};
    Action recalculate=()=>{
     previewToken.Cancel();previewToken=new CancellationTokenSource();var active=previewToken;
     var snapshot=Store.Copy(editor.Project);
     var selected=points.Where(p=>p.frame==frame).Select(p=>new Sam2Prompt090{frame=p.frame,x=p.x,y=p.y,positive=p.positive,object_id=p.object_id}).ToList();
     if(!selected.Any(p=>p.positive)){monitor.ShowObjectPreview(null);return;}
     label.Text="Ajustando selección de este fotograma…";
     Task.Factory.StartNew(()=>{try{var mask=Sam2Runner090.Preview(snapshot,id,frame,selected,active.Token);UI(()=>{try{if(dialog.IsDisposed||active!=previewToken)return;monitor.ShowObjectPreview(mask);refresh();}finally{mask.Dispose();}});}catch(OperationCanceledException){}catch(Exception ex){UI(()=>{if(dialog.IsDisposed||active!=previewToken)return;status.Text="No se pudo actualizar la vista: "+ex.Message;refresh();});}});
    };
    monitor.BeginObjectPickWithKeys((x,y,keys)=>{
     bool subtract=(keys&Keys.Shift)==Keys.Shift,add=(keys&Keys.Alt)==Keys.Alt,newZone=(keys&Keys.Control)==Keys.Control;
     if(newZone){objectId=Math.Min(64,Math.Max(objectId,points.Count==0?0:points.Max(p=>p.object_id))+1);}
     else if(!add&&!subtract)points.RemoveAll(p=>p.frame==frame&&p.object_id==objectId);
     points.Add(new Sam2Prompt090{frame=frame,x=x,y=y,positive=!subtract,object_id=objectId});
     candidatesToken.Cancel();monitor.SetObjectCandidates(null);monitor.ShowObjectPreview(null);refresh();recalculate();
    },()=>{if(!dialog.IsDisposed)dialog.Close();});
    var candidateSnapshot=Store.Copy(editor.Project);
    Task.Factory.StartNew(()=>{try{var candidates=Sam2Runner090.Candidates(candidateSnapshot,id,frame,candidatesToken.Token);UI(()=>{if(dialog.IsDisposed||candidatesToken.IsCancellationRequested){foreach(var candidate in candidates)candidate.Dispose();return;}monitor.SetObjectCandidates(candidates);refresh();});}catch(OperationCanceledException){}catch(Exception ex){UI(()=>{if(!dialog.IsDisposed){refresh();status.Text="Selección automática no disponible: "+ex.Message;}});}});
    process.Click+=(s,e)=>{try{if(!points.Any(p=>p.positive))throw new Exception("Selecciona al menos un objeto");var selected=points.Select(p=>new Sam2Prompt090{frame=p.frame,x=p.x,y=p.y,positive=p.positive,object_id=p.object_id}).ToList();dialog.Close();RunSmartCut091(id,selected,prior);}catch(Exception ex){Error(ex);}};
    cancel.Click+=(s,e)=>dialog.Close();
    dialog.FormClosed+=(s,e)=>{candidatesToken.Cancel();previewToken.Cancel();monitor.CancelObjectPick(false);RequestFrame();dialog.Dispose();};
    dialog.Show(this);monitor.Focus();status.Text="Recorte inteligente: selecciona el objeto en el monitor";
   }catch(Exception ex){Error(ex);}
  }
 }
}
