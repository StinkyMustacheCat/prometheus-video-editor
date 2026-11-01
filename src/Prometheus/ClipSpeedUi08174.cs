using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  void ChangeClipSpeed(string clipId){
   var clip=editor.Project.Active.clips.Find(c=>c.id==clipId);if(clip==null)return;
   using(var dialog=new Form{Text="Velocidad y duración",ClientSize=new Size(440,190),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,BackColor=panel,ForeColor=text,Font=new Font("Segoe UI",9)}){
    var heading=new Label{Text=clip.name,Left=18,Top=15,Width=400,Height=25,AutoEllipsis=true};
    var caption=new Label{Text="Velocidad del medio",Left=18,Top=58,Width=200,Height=23};
    var speed=new NumericUpDown{Left=222,Top=55,Width=115,Minimum=25,Maximum=400,DecimalPlaces=2,Increment=5,Value=(decimal)Math.Max(25,Math.Min(400,clip.speed.Seconds*100)),BackColor=bg,ForeColor=text};
    var suffix=new Label{Text="%",Left=344,Top=58,Width=40,Height=23};
    var duration=new Label{Left=18,Top=92,Width=400,Height=25,ForeColor=Color.FromArgb(179,204,182)};
    Action update=()=>duration.Text="Duración aproximada: "+Timeline.TimeLabel(clip.duration.Seconds*clip.speed.Seconds/((double)speed.Value/100));
    speed.ValueChanged+=(s,e)=>update();update();
    var note=new Label{Text="Los demás medios conservarán su posición.",Left=18,Top=122,Width=410,Height=22,ForeColor=Color.FromArgb(171,175,182)};
    var cancel=new Button{Text="Cancelar",Left=220,Top=148,Width=95,Height=30,DialogResult=DialogResult.Cancel};
    var accept=new Button{Text="Aplicar",Left=325,Top=148,Width=95,Height=30,DialogResult=DialogResult.OK};
    dialog.Controls.AddRange(new Control[]{heading,caption,speed,suffix,duration,note,cancel,accept});dialog.AcceptButton=accept;dialog.CancelButton=cancel;
    if(dialog.ShowDialog(this)!=DialogResult.OK)return;
    try{bool limited=false;Edit(()=>editor.Commit(p=>limited=ClipSpeed08174.Change(p,clipId,speed.Value)));status.Text=limited?"Velocidad aplicada · duración limitada por el siguiente medio":"Velocidad aplicada: "+speed.Value+" % · Ctrl+Z para deshacer";}
    catch(Exception ex){Error(ex);}
   }
  }
 }
}
