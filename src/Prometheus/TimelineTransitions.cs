using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public partial class Timeline {
  public Action<string,string,Point> TransitionContext;
  public Action<string,string,Point> JunctionContext;
  public Action<string> TransitionSelected;
  public Action<string,int> TransitionResizeRequested;
  public Action<string,string,string> DropTransition;
  public string SelectedTransitionId;
  public string SelectedJunctionLeft,SelectedJunctionRight;
  string resizingTransitionId;
  int resizingTransitionEdge,resizingTransitionFrames;
  string dropTransition,dropTransitionLeft,dropTransitionRight;
  void ClearJunctionSelection(){SelectedJunctionLeft=SelectedJunctionRight=null;}
  bool TryJunctionPointer(MouseEventArgs e){
   if(e.Button!=MouseButtons.Left&&e.Button!=MouseButtons.Right||e.X<headerWidth||e.Y<32)return false;
   var track=TrackAt(e.Y);var pair=RollPairAt(e.Location,track);
   if(pair==null)return false;
   SelectedJunctionLeft=pair.Item1.id;SelectedJunctionRight=pair.Item2.id;
   Selected=null;Selection.Clear();SelectedGapTrack=null;SelectedTransitionId=null;
   if(SelectionChanged!=null)SelectionChanged(null);
   if(e.Button==MouseButtons.Right){if(JunctionContext!=null)JunctionContext(pair.Item1.id,pair.Item2.id,e.Location);}
   else BeginRollGesture(pair.Item1,pair.Item2,track,e);
   Invalidate();return true;
  }
  public Tuple<Clip,Clip> JunctionNear(Point point,Track track,float tolerance=12){
   if(Project==null||track==null||point.X<headerWidth)return null;
   var clips=Project.Active.clips.Where(c=>c.track_id==track.id).OrderBy(c=>c.timeline_start.Seconds).ToList();
   for(int i=0;i+1<clips.Count;i++){
    var left=clips[i];var right=clips[i+1];
    if(Math.Abs(left.End-right.timeline_start.Seconds)>0.000001)continue;
    var bounds=ClipBounds(left);
    if(Math.Abs(point.X-X(left.End))<=tolerance&&point.Y>=bounds.Top&&point.Y<=bounds.Bottom)return Tuple.Create(left,right);
   }
   return null;
  }
  RectangleF TransitionBadge(Clip left){var bounds=ClipBounds(left);float rollTop=bounds.Bottom-Math.Max(12,Math.Min(20,bounds.Height/3));float top=Math.Max(bounds.Top+1,Math.Min(bounds.Top+9,rollTop-10));return new RectangleF(X(left.End)-9,top,18,10);}
  // The span is kept separate from the badge: later it can supply resize hit
  // targets without changing how the lower Roll Edit semicircle behaves.
  public static Tuple<double,double> TransitionTimeSpan(TransitionInstance transition){return TransitionTimeSpan(transition,transition.duration_frames);}
  static Tuple<double,double> TransitionTimeSpan(TransitionInstance transition,int frames){double cut=transition.cut_frame/60.0,duration=frames/60.0;return transition.transition_id==TransitionContracts.Dissolve?Tuple.Create(cut-duration,cut):Tuple.Create(cut-duration/2,cut+duration/2);}
  RectangleF TransitionSpan(TransitionInstance transition,Clip left){var time=TransitionTimeSpan(transition,transition.id==resizingTransitionId?resizingTransitionFrames:transition.duration_frames);var bounds=ClipBounds(left);float x=X(time.Item1),right=X(time.Item2);return new RectangleF(x,bounds.Top+2,Math.Max(1,right-x),6);}
  Tuple<TransitionInstance,int> TransitionHandleAt(Point point){
   if(Project==null||point.X<headerWidth)return null;
   foreach(var transition in Project.Active.transitions){
    var left=Project.Active.clips.Find(c=>c.id==transition.left_clip_id);
    if(left==null||!TrackVisible(left.track_id))continue;
    var track=Project.Active.tracks.Find(t=>t.id==left.track_id);if(track==null||track.locked)continue;
    var bounds=ClipBounds(left);if(point.Y<bounds.Top-4||point.Y>bounds.Top+9)continue;
    var span=TransitionSpan(transition,left);float start=span.Left,end=span.Right;
    if(point.X>=start-10&&point.X<=start+2)return Tuple.Create(transition,-1);
    if(transition.transition_id!=TransitionContracts.Dissolve&&end-start>=18&&point.X>=end-2&&point.X<=end+10)return Tuple.Create(transition,1);
   }
   return null;
  }
  bool UpdateTransitionResize(MouseEventArgs e){
   if(resizingTransitionId==null)return false;
   var transition=Project.Active.transitions.Find(t=>t.id==resizingTransitionId);
   if(transition==null){resizingTransitionId=null;Capture=false;return true;}
   var left=Project.Active.clips.Find(c=>c.id==transition.left_clip_id);var right=Project.Active.clips.Find(c=>c.id==transition.right_clip_id);
   if(left==null||right==null){resizingTransitionId=null;Capture=false;return true;}
   int max=TransitionContracts.MaxLinkedFrames(Project,Project.Active,left,right,transition.mgt_resource_id??transition.transition_id);
   double cut=transition.cut_frame/60.0,time=TimeAt(e.X);
   int frames=(int)Math.Round((resizingTransitionEdge<0?cut-time:time-cut)*(transition.transition_id==TransitionContracts.Dissolve?60:120));
   resizingTransitionFrames=Math.Max(2,Math.Min(Math.Max(2,max),frames));
   Invalidate();return true;
  }
  bool FinishTransitionResize(MouseEventArgs e){
   if(resizingTransitionId==null)return false;
   string id=resizingTransitionId;int frames=resizingTransitionFrames;resizingTransitionId=null;Capture=false;
   var transition=Project.Active.transitions.Find(t=>t.id==id);
   if(transition!=null&&frames!=transition.duration_frames&&TransitionResizeRequested!=null)TransitionResizeRequested(id,frames);
   Invalidate();return true;
  }
  TransitionInstance TransitionBadgeAt(Point point){
   var track=TrackAt(point.Y);var pair=JunctionNear(point,track);return pair!=null&&TransitionBadge(pair.Item1).Contains(point)?TransitionContracts.At(Project.Active,pair.Item1.id,pair.Item2.id):null;
  }
  bool TryTransitionPointer(MouseEventArgs e){
   if(e.X<headerWidth||e.Y<32||e.Button!=MouseButtons.Left&&e.Button!=MouseButtons.Right)return false;
   var handle=TransitionHandleAt(e.Location);
   if(handle!=null){
    if(e.Button==MouseButtons.Right){if(TransitionContext!=null)TransitionContext(handle.Item1.left_clip_id,handle.Item1.right_clip_id,e.Location);return true;}
    if(e.Button==MouseButtons.Left){
     var handleTransition=handle.Item1;Selected=null;Selection.Clear();SelectedGapTrack=null;
     if(TransitionSelected!=null)TransitionSelected(handleTransition.id);
     resizingTransitionId=handleTransition.id;resizingTransitionEdge=handle.Item2;resizingTransitionFrames=handleTransition.duration_frames;
     Capture=true;Invalidate();return true;
    }
   }
   var track=TrackAt(e.Y);var pair=JunctionNear(e.Location,track);
   if(pair==null)return false;
   if(e.Button==MouseButtons.Right&&TransitionContext!=null){TransitionContext(pair.Item1.id,pair.Item2.id,e.Location);return true;}
   var transition=TransitionContracts.At(Project.Active,pair.Item1.id,pair.Item2.id);
   if(e.Button==MouseButtons.Left&&transition!=null&&(TransitionBadge(pair.Item1).Contains(e.Location)||TransitionSpan(transition,pair.Item1).Contains(e.Location))){
    Selected=null;Selection.Clear();SelectedGapTrack=null;if(TransitionSelected!=null)TransitionSelected(transition.id);Invalidate();return true;
   }
   return false;
  }
  void DrawTransitionHandle(Graphics g,float x,float cornerY,bool selected,bool left){
   var fill=selected?Color.FromArgb(219,226,234):Color.FromArgb(151,162,175);
   var state=g.Save();g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
   using(var brush=new SolidBrush(fill))g.FillPie(brush,x-9,cornerY-9,18,18,left?90:0,90);
   using(var border=new Pen(Color.FromArgb(44,49,57),1.2f)){
    g.DrawArc(border,x-9,cornerY-9,18,18,left?90:0,90);
    g.DrawLine(border,left?x-9:x,cornerY,left?x:x+9,cornerY);
    g.DrawLine(border,x,cornerY,x,cornerY+9);
   }
   g.Restore(state);
  }
  void DrawTransitionBadges(Graphics g){
   if(Project==null)return;
   foreach(var transition in Project.Active.transitions){
    var left=Project.Active.clips.Find(c=>c.id==transition.left_clip_id);if(left==null||!TrackVisible(left.track_id))continue;
    var right=Project.Active.clips.Find(c=>c.id==transition.right_clip_id);if(right==null)continue;
    var track=Project.Active.tracks.Find(t=>t.id==left.track_id);if(track==null)continue;
    RectangleF rect=TransitionBadge(left),span=TransitionSpan(transition,left);if(rect.Right<headerWidth||rect.Left>Width)continue;
    if(Zoom<5&&transition.id!=SelectedTransitionId&&transition.id!=resizingTransitionId&&dropTransitionLeft!=left.id)continue;
    var state=g.Save();g.SetClip(TrackViewport(track.kind),System.Drawing.Drawing2D.CombineMode.Intersect);
    bool selected=transition.id==SelectedTransitionId;
    Color strip=selected?Color.FromArgb(255,174,83):transition.enabled?Color.FromArgb(125,190,208):Color.FromArgb(105,109,115);
    var range=new RectangleF(span.X,span.Y,span.Width,Math.Max(5,ClipBounds(left).Height-RollHandleRadius(track)-4));
    using(var wash=new SolidBrush(Color.FromArgb(selected?66:42,strip)))g.FillRectangle(wash,range);
    using(var fill=new SolidBrush(Color.FromArgb(selected?175:138,strip)))g.FillRectangle(fill,span);
    using(var edge=new Pen(Color.FromArgb(selected?235:205,strip),1)){g.DrawLine(edge,span.Left,span.Top,span.Right,span.Top);if(span.Width>=7){g.DrawLine(edge,span.Left,span.Top,span.Left,span.Bottom);g.DrawLine(edge,span.Right,span.Top,span.Right,span.Bottom);}}
    float handleCorner=ClipBounds(left).Top+1;
    DrawTransitionHandle(g,span.Left,handleCorner,selected||resizingTransitionId==transition.id,true);
    if(span.Width>=18)DrawTransitionHandle(g,span.Right,handleCorner,selected||resizingTransitionId==transition.id,false);
    using(var fill=new SolidBrush(selected?Color.FromArgb(255,156,44):transition.enabled?Color.FromArgb(50,123,151):Color.FromArgb(70,73,77)))g.FillRectangle(fill,rect);
    using(var border=new Pen(selected?Color.FromArgb(255,222,172):Color.FromArgb(210,181,220,230)))g.DrawRectangle(border,rect.X,rect.Y,rect.Width,rect.Height);
    using(var symbol=new Font("Segoe UI Symbol",7,FontStyle.Bold))TextRenderer.DrawText(g,"↔",symbol,Rectangle.Round(rect),Color.WhiteSmoke,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
    g.Restore(state);
   }
   if(dropTransition!=null&&dropTransitionLeft!=null){var left=Project.Active.clips.Find(c=>c.id==dropTransitionLeft);if(left!=null){var rect=TransitionBadge(left);using(var border=new Pen(Color.FromArgb(255,186,221),2))g.DrawRectangle(border,rect.X,rect.Y,rect.Width,rect.Height);}}
  }
  bool TryTransitionDrop(DragEventArgs e,Point point,Track track){
   string id=e.Data.GetDataPresent("Prometheus.TransitionId")?e.Data.GetData("Prometheus.TransitionId") as string:null;
   if(id==null)return false;
   var pair=JunctionNear(point,track,16);
   if(pair!=null&&track!=null&&!track.locked&&TransitionContracts.MaxLinkedFrames(Project,Project.Active,pair.Item1,pair.Item2,id)>=2){
    dropTransition=id;dropTransitionLeft=pair.Item1.id;dropTransitionRight=pair.Item2.id;e.Effect=DragDropEffects.Copy;
    var contract=TransitionContracts.All.FirstOrDefault(c=>c.id==id);var mgt=Project.mgt_transitions.FirstOrDefault(r=>r.id==id);if(DropFeedback!=null)DropFeedback("Aplicar "+(mgt==null?contract.name:mgt.name)+" en el corte");
   }
   Invalidate();return true;
  }
 }
}
