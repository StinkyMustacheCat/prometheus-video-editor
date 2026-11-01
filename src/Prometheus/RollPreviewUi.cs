using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  readonly System.Windows.Forms.Timer rollPreviewTimer=new System.Windows.Forms.Timer{Interval=260};
  CancellationTokenSource rollPreviewToken;
  string rollPreviewLeft,rollPreviewRight;
  double rollPreviewCut;
  bool rollPreviewLinked,rollPreviewShowing;
  int rollPreviewRevision;

  void SetupRollPreview(){rollPreviewTimer.Tick+=(s,e)=>RenderPendingRollPreview();}

  void QueueRollPreview(string left,string right,double cut,bool linked){
   rollPreviewTimer.Stop();
   if(rollPreviewToken!=null)rollPreviewToken.Cancel();
   if(left==null||right==null){
    rollPreviewLeft=rollPreviewRight=null;
    if(rollPreviewShowing&&editor.Project.revision==rollPreviewRevision)RequestFrame();
    rollPreviewShowing=false;
    return;
   }
   if(rollPreviewLeft==null)rollPreviewRevision=editor.Project.revision;
   rollPreviewLeft=left;rollPreviewRight=right;rollPreviewCut=cut;rollPreviewLinked=linked;
   var leftClip=editor.Project.Active.clips.Find(c=>c.id==left);
   if(leftClip==null||Math.Abs(cut-leftClip.End)<0.000001)return;
   rollPreviewTimer.Start();
  }

  void RenderPendingRollPreview(){
   rollPreviewTimer.Stop();
   if(rollPreviewLeft==null||rollPreviewRight==null||closing)return;
   string leftId=rollPreviewLeft,rightId=rollPreviewRight;
   double cut=rollPreviewCut;bool linked=rollPreviewLinked;
   var token=new CancellationTokenSource();rollPreviewToken=token;
   var project=Store.Copy(editor.Project);
   Stop();
   Task.Factory.StartNew(()=>{
    try{
     var preview=new Editor();preview.Reset(project);preview.RollEditLinked(leftId,rightId,cut,linked);
     double seam=preview.Project.Active.clips.Find(c=>c.id==leftId).End;
     using(var outgoing=Rendering.Frame(preview.Project,Math.Max(0,seam-1.0/60),token.Token))
     using(var incoming=Rendering.Frame(preview.Project,seam,token.Token)){
      token.Token.ThrowIfCancellationRequested();
      var combined=new Bitmap(640,360);
      using(var graphics=Graphics.FromImage(combined)){
       graphics.Clear(Color.FromArgb(24,25,28));
       graphics.DrawImage(outgoing,new Rectangle(0,76,319,180));
       graphics.DrawImage(incoming,new Rectangle(321,76,319,180));
       using(var ink=new SolidBrush(Color.Gainsboro))using(var font=new Font("Segoe UI",11,FontStyle.Bold)){
        graphics.DrawString("SALIENTE",font,ink,8,45);
        graphics.DrawString("ENTRANTE",font,ink,329,45);
       }
       using(var ink=new SolidBrush(Color.FromArgb(255,183,95)))using(var font=new Font("Segoe UI",10))graphics.DrawString("Corte  "+Timeline.TimeLabel(seam),font,ink,8,277);
      }
      UI(()=>{if(rollPreviewToken==token&&!token.IsCancellationRequested&&rollPreviewLeft==leftId&&rollPreviewRight==rightId){SetImage(combined);rollPreviewShowing=true;}else combined.Dispose();});
     }
    }catch(OperationCanceledException){}
    catch(Exception ex){UI(()=>{if(rollPreviewToken==token&&!token.IsCancellationRequested)status.Text="Vista del corte: "+ex.Message.Replace('\n',' ');});}
   });
  }

  void DisposeRollPreview(){rollPreviewTimer.Stop();rollPreviewTimer.Dispose();if(rollPreviewToken!=null)rollPreviewToken.Cancel();}
 }
}
