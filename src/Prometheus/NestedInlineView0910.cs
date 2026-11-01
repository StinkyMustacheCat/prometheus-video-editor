using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public sealed class NestedInlineView0910:Panel {
  Project project;Clip outer;NestedContent0910 content;List<string> lanes=new List<string>();
  string dragging;int dragMode;Point origin;double oldStart,oldDuration,previewStart,previewDuration;string previewTrack;
  const int LabelWidth=150,Row=26,Header=29;
  public Action<string,string,double,double,string> ChildChanged;public Action<string,string> EditChildRequested,RemoveChildRequested;public Action CloseRequested;
  public NestedInlineView0910(){DoubleBuffered=true;BackColor=Color.FromArgb(32,35,40);ForeColor=Color.Gainsboro;Visible=false;Height=0;SetStyle(ControlStyles.ResizeRedraw,true);}
  public void Set(Project p,string clipId){project=p;outer=p==null?null:p.Active.clips.Find(c=>c.id==clipId);content=NestedMedia0910.IsNested(p,outer)?NestedMedia0910.Read(outer):null;lanes=content==null?new List<string>():content.video_orders.OrderByDescending(x=>x.Value).Select(x=>x.Key).Concat(content.audio_orders.OrderBy(x=>x.Value).Select(x=>x.Key)).ToList();Visible=content!=null;Height=content==null?0:Header+(lanes.Count+1)*Row+3;Invalidate();}
  double PixelsPerSecond{get{return outer==null?1:Math.Max(1,(Width-LabelWidth-16)/Math.Max(outer.duration.Seconds,content.clips.Max(c=>c.End)));}}
  int VideoRows{get{return content==null?0:content.video_orders.Count;}}
  int LaneY(int index){return Header+(index+(index>=VideoRows?1:0))*Row;}
  Rectangle ChildRect(Clip c){int lane=lanes.IndexOf(c.track_id);return new Rectangle(LabelWidth+(int)Math.Round(c.timeline_start.Seconds*PixelsPerSecond),LaneY(lane)+3,Math.Max(6,(int)Math.Round(c.duration.Seconds*PixelsPerSecond)),Row-6);}
  protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;g.Clear(BackColor);if(content==null)return;
   using(var header=new SolidBrush(Color.FromArgb(42,46,53)))g.FillRectangle(header,0,0,Width,Header);
   TextRenderer.DrawText(g,"Editar medio anidado · "+outer.name,Font,new Rectangle(10,5,Math.Max(30,Width-165),20),Color.White,TextFormatFlags.EndEllipsis);
   TextRenderer.DrawText(g,"Listo  ×",Font,new Rectangle(Width-73,5,69,20),Color.FromArgb(255,178,91),TextFormatFlags.Right);
   for(int i=0;i<lanes.Count;i++){string lane=lanes[i];bool audio=content.audio_orders.ContainsKey(lane);int y=LaneY(i);using(var b=new SolidBrush(i%2==0?Color.FromArgb(34,38,44):Color.FromArgb(39,43,49)))g.FillRectangle(b,0,y,Width,Row);TextRenderer.DrawText(g,(audio?"A":"V")+" · pista interna",Font,new Rectangle(8,y+5,LabelWidth-14,18),Color.FromArgb(178,187,197),TextFormatFlags.EndEllipsis);}
   foreach(var child in content.clips){int lane=lanes.IndexOf(child.track_id);if(lane<0)continue;var rect=ChildRect(child);if(child.id==dragging){rect.X=LabelWidth+(int)Math.Round(previewStart*PixelsPerSecond);rect.Width=Math.Max(6,(int)Math.Round(previewDuration*PixelsPerSecond));}bool audio=content.audio_orders.ContainsKey(child.track_id);using(var fill=new SolidBrush(audio?Color.FromArgb(83,151,188):Color.FromArgb(222,145,75)))g.FillRectangle(fill,rect);using(var edge=new Pen(Color.FromArgb(245,230,221)))g.DrawRectangle(edge,rect);TextRenderer.DrawText(g,child.name,Font,new Rectangle(rect.X+5,rect.Y+2,Math.Max(1,rect.Width-10),rect.Height-4),Color.FromArgb(22,24,28),TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter);}
   int foot=Header+VideoRows*Row;using(var b=new SolidBrush(Color.FromArgb(44,49,55)))g.FillRectangle(b,0,foot,Width,Row);TextRenderer.DrawText(g,"MEDIO",Font,new Rectangle(8,foot+5,LabelWidth-16,19),Color.FromArgb(255,177,89));using(var pen=new Pen(Color.FromArgb(255,177,89),2))g.DrawRectangle(pen,LabelWidth,foot+4,Math.Max(8,(int)Math.Round(outer.duration.Seconds*PixelsPerSecond)),Row-8);
  }
  protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(content==null)return;if(e.Y<Header){if(e.Button==MouseButtons.Left&&e.X>Width-85&&CloseRequested!=null)CloseRequested();return;}var hit=content.clips.LastOrDefault(c=>ChildRect(c).Contains(e.Location));if(hit==null)return;if(e.Button==MouseButtons.Right){string outerId=outer.id,childId=hit.id;var menu=new ContextMenuStrip();menu.Items.Add("Propiedades del componente…",null,(s,a)=>{if(EditChildRequested!=null)EditChildRequested(outerId,childId);});menu.Items.Add("Quitar del medio anidado",null,(s,a)=>{if(RemoveChildRequested!=null)RemoveChildRequested(outerId,childId);});menu.Closed+=(s,a)=>BeginInvoke((Action)(()=>menu.Dispose()));menu.Show(this,e.Location);return;}if(e.Button!=MouseButtons.Left)return;var bounds=ChildRect(hit);dragging=hit.id;dragMode=e.X>=bounds.Right-8?2:e.X<bounds.Left+8?1:0;origin=e.Location;oldStart=previewStart=hit.timeline_start.Seconds;oldDuration=previewDuration=hit.duration.Seconds;previewTrack=hit.track_id;Capture=true;Invalidate();}
  protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging==null)return;double delta=(e.X-origin.X)/PixelsPerSecond;double step=1.0/60;delta=Math.Round(delta/step)*step;if(dragMode==2)previewDuration=Math.Max(step,oldDuration+delta);else if(dragMode==1){previewStart=Math.Max(0,Math.Min(oldStart+oldDuration-step,oldStart+delta));previewDuration=oldDuration+oldStart-previewStart;}else{previewStart=Math.Max(0,oldStart+delta);int row=e.Y<Header?-1:(e.Y-Header)/Row;if(row>VideoRows)row--;else if(row==VideoRows)row=-1;if(row>=0&&row<lanes.Count){var source=content.clips.Find(c=>c.id==dragging);bool audio=content.audio_orders.ContainsKey(source.track_id);if(audio==content.audio_orders.ContainsKey(lanes[row]))previewTrack=lanes[row];}}Invalidate();}
  protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(dragging==null)return;string id=dragging;dragging=null;Capture=false;double start=previewStart,duration=previewDuration;string track=previewTrack;Invalidate();if(Math.Abs(start-oldStart)>0.00001||Math.Abs(duration-oldDuration)>0.00001||track!=content.clips.Find(c=>c.id==id).track_id)if(ChildChanged!=null)ChildChanged(outer.id,id,start,duration,track);}
 }
}

