using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public sealed class ColorPanelView:FlowLayoutPanel {
  public Action<string,string,string,double> ValueChanged,ValuePreview;
  Project project;Clip selected;double playhead;
  public ColorPanelView(){Dock=DockStyle.Fill;FlowDirection=FlowDirection.TopDown;WrapContents=false;AutoScroll=true;Padding=new Padding(10);BackColor=Color.FromArgb(36,38,42);Resize+=(s,e)=>ResizeRows();SetContext(null,null,0);}
  void ResizeRows(){foreach(Control child in Controls)child.Width=Math.Max(210,ClientSize.Width-34);}
  void Heading(string text){Controls.Add(new Label{Text=text,Height=28,ForeColor=Color.FromArgb(255,156,44),Font=new Font("Segoe UI",9,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft});}
  void Row(string effectId,string parameter,string label,double fallback,double min,double max,string suffix=""){
   var effect=selected.effects.FirstOrDefault(e=>e.effect_id==effectId);
   double baseValue=effect==null?fallback:effect.parameters[parameter].number.Value;
   double value=effect==null?baseValue:ClipKeyframes.Value(selected,"effect:"+effect.id+":"+parameter,playhead,baseValue);
   var row=new ScrubNumber(label,value,min,max,Math.Max(min,value-(max-min)/4),Math.Min(max,value+(max-min)/4),Math.Max(0.01,(max-min)/400),2,suffix);
   row.PreviewChanged=next=>{if(ValuePreview!=null)ValuePreview(selected.id,effectId,parameter,next);};
   row.Committed=next=>{if(ValueChanged!=null)ValueChanged(selected.id,effectId,parameter,next);};
   Controls.Add(row);
  }
  public void SetContext(Project next,Clip clip,double time){
   project=next;selected=clip;playhead=time;SuspendLayout();foreach(Control control in Controls.Cast<Control>().ToArray()){Controls.Remove(control);control.Dispose();}
   Heading("COLOR");
   if(project==null||selected==null||!project.Active.tracks.Any(t=>t.id==selected.track_id&&t.kind=="video")){
    Controls.Add(new Label{Text="Selecciona un clip visual para corregir su color.",Height=54,ForeColor=Color.Gainsboro});ResizeRows();ResumeLayout(true);return;
   }
   Controls.Add(new Label{Text=selected.name,Height=26,ForeColor=Color.Gainsboro,AutoEllipsis=true});
   Heading("LUZ");Row(EffectContracts.ExposureId,"stops","Exposición",0,-3,3," EV");Row(EffectContracts.ExposureId,"black","Nivel de negro",0,-0.5,0.5);
   Row(EffectContracts.BasicColorId,"brightness","Brillo",0,-1,1);Row(EffectContracts.BasicColorId,"contrast","Contraste",1,0,2);
   Heading("COLOR");Row(EffectContracts.BasicColorId,"saturation","Saturación",1,0,3);Row(EffectContracts.VibranceId,"intensity","Intensidad",0,-2,2);Row(EffectContracts.HueId,"degrees","Tono",0,-180,180," °");
   ResizeRows();ResumeLayout(true);
  }
 }
}
