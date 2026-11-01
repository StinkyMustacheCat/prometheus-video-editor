using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  void ShowMonitorContext01034(Point point){
   var menu=new ContextMenuStrip();var capture=new ToolStripMenuItem("Capturar fotograma");
   var insert=capture.DropDownItems.Add("Incrustar en línea de tiempo",null,(s,e)=>InsertCaptureFrame01034());
   insert.ToolTipText="Inserta el fotograma y desplaza el montaje posterior";
   var overlay=capture.DropDownItems.Add("Superponer en línea de tiempo",null,(s,e)=>OverlayCaptureFrame01042());
   overlay.ToolTipText="Coloca el fotograma en una pista libre, sin mover los medios existentes";
   capture.DropDownItems.Add("Añadir fotograma a Medios",null,(s,e)=>AddCaptureToMedia01034());
   capture.DropDownItems.Add("Guardar fotograma como PNG…",null,(s,e)=>SaveCapture01034());
   capture.DropDownItems.Add("Copiar fotograma",null,(s,e)=>CopyCapture01034());
   menu.Items.Add(capture);menu.Items.Add(new ToolStripSeparator());
   var fps=new ToolStripMenuItem("FPS del monitor");foreach(int value in new[]{15,30,60}){int selected=value;var item=(ToolStripMenuItem)fps.DropDownItems.Add(value+" FPS",null,(s,e)=>monitorView.SetPreviewFps(selected));item.Checked=monitorView.PreviewFps==value;}menu.Items.Add(fps);
   var quality=new ToolStripMenuItem("Calidad del monitor");foreach(string value in new[]{"Baja","Media","Alta"}){string selected=value;var item=(ToolStripMenuItem)quality.DropDownItems.Add(value,null,(s,e)=>monitorView.SetPreviewQuality(selected));item.Checked=monitorView.PreviewQuality==value;}menu.Items.Add(quality);
   menu.Items.Add("Ajustar imagen",null,(s,e)=>monitorView.Fit());
   menu.Items.Add(monitorView.ControlsVisible?"Ocultar controles del monitor":"Mostrar controles del monitor",null,(s,e)=>monitorView.SetControlsVisible(!monitorView.ControlsVisible));
   menu.Closed+=(s,e)=>BeginInvoke((Action)(()=>menu.Dispose()));menu.Show(monitor,point);
  }
  Bitmap CaptureFrame01034(){
   if(playing)Stop();if(busy)throw new Exception("Espera a que termine la tarea actual");
   var project=Store.Copy(editor.Project);double at=timeline.Cursor;
   var visible=project.Active.clips.Where(c=>c.enabled&&c.timeline_start.Seconds<=at+0.000001&&c.End>at+0.000001&&project.Active.tracks.Any(t=>t.id==c.track_id&&t.kind=="video"&&t.enabled)).ToList();
   if(visible.Count==0)throw new Exception("Sitúa el lector sobre un medio visual para capturar el fotograma");
   bool preview=false;
   foreach(var clip in visible){var asset=project.assets.Find(a=>a.id==clip.asset_id);if(asset==null||asset.source.kind=="generated")continue;
    if(Media.SelectRepresentation(asset,false)==null){if(Media.SelectRepresentation(asset,true)==null)throw new Exception("Resuelve o prepara una vista local de "+asset.name+" antes de capturar");preview=true;}
   }
   if(preview){if(MessageBox.Show(this,"Un medio visible solo tiene vista ligera. La captura puede perder detalle. ¿Continuar con la mejor calidad disponible localmente?","Calidad del fotograma",MessageBoxButtons.YesNo,MessageBoxIcon.Information)!=DialogResult.Yes)return null;
    foreach(var asset in project.assets.Where(a=>Media.SelectRepresentation(a,false)!=null))asset.representations.RemoveAll(r=>r.role=="preview"||r.role=="proxy");
   }
   UseWaitCursor=true;status.Text="Capturando fotograma a la mejor calidad disponible…";
   try{return Rendering.FrameExact(project,at,CancellationToken.None,project.Active.width,project.Active.height,preview);}
   finally{UseWaitCursor=false;}
  }
  string StoreCapture01034(Bitmap image){
   string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","CapturedFrames",editor.Project.id);
   Directory.CreateDirectory(directory);string file=Path.Combine(directory,"Fotograma-"+Store.Id()+".png");image.Save(file,ImageFormat.Png);return file;
  }
  void InsertCaptureFrame01034(){try{using(var image=CaptureFrame01034()){if(image==null)return;string file=StoreCapture01034(image);try{var asset=Media.Inspect(file,CancellationToken.None);Clip placed=null;Edit(()=>editor.Commit(p=>placed=FrameInsertion01034.Insert(p,asset,timeline.Cursor)));timeline.Selection=new System.Collections.Generic.List<string>{placed.id};timeline.Selected=placed.id;timeline.ActiveTrack=placed.track_id;timeline.Invalidate();ShowClip();status.Text="Fotograma insertado · contenido posterior desplazado · Ctrl+Z para deshacer";}catch{File.Delete(file);throw;}}}catch(Exception ex){Error(ex);}}
  void OverlayCaptureFrame01042(){try{using(var image=CaptureFrame01034()){if(image==null)return;string file=StoreCapture01034(image);try{var asset=Media.Inspect(file,CancellationToken.None);Clip placed=null;Edit(()=>editor.Commit(p=>placed=FrameInsertion01034.Overlay(p,asset,timeline.Cursor)));timeline.Selection=new System.Collections.Generic.List<string>{placed.id};timeline.Selected=placed.id;timeline.ActiveTrack=placed.track_id;timeline.Invalidate();ShowClip();status.Text="Fotograma superpuesto · Ctrl+Z para deshacer";}catch{File.Delete(file);throw;}}}catch(Exception ex){Error(ex);}}
  void AddCaptureToMedia01034(){try{using(var image=CaptureFrame01034()){if(image==null)return;string file=StoreCapture01034(image);try{var asset=Media.Inspect(file,CancellationToken.None);Edit(()=>editor.Commit(p=>p.assets.Add(asset)));mediaView.Reveal(asset.id);status.Text="Fotograma añadido a Medios";}catch{File.Delete(file);throw;}}}catch(Exception ex){Error(ex);}}
  void SaveCapture01034(){try{using(var image=CaptureFrame01034()){if(image==null)return;using(var picker=new SaveFileDialog{Title="Guardar fotograma",Filter="Imagen PNG (*.png)|*.png",DefaultExt="png",AddExtension=true,FileName="Fotograma.png"})if(picker.ShowDialog(this)==DialogResult.OK){image.Save(picker.FileName,ImageFormat.Png);status.Text="Fotograma guardado en "+picker.FileName;}}}catch(Exception ex){Error(ex);}}
  void CopyCapture01034(){try{using(var image=CaptureFrame01034()){if(image==null)return;Clipboard.SetImage(image);status.Text="Fotograma copiado al portapapeles";}}catch(Exception ex){Error(ex);}}
 }
}
