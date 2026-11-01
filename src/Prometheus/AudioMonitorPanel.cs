using System;
using System.Drawing;
using System.Windows.Forms;

namespace Prometheus {
 public sealed class AudioMonitorPanel:Panel {
  readonly Timer refresh=new Timer{Interval=50};
  readonly Button left=new Button{Text="L",Width=36,Height=28,FlatStyle=FlatStyle.Flat};
  readonly Button right=new Button{Text="R",Width=36,Height=28,FlatStyle=FlatStyle.Flat};
  double holdL,holdR;
  public AudioMonitorPanel(){
   DoubleBuffered=true;BackColor=Color.FromArgb(36,38,42);ForeColor=Color.Gainsboro;
   var buttons=new FlowLayoutPanel{Dock=DockStyle.Top,Height=38,Padding=new Padding(8,5,0,0)};
   foreach(var b in new[]{left,right}){b.BackColor=Color.FromArgb(49,51,56);b.ForeColor=ForeColor;b.FlatAppearance.BorderColor=Color.FromArgb(80,84,90);buttons.Controls.Add(b);}
   left.Click+=(s,e)=>{AudioMonitorState.MuteLeft=!AudioMonitorState.MuteLeft;Invalidate();};
   right.Click+=(s,e)=>{AudioMonitorState.MuteRight=!AudioMonitorState.MuteRight;Invalidate();};
   Controls.Add(buttons);refresh.Tick+=(s,e)=>{holdL=Math.Max(AudioMonitorState.LeftPeak,holdL*0.93);holdR=Math.Max(AudioMonitorState.RightPeak,holdR*0.93);Invalidate();};refresh.Start();
  }
  static float Extent(double value){if(value<=0)return 0;double db=20*Math.Log10(value);return (float)Math.Max(0,Math.Min(1,(db+60)/60));}
  void DrawMeter(Graphics g,string name,int x,int y,int width,int height,double rms,double peak,double hold,bool muted){
   using(var bg=new SolidBrush(Color.FromArgb(22,24,27)))g.FillRectangle(bg,x,y,width,height);
   int fill=(int)Math.Round(height*Extent(rms)),peakY=y+height-(int)Math.Round(height*Extent(peak)),holdY=y+height-(int)Math.Round(height*Extent(hold));
   using(var green=new SolidBrush(muted?Color.FromArgb(75,92,78):Color.FromArgb(109,192,126)))g.FillRectangle(green,x,y+height-fill,width,fill);
   using(var bright=new Pen(peak>=0.98?Color.FromArgb(234,91,80):Color.FromArgb(230,208,101),2))g.DrawLine(bright,x,peakY,x+width-1,peakY);
   using(var marker=new Pen(Color.WhiteSmoke))g.DrawLine(marker,x,holdY,x+width-1,holdY);
   TextRenderer.DrawText(g,name,Font,new Rectangle(x,y+height+3,width,20),muted?Color.Gray:ForeColor,TextFormatFlags.HorizontalCenter);
   TextRenderer.DrawText(g,peak<=0?"−∞":(20*Math.Log10(peak)).ToString("0.0")+" dB",Font,new Rectangle(x-8,y+height+23,width+16,20),Color.FromArgb(169,174,180),TextFormatFlags.HorizontalCenter);
  }
  protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);left.BackColor=AudioMonitorState.MuteLeft?Color.FromArgb(113,56,52):Color.FromArgb(49,51,56);right.BackColor=AudioMonitorState.MuteRight?Color.FromArgb(113,56,52):Color.FromArgb(49,51,56);
   int top=58,bottom=Math.Max(top+80,Height-71),meterHeight=bottom-top,meterWidth=Math.Max(18,Math.Min(48,(Width-86)/3));int center=Math.Max(60,Width/2),gap=18;
   DrawMeter(e.Graphics,"L",center-gap/2-meterWidth,top,meterWidth,meterHeight,AudioMonitorState.LeftRms,AudioMonitorState.LeftPeak,holdL,AudioMonitorState.MuteLeft);
   DrawMeter(e.Graphics,"R",center+gap/2,top,meterWidth,meterHeight,AudioMonitorState.RightRms,AudioMonitorState.RightPeak,holdR,AudioMonitorState.MuteRight);
   using(var ticks=new Pen(Color.FromArgb(112,118,124)))foreach(int db in new[]{0,-6,-12,-24,-48}){int y=top+meterHeight-(int)Math.Round(meterHeight*(db+60)/60.0);e.Graphics.DrawLine(ticks,center-gap/2-meterWidth-7,y,center-gap/2-meterWidth-2,y);}
   TextRenderer.DrawText(e.Graphics,"Silencio L/R solo en escucha",Font,new Rectangle(5,Height-25,Math.Max(1,Width-10),20),Color.FromArgb(169,174,180),TextFormatFlags.HorizontalCenter);
  }
  protected override void Dispose(bool disposing){if(disposing){refresh.Stop();refresh.Dispose();}base.Dispose(disposing);}
 }
}
