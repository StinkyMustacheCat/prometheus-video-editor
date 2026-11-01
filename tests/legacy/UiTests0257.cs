using System;
using System.Drawing;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Forms;
using Prometheus;

class ProbeTimeline0257:Timeline {
 public void Down(int x,int y){OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,x,y,0));}
 public void MoveTo(int x,int y){OnMouseMove(new MouseEventArgs(MouseButtons.Left,0,x,y,0));}
 public void Up(int x,int y){OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,x,y,0));}
 public void LeavePointer(){OnMouseLeave(EventArgs.Empty);}
}
class UiTests0257 {
 static int count;
 static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);count++;}
 [STAThread]static void Main(string[] args){
  Application.EnableVisualStyles();Tools.Root=System.IO.Path.GetFullPath(args[0]);
  var project=Store.Load(System.IO.Path.Combine(Tools.Root,"demo","Demostracion.prometheus.json"));
  var clip=project.Active.clips.First(c=>project.Active.tracks.Find(t=>t.id==c.track_id).kind=="video" && c.duration.Seconds>=2 && project.assets.Find(a=>a.id==c.asset_id).streams.Find(s=>s.id==c.stream_id).kind=="video");
  var track=project.Active.tracks.Find(t=>t.id==clip.track_id);
  using(var timeline=new ProbeTimeline0257{Project=project,Width=1100,Height=420,ActiveTool=Timeline.ToolMode.Razor}){
   int y=timeline.TrackTop(track.id)+12;
   int center=(int)timeline.X(clip.timeline_start.Seconds+1);
   string cutId=null;int cutCount=0;List<string> targets=null;int rangeCount=0;
   timeline.RazorCut=(id,time)=>{cutId=id;cutCount++;};
   timeline.RangeTrimAction=(ids,a,b)=>{targets=ids;rangeCount++;};
   timeline.Down(center,y);timeline.Up(center,y);
   Check(cutCount==1&&cutId==clip.id,"Razor click cuts only the clip under the pointer");
   int empty=(int)timeline.X(Math.Min(12,project.Active.Length+2));
   int emptyY=timeline.TrackTop(track.id)+12;
   if(project.Active.clips.Any(c=>c.track_id==track.id&&c.timeline_start.Seconds<=timeline.TimeAt(empty)&&c.End>timeline.TimeAt(empty)))empty=(int)timeline.X(project.Active.Length+3);
   timeline.Down(empty,emptyY);timeline.Up(empty,emptyY);
   Check(cutCount==1,"Razor click on empty timeline does not cut");
   timeline.Down(center,y);timeline.MoveTo(center+22,y);timeline.Up(center+22,y);
   Check(rangeCount==1&&targets.Count==1&&targets[0]==clip.id,"Razor drag from a clip trims only that clip");
   timeline.Down(empty,emptyY);timeline.MoveTo(empty-22,emptyY);timeline.Up(empty-22,emptyY);
   Check(rangeCount==2&&targets.Count==0,"Razor drag from empty timeline uses general range");
   timeline.ActiveTool=Timeline.ToolMode.Hand;timeline.MoveTo(center,y);
   using(var image=new Bitmap(timeline.Width,timeline.Height)){timeline.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));Check(image.GetPixel(center,12).R>100,"ruler shows mouse position for non-Razor tools");}
   timeline.LeavePointer();
  }
  using(var timeline=new ProbeTimeline0257{Project=project,Width=1100,Height=420,ActiveTool=Timeline.ToolMode.Slip}){
   int y=timeline.TrackTop(track.id)+12;
   int left=(int)timeline.ClipBounds(clip).Left+1;
   int right=(int)timeline.ClipBounds(clip).Right-2;
   int center=(int)(timeline.ClipBounds(clip).Left+timeline.ClipBounds(clip).Width/2);
   int slip=0,trim=0;bool? edge=null;
   timeline.SlipClip=(id,source)=>slip++;
   timeline.TrimClip=(id,isLeft,time)=>{trim++;edge=isLeft;};
   timeline.Down(left,y);timeline.MoveTo(left+18,y);timeline.Up(left+18,y);
   Check(trim==1&&slip==0&&edge==true,"Slip tool left edge trims instead of slipping");
   timeline.Down(right,y);timeline.MoveTo(right-18,y);timeline.Up(right-18,y);
   Check(trim==2&&slip==0&&edge==false,"Slip tool right edge trims instead of slipping");
   timeline.Down(center,y);timeline.MoveTo(center+22,y);timeline.Up(center+22,y);
   Check(slip==1&&trim==2,"Slip tool body still moves internal content");
  }
  var linked=project.Active.link_groups.First(g=>g.clip_ids.Contains(clip.id));
  string audioId=linked.clip_ids.First(id=>id!=clip.id);
  var editor=new Editor();editor.Reset(Store.Copy(project));
  Check(editor.CutSelection(new List<string>{clip.id},clip.timeline_start.Seconds+1,false),"single-clip Razor cut commits");
  Check(editor.Project.Active.clips.Count(c=>c.asset_id==clip.asset_id&&c.track_id==clip.track_id)==3,"single-clip Razor cut makes one additional video segment");
  Check(editor.Project.Active.clips.Count(c=>c.id==audioId)==1,"single-clip Razor cut keeps linked audio untouched");
  Store.Validate(editor.Project);
  editor.Reset(Store.Copy(project));
  Check(editor.RangeTrimSelection(new List<string>{clip.id},clip.timeline_start.Seconds+0.5,clip.timeline_start.Seconds+1),"range trim from clip commits");
  Check(editor.Project.Active.clips.Count(c=>c.id==audioId)==1&&editor.Project.Active.clips.Find(c=>c.id==audioId).duration.Seconds==2,"range trim from clip keeps linked audio untouched");
  Store.Validate(editor.Project);
  Console.WriteLine("PASS TOTAL "+count);
 }
}
