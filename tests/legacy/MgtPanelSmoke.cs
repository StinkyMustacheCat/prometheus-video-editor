using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Prometheus;
class MgtPanelSmoke {
 [STAThread]static void Main(string[] args){
  Application.EnableVisualStyles();var project=Store.Load(args[0]);
  var panel=new MgtControlsPanelView{Size=new Size(420,600)};
  panel.ShowClip(project,project.Active.clips.First(c=>project.assets.Any(a=>a.id==c.asset_id&&MgtMedia.IsMgt(a))));
  using(var bitmap=new Bitmap(panel.Width,panel.Height)){panel.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(args[1]);}
  Console.WriteLine("FIELDS "+panel.Controls.Count);
 }
}
