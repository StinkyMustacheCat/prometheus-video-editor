using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Prometheus;

class MilestoneTests1021 {
 [STAThread]static int Main(string[] args){try{
  string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);string imageFile=Path.Combine(output,"gap.png");using(var bitmap=new Bitmap(400,400))bitmap.Save(imageFile,ImageFormat.Png);
  var p=Store.New();var asset=Media.Inspect(imageFile,CancellationToken.None);p.assets.Add(asset);var track=p.Active.tracks.First(t=>t.kind=="video"&&t.order==0);Media.Insert(p,asset.id,track.id,0);p.Active.clips[0].duration=Rational.Frame(1);Media.Insert(p,asset.id,track.id,61.0/60);p.Active.clips.Last().duration=Rational.Frame(1);Store.Validate(p);
  using(var form=new Form{Width=900,Height=350})using(var timeline=new Timeline{Dock=DockStyle.Fill,Project=p,Zoom=55}){form.Controls.Add(timeline);form.Show();Application.DoEvents();
   using(var picture=new Bitmap(timeline.Width,timeline.Height)){timeline.DrawToBitmap(picture,new Rectangle(0,0,picture.Width,picture.Height));bool red=false;int x=(int)Math.Round(timeline.X(61.0/60));for(int y=32;y<picture.Height;y++)for(int dx=-3;dx<=3;dx++){int px=Math.Max(0,Math.Min(picture.Width-1,x+dx));Color color=picture.GetPixel(px,y);if(color.R>180&&color.G<110&&color.B<110)red=true;}if(!red)throw new Exception("No se señaló el hueco de un fotograma");}
   timeline.SelectedGapTrack=track.id;timeline.SelectedGapStart=1;timeline.SelectedGapEnd=61.0/60;p.Active.clips.Last().timeline_start=Rational.Frame(1);timeline.ClearInvalidEditCues();if(timeline.SelectedGapTrack!=null)throw new Exception("Persistió una selección fantasma después de cerrar el hueco");
   track.enabled=false;using(var picture=new Bitmap(timeline.Width,timeline.Height)){timeline.DrawToBitmap(picture,new Rectangle(0,0,picture.Width,picture.Height));Color pixel=picture.GetPixel((int)timeline.X(.5),30);if(pixel.R>180&&pixel.G>150&&pixel.B<140)throw new Exception("Una pista oculta mantiene la línea de render");}
   form.Close();
  }
  Console.WriteLine("PASS short-gap/ghost-selection/hidden-render-status");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
