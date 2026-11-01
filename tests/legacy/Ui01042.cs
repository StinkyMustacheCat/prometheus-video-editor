using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Prometheus;

class Ui01042 {
 static T Field<T>(object target,string name){return (T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
 [STAThread]static int Main(string[] args){try{
  Application.EnableVisualStyles();typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  string folder=Path.GetFullPath(args[0]);Directory.CreateDirectory(folder);string imagePath=Path.Combine(folder,"image.png"),projectPath=Path.Combine(folder,"animation.pthproj");
  using(var image=new Bitmap(160,90)){using(var drawing=Graphics.FromImage(image))drawing.Clear(Color.DarkSlateBlue);image.Save(imagePath,ImageFormat.Png);}
  var project=Store.New();var asset=Media.Inspect(imagePath,CancellationToken.None);project.assets.Add(asset);Media.Insert(project,asset.id,project.Active.tracks.First(t=>t.kind=="video").id,0);ClipKeyframes.Arm(project.Active.clips.Last(),"transform.scale");Store.Save(project,projectPath);
  using(var form=new MainForm()){
   var workspace=Field<PanelWorkspace>(form,"workspace");workspace.PersistenceEnabled=false;form.Show();Application.DoEvents();
   var split=Field<SplitContainer>(form,"effectSplit");var toggle=Field<Button>(form,"keyframeToggle");
   if(!split.Panel2Collapsed||toggle.Visible)throw new Exception("El panel temporal aparece sin animación");
   form.LoadFixture(projectPath);var timeline=Field<Timeline>(form,"timeline");timeline.Selected=project.Active.clips.Last().id;timeline.Selection.Add(timeline.Selected);
   typeof(MainForm).GetMethod("ShowClip",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);Application.DoEvents();
   if(split.Panel2Collapsed||!toggle.Visible)throw new Exception("El panel temporal no apareció al activar el reloj");
   toggle.PerformClick();if(!split.Panel2Collapsed||!toggle.Text.Contains("▴"))throw new Exception("No plegó el panel temporal");
   toggle.PerformClick();if(split.Panel2Collapsed||!toggle.Text.Contains("▾"))throw new Exception("No desplegó el panel temporal");
   form.Close();
  }
  using(var row=new ScrubNumber("Escala",100,0,400,0,200,1,0," %")){
   row.EnableKeyframe(true,false,()=>{},null,delta=>{});
   var previous=Field<Button>(row,"previous");var next=Field<Button>(row,"next");
   if(previous.Visible||next.Visible)throw new Exception("Quedaron flechas duplicadas junto al reloj");
  }
  Console.WriteLine("PASS 0.10.42 keyframe visibility/collapse/clock-only");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
