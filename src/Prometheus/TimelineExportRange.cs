using System;
using System.Drawing;
using System.Windows.Forms;

namespace Prometheus {
 public partial class Timeline {
  void DrawExportRange(Graphics graphics){
   if(Project==null)return;var marks=SequenceExportRange.Read(Project.Active);
   if(marks.Complete){float left=X(marks.InFrame.Value/60.0),right=X(marks.OutFrame.Value/60.0);
    using(var fill=new SolidBrush(Color.FromArgb(95,255,158,57)))graphics.FillRectangle(fill,left,24,Math.Max(0,right-left),7);
   }
   Action<long?,string> draw=(frame,label)=>{if(!frame.HasValue)return;float x=X(frame.Value/60.0);if(x<headerWidth||x>Width)return;
    using(var pen=new Pen(Color.FromArgb(150,255,190,103),1.5f))graphics.DrawLine(pen,x,18,x,Height);
    using(var fill=new SolidBrush(Color.FromArgb(240,255,166,57)))graphics.FillRectangle(fill,x-8,18,16,15);
    TextRenderer.DrawText(graphics,label,Font,new Rectangle((int)x-8,18,16,15),Color.FromArgb(28,29,32),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
   };
   draw(marks.InFrame,"I");draw(marks.OutFrame,"O");
  }
 }
}
