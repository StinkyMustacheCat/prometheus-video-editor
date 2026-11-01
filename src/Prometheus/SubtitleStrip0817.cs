using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public sealed class SubtitleStrip0817:Panel {
  readonly ToolTip hints=new ToolTip();readonly Timeline timeline;readonly Panel header=new Panel(),content=new Panel(),compactActions=new Panel();readonly Button toggle=new Button(),add=new Button(),animation=new Button(),eye=new Button(),compactAdd=new Button(),compactAnimation=new Button(),compactEye=new Button();readonly CaptionCanvas canvas;
  bool expanded,previewVisible=true,compact;string selectedId;
  public Action AddRequested,AnimationRequested,AnimationRemoveRequested,PreviewVisibilityChanged;public Action<string> Selected,EditRequested,RemoveRequested,DeleteIntervalRequested,SplitRequested;public Action<string,string,double> DragRequested;
  public string SelectedId{get{return selectedId;}}
  public bool Expanded{get{return expanded;}}
  public bool PreviewVisible{get{return previewVisible;}}
  public SubtitleStrip0817(Timeline timeline){
   this.timeline=timeline;Dock=DockStyle.Top;Height=28;BackColor=Color.FromArgb(32,36,42);
   header.Dock=DockStyle.Left;header.Width=timeline.HeaderWidth;header.BackColor=Color.FromArgb(43,47,53);
   toggle.Dock=DockStyle.Fill;toggle.FlatStyle=FlatStyle.Flat;toggle.FlatAppearance.BorderSize=0;toggle.Text="▸ Subtítulos";toggle.TextAlign=ContentAlignment.MiddleLeft;toggle.ForeColor=Color.Gainsboro;toggle.BackColor=header.BackColor;toggle.Click+=(s,e)=>SetExpanded(!expanded);
   add.Dock=DockStyle.Right;add.Width=30;add.Text="+";add.FlatStyle=FlatStyle.Flat;add.FlatAppearance.BorderSize=0;add.ForeColor=Color.Gainsboro;add.BackColor=header.BackColor;add.Click+=(s,e)=>{if(AddRequested!=null)AddRequested();};hints.SetToolTip(add,"Añadir subtítulo en el lector");
   animation.Dock=DockStyle.Right;animation.Width=34;animation.Text="≡A";animation.FlatStyle=FlatStyle.Flat;animation.FlatAppearance.BorderSize=1;animation.FlatAppearance.BorderColor=header.BackColor;animation.ForeColor=Color.Gainsboro;animation.BackColor=header.BackColor;animation.Click+=(s,e)=>{if(AnimationRequested!=null)AnimationRequested();};hints.SetToolTip(animation,"Elegir o reemplazar animación MGT de subtítulos");
   var animationMenu=new ContextMenuStrip();animationMenu.Items.Add("Quitar animación MGT",null,(s,e)=>{if(AnimationRemoveRequested!=null)AnimationRemoveRequested();});animation.ContextMenuStrip=animationMenu;
   eye.Dock=DockStyle.Right;eye.Width=30;eye.FlatStyle=FlatStyle.Flat;eye.FlatAppearance.BorderSize=0;eye.ForeColor=Color.FromArgb(255,156,44);eye.BackColor=header.BackColor;eye.Paint+=(s,e)=>{e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using(var pen=new Pen(eye.ForeColor,1.4f)){e.Graphics.DrawEllipse(pen,6,9,18,10);if(previewVisible)using(var brush=new SolidBrush(eye.ForeColor))e.Graphics.FillEllipse(brush,13,11,4,5);else e.Graphics.DrawLine(pen,6,21,24,6);}};eye.Click+=(s,e)=>{previewVisible=!previewVisible;eye.ForeColor=previewVisible?Color.FromArgb(255,156,44):Color.Gray;eye.Invalidate();if(PreviewVisibilityChanged!=null)PreviewVisibilityChanged();};hints.SetToolTip(eye,"Mostrar u ocultar subtítulos solo en el monitor");
   header.Controls.Add(toggle);header.Controls.Add(eye);header.Controls.Add(animation);header.Controls.Add(add);
   compactActions.Dock=DockStyle.Top;compactActions.Height=26;compactActions.BackColor=header.BackColor;
   foreach(var button in new[]{compactAdd,compactAnimation,compactEye}){button.Width=34;button.Dock=DockStyle.Left;button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderSize=0;button.BackColor=header.BackColor;button.ForeColor=Color.Gainsboro;compactActions.Controls.Add(button);}
   compactAdd.Text="+";compactAdd.Click+=(s,e)=>{if(AddRequested!=null)AddRequested();};compactAnimation.Text="≡A";compactAnimation.Click+=(s,e)=>{if(AnimationRequested!=null)AnimationRequested();};compactEye.Text="◉";compactEye.Click+=(s,e)=>eye.PerformClick();
   hints.SetToolTip(compactAdd,"Añadir subtítulo");hints.SetToolTip(compactAnimation,"Elegir animación MGT");hints.SetToolTip(compactEye,"Mostrar u ocultar subtítulos en el monitor");
   canvas=new CaptionCanvas(this){Dock=DockStyle.Fill};content.Dock=DockStyle.Fill;content.Controls.Add(canvas);content.Controls.Add(compactActions);Controls.Add(content);Controls.Add(header);SetHeaderWidth(timeline.HeaderWidth);
   timeline.Invalidated+=(s,e)=>{if(!IsDisposed)canvas.Invalidate();};
  }
  public void SetHeaderWidth(int width){header.Width=width;compact=width<155;add.Visible=animation.Visible=eye.Visible=!compact;compactActions.Visible=compact&&expanded;toggle.Text=expanded?(compact?"▾ Sub.":"▾ Subtítulos"):(compact?"▸ Sub.":"▸ Subtítulos");Invalidate();canvas.Invalidate();}
  public void SetExpanded(bool value){expanded=value;Height=value?(compact?76:70):28;toggle.Text=value?(compact?"▾ Sub.":"▾ Subtítulos"):(compact?"▸ Sub.":"▸ Subtítulos");compactActions.Visible=compact&&value;canvas.Visible=value;if(!value)ClearSelection();Invalidate();}
  public void SetAnimationActive(bool active){animation.FlatAppearance.BorderSize=1;animation.FlatAppearance.BorderColor=active?Color.FromArgb(255,156,44):header.BackColor;animation.ForeColor=compactAnimation.ForeColor=active?Color.FromArgb(255,156,44):Color.Gainsboro;}
  public void RefreshToolState(){timeline.HighlightedSubtitleId=expanded&&timeline.ActiveTool==Timeline.ToolMode.Razor?selectedId:null;timeline.Invalidate();canvas.Invalidate();}
  public void RefreshCues(){if(timeline.Project==null||!SubtitleTrack0817.Cues(timeline.Project).Any(c=>c.id==selectedId))ClearSelection();canvas.Invalidate();}
  public void SelectCue(string id){selectedId=id;RefreshToolState();if(Selected!=null)Selected(id);}
  public void ClearSelection(){selectedId=null;timeline.HighlightedSubtitleId=null;timeline.Invalidate();canvas.Invalidate();if(Selected!=null)Selected(null);}
  public bool HandleDelete(){if(!expanded||selectedId==null||!ContainsFocus)return false;if(timeline.ActiveTool==Timeline.ToolMode.Razor){if(DeleteIntervalRequested!=null)DeleteIntervalRequested(selectedId);}else if(RemoveRequested!=null)RemoveRequested(selectedId);return true;}
  protected override void Dispose(bool disposing){if(disposing)hints.Dispose();base.Dispose(disposing);}
  sealed class CaptionCanvas:Control {
   readonly SubtitleStrip0817 owner;readonly ContextMenuStrip menu=new ContextMenuStrip();Clip dragClip;string dragMode;int dragX;double dragValue;
   public CaptionCanvas(SubtitleStrip0817 strip){owner=strip;DoubleBuffered=true;BackColor=Color.FromArgb(32,36,42);ForeColor=Color.Gainsboro;TabStop=true;SetStyle(ControlStyles.Selectable,true);}
   Clip At(int x){var project=owner.timeline.Project;if(project==null)return null;double time=owner.timeline.Scroll+x/owner.timeline.Zoom;return SubtitleTrack0817.Cues(project).FirstOrDefault(c=>time>=c.timeline_start.Seconds&&time<c.End);}
   protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);Focus();var clip=At(e.X);owner.SelectCue(clip==null?null:clip.id);
    if(e.Button==MouseButtons.Left&&clip!=null&&owner.timeline.ActiveTool!=Timeline.ToolMode.Razor){float left=(float)((clip.timeline_start.Seconds-owner.timeline.Scroll)*owner.timeline.Zoom),right=(float)((clip.End-owner.timeline.Scroll)*owner.timeline.Zoom);float grip=Math.Min(7,Math.Max(1,(right-left)/3));dragClip=clip;dragX=e.X;dragValue=clip.timeline_start.Seconds;dragMode=Math.Abs(e.X-left)<=grip?"left":Math.Abs(e.X-right)<=grip?"right":"move";Capture=true;}
    if(e.Button==MouseButtons.Right&&clip!=null){menu.Items.Clear();menu.Items.Add("Editar texto…",null,(s,a)=>{if(owner.EditRequested!=null)owner.EditRequested(clip.id);});menu.Items.Add("Dividir en el lector",null,(s,a)=>{if(owner.SplitRequested!=null)owner.SplitRequested(clip.id);});menu.Items.Add("Eliminar intervalo del montaje",null,(s,a)=>{if(owner.DeleteIntervalRequested!=null)owner.DeleteIntervalRequested(clip.id);});menu.Items.Add("Eliminar solo subtítulo",null,(s,a)=>{if(owner.RemoveRequested!=null)owner.RemoveRequested(clip.id);});menu.Show(this,e.Location);}
   }
   protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragClip!=null){double delta=(e.X-dragX)/owner.timeline.Zoom;dragValue=Rational.Frame((dragMode=="right"?dragClip.End:dragClip.timeline_start.Seconds)+delta).Seconds;Cursor=dragMode=="move"?Cursors.SizeAll:Cursors.SizeWE;Invalidate();return;}var clip=At(e.X);if(clip==null){Cursor=Cursors.Default;return;}if(owner.timeline.ActiveTool==Timeline.ToolMode.Razor){Cursor=Cursors.Cross;return;}float left=(float)((clip.timeline_start.Seconds-owner.timeline.Scroll)*owner.timeline.Zoom),right=(float)((clip.End-owner.timeline.Scroll)*owner.timeline.Zoom),grip=Math.Min(7,Math.Max(1,(right-left)/3));Cursor=Math.Abs(e.X-left)<=grip||Math.Abs(e.X-right)<=grip?Cursors.SizeWE:Cursors.SizeAll;}
   protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(dragClip==null)return;var clip=dragClip;string mode=dragMode;bool changed=Math.Abs(e.X-dragX)>=2;dragClip=null;dragMode=null;Capture=false;Cursor=Cursors.Default;Invalidate();if(changed&&owner.DragRequested!=null)owner.DragRequested(clip.id,mode,dragValue);}
   protected override void OnMouseDoubleClick(MouseEventArgs e){base.OnMouseDoubleClick(e);var clip=At(e.X);if(clip!=null&&owner.EditRequested!=null)owner.EditRequested(clip.id);}
   protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;g.Clear(BackColor);var project=owner.timeline.Project;if(project==null)return;
    foreach(var clip in SubtitleTrack0817.Cues(project)){
     float x=(float)((clip.timeline_start.Seconds-owner.timeline.Scroll)*owner.timeline.Zoom),right=(float)((clip.End-owner.timeline.Scroll)*owner.timeline.Zoom);
     if(right<0||x>Width)continue;var box=new RectangleF(x+1,4,Math.Max(2,right-x-2),Height-9);bool selected=clip.id==owner.selectedId;
     using(var fill=new SolidBrush(selected?Color.FromArgb(93,131,163):Color.FromArgb(65,89,108)))g.FillRectangle(fill,box);
     using(var border=new Pen(selected?Color.WhiteSmoke:Color.FromArgb(116,145,167),selected?2:1))g.DrawRectangle(border,box.X,box.Y,box.Width,box.Height);
     var asset=project.assets.FirstOrDefault(a=>a.id==clip.asset_id);string text=asset==null?clip.name:GeneratedMedia.Text(asset);
     TextRenderer.DrawText(g,text,Font,Rectangle.Round(box),Color.WhiteSmoke,TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.NoPadding);
    }
    if(dragClip!=null){double begin=dragMode=="move"?dragValue:dragMode=="left"?dragValue:dragClip.timeline_start.Seconds;double end=dragMode=="move"?dragValue+dragClip.duration.Seconds:dragMode=="right"?dragValue:dragClip.End;float x=(float)((begin-owner.timeline.Scroll)*owner.timeline.Zoom),right=(float)((end-owner.timeline.Scroll)*owner.timeline.Zoom);if(right>x)using(var pen=new Pen(Color.WhiteSmoke,2)){pen.DashStyle=System.Drawing.Drawing2D.DashStyle.Dash;g.DrawRectangle(pen,x+1,2,right-x-2,Height-5);}}
    if(SubtitleTrack0817.Cues(project).Count==0)TextRenderer.DrawText(g,"Añade frases con +",Font,new Rectangle(8,0,Math.Max(1,Width-12),Height),Color.FromArgb(149,160,171),TextFormatFlags.VerticalCenter|TextFormatFlags.Left);
   }
   protected override void Dispose(bool disposing){if(disposing)menu.Dispose();base.Dispose(disposing);}
  }
 }
}
