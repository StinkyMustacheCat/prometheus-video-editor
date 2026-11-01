using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Prometheus {
 public partial class MainForm {
  CancellationTokenSource previewRenderToken;
  void CancelTransitionPreviewRender(){if(previewRenderToken!=null){previewRenderToken.Cancel();previewRenderToken=null;}monitor.ProcessingStatus=null;monitor.Invalidate();}
  void RenderRedPreviews(){
   if(busy){status.Text="Espera a que termine la tarea actual";return;}
   if(previewRenderToken!=null){status.Text="Ya se están procesando transiciones · Esc cancela";return;}
   var marks=SequenceExportRange.Read(editor.Project.Active);
   double begin=marks.Complete?marks.InFrame.Value/60.0:0;
   double end=marks.Complete?marks.OutFrame.Value/60.0:editor.Project.Active.Length;
   int width=monitorView.PreviewWidth,height=monitorView.PreviewHeight;
   var snapshot=Store.Copy(editor.Project);
   var pending=snapshot.Active.transitions.Where(MgtTransitionCache.IsMgt)
    .Where(t=>MgtTransitionCache.End(t,60)>begin&&MgtTransitionCache.Start(t,60)<end)
    .Where(t=>!MgtTransitionCache.Ready(snapshot,t,"preview",60,width,height))
    .OrderBy(t=>t.cut_frame).ToList();
   if(pending.Count==0){status.Text="No hay tramos rojos pendientes en "+(marks.Complete?"entrada y salida":"la secuencia");return;}
   previewRenderToken=new CancellationTokenSource();var token=previewRenderToken;
   status.Text="Procesando "+pending.Count+" transición(es) MGT desde la primera pendiente… Esc cancela";
   monitor.ProcessingStatus="Procesando transición MGT 1/"+pending.Count+" · 0 %";monitor.Invalidate();
   Task.Factory.StartNew(()=>{
    try{
     for(int i=0;i<pending.Count;i++){
      token.Token.ThrowIfCancellationRequested();int ordinal=i;var transition=pending[i];
      MgtTransitionCache.Ensure(snapshot,transition,"preview",60,width,height,token.Token,
       percent=>UI(()=>{if(!token.IsCancellationRequested){status.Text="Procesando transición "+(ordinal+1)+"/"+pending.Count+" · "+percent+" %";monitor.ProcessingStatus=status.Text;monitor.Invalidate();}}));
      UI(()=>timeline.Invalidate());
     }
     UI(()=>{if(previewRenderToken!=token)return;previewRenderToken=null;monitor.ProcessingStatus=null;monitor.Invalidate();timeline.Invalidate();status.Text="Vistas previas listas: "+pending.Count;if(!playing)RequestFrame();});
    }catch(OperationCanceledException){UI(()=>{if(previewRenderToken!=token)return;previewRenderToken=null;monitor.ProcessingStatus=null;monitor.Invalidate();timeline.Invalidate();status.Text="Procesamiento cancelado";});}
    catch(Exception ex){UI(()=>{if(previewRenderToken!=token)return;previewRenderToken=null;monitor.ProcessingStatus=null;monitor.Invalidate();timeline.Invalidate();Error(ex);});}
   });
  }
 }
}
