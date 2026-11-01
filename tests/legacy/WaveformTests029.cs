using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Prometheus;

class WaveformTests029 {
 static int count;
 static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);count++;}
 [STAThread]static void Main(string[] args){
  Tools.Root=Path.GetFullPath(args[0]);
  string cache=Path.GetFullPath(args[1]);Directory.CreateDirectory(cache);
  var asset=Media.Inspect(Path.Combine(Tools.Root,"demo","voz.wav"),CancellationToken.None);
  var stream=asset.streams.First(s=>s.kind=="audio");
  var rep=Media.SelectRepresentation(asset,false);
  string key=WaveformCache.Identity(asset,stream,rep);
  var first=WaveformCache.LoadOrBuild(asset,stream,rep,cache,CancellationToken.None);
  Check(first.Key==key&&first.BinsPerSecond==200&&first.Count>100,"analiza audio en picos reutilizables");
  Check(first.Maximum.Zip(first.Minimum,(high,low)=>Math.Max(Math.Abs((int)high),Math.Abs((int)low))).Max()>100,"la onda contiene amplitud real");
  string cacheFile=WaveformCache.CachePath(cache,key);
  Check(File.Exists(cacheFile),"guarda la onda en caché local");
  var second=WaveformCache.LoadOrBuild(asset,stream,rep,cache,CancellationToken.None);
  Check(second.Count==first.Count&&second.Maximum.SequenceEqual(first.Maximum),"reutiliza la caché sin cambiar los picos");
  var video=Media.Inspect(Path.Combine(Tools.Root,"demo","video.mp4"),CancellationToken.None);
  var videoAudio=video.streams.First(s=>s.kind=="audio");
  var linked=WaveformCache.LoadOrBuild(video,videoAudio,Media.SelectRepresentation(video,false),cache,CancellationToken.None);
  Check(linked.Count>100,"también analiza el audio vinculado a video");
  var clip=new Clip{source_in=Rational.Time(2),timeline_start=Rational.Time(5),duration=Rational.Time(4),speed=Rational.Time(2)};
  Check(Math.Abs(Timeline.SourceTime(clip,6)-4)<0.00001,"onda respeta trim y velocidad del clip");
  using(var timeline=new Timeline{Width=900,Height=320,Project=Store.New()}){
   timeline.Project.assets.Add(asset);
   var track=timeline.Project.Active.tracks.First(t=>t.kind=="audio");
   Media.Insert(timeline.Project,asset.id,track.id,0);
   var placed=timeline.Project.Active.clips.First(c=>c.stream_id==stream.id);
   timeline.Waveforms[stream.id]=first;
   using(var image=new Bitmap(900,320))using(var plain=new Bitmap(900,320)){
    timeline.ShowWaveforms=false;
    timeline.DrawToBitmap(plain,new Rectangle(0,0,900,320));
    timeline.ShowWaveforms=true;
    timeline.DrawToBitmap(image,new Rectangle(0,0,900,320));
    var bounds=timeline.ClipBounds(placed);
    int changed=0;
    for(int x=Math.Max(0,(int)bounds.Left+12);x<Math.Min(900,(int)bounds.Right-12);x++)
     for(int y=Math.Max(0,(int)bounds.Top+20);y<Math.Min(320,(int)bounds.Bottom-3);y++)
      if(image.GetPixel(x,y)!=plain.GetPixel(x,y))changed++;
    Check(changed>10,"la onda aparece y puede ocultarse en la línea de tiempo");
    if(args.Length>3)image.Save(args[3]);
   }
  }
  typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  using(var form=new MainForm()){
   typeof(MainForm).GetField("waveformCacheDir",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,cache);
   form.Show();
   form.LoadFixture(Path.Combine(Tools.Root,"demo","Demostracion.prometheus.json"));
   var live=(Timeline)typeof(MainForm).GetField("timeline",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
   var until=DateTime.UtcNow.AddSeconds(8);
   while(live.Waveforms.Count<2&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(30);}
   Check(live.Waveforms.Count>=2,"la interfaz carga ondas en segundo plano para audio y video");
   var workspace=(PanelWorkspace)typeof(MainForm).GetField("workspace",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
   using(var menu=new ContextMenuStrip()){
    workspace.PopulatePanelMenu("timeline",menu);
    var toggle=menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(item=>item.Text=="Mostrar ondas de audio");
    Check(toggle!=null&&toggle.Checked,"el menú del panel ofrece mostrar ondas");
    toggle.Checked=false;Check(!live.ShowWaveforms,"el menú oculta las ondas");
    toggle.Checked=true;Check(live.ShowWaveforms,"el menú vuelve a mostrarlas");
   }
   if(args.Length>2)using(var capture=new Bitmap(form.Width,form.Height)){
    form.DrawToBitmap(capture,new Rectangle(0,0,capture.Width,capture.Height));capture.Save(args[2]);
   }
   form.Close();
  }
  Console.WriteLine("PASS TOTAL "+count);
 }
}
