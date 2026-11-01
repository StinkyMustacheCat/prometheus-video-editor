using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public sealed class ShelfWindow:Form {
  readonly TextBox input=new TextBox(),search=new TextBox();
  readonly ListView list=new ListView();
  readonly ImageList images=new ImageList();
  readonly Label count=new Label();
  readonly List<ShelfItem> items=new List<ShelfItem>();
  readonly List<Button> selectedButtons=new List<Button>(),allButtons=new List<Button>();
  readonly Color background=Color.FromArgb(36,38,42),surface=Color.FromArgb(48,50,55),ink=Color.FromArgb(228,230,233);
  bool grid;
  public Action<string> AddRequested;
  public Action<string[]> AddFilesRequested;
  public Action<List<string>,bool> ImportRequested;
  public Action<List<string>> RemoveRequested;
  public Action<string,int> MoveRequested;
  public Action<string> RangeRequested;
  public ShelfWindow(){
   Text="Bandeja de medios";Width=690;Height=580;MinimumSize=new Size(470,370);StartPosition=FormStartPosition.Manual;BackColor=background;ForeColor=ink;Font=new Font("Segoe UI",9);ShowInTaskbar=false;KeyPreview=true;
   KeyDown+=(s,e)=>{if(e.Control&&e.KeyCode==Keys.V&&ActiveControl!=input&&ActiveControl!=search){try{if(Clipboard.ContainsText()&&AddRequested!=null)AddRequested(Clipboard.GetText());}catch(System.Runtime.InteropServices.ExternalException){}e.Handled=true;e.SuppressKeyPress=true;}};
   var head=new TableLayoutPanel{Dock=DockStyle.Top,Height=91,ColumnCount=2,RowCount=2,Padding=new Padding(9,8,9,0)};
   head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,90));
   head.RowStyles.Add(new RowStyle(SizeType.Absolute,54));head.RowStyles.Add(new RowStyle(SizeType.Absolute,27));
   input.Dock=DockStyle.Fill;input.Multiline=true;input.ScrollBars=ScrollBars.Vertical;input.BackColor=surface;input.ForeColor=ink;input.BorderStyle=BorderStyle.FixedSingle;input.Text="Pega uno o varios enlaces públicos…";input.ForeColor=Color.FromArgb(145,149,154);
   input.Enter+=(s,e)=>{if(input.Text=="Pega uno o varios enlaces públicos…"){input.Clear();input.ForeColor=ink;}};
   input.Leave+=(s,e)=>{if(String.IsNullOrWhiteSpace(input.Text)){input.Text="Pega uno o varios enlaces públicos…";input.ForeColor=Color.FromArgb(145,149,154);}};
   input.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter&&e.Control){e.SuppressKeyPress=true;AddText();}};
   var add=MakeButton("Añadir",AddText);add.Dock=DockStyle.Fill;
   search.Dock=DockStyle.Fill;search.BackColor=surface;search.ForeColor=ink;search.BorderStyle=BorderStyle.FixedSingle;search.TextChanged+=(s,e)=>Rebuild();
   var views=MakeButton("Vista",()=>{grid=!grid;Rebuild();});views.Dock=DockStyle.Fill;
   head.Controls.Add(input,0,0);head.Controls.Add(add,1,0);head.Controls.Add(search,0,1);head.Controls.Add(views,1,1);
   list.Dock=DockStyle.Fill;list.BackColor=background;list.ForeColor=ink;list.BorderStyle=BorderStyle.None;list.FullRowSelect=true;list.MultiSelect=true;list.HideSelection=false;list.View=View.Details;list.OwnerDraw=true;list.Columns.Add("Nombre",360);list.Columns.Add("Origen",105);list.Columns.Add("Duración / tramo",155);
   list.Resize+=(s,e)=>{if(list.Columns.Count>0)list.Columns[0].Width=Math.Max(170,list.ClientSize.Width-260);};
   list.DrawColumnHeader+=(s,e)=>{using(var brush=new SolidBrush(surface))e.Graphics.FillRectangle(brush,e.Bounds);TextRenderer.DrawText(e.Graphics,e.Header.Text,Font,new Rectangle(e.Bounds.Left+6,e.Bounds.Top+3,e.Bounds.Width-10,e.Bounds.Height-4),ink,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);};
   list.DrawSubItem+=(s,e)=>{using(var brush=new SolidBrush(e.Item.Selected?Color.FromArgb(67,76,87):background))e.Graphics.FillRectangle(brush,e.Bounds);int left=e.Bounds.Left+6;if(e.ColumnIndex==0&&e.Item.ImageIndex>=0&&e.Item.ImageIndex<images.Images.Count){e.Graphics.DrawImage(images.Images[e.Item.ImageIndex],left,e.Bounds.Top+2,42,24);left+=48;}TextRenderer.DrawText(e.Graphics,e.SubItem.Text,Font,new Rectangle(left,e.Bounds.Top+2,Math.Max(1,e.Bounds.Right-left-4),e.Bounds.Height-3),ink,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);};
   list.DrawItem+=(s,e)=>{if(list.View!=View.LargeIcon)return;using(var brush=new SolidBrush(e.Item.Selected?Color.FromArgb(67,76,87):background))e.Graphics.FillRectangle(brush,e.Bounds);if(e.Item.ImageIndex>=0&&e.Item.ImageIndex<images.Images.Count)e.Graphics.DrawImage(images.Images[e.Item.ImageIndex],e.Bounds.Left+34,e.Bounds.Top+3,74,42);TextRenderer.DrawText(e.Graphics,e.Item.Text,Font,new Rectangle(e.Bounds.Left+3,e.Bounds.Top+47,e.Bounds.Width-6,e.Bounds.Height-49),ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.EndEllipsis);};
   images.ImageSize=new Size(74,42);images.ColorDepth=ColorDepth.Depth32Bit;list.SmallImageList=images;list.LargeImageList=images;
   list.ItemDrag+=(s,e)=>{var row=e.Item as ListViewItem;if(row==null)return;var payload=new DataObject();payload.SetData(typeof(ListViewItem),row);payload.SetData("Prometheus.ShelfId",(string)row.Tag);list.DoDragDrop(payload,DragDropEffects.Copy|DragDropEffects.Move);};
   list.AllowDrop=true;list.DragEnter+=(s,e)=>e.Effect=e.Data.GetDataPresent(typeof(ListViewItem))?DragDropEffects.Move:e.Data.GetDataPresent(DataFormats.FileDrop)||WebDrop.Read(e.Data)!=null?DragDropEffects.Copy:DragDropEffects.None;
   list.DragDrop+=(s,e)=>{var source=e.Data.GetData(typeof(ListViewItem)) as ListViewItem;if(source!=null){var at=list.PointToClient(new Point(e.X,e.Y));var target=list.GetItemAt(at.X,at.Y);if(target!=null&&source!=target&&MoveRequested!=null)MoveRequested((string)source.Tag,items.FindIndex(x=>x.id==(string)target.Tag));return;}AcceptDrop(e.Data);};
   list.DoubleClick+=(s,e)=>Import(false);
   list.SelectedIndexChanged+=(s,e)=>UpdateActions();
   list.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Right){var row=list.GetItemAt(e.X,e.Y);if(row!=null&&!row.Selected){foreach(var previous in list.SelectedItems.Cast<ListViewItem>().ToList())previous.Selected=false;row.Selected=true;}}};
   AllowDrop=true;DragEnter+=(s,e)=>e.Effect=e.Data.GetDataPresent(DataFormats.FileDrop)||WebDrop.Read(e.Data)!=null?DragDropEffects.Copy:DragDropEffects.None;DragDrop+=(s,e)=>AcceptDrop(e.Data);
   var context=new ContextMenuStrip();context.Items.Add("Elegir tramo…",null,(s,e)=>{var ids=SelectedIds();if(ids.Count==1&&RangeRequested!=null)RangeRequested(ids[0]);});context.Items.Add("Importar en pista",null,(s,e)=>Import(true));context.Items.Add("Guardar en Medios",null,(s,e)=>Import(false));context.Items.Add("Quitar de Bandeja",null,(s,e)=>Remove());list.ContextMenuStrip=context;
   var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=43,Padding=new Padding(8,6,0,0),WrapContents=false};
   selectedButtons.Add(MakeButton("Importar en pista",()=>Import(true)));
   selectedButtons.Add(MakeButton("Guardar en Medios",()=>Import(false)));
   allButtons.Add(MakeButton("Importar todos",()=>ImportAll(true)));
   allButtons.Add(MakeButton("Guardar todos",()=>ImportAll(false)));
   selectedButtons.Add(MakeButton("Quitar",Remove));
   foreach(var button in selectedButtons.Concat(allButtons))footer.Controls.Add(button);
   count.Dock=DockStyle.Bottom;count.Height=27;count.Padding=new Padding(11,5,0,0);count.ForeColor=Color.FromArgb(170,174,180);
   Controls.Add(list);Controls.Add(count);Controls.Add(footer);Controls.Add(head);
   FormClosing+=(s,e)=>{if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}};
   UpdateActions();
  }
  Button MakeButton(string label,Action action){var button=new Button{Text=label,AutoSize=true,Height=29,FlatStyle=FlatStyle.Flat,BackColor=surface,ForeColor=ink,Margin=new Padding(2,0,3,0)};button.FlatAppearance.BorderColor=Color.FromArgb(68,71,77);button.Click+=(s,e)=>action();return button;}
  public void CaptureText(string text){if(String.IsNullOrWhiteSpace(text))return;if(input.Text=="Pega uno o varios enlaces públicos…")input.Clear();input.ForeColor=ink;input.Text+=(input.Text.Length>0?Environment.NewLine:"")+text.Trim();}
  void AcceptDrop(IDataObject data){if(data.GetDataPresent(DataFormats.FileDrop)){var files=data.GetData(DataFormats.FileDrop) as string[];if(files!=null&&AddFilesRequested!=null)AddFilesRequested(files);return;}var web=WebDrop.Read(data);if(web!=null)CaptureText(web.Url);}
  void AddText(){string value=input.Text;if(String.IsNullOrWhiteSpace(value)||value=="Pega uno o varios enlaces públicos…")return;if(AddRequested!=null)AddRequested(value);}
  public void ClearInput(){input.Clear();}
  public void SetInput(string value){input.Text=value??"";input.ForeColor=ink;}
  public void SetItems(IEnumerable<ShelfItem> source){items.Clear();items.AddRange(source);Rebuild();}
  List<string> SelectedIds(){return list.SelectedItems.Cast<ListViewItem>().Select(x=>(string)x.Tag).ToList();}
  void UpdateActions(){bool selected=list.SelectedItems.Count>0;foreach(var button in selectedButtons)button.Enabled=selected;foreach(var button in allButtons)button.Enabled=items.Count>0;}
  void Import(bool timeline){var ids=SelectedIds();if(ids.Count>0&&ImportRequested!=null)ImportRequested(ids,timeline);}
  void ImportAll(bool timeline){var ids=items.Select(x=>x.id).ToList();if(ids.Count>0&&ImportRequested!=null)ImportRequested(ids,timeline);}
  void Remove(){var ids=SelectedIds();if(ids.Count>0&&RemoveRequested!=null)RemoveRequested(ids);}
  static string Duration(Asset asset){double seconds=asset.streams.Where(x=>x.duration!=null).Select(x=>x.duration.Seconds).DefaultIfEmpty(0).Max();return TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");}
  void Rebuild(){
   string selected=list.SelectedItems.Count>0?(string)list.SelectedItems[0].Tag:null;
   list.BeginUpdate();images.Images.Clear();list.Items.Clear();list.View=grid?View.LargeIcon:View.Details;
   string query=search.Text.Trim();
   foreach(var item in items.Where(x=>query.Length==0||(x.asset.name??"").IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0)){
    var row=new ListViewItem(item.asset.name??item.url){Tag=item.id};row.SubItems.Add(item.asset.source.kind=="local"?"Local":WebMedia.PlatformLabel(WebMedia.Platform(item.url)));row.SubItems.Add(item.range_in.HasValue?TimeSpan.FromSeconds(item.range_in.Value).ToString(@"hh\:mm\:ss")+"–"+TimeSpan.FromSeconds(item.range_out.Value).ToString(@"hh\:mm\:ss"):Duration(item.asset));
    string path=null;try{path=WebMedia.ThumbnailPath(item.asset,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","WebCache"));}catch{}
    if(path!=null&&File.Exists(path))try{using(var bitmap=Image.FromFile(path))using(var thumbnail=new Bitmap(bitmap))images.Images.Add(thumbnail);row.ImageIndex=images.Images.Count-1;}catch{}
    list.Items.Add(row);if(item.id==selected)row.Selected=true;
   }
   list.EndUpdate();UpdateActions();count.Text=items.Count+" pendientes · solo referencias; al importarlos salen de la Bandeja";
  }
  protected override void Dispose(bool disposing){if(disposing)images.Dispose();base.Dispose(disposing);}
 }
}
