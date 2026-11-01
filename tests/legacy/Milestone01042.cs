using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Prometheus;

class TestTimeline01042:Timeline {
 public Keys SimulatedModifiers;
 protected override Keys CurrentModifiers { get { return SimulatedModifiers; } }
 public void Down(int x,int y){OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,x,y,0));}
 public void Move(int x,int y){OnMouseMove(new MouseEventArgs(MouseButtons.Left,0,x,y,0));}
 public void Up(int x,int y){OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,x,y,0));}
}
class Milestone01042 {
 [STAThread] static int Main(string[] args){try{
  string folder=Path.GetFullPath(args[0]);Directory.CreateDirectory(folder);
  string path=Path.Combine(folder,"frame.png");using(var image=new Bitmap(160,90)){using(var drawing=Graphics.FromImage(image))drawing.Clear(Color.CornflowerBlue);image.Save(path,ImageFormat.Png);}
  var project=Store.New();var tracks=project.Active.tracks.Where(t=>t.kind=="video").OrderBy(t=>t.order).ToList();
  var first=Media.Inspect(path,CancellationToken.None);project.assets.Add(first);Media.Insert(project,first.id,tracks[0].id,0);var original=project.Active.clips.Last();original.duration=Rational.Frame(10);
  var second=Media.Inspect(path,CancellationToken.None);var overlaid=FrameInsertion01034.Overlay(project,second,3);
  if(overlaid.track_id!=tracks[1].id||Math.Abs(original.End-10)>0.000001||Math.Abs(overlaid.timeline_start.Seconds-3)>0.000001)throw new Exception("Superponer alteró el montaje o eligió otra pista");
  var third=Media.Inspect(path,CancellationToken.None);var extra=FrameInsertion01034.Overlay(project,third,3);
  if(extra.track_id==overlaid.track_id||project.Active.tracks.Count(t=>t.kind=="video")!=3)throw new Exception("No creó una pista libre para el segundo fotograma");
  Store.Validate(project);
  var gapProject=Store.New();var gapAsset=Media.Inspect(path,CancellationToken.None);gapProject.assets.Add(gapAsset);var v1=gapProject.Active.tracks.First(t=>t.kind=="video");Media.Insert(gapProject,gapAsset.id,v1.id,0);var left=gapProject.Active.clips.Last();left.duration=Rational.Frame(2);Media.Insert(gapProject,gapAsset.id,v1.id,3);var right=gapProject.Active.clips.Last();right.duration=Rational.Frame(2);
  using(var timeline=new TestTimeline01042{Width=1000,Height=450,Project=gapProject}){
   int y=timeline.TrackTop(v1.id)+timeline.TrackHeight(v1.id)/2,x=(int)timeline.X(2.5);
   timeline.Down(x,y);timeline.Up(x,y);if(timeline.SelectedGapTrack!=v1.id)throw new Exception("No seleccionó el hueco con un clic");
   timeline.Down(x,y);timeline.Up(x,y);if(timeline.SelectedGapTrack!=null)throw new Exception("El segundo clic no deseleccionó el hueco");
   timeline.Down(x,y);timeline.Move((int)timeline.X(3.5),y);timeline.Up((int)timeline.X(3.5),y);
   if(timeline.SelectedGapTrack!=null||!timeline.Selection.Contains(right.id))throw new Exception("El arrastre desde el hueco no formó selección múltiple");
   int videoAudioBorder=gapProject.Active.tracks.Where(t=>t.kind=="audio").Select(t=>timeline.TrackTop(t.id)).Min();
   if(timeline.TrackAt(videoAudioBorder)==null||timeline.TrackAt(videoAudioBorder-1)==null)throw new Exception("Quedó una zona muerta entre vídeo y audio");
   double movedAt=-1;string movedTrack=null;timeline.MoveClip=(id,at,track)=>{movedAt=at;movedTrack=track;};timeline.SimulatedModifiers=Keys.Shift;
   int from=(int)timeline.X(4),targetY=timeline.TrackTop(gapProject.Active.tracks.Where(t=>t.kind=="video").OrderByDescending(t=>t.order).First().id)+20;
   timeline.Down(from,y);timeline.Move(from+60,targetY);timeline.Up(from+60,targetY);
   if(Math.Abs(movedAt-3)>0.000001||movedTrack==v1.id)throw new Exception("Mayús no mantuvo el tiempo al cambiar de pista");
  }
  using(var monitor=new MonitorPanelView{Width=640,Height=400}){
   monitor.SetCanvasSize(1920,1080);monitor.PerformLayout();monitor.Canvas.PerformLayout();
   var focus=new Point(180,130);var before=monitor.Canvas.ImageBounds();double relative=(focus.X-before.Left)/before.Width;
   monitor.Canvas.ZoomAt(110,focus);var after=monitor.Canvas.ImageBounds();
   if(Math.Abs(relative-(focus.X-after.Left)/after.Width)>0.01)throw new Exception("El zoom del monitor no conservó el punto bajo el cursor");
   monitor.SetControlsVisible(false);if(monitor.ControlsVisible)throw new Exception("No ocultó la barra del monitor");
   monitor.SetControlsVisible(true);monitor.SetPreviewFps(60);monitor.SetPreviewQuality("Alta");
   if(!monitor.ControlsVisible||monitor.PreviewFps!=60||monitor.PreviewQuality!="Alta")throw new Exception("El menú del monitor no actualizó sus controles");
  }
  Console.WriteLine("PASS 0.10.42 overlay/gap/marquee/track-border/shift-vertical");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
