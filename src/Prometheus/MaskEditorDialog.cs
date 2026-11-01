using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public sealed class MaskEditorDialog:Form {
  sealed class MaskCanvas:Control {
   public ClipMask Mask;public Bitmap Image;int dragging=-1;
   public MaskCanvas(){DoubleBuffered=true;ResizeRedraw=true;BackColor=Color.FromArgb(20,22,25);Cursor=Cursors.Cross;}
   RectangleF Picture(){if(Image==null)return new RectangleF(0,0,Math.Max(1,Width),Math.Max(1,Height));float factor=Math.Min((float)Math.Max(1,Width)/Image.Width,(float)Math.Max(1,Height)/Image.Height);float w=Image.Width*factor,h=Image.Height*factor;return new RectangleF((Width-w)/2,(Height-h)/2,w,h);}
   PointF Screen(MaskVertex point){var r=Picture();return new PointF(r.Left+(float)point.x*r.Width,r.Top+(float)point.y*r.Height);}
   MaskVertex Normal(Point point){var r=Picture();return new MaskVertex{x=Math.Max(0,Math.Min(1,(point.X-r.Left)/r.Width)),y=Math.Max(0,Math.Min(1,(point.Y-r.Top)/r.Height))};}
   protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;g.Clear(BackColor);var view=Picture();if(Image!=null)g.DrawImage(Image,view);
    if(Mask==null||Mask.points.Count==0)return;g.SmoothingMode=SmoothingMode.AntiAlias;
    using(var path=new GraphicsPath()){
     if(Mask.shape=="polygon"&&Mask.points.Count>=2)path.AddLines(Mask.points.Select(Screen).ToArray());
     else if(Mask.points.Count>=2){var a=Screen(Mask.points[0]);var b=Screen(Mask.points[1]);var rect=RectangleF.FromLTRB(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Max(a.X,b.X),Math.Max(a.Y,b.Y));if(Mask.shape=="ellipse")path.AddEllipse(rect);else path.AddRectangle(rect);}
     if(path.PointCount>0){using(var fill=new SolidBrush(Color.FromArgb(65,255,156,44)))g.FillPath(fill,path);using(var edge=new Pen(Color.FromArgb(255,205,122),2))g.DrawPath(edge,path);}
    }
    for(int i=0;i<Mask.points.Count;i++){var point=Screen(Mask.points[i]);using(var fill=new SolidBrush(Color.FromArgb(255,205,122)))g.FillEllipse(fill,point.X-5,point.Y-5,10,10);using(var border=new Pen(Color.FromArgb(23,25,27),1.5f))g.DrawEllipse(border,point.X-5,point.Y-5,10,10);}
   }
   protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(Mask==null)return;if(e.Button==MouseButtons.Right&&Mask.shape=="polygon"){if(Mask.points.Count>0)Mask.points.RemoveAt(Mask.points.Count-1);Invalidate();return;}if(e.Button!=MouseButtons.Left)return;
    dragging=-1;for(int i=0;i<Mask.points.Count;i++){var p=Screen(Mask.points[i]);if(Math.Abs(p.X-e.X)<=10&&Math.Abs(p.Y-e.Y)<=10){dragging=i;break;}}
    if(Mask.shape=="polygon"){if(dragging<0&&Mask.points.Count<64){Mask.points.Add(Normal(e.Location));dragging=Mask.points.Count-1;}}
    else if(dragging<0){var point=Normal(e.Location);Mask.points.Clear();Mask.points.Add(point);Mask.points.Add(new MaskVertex{x=point.x,y=point.y});dragging=1;}
    Capture=true;Invalidate();
   }
   protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging<0||!Capture||Mask==null)return;var point=Normal(e.Location);Mask.points[dragging].x=point.x;Mask.points[dragging].y=point.y;Invalidate();}
   protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(dragging<0||Mask==null)return;Capture=false;dragging=-1;if(Mask.shape!="polygon"&&Mask.points.Count==2){double x=Math.Min(Mask.points[0].x,Mask.points[1].x),y=Math.Min(Mask.points[0].y,Mask.points[1].y),right=Math.Max(Mask.points[0].x,Mask.points[1].x),bottom=Math.Max(Mask.points[0].y,Mask.points[1].y);Mask.points[0].x=x;Mask.points[0].y=y;Mask.points[1].x=right;Mask.points[1].y=bottom;}Invalidate();}
  }
  readonly MaskCanvas canvas=new MaskCanvas();
  readonly ComboBox shape=new ComboBox();readonly NumericUpDown feather=new NumericUpDown();readonly CheckBox invert=new CheckBox();
  public ClipMask Result {get{return canvas.Mask;}}
  public MaskEditorDialog(ClipMask initial,Bitmap background){
   Text="Editar máscara";ClientSize=new Size(850,575);MinimumSize=new Size(570,430);StartPosition=FormStartPosition.CenterParent;BackColor=Color.FromArgb(36,38,42);ForeColor=Color.Gainsboro;
   canvas.Mask=Store.Json().Deserialize<ClipMask>(Store.Json().Serialize(Store.Fields(initial)));canvas.Image=background==null?null:new Bitmap(background);canvas.Dock=DockStyle.Fill;
   var toolbar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=44,Padding=new Padding(9,8,0,0),WrapContents=false};
   shape.DropDownStyle=ComboBoxStyle.DropDownList;shape.Width=140;shape.Items.AddRange(new object[]{"Rectángulo","Elipse","Forma libre"});shape.SelectedIndex=initial.shape=="ellipse"?1:initial.shape=="polygon"?2:0;
   shape.SelectedIndexChanged+=(s,e)=>{string next=shape.SelectedIndex==1?"ellipse":shape.SelectedIndex==2?"polygon":"rectangle";var replacement=ClipMasks.New(next);replacement.inverted=invert.Checked;replacement.feather=(double)feather.Value/100;canvas.Mask=replacement;canvas.Invalidate();};toolbar.Controls.Add(shape);
   var redraw=new Button{Text="Redibujar",Width=92,Height=26};redraw.Click+=(s,e)=>{canvas.Mask.points.Clear();canvas.Invalidate();};toolbar.Controls.Add(redraw);
   invert.Text="Invertir";invert.Checked=initial.inverted;invert.AutoSize=true;invert.Padding=new Padding(8,4,0,0);invert.CheckedChanged+=(s,e)=>canvas.Mask.inverted=invert.Checked;toolbar.Controls.Add(invert);
   toolbar.Controls.Add(new Label{Text="Suavidad",Width=70,Height=26,TextAlign=ContentAlignment.MiddleRight});
   feather.DecimalPlaces=1;feather.Minimum=0;feather.Maximum=20;feather.Increment=0.5m;feather.Value=(decimal)(initial.feather*100);feather.Width=66;feather.ValueChanged+=(s,e)=>canvas.Mask.feather=(double)feather.Value/100;toolbar.Controls.Add(feather);
   toolbar.Controls.Add(new Label{Text="% del lado corto",Width=112,Height=26,TextAlign=ContentAlignment.MiddleLeft});
   var footer=new Panel{Dock=DockStyle.Bottom,Height=62};var help=new Label{Text="Arrastra esquinas o puntos. En forma libre, haz clic para añadir vértices; clic derecho quita el último.",Left=14,Top=4,Width=600,Height=43,ForeColor=Color.FromArgb(175,181,189)};footer.Controls.Add(help);
   var apply=new Button{Text="Aplicar",Width=91,Height=29,Top=17};apply.Click+=(s,e)=>{try{ClipMasks.Validate(canvas.Mask);DialogResult=DialogResult.OK;Close();}catch(Exception ex){MessageBox.Show(this,ex.Message,"Máscara",MessageBoxButtons.OK,MessageBoxIcon.Information);}};footer.Controls.Add(apply);AcceptButton=apply;
   var cancel=new Button{Text="Cancelar",Width=91,Height=29,Top=17,DialogResult=DialogResult.Cancel};footer.Controls.Add(cancel);CancelButton=cancel;
   footer.Resize+=(s,e)=>{cancel.Left=Math.Max(8,footer.ClientSize.Width-cancel.Width-14);apply.Left=Math.Max(8,cancel.Left-apply.Width-8);help.Width=Math.Max(80,apply.Left-24);};
   Controls.Add(canvas);Controls.Add(footer);Controls.Add(toolbar);FormClosed+=(s,e)=>{if(canvas.Image!=null)canvas.Image.Dispose();};
  }
 }
}
