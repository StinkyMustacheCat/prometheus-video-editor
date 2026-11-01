using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Prometheus {
 public partial class MainForm {
  // Sample the monitor, not the desktop. For chroma, show the frame before the
  // selected effect so the key color remains available after keying it out.
  void BeginEffectColorSample(bool onTrack,string ownerId,string effectId,Action<Color> apply){
   try{
    Stop();
    if(editor.Project.Active.Length<=0||timeline.Cursor>=editor.Project.Active.Length)throw new Exception("Sitúa el lector sobre un fotograma para tomar su color");
    var snapshot=Store.Copy(editor.Project);
    var targetTrack=snapshot.Active.tracks.Find(t=>t.id==ownerId);
    var targetClip=snapshot.Active.clips.Find(c=>c.id==ownerId);
    var effects=onTrack?(targetTrack==null?null:targetTrack.effects):(targetClip==null?null:targetClip.effects);
    int index=effects==null?-1:effects.FindIndex(x=>x.id==effectId);
    if(index<0)throw new Exception("Selecciona de nuevo el efecto antes de usar el gotero");
    for(int i=index;i<effects.Count;i++)effects[i].enabled=false;
    if(onTrack)foreach(var clip in snapshot.Active.clips.Where(c=>c.track_id==ownerId))foreach(var later in clip.effects)later.enabled=false;
    var token=new CancellationTokenSource();renderToken=token;
    double at=timeline.Cursor;int width=monitorView.PreviewWidth,height=monitorView.PreviewHeight;
    status.Text="Preparando fotograma para el gotero…";
    Task.Factory.StartNew(()=>{
     try{
      var frame=Rendering.Frame(snapshot,at,token.Token,width,height);
      UI(()=>{
       if(renderToken!=token||token.IsCancellationRequested){frame.Dispose();return;}
       SetImage(frame);
       monitor.BeginColorSample(color=>{
        if(renderToken!=token||token.IsCancellationRequested)return;
        apply(color);
        if(renderToken==token)RequestFrame();
       },()=>{if(renderToken==token)RequestFrame();});
       status.Text="Gotero: haz clic en el monitor para tomar color · Esc para cancelar";
      });
     }catch(OperationCanceledException){}catch(Exception ex){UI(()=>{if(renderToken==token){RequestFrame();status.Text="Gotero: "+ex.Message.Replace('\n',' ');}});}
    });
   }catch(Exception ex){Error(ex);}
  }
 }
}
