using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class PreviewTimeline0292:Timeline {
 public void Down(int x,int y){OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,x,y,0));}
 public void Drag(int x,int y){OnMouseMove(new MouseEventArgs(MouseButtons.Left,0,x,y,0));}
 public void Up(int x,int y){OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,x,y,0));}
 public List<Clip> Preview(){return (List<Clip>)typeof(Timeline).GetMethod("TrimPreviewClips",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(this,null);}
}
class TrimPreviewTests0292 {
 static int count;
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);count++;}
 static Clip Make(string id,string track,double start,double duration){return new Clip{id=id,name=id,track_id=track,asset_id="asset",stream_id=id.EndsWith("a")?"audio":"video",timeline_start=Rational.Time(start),duration=Rational.Time(duration),source_in=Rational.Time(start),speed=Rational.Time(1)};}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();
  using(var form=new Form{Width=1000,Height=450})using(var timeline=new PreviewTimeline0292{Dock=DockStyle.Fill,Snap=false,Project=Store.New()}){
   form.Controls.Add(timeline);form.Show();Application.DoEvents();
   var video=timeline.Tracks().First(t=>t.kind=="video");var audio=timeline.Tracks().First(t=>t.kind=="audio");
   timeline.Project.assets.Add(new Asset{id="asset",name="Fixture",source=new MediaSource{kind="local",uri="missing.mp4"},presentation_origin=Rational.Time(0),streams={new StreamInfo{id="video",ordinal=0,kind="video",codec="h264",pts_origin=Rational.Time(0),time_base=new Rational(1,60),duration=Rational.Time(10)},new StreamInfo{id="audio",ordinal=1,kind="audio",codec="aac",pts_origin=Rational.Time(0),time_base=new Rational(1,48000),duration=Rational.Time(10)}}});
   timeline.Project.Active.clips.AddRange(new[]{Make("lv",video.id,1,2),Make("rv",video.id,3,2),Make("la",audio.id,1,2),Make("ra",audio.id,3,2)});
   timeline.Project.Active.link_groups.Add(new LinkGroup{id="g1",clip_ids={"lv","la"}});
   timeline.Project.Active.link_groups.Add(new LinkGroup{id="g2",clip_ids={"rv","ra"}});
   var lv=timeline.Project.Active.clips.First(c=>c.id=="lv");int seam=(int)timeline.X(3);
   int upper=timeline.TrackTop(video.id)+8,bottom=timeline.TrackTop(video.id)+timeline.TrackHeight(video.id)-4;
   timeline.Down(seam-1,upper);timeline.Drag(seam-25,upper);
   var previews=timeline.Preview();
   Check(previews.Count==2&&previews.All(c=>c.duration.Seconds<2),"Trim en vivo reduce vídeo y audio vinculados");
   Check(timeline.Project.Active.clips.First(c=>c.id=="lv").duration.Seconds==2,"el modelo permanece intacto mientras se arrastra");
   timeline.Up(seam-25,upper);
   timeline.Down(seam,bottom);timeline.Drag(seam+20,bottom);
   previews=timeline.Preview();
   Check(previews.Count==4,"Roll Edit en vivo representa ambos lados en las dos pistas");
   Check(previews.First(c=>c.id=="lv").End==previews.First(c=>c.id=="la").End,"ambos cortes mostrados permanecen alineados");
   using(var image=new Bitmap(timeline.Width,timeline.Height))timeline.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));
   timeline.Up(seam+20,bottom);
   var videoLeft=timeline.Project.Active.clips.First(c=>c.id=="lv");
   var videoRight=timeline.Project.Active.clips.First(c=>c.id=="rv");
   videoLeft.duration=Rational.Time(2.5);videoRight.timeline_start=Rational.Time(3.5);videoRight.source_in=Rational.Time(3.5);videoRight.duration=Rational.Time(1.5);
   using(var image=new Bitmap(timeline.Width,timeline.Height)){
    timeline.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));
    var pixel=image.GetPixel((int)timeline.X(3.5),timeline.TrackTop(video.id)+7);
    Check(pixel.R>200&&pixel.G>100,"un corte separado muestra una marca J/L");
   }
  }
  Console.WriteLine("PASS TOTAL "+count);
 }
}
