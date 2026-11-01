using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class MediaViewTests03142 {
 static object Field(object o,string name){return o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
 static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();
  using(var form=new Form{Width=620,Height=460})using(var view=new MediaPanelView()){
   form.Controls.Add(view);form.Show();Application.DoEvents();
   var button=(Button)Field(view,"viewButton");
   var header=(Panel)Field(view,"listHeader");
   var pane=(TableLayoutPanel)Field(view,"viewPane");
   for(int i=1;i<=3;i++){
    typeof(Control).GetMethod("OnMouseDown",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(button,new object[]{new MouseEventArgs(MouseButtons.Left,1,12,12,0)});
    typeof(Control).GetMethod("OnMouseUp",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(button,new object[]{new MouseEventArgs(MouseButtons.Left,1,12,12,0)});
    Application.DoEvents();
    int mode=(int)Field(view,"viewMode");Check(mode==i%3,"clic rápido: vista "+mode);
    if(mode==2){Check(header.Visible&&pane.RowStyles[0].Height==27&&view.Library.ItemHeight==26,"lista compacta visible con encabezado propio");Check(view.Library.Top>=header.Bottom,"la lista no cubre el encabezado");}
   }
   typeof(MediaPanelView).GetMethod("SetViewMode",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(view,new object[]{2});
   using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save("media-view-03142.png");}
   form.Close();
  }
 }
}
