using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 // A compact value graph. The whole graph belongs to one selected property.
 public sealed class KeyframeCurveGraph:Control {
  Clip clip;string target;double? selectedSource;int handle=-1;double handleSource,ghost;
  PointF firstHandle,lastHandle;double firstY,lastY,firstSource,lastSource;
  int ButtonWidth{get{return Math.Max(38,(Width-10)/4);}}
  public Action<string,string,string> InterpolationChanged;
  public Action<string,string,double,bool,double> TangentChanged;
  static readonly string[] Modes={"linear","hold","smooth","bezier"};
  static readonly string[] Names={"Lineal","Mantener","Suave","Bézier"};
  public KeyframeCurveGraph(){Height=119;DoubleBuffered=true;BackColor=Color.FromArgb(25,27,31);Font=new Font("Segoe UI",8);Cursor=Cursors.Cross;}
  public void SetContext(Clip value,string property,double? source){clip=value;target=property;selectedSource=source;Invalidate();}
  KeyframeCurve Curve(){return clip==null||target==null?null:ClipKeyframes.Find(clip,target);}
  double At(KeyframePoint point){return clip.timeline_start.Seconds+(point.source_seconds-clip.source_in.Seconds)/clip.speed.Seconds;}
  float X(double time){return 10+(float)((time-clip.timeline_start.Seconds)/Math.Max(1.0/60,clip.duration.Seconds)*(Width-20));}
  float Y(double value,double min,double max){return 106-(float)((value-min)/(max-min)*69);}
  protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;g.Clear(BackColor);var curve=Curve();if(curve==null)return;
   for(int i=0;i<4;i++){var box=new Rectangle(5+i*ButtonWidth,3,ButtonWidth-3,22);using(var b=new SolidBrush(curve.interpolation==Modes[i]?Color.FromArgb(89,65,39):Color.FromArgb(42,44,49)))g.FillRectangle(b,box);TextRenderer.DrawText(g,Names[i],Font,box,Color.FromArgb(225,227,230),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);}
   if(curve.points.Count==0)return;double min=curve.points.Min(p=>p.value),max=curve.points.Max(p=>p.value);double gap=Math.Max(0.01,(max-min)*0.16);min-=gap;max+=gap;
   using(var grid=new Pen(Color.FromArgb(72,75,82))){g.DrawRectangle(grid,9,36,Math.Max(1,Width-19),71);g.DrawLine(grid,9,71,Width-10,71);}
   PointF? previous=null;using(var line=new Pen(curve.enabled?Color.FromArgb(255,174,77):Color.FromArgb(125,128,133),1.8f))for(int i=0;i<=Math.Max(2,Width-20);i+=3){double time=clip.timeline_start.Seconds+clip.duration.Seconds*i/Math.Max(2,Width-20);var point=new PointF(10+i,Y(ClipKeyframes.StoredValue(clip,target,time,curve.points[0].value),min,max));if(previous.HasValue)g.DrawLine(line,previous.Value,point);previous=point;}
   foreach(var point in curve.points){float x=X(At(point)),y=Y(point.value,min,max);using(var fill=new SolidBrush(Color.White))g.FillEllipse(fill,x-3,y-3,6,6);}
   firstHandle=lastHandle=PointF.Empty;
   if(curve.interpolation!="bezier"||curve.points.Count<2)return;
   var points=curve.points.OrderBy(p=>p.source_seconds).ToList();int index=selectedSource.HasValue?points.FindIndex(p=>Math.Abs(p.source_seconds-selectedSource.Value)<1.0/48000):0;if(index<0)index=0;if(index>=points.Count-1)index=points.Count-2;
   var a=points[index];var nextPoint=points[index+1];firstSource=a.source_seconds;lastSource=nextPoint.source_seconds;float x0=X(At(a)),x1=X(At(nextPoint));firstY=Y(a.value,min,max);lastY=Y(nextPoint.value,min,max);if(Math.Abs(firstY-lastY)<1)return;
   double outSlope=handle==0?ghost:a.out_tangent,inSlope=handle==1?ghost:nextPoint.in_tangent;
   firstHandle=new PointF(x0+(x1-x0)/3,(float)(firstY+(lastY-firstY)*outSlope/3));lastHandle=new PointF(x1-(x1-x0)/3,(float)(lastY-(lastY-firstY)*inSlope/3));
   using(var p=new Pen(Color.FromArgb(150,202,255))){g.DrawLine(p,x0,(float)firstY,firstHandle.X,firstHandle.Y);g.DrawLine(p,x1,(float)lastY,lastHandle.X,lastHandle.Y);}
   using(var fill=new SolidBrush(Color.FromArgb(150,202,255))){g.FillEllipse(fill,firstHandle.X-5,firstHandle.Y-5,10,10);g.FillEllipse(fill,lastHandle.X-5,lastHandle.Y-5,10,10);}
  }
  protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);var curve=Curve();if(curve==null||e.Button!=MouseButtons.Left)return;
   if(e.Y<27){int choice=(e.X-5)/ButtonWidth;if(e.X>=5&&choice>=0&&choice<4&&InterpolationChanged!=null)InterpolationChanged(clip.id,target,Modes[choice]);return;}
   if(curve.interpolation!="bezier")return;
   if(Math.Abs(e.X-firstHandle.X)<=9&&Math.Abs(e.Y-firstHandle.Y)<=9){handle=0;handleSource=firstSource;ghost=curve.points.First(p=>p.source_seconds==firstSource).out_tangent;Capture=true;}
   else if(Math.Abs(e.X-lastHandle.X)<=9&&Math.Abs(e.Y-lastHandle.Y)<=9){handle=1;handleSource=lastSource;ghost=curve.points.First(p=>p.source_seconds==lastSource).in_tangent;Capture=true;}
  }
  protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(handle<0)return;double delta=lastY-firstY;if(Math.Abs(delta)<1)return;ghost=Math.Max(0,Math.Min(3,handle==0?3*(e.Y-firstY)/delta:3*(lastY-e.Y)/delta));Invalidate();}
  protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(handle<0)return;bool incoming=handle==1;handle=-1;Capture=false;if(TangentChanged!=null)TangentChanged(clip.id,target,handleSource,incoming,ghost);Invalidate();}
 }
}
