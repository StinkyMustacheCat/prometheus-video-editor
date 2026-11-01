using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
namespace Prometheus {
 public sealed class KeyframeSelection {public string target;public double source_seconds;}
 public sealed class KeyframeTransfer {public string target,contract_id,parameter,interpolation;public int effect_ordinal;public double offset_seconds,value,in_tangent,out_tangent;}
 public sealed class KeyframeTimelineView:Control {
  Clip clip;double playhead;int scroll;string active,dragTarget;double dragSource;double? dragPreviewTime;Point down;bool scrolling,scrubbing;
  readonly List<KeyframeSelection> selected=new List<KeyframeSelection>();
  readonly HashSet<string> expanded=new HashSet<string>();
  readonly Dictionary<string,KeyframeCurveGraph> graphs=new Dictionary<string,KeyframeCurveGraph>();
  static List<KeyframeTransfer> copied=new List<KeyframeTransfer>();
  const int Row=27,Ruler=28,Bar=10,GraphHeight=119;
  int LabelWidth {get{return Math.Min(150,Math.Max(125,Width/2));}}
  List<KeyframeCurve> Curves(){return clip==null||clip.animation==null?new List<KeyframeCurve>():clip.animation.OrderBy(c=>c.target).ToList();}
  int ContentHeight {get{return Curves().Sum(c=>Row+(expanded.Contains(c.target)?GraphHeight:0));}}
  int MaxScroll {get{return Math.Max(0,ContentHeight-Math.Max(1,Height-Ruler));}}
  int ContentRight {get{return Width-(MaxScroll>0?Bar:0)-4;}}
  int ContentLeft {get{return LabelWidth+7;}}
  double Start {get{return clip==null?0:clip.timeline_start.Seconds;}}
  double Duration {get{return clip==null?1:Math.Max(1.0/60,clip.duration.Seconds);}}
  double At(KeyframePoint point){return Start+(point.source_seconds-clip.source_in.Seconds)/clip.speed.Seconds;}
  int X(double time){return ContentLeft+(int)Math.Round((time-Start)/Duration*Math.Max(1,ContentRight-ContentLeft));}
  double Time(int x){return Start+Math.Max(0,Math.Min(1,(x-ContentLeft)/(double)Math.Max(1,ContentRight-ContentLeft)))*Duration;}
  double SnapAcrossRows(double time,string target,double source){double? nearest=Curves().Where(c=>c.target!=target).SelectMany(c=>c.points.Select(p=>At(p))).Where(at=>at>=Start&&at<clip.End).OrderBy(at=>Math.Abs(X(at)-X(time))).Select(at=>(double?)at).FirstOrDefault();return nearest.HasValue&&Math.Abs(X(nearest.Value)-X(time))<=8?Rational.Frame(nearest.Value).Seconds:Rational.Frame(time).Seconds;}
  bool Selected(string target,double source){return selected.Any(s=>s.target==target&&Math.Abs(s.source_seconds-source)<1.0/48000);}
  string DisplayName(string target){if(target=="transform.scale")return "Escala X";if(target=="transform.scale_y")return "Escala Y";if(target=="transform.x")return "Posición X";if(target=="transform.y")return "Posición Y";if(target=="transform.opacity")return "Opacidad";var parts=target.Split(':');return parts.Length==3?parts[2]:target;}
  public Action<double> SeekRequested;
  public Action<string,string,double,double> KeyframeMoved;
  public Action<string,string,double> KeyframeToggled;
  public Action<List<KeyframeSelection>> KeyframesDeleted;
  public Action<List<KeyframeTransfer>,double> KeyframesPasted;
  public Action<string> ActiveTargetChanged;
  public Action<bool> GraphVisibilityChanged;
  Action<string,string,string> interpolationChanged;Action<string,string,double,bool,double> tangentChanged;
  public Action<string,string,string> InterpolationChanged {get{return interpolationChanged;}set{interpolationChanged=value;foreach(var g in graphs.Values)g.InterpolationChanged=value;}}
  public Action<string,string,double,bool,double> TangentChanged {get{return tangentChanged;}set{tangentChanged=value;foreach(var g in graphs.Values)g.TangentChanged=value;}}
  public KeyframeTimelineView(){DoubleBuffered=true;BackColor=Color.FromArgb(31,33,37);ForeColor=Color.Gainsboro;Font=new Font("Segoe UI",8);SetStyle(ControlStyles.Selectable,true);TabStop=true;Resize+=(s,e)=>LayoutGraphs();}
  void Activate(string target){if(active==target)return;active=target;if(ActiveTargetChanged!=null)ActiveTargetChanged(target);Invalidate();}
  int RowTop(string target){int y=Ruler-scroll;foreach(var curve in Curves()){if(curve.target==target)return y;y+=Row+(expanded.Contains(curve.target)?GraphHeight:0);}return -1;}
  void LayoutGraphs(){scroll=Math.Min(scroll,MaxScroll);var valid=new HashSet<string>(Curves().Select(c=>c.target));foreach(var key in graphs.Keys.Where(x=>!valid.Contains(x)).ToList()){Controls.Remove(graphs[key]);graphs[key].Dispose();graphs.Remove(key);expanded.Remove(key);}
   foreach(var curve in Curves())if(expanded.Contains(curve.target)){KeyframeCurveGraph graph;if(!graphs.TryGetValue(curve.target,out graph)){graph=new KeyframeCurveGraph();graph.InterpolationChanged=interpolationChanged;graph.TangentChanged=tangentChanged;graph.MouseWheel+=(s,e)=>OnMouseWheel(e);graphs[curve.target]=graph;Controls.Add(graph);}var chosen=selected.FirstOrDefault(s=>s.target==curve.target);graph.SetContext(clip,curve.target,chosen==null?(double?)null:chosen.source_seconds);graph.SetBounds(0,RowTop(curve.target)+Row,ContentRight,GraphHeight);int top=Math.Max(0,Ruler-graph.Top),bottom=Math.Min(GraphHeight,Height-graph.Top);graph.Visible=bottom>top;if(graph.Visible){var previous=graph.Region;graph.Region=new Region(new Rectangle(0,top,graph.Width,bottom-top));if(previous!=null)previous.Dispose();}}
   foreach(var pair in graphs)if(!expanded.Contains(pair.Key))pair.Value.Visible=false;Invalidate();
  }
  public void SelectTarget(string target){if(target==null)return;Activate(target);int top=RowTop(target);if(top<Ruler)scroll=Math.Max(0,scroll+top-Ruler);else if(top+Row>Height)scroll=Math.Min(MaxScroll,scroll+top+Row-Height);LayoutGraphs();}
  public void SetClip(Clip value,double time){if(clip==null||value==null||clip.id!=value.id){selected.Clear();active=null;scroll=0;expanded.Clear();}clip=value;playhead=time;selected.RemoveAll(s=>!Curves().Any(c=>c.target==s.target&&c.points.Any(p=>Math.Abs(p.source_seconds-s.source_seconds)<1.0/48000)));LayoutGraphs();}
  public void SetPlayhead(double time){playhead=time;Invalidate();}
  void Navigate(int direction,string target){if(clip==null)return;var candidates=Curves().Where(c=>c.enabled&&(target==null||c.target==target)).SelectMany(c=>c.points.Select(p=>new{curve=c,point=p,time=At(p)})).Where(p=>p.time>=Start&&p.time<clip.End);
   var next=direction<0?candidates.Where(p=>p.time<playhead-1.0/120).OrderByDescending(p=>p.time).FirstOrDefault():candidates.Where(p=>p.time>playhead+1.0/120).OrderBy(p=>p.time).FirstOrDefault();if(next==null)return;playhead=next.time;selected.Clear();selected.Add(new KeyframeSelection{target=next.curve.target,source_seconds=next.point.source_seconds});SelectTarget(next.curve.target);if(SeekRequested!=null)SeekRequested(next.time);
  }
  void TogglePoint(string target,double time){if(clip==null||target==null)return;Activate(target);if(KeyframeToggled!=null)KeyframeToggled(clip.id,target,Rational.Frame(Math.Max(Start,Math.Min(clip.End-1.0/60,time))).Seconds);}
  public bool HandleShortcut(Keys key){if(clip==null)return false;
   if(key==Keys.Delete){if(selected.Count==0)return false;if(KeyframesDeleted!=null)KeyframesDeleted(selected.Select(s=>new KeyframeSelection{target=s.target,source_seconds=s.source_seconds}).ToList());selected.Clear();LayoutGraphs();return true;}
   if(key==(Keys.Control|Keys.C)){if(selected.Count==0)return false;double first=selected.Min(s=>Start+(s.source_seconds-clip.source_in.Seconds)/clip.speed.Seconds);copied.Clear();foreach(var s in selected){var curve=Curves().FirstOrDefault(c=>c.target==s.target);var point=curve==null?null:curve.points.FirstOrDefault(p=>Math.Abs(p.source_seconds-s.source_seconds)<1.0/48000);if(point==null)continue;var item=new KeyframeTransfer{target=s.target,offset_seconds=At(point)-first,value=point.value,in_tangent=point.in_tangent,out_tangent=point.out_tangent,interpolation=curve.interpolation};var bits=s.target.Split(':');if(bits.Length==3&&bits[0]=="effect"){var effect=clip.effects.FirstOrDefault(e=>e.id==bits[1]);if(effect!=null){item.contract_id=effect.effect_id;item.parameter=bits[2];item.effect_ordinal=clip.effects.Where(e=>e.effect_id==effect.effect_id).ToList().FindIndex(e=>e.id==effect.id);}}copied.Add(item);}return copied.Count>0;}
   if(key==(Keys.Control|Keys.V)){if(copied.Count==0)return false;if(KeyframesPasted!=null)KeyframesPasted(copied.Select(s=>new KeyframeTransfer{target=s.target,contract_id=s.contract_id,parameter=s.parameter,effect_ordinal=s.effect_ordinal,interpolation=s.interpolation,offset_seconds=s.offset_seconds,value=s.value,in_tangent=s.in_tangent,out_tangent=s.out_tangent}).ToList(),playhead);return true;}return false;
  }
  static string FrameLabel(double time){long frames=(long)Math.Round(time*60);return String.Format("{0:00}:{1:00}:{2:00}",frames/3600,(frames/60)%60,frames%60);}
  protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;g.Clear(BackColor);using(var p=new Pen(Color.FromArgb(65,68,74)))g.DrawLine(p,LabelWidth,0,LabelWidth,Height);
   int first=LabelWidth-62;TextRenderer.DrawText(g,clip==null?"FOTOGRAMAS":clip.name,Font,new Rectangle(7,3,Math.Max(15,first-11),21),Color.FromArgb(225,227,230),TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter);
   string[] buttons={"‹","◆","›"};for(int i=0;i<3;i++)TextRenderer.DrawText(g,buttons[i],Font,new Rectangle(first+i*19,2,19,22),active==null&&i==1?Color.Gray:Color.FromArgb(255,174,77),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
   if(clip==null){TextRenderer.DrawText(g,"Selecciona un clip",Font,new Rectangle(LabelWidth+10,30,Math.Max(1,Width-LabelWidth-10),25),Color.Gray);return;}
   int ticks=Math.Max(1,Math.Min(4,(ContentRight-ContentLeft)/90));using(var p=new Pen(Color.FromArgb(83,87,94))){g.DrawLine(p,ContentLeft,Ruler,ContentRight,Ruler);for(int i=0;i<=ticks;i++){int x=ContentLeft+(ContentRight-ContentLeft)*i/ticks;g.DrawLine(p,x,17,x,Height);string label=FrameLabel(Start+Duration*i/ticks);TextRenderer.DrawText(g,label,Font,new Rectangle(i==ticks?x-72:x+2,2,72,16),Color.FromArgb(155,161,168),TextFormatFlags.EndEllipsis);}}
   var saved=g.Save();g.SetClip(new Rectangle(0,Ruler,ContentRight,Math.Max(0,Height-Ruler)));int y=Ruler-scroll;foreach(var curve in Curves()){if(y+Row>=Ruler&&y<Height){using(var b=new SolidBrush(curve.target==active?Color.FromArgb(57,50,43):Color.FromArgb(38,40,45)))g.FillRectangle(b,0,y,ContentRight,Row);Color ink=curve.enabled?Color.FromArgb(215,219,224):Color.FromArgb(120,124,130);TextRenderer.DrawText(g,DisplayName(curve.target),Font,new Rectangle(7,y+2,LabelWidth-73,Row-4),ink,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
    string[] controls={"‹","◆","›",expanded.Contains(curve.target)?"▴":"▾"};for(int i=0;i<4;i++){
     var bounds=new Rectangle(LabelWidth-68+i*17,y+2,17,Row-4);
     if(i==3){using(var fill=new SolidBrush(expanded.Contains(curve.target)?Color.FromArgb(120,77,37):Color.FromArgb(65,69,76)))g.FillRectangle(fill,bounds);using(var border=new Pen(Color.FromArgb(142,148,157)))g.DrawRectangle(border,bounds);}
     TextRenderer.DrawText(g,controls[i],Font,bounds,i==3?Color.White:Color.FromArgb(255,174,77),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
    }
    foreach(var point in curve.points){double at=At(point);if(at<Start-0.00001||at>clip.End+0.00001)continue;int x=X(at),cy=y+Row/2;bool chosen=Selected(curve.target,point.source_seconds);using(var b=new SolidBrush(chosen?Color.White:curve.enabled?Color.FromArgb(255,174,77):Color.Gray))g.FillPolygon(b,new[]{new Point(x,cy-6),new Point(x+6,cy),new Point(x,cy+6),new Point(x-6,cy)});if(chosen)using(var p=new Pen(Color.FromArgb(255,174,77),1.5f))g.DrawPolygon(p,new[]{new Point(x,cy-7),new Point(x+7,cy),new Point(x,cy+7),new Point(x-7,cy)});}}
    y+=Row+(expanded.Contains(curve.target)?GraphHeight:0);
   }g.Restore(saved);
   if(MaxScroll>0){int area=Math.Max(1,Height-Ruler),thumb=Math.Max(18,area*area/Math.Max(1,ContentHeight)),top=Ruler+(area-thumb)*scroll/MaxScroll;using(var b=new SolidBrush(Color.FromArgb(24,26,30)))g.FillRectangle(b,Width-Bar,Ruler,Bar,area);using(var b=new SolidBrush(Color.FromArgb(104,108,115)))g.FillRectangle(b,Width-Bar+2,top,Bar-4,thumb);}
   int cursor=X(playhead);if(cursor>=LabelWidth&&cursor<ContentRight){using(var p=new Pen(Color.FromArgb(255,158,54),2))g.DrawLine(p,cursor,0,cursor,Height);using(var b=new SolidBrush(Color.FromArgb(255,158,54)))g.FillPolygon(b,new[]{new Point(cursor-5,0),new Point(cursor+5,0),new Point(cursor,7)});}
   if(dragTarget!=null&&dragPreviewTime.HasValue){int x=X(dragPreviewTime.Value),cy=RowTop(dragTarget)+Row/2;if(x>=ContentLeft&&x<ContentRight){using(var guide=new Pen(Color.FromArgb(215,232,239,247),1)){guide.DashStyle=System.Drawing.Drawing2D.DashStyle.Dash;g.DrawLine(guide,x,Ruler,x,Height);}if(cy>=Ruler&&cy<Height)using(var fill=new SolidBrush(Color.White))g.FillPolygon(fill,new[]{new Point(x,cy-7),new Point(x+7,cy),new Point(x,cy+7),new Point(x-7,cy)});string label=FrameLabel(dragPreviewTime.Value);var badge=new Rectangle(Math.Max(ContentLeft,Math.Min(ContentRight-65,x+9)),Math.Max(1,cy-26),61,19);using(var background=new SolidBrush(Color.FromArgb(236,35,39,46)))g.FillRectangle(background,badge);TextRenderer.DrawText(g,label,Font,badge,Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}}
  }
  protected override void OnMouseWheel(MouseEventArgs e){if(MaxScroll>0){scroll=Math.Max(0,Math.Min(MaxScroll,scroll+(e.Delta>0?-54:54)));LayoutGraphs();}else base.OnMouseWheel(e);}
  void ScrollTo(int y){scroll=Math.Max(0,Math.Min(MaxScroll,(int)Math.Round((y-Ruler)/(double)Math.Max(1,Height-Ruler)*MaxScroll)));LayoutGraphs();}
  KeyframeCurve RowAt(int y){int top=Ruler-scroll;foreach(var curve in Curves()){if(y>=top&&y<top+Row)return curve;top+=Row+(expanded.Contains(curve.target)?GraphHeight:0);}return null;}
  protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);Focus();if(e.Button!=MouseButtons.Left||clip==null)return;down=e.Location;if(MaxScroll>0&&e.X>=Width-Bar){scrolling=true;Capture=true;ScrollTo(e.Y);return;}
   if(e.Y<Ruler){if(e.X<LabelWidth){int slot=(e.X-(LabelWidth-62))/19;if(slot==0)Navigate(-1,null);else if(slot==1)TogglePoint(active,playhead);else if(slot==2)Navigate(1,null);}else{scrubbing=true;Capture=true;if(SeekRequested!=null)SeekRequested(Rational.Frame(Time(e.X)).Seconds);}return;}
   var curve=RowAt(e.Y);if(curve==null)return;if(e.X<LabelWidth){Activate(curve.target);int slot=(e.X-(LabelWidth-68))/17;if(slot==0)Navigate(-1,curve.target);else if(slot==1)TogglePoint(curve.target,playhead);else if(slot==2)Navigate(1,curve.target);else if(slot==3){if(!expanded.Add(curve.target))expanded.Remove(curve.target);LayoutGraphs();if(GraphVisibilityChanged!=null)GraphVisibilityChanged(expanded.Count>0);}return;}
   Activate(curve.target);var point=curve.points.FirstOrDefault(p=>Math.Abs(X(At(p))-e.X)<=8);if(point!=null){bool was=Selected(curve.target,point.source_seconds);if((ModifierKeys&Keys.Shift)==0)selected.Clear();if(!was||selected.Count==0)selected.Add(new KeyframeSelection{target=curve.target,source_seconds=point.source_seconds});else if((ModifierKeys&Keys.Shift)!=0)selected.RemoveAll(s=>s.target==curve.target&&Math.Abs(s.source_seconds-point.source_seconds)<1.0/48000);LayoutGraphs();if(selected.Count==1){dragTarget=curve.target;dragSource=point.source_seconds;Capture=true;}return;}scrubbing=true;Capture=true;if(SeekRequested!=null)SeekRequested(Rational.Frame(Time(e.X)).Seconds);
  }
  protected override void OnMouseDoubleClick(MouseEventArgs e){base.OnMouseDoubleClick(e);if(e.Button!=MouseButtons.Left||e.X<LabelWidth)return;var curve=RowAt(e.Y);if(curve==null)return;double at=SnapAcrossRows(Math.Min(clip.End-1.0/60,Time(e.X)),curve.target,Double.NaN);TogglePoint(curve.target,at);if(SeekRequested!=null)SeekRequested(at);}
  protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(scrolling)ScrollTo(e.Y);else if(scrubbing&&SeekRequested!=null)SeekRequested(Rational.Frame(Time(e.X)).Seconds);else if(dragTarget!=null){double? next=Math.Abs(e.X-down.X)>3?(double?)SnapAcrossRows(Time(e.X),dragTarget,dragSource):null;if(next!=dragPreviewTime){dragPreviewTime=next;Invalidate();}}}
  protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(scrolling){scrolling=false;Capture=false;return;}if(scrubbing){scrubbing=false;Capture=false;return;}if(dragTarget==null)return;string target=dragTarget;double? destination=dragPreviewTime;if(!destination.HasValue&&Math.Abs(e.X-down.X)>3)destination=SnapAcrossRows(Time(e.X),target,dragSource);dragTarget=null;dragPreviewTime=null;Capture=false;Invalidate();if(destination.HasValue&&KeyframeMoved!=null)KeyframeMoved(clip.id,target,dragSource,destination.Value);}
 }
}
