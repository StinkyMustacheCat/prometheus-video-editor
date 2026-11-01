using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Prometheus {
 // A single numeric gesture emits many previews but just one committed edit.
 public sealed class ScrubNumber:Panel {
  readonly Label caption=new Label(),valueLabel=new Label();
  readonly TextBox edit=new TextBox();readonly Button arrow=new Button(),reset=new Button(),keyframe=new Button(),previous=new Button(),next=new Button();readonly TrackBar slider=new TrackBar();
  readonly ContextMenuStrip keyframeMenu=new ContextMenuStrip();
  readonly double minimum,maximum,sliderMinimum,sliderMaximum,step;
  readonly int decimals;readonly string suffix;
  double value,startValue;int downX;bool pressed,moved,updating;
  public Action<double> PreviewChanged,Committed;
  public Action ResetRequested,KeyframeRequested;public Action<int> KeyframeNavigateRequested;
  public Action Selected;
  public bool AcceleratedScrub;
  public double Value {get{return value;}}
  public ScrubNumber(string title,double initial,double min,double max,double sliderMin,double sliderMax,double pixelsStep,int digits,string unit){
   minimum=min;maximum=max;sliderMinimum=sliderMin;sliderMaximum=sliderMax;step=pixelsStep;decimals=digits;suffix=unit;
   Height=30;Width=214;BackColor=Color.FromArgb(36,38,42);
   caption.Text=title;caption.Left=0;caption.Top=2;caption.Width=110;caption.Height=24;caption.ForeColor=Color.FromArgb(225,227,230);caption.TextAlign=ContentAlignment.MiddleLeft;caption.AutoEllipsis=true;
   valueLabel.Left=112;valueLabel.Top=3;valueLabel.Width=94;valueLabel.Height=23;valueLabel.ForeColor=Color.FromArgb(145,199,255);valueLabel.BackColor=Color.FromArgb(28,30,34);valueLabel.TextAlign=ContentAlignment.MiddleCenter;valueLabel.Cursor=Cursors.SizeWE;
   edit.SetBounds(112,3,94,23);edit.Visible=false;edit.BackColor=Color.FromArgb(28,30,34);edit.ForeColor=Color.WhiteSmoke;edit.BorderStyle=BorderStyle.FixedSingle;
   arrow.Text="▾";arrow.SetBounds(208,3,26,23);arrow.FlatStyle=FlatStyle.Flat;arrow.FlatAppearance.BorderColor=Color.FromArgb(76,79,85);arrow.ForeColor=Color.WhiteSmoke;arrow.BackColor=Color.FromArgb(50,52,57);
   reset.Text="↶";reset.SetBounds(236,3,23,23);reset.FlatStyle=FlatStyle.Flat;reset.FlatAppearance.BorderColor=Color.FromArgb(76,79,85);reset.ForeColor=Color.WhiteSmoke;reset.BackColor=Color.FromArgb(50,52,57);reset.Visible=false;
   keyframe.Text="◇";keyframe.SetBounds(262,3,23,23);keyframe.FlatStyle=FlatStyle.Flat;keyframe.FlatAppearance.BorderColor=Color.FromArgb(76,79,85);keyframe.ForeColor=Color.FromArgb(145,199,255);keyframe.BackColor=Color.FromArgb(50,52,57);keyframe.Visible=false;
   foreach(var button in new[]{previous,next}){button.SetBounds(0,3,18,23);button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderColor=Color.FromArgb(76,79,85);button.ForeColor=Color.FromArgb(176,184,196);button.BackColor=Color.FromArgb(50,52,57);button.Visible=false;}previous.Text="‹";next.Text="›";
   slider.SetBounds(104,32,130,33);slider.Minimum=0;slider.Maximum=1000;slider.TickStyle=TickStyle.None;slider.Visible=false;slider.BackColor=BackColor;
   Controls.Add(caption);Controls.Add(valueLabel);Controls.Add(edit);Controls.Add(arrow);Controls.Add(reset);Controls.Add(previous);Controls.Add(keyframe);Controls.Add(next);Controls.Add(slider);
   caption.Click+=(s,e)=>{if(Selected!=null)Selected();};
   valueLabel.Click+=(s,e)=>{if(Selected!=null)Selected();};
   Resize+=(s,e)=>LayoutParts();LayoutParts();
   SetValue(initial,false);
   valueLabel.MouseDown+=(s,e)=>{if(e.Button!=MouseButtons.Left)return;pressed=true;moved=false;downX=e.X;startValue=value;valueLabel.Capture=true;};
   valueLabel.MouseMove+=(s,e)=>{if(!pressed)return;int delta=e.X-downX;if(Math.Abs(delta)<3&&!moved)return;moved=true;double distance=Math.Abs(delta),adjustment=delta*step;if(AcceleratedScrub){adjustment=Math.Sign(delta)*step*(distance<=24?distance*.5:12+(distance-24)+Math.Pow(distance-24,2)/120);if((Control.ModifierKeys&Keys.Shift)!=0)adjustment/=5;}SetValue(startValue+adjustment,true);};
   valueLabel.MouseUp+=(s,e)=>{if(!pressed)return;pressed=false;valueLabel.Capture=false;if(moved)Commit();else BeginEdit();};
   edit.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){ApplyText();e.SuppressKeyPress=true;}else if(e.KeyCode==Keys.Escape){edit.Visible=false;valueLabel.Visible=true;e.SuppressKeyPress=true;}};
   edit.LostFocus+=(s,e)=>{if(edit.Visible)ApplyText();};
   arrow.Click+=(s,e)=>{slider.Visible=!slider.Visible;Height=slider.Visible?70:30;arrow.Text=slider.Visible?"▴":"▾";};
   reset.Click+=(s,e)=>{if(ResetRequested!=null)ResetRequested();};
   keyframe.Click+=(s,e)=>{if(KeyframeRequested!=null)KeyframeRequested();};
   previous.Click+=(s,e)=>{if(KeyframeNavigateRequested!=null)KeyframeNavigateRequested(-1);};next.Click+=(s,e)=>{if(KeyframeNavigateRequested!=null)KeyframeNavigateRequested(1);};
   slider.MouseDown+=(s,e)=>startValue=value;
   slider.Scroll+=(s,e)=>{if(!updating)SetValue(sliderMinimum+(sliderMaximum-sliderMinimum)*slider.Value/1000,true);};
   slider.MouseUp+=(s,e)=>Commit();
  }
  public void EnableReset(Action action){ResetRequested=action;reset.Visible=action!=null;LayoutParts();}
  public void HideSlider(){slider.Visible=false;arrow.Visible=false;Height=30;LayoutParts();}
  public void EnableKeyframe(bool active,bool atCurrent,Action action,Action clear=null,Action<int> navigate=null){KeyframeRequested=action;KeyframeNavigateRequested=navigate;keyframe.Visible=action!=null;previous.Visible=next.Visible=false;keyframe.Text=active?"◷":"◴";keyframe.BackColor=atCurrent?Color.FromArgb(80,62,42):Color.FromArgb(50,52,57);keyframe.ForeColor=active?Color.FromArgb(255,170,65):Color.FromArgb(145,199,255);keyframeMenu.Items.Clear();if(clear!=null)keyframeMenu.Items.Add("Eliminar todos los fotogramas clave",null,(s,e)=>clear());keyframe.ContextMenuStrip=clear==null?null:keyframeMenu;LayoutParts();}
  public void DisplayKeyframeState(bool active,bool atCurrent){if(!keyframe.Visible)return;string glyph=active?"◷":"◴";if(keyframe.Text!=glyph)keyframe.Text=glyph;Color background=atCurrent?Color.FromArgb(80,62,42):Color.FromArgb(50,52,57),foreground=active?Color.FromArgb(255,170,65):Color.FromArgb(145,199,255);if(keyframe.BackColor!=background)keyframe.BackColor=background;if(keyframe.ForeColor!=foreground)keyframe.ForeColor=foreground;}
  void LayoutParts(){
   int resetSpace=reset.Visible?25:0,keyframeSpace=keyframe.Visible?25:0,navSpace=previous.Visible?40:0;
   int titleWidth=Math.Max(58,Width-(resetSpace+keyframeSpace+navSpace==0?124:150+keyframeSpace+navSpace));
   int valueWidth=Math.Max(50,Width-titleWidth-2-(arrow.Visible?26:0)-resetSpace-keyframeSpace-navSpace-4);
   caption.Width=titleWidth;valueLabel.Left=edit.Left=titleWidth+2;valueLabel.Width=edit.Width=valueWidth;
   arrow.Left=valueLabel.Right+2;arrow.Width=24;reset.Left=arrow.Right+2;previous.Left=reset.Visible?reset.Right+2:arrow.Right+2;keyframe.Left=previous.Visible?previous.Right+1:previous.Left;next.Left=keyframe.Right+1;
   slider.Left=titleWidth;slider.Width=Math.Max(88,Width-titleWidth-6);
  }
  void BeginEdit(){startValue=value;edit.Text=value.ToString("F"+decimals,CultureInfo.CurrentCulture);valueLabel.Visible=false;edit.Visible=true;edit.Focus();edit.SelectAll();}
  void ApplyText(){
   if(!edit.Visible)return;
   string entered=edit.Text.Trim().TrimEnd('%');
   edit.Visible=false;valueLabel.Visible=true;
   double parsed;
   if(Double.TryParse(entered,NumberStyles.Float,CultureInfo.CurrentCulture,out parsed)||Double.TryParse(entered,NumberStyles.Float,CultureInfo.InvariantCulture,out parsed)){
    SetValue(parsed,true);Commit();
   }
  }
  public void DisplayValue(double next){SetValue(next,false);startValue=value;}
  void SetValue(double next,bool preview){
   next=Math.Max(minimum,Math.Min(maximum,Math.Round(next,decimals)));
   if(Math.Abs(next-value)<0.0000001&&valueLabel.Text.Length>0)return;
   value=next;valueLabel.Text=value.ToString("F"+decimals,CultureInfo.CurrentCulture)+suffix;
   updating=true;slider.Value=(int)Math.Max(0,Math.Min(1000,Math.Round((value-sliderMinimum)/(sliderMaximum-sliderMinimum)*1000)));updating=false;
   if(preview&&PreviewChanged!=null)PreviewChanged(value);
  }
  void Commit(){
   if(Math.Abs(value-startValue)<=0.0000001)return;
   startValue=value;
   if(Committed!=null)Committed(value);
  }
 }
}
