using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Prometheus;

class ShelfRangeUiTests0316 {
 [STAThread]static void Main(){
  Application.EnableVisualStyles();
  var asset=WebMedia.FromMetadata("https://www.youtube.com/watch?v=demo0316","{\"id\":\"demo0316\",\"title\":\"Ensayo de rango\",\"duration\":100,\"fps\":60,\"width\":1280,\"height\":720,\"vcodec\":\"h264\",\"acodec\":\"aac\"}");
  var project=Store.New();ShelfStore.Add(project,asset);var item=ShelfStore.Read(project).Single();ShelfStore.SetRange(project,item.id,20,25);item=ShelfStore.Read(project).Single();
  if(item.range_in!=20||item.range_out!=25)throw new Exception("El rango de Bandeja no se guardó");
  using(var window=new ShelfWindow()){
   window.SetItems(new[]{item});window.Show();Application.DoEvents();
   var list=window.Controls.OfType<ListView>().Single();
   if(!list.Items[0].SubItems[2].Text.Contains("–"))throw new Exception("El rango no aparece en Bandeja");
   using(var image=new Bitmap(window.Width,window.Height)){window.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save("shelf-range-0316.png");}
   window.Close();
  }
  Console.WriteLine("PASS tramo elegido y visible en Bandeja");
 }
}
