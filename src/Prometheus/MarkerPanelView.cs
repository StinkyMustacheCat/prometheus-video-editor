using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public sealed class MarkerPanelView:Panel {
  readonly ListBox list=new ListBox();
  public double CurrentTime;
  public Action<double> AddRequested,SeekRequested;
  public Action<string> RenameRequested,DeleteRequested;
  public MarkerPanelView(){
   Dock=DockStyle.Fill;BackColor=Color.FromArgb(36,38,42);
   var bar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=39,WrapContents=false,Padding=new Padding(5,4,0,0)};
   bar.Controls.Add(Button("+ Marcador",()=>{if(AddRequested!=null)AddRequested(CurrentTime);}));
   bar.Controls.Add(Button("Renombrar",()=>{var selected=list.SelectedItem as SequenceMarker;if(selected!=null&&RenameRequested!=null)RenameRequested(selected.Id);}));
   bar.Controls.Add(Button("Eliminar",()=>{var selected=list.SelectedItem as SequenceMarker;if(selected!=null&&DeleteRequested!=null)DeleteRequested(selected.Id);}));
   list.Dock=DockStyle.Fill;list.BorderStyle=BorderStyle.None;list.BackColor=Color.FromArgb(27,28,31);list.ForeColor=Color.Gainsboro;list.DrawMode=DrawMode.OwnerDrawFixed;list.ItemHeight=28;
   list.DrawItem+=(s,e)=>{
    e.DrawBackground();if(e.Index<0)return;
    var marker=(SequenceMarker)list.Items[e.Index];
    using(var fill=new SolidBrush(Color.FromArgb(marker.ColorArgb)))e.Graphics.FillRectangle(fill,e.Bounds.Left+8,e.Bounds.Top+8,11,11);
    TextRenderer.DrawText(e.Graphics,Timeline.TimeLabel(marker.Seconds)+"  "+marker.Name,list.Font,new Rectangle(e.Bounds.Left+26,e.Bounds.Top,e.Bounds.Width-28,e.Bounds.Height),Color.Gainsboro,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
    e.DrawFocusRectangle();
   };
   list.DoubleClick+=(s,e)=>{var selected=list.SelectedItem as SequenceMarker;if(selected!=null&&SeekRequested!=null)SeekRequested(selected.Seconds);};
   Controls.Add(list);Controls.Add(bar);
  }
  Button Button(string text,Action action){var button=new Button{Text=text,Height=29,AutoSize=true,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(49,51,56),ForeColor=Color.Gainsboro,Margin=new Padding(3,0,3,0)};button.FlatAppearance.BorderColor=Color.FromArgb(66,68,74);button.Click+=(s,e)=>action();return button;}
  public void SetMarkers(Sequence sequence){
   string selected=(list.SelectedItem as SequenceMarker)==null?null:((SequenceMarker)list.SelectedItem).Id;
   list.BeginUpdate();list.Items.Clear();
   foreach(var marker in Markers.Read(sequence))list.Items.Add(marker);
   if(selected!=null){for(int i=0;i<list.Items.Count;i++)if(((SequenceMarker)list.Items[i]).Id==selected){list.SelectedIndex=i;break;}}
   list.EndUpdate();
  }
 }
}
