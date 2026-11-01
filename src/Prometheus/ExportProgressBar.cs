using System;using System.Drawing;using System.Windows.Forms;
namespace Prometheus {
 public class ExportProgressBar:Control {
  int value,phase;bool indeterminate;readonly Timer animation=new Timer();
  public int Value{get{return value;}set{this.value=Math.Max(0,Math.Min(100,value));Invalidate();}}
  public bool Indeterminate{get{return indeterminate;}set{indeterminate=value;animation.Enabled=value;Invalidate();}}
  public ExportProgressBar(){DoubleBuffered=true;BackColor=Color.FromArgb(49,51,56);ForeColor=Color.FromArgb(255,156,44);animation.Interval=75;animation.Tick+=(s,a)=>{phase=(phase+7)%Math.Max(1,ClientSize.Width+ClientSize.Width/3);Invalidate();};}
  protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.Clear(BackColor);using(var brush=new SolidBrush(ForeColor))e.Graphics.FillRectangle(brush,0,0,ClientSize.Width*value/100.0f,ClientSize.Height);if(indeterminate)using(var activity=new SolidBrush(Color.FromArgb(145,255,207,143))){int width=Math.Max(18,ClientSize.Width/4);e.Graphics.FillRectangle(activity,phase-width,0,width,ClientSize.Height);}using(var pen=new Pen(Color.FromArgb(92,95,102)))e.Graphics.DrawRectangle(pen,0,0,Math.Max(0,ClientSize.Width-1),Math.Max(0,ClientSize.Height-1));}
  protected override void Dispose(bool disposing){if(disposing)animation.Dispose();base.Dispose(disposing);}
 }
}
