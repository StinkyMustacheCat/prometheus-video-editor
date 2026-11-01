using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public partial class Timeline {
  readonly ContextMenuStrip markerMenu=new ContextMenuStrip();
  string markerDragging;
  double markerDragTime;
  int markerOriginX;
  public Action<double> AddMarkerRequested;
  public Action<string,double> MoveMarkerRequested;
  public Action<string> RenameMarkerRequested,DeleteMarkerRequested;
  public Action<string,int> MarkerColorRequested;
  public Action<double> MarkInRequested,MarkOutRequested;
  public Action ClearExportMarksRequested;

  SequenceMarker MarkerAt(int x){
   if(Project==null)return null;
   return Markers.Read(Project.Active).Where(marker=>Math.Abs(X(marker.Seconds)-x)<=7).OrderBy(marker=>Math.Abs(X(marker.Seconds)-x)).FirstOrDefault();
  }
  double AdjustMarker(double time){
   time=Rational.Frame(Math.Max(0,time)).Seconds;
   if(!Snap||Project==null)return time;
   var points=Project.Active.clips.SelectMany(c=>new[]{c.timeline_start.Seconds,c.End})
    .Concat(Markers.Read(Project.Active).Where(marker=>marker.Id!=markerDragging).Select(marker=>marker.Seconds))
    .Concat(new[]{Cursor,0.0});
   double nearest=points.OrderBy(value=>Math.Abs(value-time)).First();
   return Math.Abs(nearest-time)*Zoom<8?nearest:time;
  }
  bool TryStartMarkerGesture(MouseEventArgs e){
   if(Project==null||e.Y>=32||e.X<headerWidth)return false;
   var marker=MarkerAt(e.X);
   if(e.Button==MouseButtons.Right){ShowMarkerContext(marker,e.Location);return true;}
   if(e.Button!=MouseButtons.Left||marker==null)return false;
   markerDragging=marker.Id;markerDragTime=marker.Seconds;markerOriginX=e.X;Capture=true;Invalidate();return true;
  }
  bool UpdateMarkerDrag(MouseEventArgs e){
   if(markerDragging==null)return false;
   markerDragTime=AdjustMarker(TimeAt(e.X));
   Invalidate();return true;
  }
  bool FinishMarkerDrag(MouseEventArgs e){
   if(markerDragging==null)return false;
   string id=markerDragging;double time=markerDragTime;
   markerDragging=null;Capture=false;
   if(Math.Abs(e.X-markerOriginX)>2){if(MoveMarkerRequested!=null)MoveMarkerRequested(id,time);}
   else{var marker=Markers.Read(Project.Active).FirstOrDefault(m=>m.Id==id);if(marker!=null&&Seek!=null)Seek(marker.Seconds);}
   Invalidate();return true;
  }
  bool TryMarkerDoubleClick(MouseEventArgs e){
   if(Project==null||e.Y>=32||e.X<headerWidth)return false;
   var marker=MarkerAt(e.X);
   if(marker==null)return false;
   if(RenameMarkerRequested!=null)RenameMarkerRequested(marker.Id);
   return true;
  }
  void ShowMarkerContext(SequenceMarker marker,Point point){
   markerMenu.Close();markerMenu.Items.Clear();
   if(marker==null){double time=TimeAt(point.X);markerMenu.Items.Add("Añadir marcador aquí",null,(s,e)=>{if(AddMarkerRequested!=null)AddMarkerRequested(time);});
    markerMenu.Items.Add("Marcar entrada aquí",null,(s,e)=>{if(MarkInRequested!=null)MarkInRequested(time);});
    markerMenu.Items.Add("Marcar salida aquí",null,(s,e)=>{if(MarkOutRequested!=null)MarkOutRequested(time);});
    if(SequenceExportRange.Read(Project.Active).InFrame.HasValue||SequenceExportRange.Read(Project.Active).OutFrame.HasValue)markerMenu.Items.Add("Borrar entrada y salida",null,(s,e)=>{if(ClearExportMarksRequested!=null)ClearExportMarksRequested();});}
   else{
    string id=marker.Id;
    markerMenu.Items.Add("Ir a "+marker.Name,null,(s,e)=>{if(Seek!=null)Seek(marker.Seconds);});
    markerMenu.Items.Add("Renombrar marcador…",null,(s,e)=>{if(RenameMarkerRequested!=null)RenameMarkerRequested(id);});
    var colors=new ToolStripMenuItem("Color");
    foreach(var color in new[]{Tuple.Create("Naranja",unchecked((int)0xFFFFB95F)),Tuple.Create("Azul",unchecked((int)0xFF83C8E9)),Tuple.Create("Verde",unchecked((int)0xFF9CDDA4)),Tuple.Create("Rosa",unchecked((int)0xFFF1A3BB)),Tuple.Create("Rojo",unchecked((int)0xFFF39287))}){
     int argb=color.Item2;var item=new ToolStripMenuItem(color.Item1,null,(s,e)=>{if(MarkerColorRequested!=null)MarkerColorRequested(id,argb);});
     item.Checked=marker.ColorArgb==argb;colors.DropDownItems.Add(item);
    }
    markerMenu.Items.Add(colors);markerMenu.Items.Add(new ToolStripSeparator());
    markerMenu.Items.Add("Eliminar marcador",null,(s,e)=>{if(DeleteMarkerRequested!=null)DeleteMarkerRequested(id);});
   }
   markerMenu.Show(this,point);
  }
  void DrawSequenceMarkers(Graphics graphics){
   if(Project==null)return;
   foreach(var marker in Markers.Read(Project.Active)){
    float x=X(marker.Id==markerDragging?markerDragTime:marker.Seconds);
    if(x<headerWidth||x>Width)continue;
    var color=Color.FromArgb(marker.ColorArgb);
    using(var vertical=new Pen(Color.FromArgb(72,color)))graphics.DrawLine(vertical,x,32,x,Height);
    using(var fill=new SolidBrush(color))graphics.FillPolygon(fill,new[]{new PointF(x-6,22),new PointF(x+6,22),new PointF(x,31)});
    using(var border=new Pen(Color.FromArgb(25,27,30)))graphics.DrawPolygon(border,new[]{new PointF(x-6,22),new PointF(x+6,22),new PointF(x,31)});
   }
  }
  string MarkerTooltip(int x){var marker=MarkerAt(x);return marker==null?"":marker.Name+" · "+TimeLabel(marker.Seconds)+" · arrastra para mover";}
 }
}
