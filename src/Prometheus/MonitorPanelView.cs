using System;
using System.Drawing;
using System.Windows.Forms;

namespace Prometheus {
 // Owns the monitor canvas, transport bar and preview controls; the main form supplies editing actions.
 public sealed partial class MonitorPanelView:Panel {
  readonly Color surface=Color.FromArgb(27,28,31),barColor=Color.FromArgb(36,38,42),ink=Color.FromArgb(223,225,228);
  readonly ToolTip help=new ToolTip();
  bool updatingZoom;
  Panel transportRow;
  int canvasWidth=1920,canvasHeight=1080;
  public PreviewCanvas Canvas{get;private set;}
  public Label TimeLabel{get;private set;}
  public TrackBar ZoomSlider{get;private set;}
  public NumericUpDown ZoomPercent{get;private set;}
  public ComboBox FpsChoice{get;private set;}
  public ComboBox ResolutionChoice{get;private set;}
  public Action<int> StepRequested;
  public Action PlayPauseRequested;
  public Action PreviewOptionsChanged;
  public Action ViewChanged;
  public int PreviewFps{get{return FpsChoice.SelectedIndex==0?15:FpsChoice.SelectedIndex==2?60:30;}}
  public string PreviewQuality{get{return Convert.ToString(ResolutionChoice.SelectedItem);}}
  public bool ControlsVisible{get{return transportRow==null||transportRow.Visible;}}
  int[] PreviewSize{get{return SequenceCanvas.PreviewSize(new Sequence{width=canvasWidth,height=canvasHeight},ResolutionChoice.SelectedIndex==0?320:ResolutionChoice.SelectedIndex==2?1920:640);}}
  public int PreviewWidth{get{return PreviewSize[0];}}
  public int PreviewHeight{get{return PreviewSize[1];}}
  public void SetCanvasSize(int width,int height){if(canvasWidth==width&&canvasHeight==height)return;canvasWidth=width;canvasHeight=height;Canvas.SetCanvasSize(width,height);}
  public MonitorPanelView(){
   Dock=DockStyle.Fill;BackColor=Color.Black;
   Canvas=new PreviewCanvas{Dock=DockStyle.Fill,BackColor=Color.Black};
   var row=new Panel{Dock=DockStyle.Bottom,Height=42,BackColor=barColor,Padding=new Padding(6,5,6,4)};
   transportRow=row;
   var left=new FlowLayoutPanel{Dock=DockStyle.Left,Width=256,WrapContents=false};
   left.Controls.Add(Button("◀",()=>{if(StepRequested!=null)StepRequested(-1);}));
   left.Controls.Add(Button("▶ / Pausa",()=>{if(PlayPauseRequested!=null)PlayPauseRequested();}));
   left.Controls.Add(Button("▶",()=>{if(StepRequested!=null)StepRequested(1);}));
   foreach(Button button in left.Controls){button.AutoSize=false;button.Width=button.Text.Contains("Pausa")?82:28;button.Margin=new Padding(0,0,3,0);}
   left.Controls.Add(BuildTimecodeControl());
   var zoom=new FlowLayoutPanel{Dock=DockStyle.Right,Width=260,WrapContents=false,FlowDirection=FlowDirection.LeftToRight};
   FpsChoice=new ComboBox{Width=52,DropDownStyle=ComboBoxStyle.DropDownList,BackColor=surface,ForeColor=ink};
   FpsChoice.Items.AddRange(new object[]{"15 FPS","30 FPS","60 FPS"});FpsChoice.SelectedIndex=1;help.SetToolTip(FpsChoice,"Solo afecta la reproducción del monitor. El montaje y sus fotogramas clave mantienen 60 FPS.");
   FpsChoice.SelectedIndexChanged+=(s,e)=>{if(PreviewOptionsChanged!=null)PreviewOptionsChanged();};
   ResolutionChoice=new ComboBox{Width=62,DropDownStyle=ComboBoxStyle.DropDownList,BackColor=surface,ForeColor=ink};
   ResolutionChoice.Items.AddRange(new object[]{"Baja","Media","Alta"});ResolutionChoice.SelectedIndex=1;
   ResolutionChoice.SelectedIndexChanged+=(s,e)=>{if(PreviewOptionsChanged!=null)PreviewOptionsChanged();};
   zoom.Controls.Add(FpsChoice);zoom.Controls.Add(ResolutionChoice);
   var fit=Button("Ajustar",()=>Fit());fit.AutoSize=false;fit.Width=63;fit.Margin=new Padding(0,0,3,0);zoom.Controls.Add(fit);
   ZoomSlider=new TrackBar{Minimum=10,Maximum=200,Value=100,Width=100,Height=28,TickStyle=TickStyle.None,SmallChange=5,Margin=new Padding(0,0,1,0)};
   ZoomSlider.ValueChanged+=(s,e)=>{if(!updatingZoom)SetPercent(ZoomSlider.Value);};help.SetToolTip(ZoomSlider,"Zoom: 10–200 %. Campo manual hasta 400 %.");zoom.Controls.Add(ZoomSlider);
   ZoomPercent=new NumericUpDown{Minimum=10,Maximum=400,Value=100,Width=56,BackColor=surface,ForeColor=ink,Margin=new Padding(0,3,1,0)};
   ZoomPercent.ValueChanged+=(s,e)=>{if(!updatingZoom)SetPercent((int)ZoomPercent.Value);};zoom.Controls.Add(ZoomPercent);
   zoom.Controls.Add(new Label{Text="%",ForeColor=ink,Width=18,Height=26,TextAlign=ContentAlignment.MiddleLeft,Margin=new Padding(0)});
   row.Controls.Add(left);row.Controls.Add(zoom);
   row.Resize+=(s,e)=>{int available=Math.Max(1,row.ClientSize.Width-12);zoom.Width=Math.Max(270,Math.Min(355,available-190));left.Width=Math.Max(190,Math.Min(256,available-zoom.Width));ZoomSlider.Width=Math.Max(40,zoom.Width-275);timeHost.Width=Math.Max(44,left.Width-147);};
   help.SetToolTip(fit,"Ajustar la imagen completa al monitor");
   Controls.Add(Canvas);Controls.Add(row);
   Canvas.ZoomChanged=value=>{updatingZoom=true;ZoomPercent.Value=value;ZoomSlider.Value=Math.Min(ZoomSlider.Maximum,value);updatingZoom=false;if(ViewChanged!=null)ViewChanged();};
  }
  Button Button(string label,Action action){var button=new Button{Text=label,AutoSize=true,Height=30,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(49,51,56),ForeColor=ink,Margin=new Padding(3,0,3,0)};button.FlatAppearance.BorderColor=Color.FromArgb(66,68,74);button.Click+=(s,e)=>action();return button;}
  public void Fit(){Canvas.Fit();if(ViewChanged!=null)ViewChanged();}
  public void SetPreviewFps(int value){FpsChoice.SelectedIndex=value==15?0:value==60?2:1;}
  public void SetPreviewQuality(string value){int index=ResolutionChoice.Items.IndexOf(value);if(index>=0)ResolutionChoice.SelectedIndex=index;}
  public void SetControlsVisible(bool visible){transportRow.Visible=visible;PerformLayout();}
  public void SetPercent(int value){value=Math.Max(10,Math.Min(400,value));updatingZoom=true;ZoomPercent.Value=value;ZoomSlider.Value=Math.Min(200,value);updatingZoom=false;Canvas.Percent(value);if(ViewChanged!=null)ViewChanged();}
  public void SetTime(double seconds){TimeLabel.Text=Timeline.TimeLabel(seconds);}
  public void SetImage(Bitmap bitmap){var old=Canvas.Image;Canvas.Image=bitmap;Canvas.Invalidate();if(old!=null)old.Dispose();}
  protected override void Dispose(bool disposing){if(disposing)help.Dispose();base.Dispose(disposing);}
 }
}
