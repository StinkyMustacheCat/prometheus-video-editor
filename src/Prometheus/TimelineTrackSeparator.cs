using System;
using System.Drawing;
using System.Linq;

namespace Prometheus {
 public partial class Timeline {
  void DrawTrackTypeSeparator(Graphics graphics){
   int y=SeparatorY();if(y<32)return;
   using(var line=new Pen(Color.FromArgb(108,153,182),1))graphics.DrawLine(line,0,y,Width,y);
  }
 }
}
