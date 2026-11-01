using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Prometheus;

class LongTimelineTests0293 {
 static int count;
 static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);count++;}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();
  using(var form=new Form{Width=1100,Height=450})using(var timeline=new Timeline{Dock=DockStyle.Fill,Project=Store.New()}){
   form.Controls.Add(timeline);form.Show();Application.DoEvents();
   var tracks=timeline.Project.Active.tracks.Where(t=>t.kind=="video").ToList();
   timeline.Project.assets.Add(new Asset{id="long",name="Largo",source=new MediaSource{kind="local",uri="missing.mp4"},presentation_origin=Rational.Time(0),streams={new StreamInfo{id="stream",ordinal=0,kind="video",codec="h264",pts_origin=Rational.Time(0),time_base=new Rational(1,60),duration=Rational.Time(100000)}}});
   timeline.Project.Active.clips.Add(new Clip{id="whole",name="Día completo",asset_id="long",stream_id="stream",track_id=tracks[0].id,timeline_start=Rational.Time(0),duration=Rational.Time(86400),source_in=Rational.Time(0)});
   timeline.Project.Active.clips.Add(new Clip{id="selected",name="Detalle",asset_id="long",stream_id="stream",track_id=tracks[1].id,timeline_start=Rational.Time(7200),duration=Rational.Time(10),source_in=Rational.Time(0)});
   Store.Validate(timeline.Project);
   timeline.Fit();
   Check(timeline.Zoom<0.1&&timeline.Scroll==0,"Ajustar a contenido muestra un proyecto de 24 horas");
   Check(timeline.X(86400)<timeline.Width,"el último fotograma queda dentro de la vista");
   using(var bitmap=new Bitmap(timeline.Width,timeline.Height))using(var graphics=Graphics.FromImage(bitmap)){
    double step=timeline.RulerStep(graphics);
    float label=graphics.MeasureString(Timeline.TimeLabel(86400),timeline.Font).Width;
    Check(step*timeline.Zoom>=label+12,"las cifras de la regla tienen espacio suficiente");
    timeline.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));
   }
   double wholeZoom=timeline.Zoom;
   timeline.Selection.Add("selected");timeline.FitSelection();
   Check(timeline.Zoom>wholeZoom*100&&timeline.Scroll<7200&&timeline.X(7200)>=timeline.HeaderWidth,"Ajustar a selección enfoca el clip elegido");
   Check(timeline.MinimumZoom()<0.01,"el límite de alejamiento se adapta a la duración");
   timeline.SetZoomAt(0,timeline.HeaderWidth+300);
   Check(Math.Abs(timeline.Zoom-timeline.MinimumZoom())<0.000001,"el zoom manual alcanza el límite amplio");
  }
  Console.WriteLine("PASS TOTAL "+count);
 }
}
