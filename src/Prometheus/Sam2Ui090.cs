using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  CancellationTokenSource sam2Token090;
  void BeginSam2Pick090(string id,bool correction){
   try{if(busy)throw new Exception("Espera a que termine la tarea actual");var clip=MaskClip(editor.Project,id);var asset=editor.Project.assets.First(a=>a.id==clip.asset_id);var stream=asset.streams.First(s=>s.id==clip.stream_id);if(stream.kind!="video")throw new Exception("Selecciona un clip de vídeo");if(!Sam2SourceSettings01034.Confirm(this,asset))return;
    Stop();monitor.TextToolActive=false;monitor.BeginObjectPick((x,y,positive)=>{if(!correction&&!positive){status.Text="Haz clic izquierdo sobre el objeto";return;}RunSam2Tracking090(id,correction,x,y,positive);},()=>status.Text="Selección SAM2 cancelada");status.Text=correction?"SAM2: clic izquierdo añade objeto; clic derecho excluye zona · Esc cancela":"SAM2: haz clic sobre el objeto en el monitor · Esc cancela";
   }catch(Exception ex){Error(ex);}
  }
  void RunSam2Tracking090(string id,bool correction,double x,double y,bool positive){
   try{var source=MaskClip(editor.Project,id);var prior=correction?Sam2MaskStore090.Get(source):null;var prompts=prior==null?new List<Sam2Prompt090>():prior.prompts.ToList();int frame=Math.Max(0,Math.Min((int)Math.Ceiling(source.duration.Seconds*60)-1,(int)Math.Round((timeline.Cursor-source.timeline_start.Seconds)*60)));prompts.Add(new Sam2Prompt090{frame=frame,x=x,y=y,positive=positive});
    var snapshot=Store.Copy(editor.Project);int revision=snapshot.revision;string savedPath=projectFile;var token=new CancellationTokenSource();sam2Token090=token;busy=true;
    var window=new Form{Text="Seguimiento SAM2",ClientSize=new Size(410,125),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,BackColor=panel,ForeColor=text};var label=new Label{Text="Preparando clip…",Left=14,Top=13,Width=380,Height=26};var bar=new ProgressBar{Left=14,Top=47,Width=380,Height=23,Minimum=0,Maximum=100};var cancel=new Button{Text="Cancelar",Left=298,Top=83,Width=95,Height=29};cancel.Click+=(s,e)=>{token.Cancel();cancel.Enabled=false;label.Text="Cancelando…";};window.FormClosing+=(s,e)=>{if(busy&&e.CloseReason==CloseReason.UserClosing){token.Cancel();e.Cancel=true;label.Text="Cancelando…";}};window.Controls.AddRange(new Control[]{label,bar,cancel});window.Show(this);
    Task.Factory.StartNew(()=>{try{var result=Sam2Runner090.Track(snapshot,id,savedPath,prompts,token.Token,(percent,message)=>UI(()=>{if(window.IsDisposed)return;bar.Value=Math.Max(0,Math.Min(100,percent));label.Text=message;}));if(prior!=null)result.target=prior.target;UI(()=>{busy=false;sam2Token090=null;window.Close();window.Dispose();try{if(editor.Project.id!=snapshot.id||editor.Project.revision!=revision)throw new Exception("El proyecto cambió durante SAM2. Vuelve a calcular la máscara.");Edit(()=>editor.Commit(p=>Sam2MaskStore090.Put(MaskClip(p,id),result)));status.Text="Máscara SAM2 lista · "+result.frames+" fotogramas";}catch(Exception ex){Error(ex);}});}catch(OperationCanceledException){UI(()=>{busy=false;sam2Token090=null;if(!window.IsDisposed){window.Close();window.Dispose();}status.Text="Seguimiento SAM2 cancelado";});}catch(Exception ex){UI(()=>{busy=false;sam2Token090=null;if(!window.IsDisposed){window.Close();window.Dispose();}Error(ex);});}});
   }catch(Exception ex){busy=false;Error(ex);}
  }
 }
}
