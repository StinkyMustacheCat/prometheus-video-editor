using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  static Bitmap Sam2ProgressImage01023(string framePath,string maskPath){
   const int width=384,height=216;using(var frameFile=new Bitmap(framePath))using(var maskFile=new Bitmap(maskPath))using(var frame=new Bitmap(width,height,PixelFormat.Format32bppArgb))using(var mask=new Bitmap(width,height,PixelFormat.Format32bppArgb)){
    using(var g=Graphics.FromImage(frame))g.DrawImage(frameFile,0,0,width,height);
    using(var g=Graphics.FromImage(mask))g.DrawImage(maskFile,0,0,width,height);
    var rect=new Rectangle(0,0,width,height);var imageBits=frame.LockBits(rect,ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);var maskBits=mask.LockBits(rect,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
    try{var pixels=new byte[imageBits.Stride*height];var stencil=new byte[maskBits.Stride*height];Marshal.Copy(imageBits.Scan0,pixels,0,pixels.Length);Marshal.Copy(maskBits.Scan0,stencil,0,stencil.Length);
     for(int y=0;y<height;y++)for(int x=0;x<width;x++){int i=y*imageBits.Stride+x*4,j=y*maskBits.Stride+x*4;if(stencil[j+2]<128)continue;pixels[i]=(byte)((pixels[i]*3+50*2)/5);pixels[i+1]=(byte)((pixels[i+1]*3+156*2)/5);pixels[i+2]=(byte)((pixels[i+2]*3+255*2)/5);}Marshal.Copy(pixels,0,imageBits.Scan0,pixels.Length);
    }finally{mask.UnlockBits(maskBits);frame.UnlockBits(imageBits);}return new Bitmap(frame);
   }
  }
  void BeginSmartCutLegacy091(string id){
   try{
    if(busy)throw new Exception("Espera a que termine la tarea actual");
    var clip=MaskClip(editor.Project,id);var asset=editor.Project.assets.First(a=>a.id==clip.asset_id);var stream=asset.streams.First(s=>s.id==clip.stream_id);
    if(stream.kind!="video")throw new Exception("Recorte inteligente necesita un clip de vídeo");
    if(Media.SelectRepresentation(asset,false)==null)throw new Exception("Descarga o reconecta el medio antes de recortar");
    Stop();monitor.TextToolActive=false;
    var prior=Sam2MaskStore090.Get(clip);
    var points=prior==null?new List<Sam2Prompt090>():prior.prompts.Select(p=>new Sam2Prompt090{frame=p.frame,x=p.x,y=p.y,positive=p.positive,object_id=p.object_id}).ToList();
    int frame=Math.Max(0,Math.Min((int)Math.Ceiling(clip.duration.Seconds*60)-1,(int)Math.Round((timeline.Cursor-clip.timeline_start.Seconds)*60)));
    int objectId=points.Count==0?1:Math.Max(1,points.Max(p=>p.object_id));bool positive=true;
    var dialog=new Form{Text="Recorte inteligente · fotograma "+(frame+1),ClientSize=new Size(520,168),FormBorderStyle=FormBorderStyle.FixedToolWindow,StartPosition=FormStartPosition.CenterParent,BackColor=panel,ForeColor=text};
    var label=new Label{Left=12,Top=11,Width=495,Height=36,Text="Clic en el monitor para añadir el sujeto. Añade zonas y revisa la máscara antes de procesar."};
    var add=new Button{Text="Incluir",Left=12,Top=55,Width=92,Height=29};
    var exclude=new Button{Text="Excluir",Left=110,Top=55,Width=92,Height=29};
    var region=new Button{Text="Nueva zona",Left=208,Top=55,Width=106,Height=29};
    var undo=new Button{Text="Quitar punto",Left=320,Top=55,Width=110,Height=29};
    var preview=new Button{Text="Vista previa",Left=12,Top=106,Width=108,Height=32};
    var process=new Button{Text="Procesar clip",Left=335,Top=106,Width=108,Height=32};
    var cancel=new Button{Text="Cancelar",Left=449,Top=106,Width=61,Height=32};
    dialog.Controls.AddRange(new Control[]{label,add,exclude,region,undo,preview,process,cancel});
    var previewToken=new CancellationTokenSource();
    Action refresh=()=>{monitor.ShowObjectPoints(points,frame);label.Text="Fotograma "+(frame+1)+" · "+points.Count(p=>p.frame==frame)+" puntos · zona "+objectId+(positive?" · incluir":" · excluir")+". Pulsa Vista previa o Procesar clip.";};
    add.Click+=(s,e)=>{positive=true;refresh();monitor.Focus();};
    exclude.Click+=(s,e)=>{positive=false;refresh();monitor.Focus();};
    region.Click+=(s,e)=>{objectId=Math.Min(64,Math.Max(objectId,points.Count==0?0:points.Max(p=>p.object_id))+1);positive=true;refresh();monitor.Focus();};
    undo.Click+=(s,e)=>{int index=points.FindLastIndex(p=>p.frame==frame);if(index>=0){points.RemoveAt(index);monitor.ShowObjectPreview(null);refresh();}monitor.Focus();};
    monitor.BeginObjectPick((x,y,mousePositive)=>{points.Add(new Sam2Prompt090{frame=frame,x=x,y=y,positive=positive&&mousePositive,object_id=objectId});monitor.ShowObjectPreview(null);refresh();},()=>{if(!dialog.IsDisposed)dialog.Close();},true);
    refresh();
    preview.Click+=(s,e)=>{
     if(busy)return;try{if(!points.Any(p=>p.frame==frame&&p.positive))throw new Exception("Añade un punto positivo en este fotograma");
      var snapshot=Store.Copy(editor.Project);var snapshotPoints=points.Select(p=>new Sam2Prompt090{frame=p.frame,x=p.x,y=p.y,positive=p.positive,object_id=p.object_id}).ToList();
      busy=true;preview.Enabled=false;process.Enabled=false;label.Text="Calculando selección del fotograma…";
      Task.Factory.StartNew(()=>{try{var mask=Sam2Runner090.Preview(snapshot,id,frame,snapshotPoints,previewToken.Token);UI(()=>{try{busy=false;if(dialog.IsDisposed)return;monitor.ShowObjectPreview(mask);preview.Enabled=true;process.Enabled=true;refresh();}finally{mask.Dispose();}});}catch(OperationCanceledException){UI(()=>{busy=false;if(!dialog.IsDisposed){preview.Enabled=true;process.Enabled=true;refresh();}});}catch(Exception ex){UI(()=>{busy=false;if(!dialog.IsDisposed){preview.Enabled=true;process.Enabled=true;Error(ex);refresh();}});}});
     }catch(Exception ex){Error(ex);}
    };
    process.Click+=(s,e)=>{try{if(busy)return;if(!points.Any(p=>p.positive))throw new Exception("Incluye al menos una zona antes de procesar");var selected=points.Select(p=>new Sam2Prompt090{frame=p.frame,x=p.x,y=p.y,positive=p.positive,object_id=p.object_id}).ToList();dialog.Close();RunSmartCut091(id,selected,prior);}catch(Exception ex){Error(ex);}};
    cancel.Click+=(s,e)=>dialog.Close();
    dialog.FormClosed+=(s,e)=>{previewToken.Cancel();monitor.CancelObjectPick(false);RequestFrame();dialog.Dispose();};
    dialog.Show(this);monitor.Focus();status.Text="Recorte inteligente: selecciona zonas en el monitor y pulsa Procesar clip";
   }catch(Exception ex){Error(ex);}
  }
  void RunSmartCut091(string id,List<Sam2Prompt090> points,Sam2MaskTrack090 prior){
   try{
    var snapshot=Store.Copy(editor.Project);int revision=snapshot.revision;string savedPath=projectFile;var token=new CancellationTokenSource();sam2Token090=token;busy=true;
    var window=new Form{Text="Procesando recorte inteligente",ClientSize=new Size(410,330),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,BackColor=panel,ForeColor=text};
    var label=new Label{Text="Preparando clip…",Left=14,Top=13,Width=380,Height=26};
    var picture=new PictureBox{Left=14,Top=41,Width=380,Height=214,SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.FromArgb(28,30,34)};
    var bar=new ProgressBar{Left=14,Top=264,Width=380,Height=23,Minimum=0,Maximum=100};var cancel=new Button{Text="Cancelar",Left=298,Top=295,Width=95,Height=29};
    cancel.Click+=(s,e)=>{token.Cancel();cancel.Enabled=false;label.Text="Cancelando…";};window.FormClosing+=(s,e)=>{if(busy&&e.CloseReason==CloseReason.UserClosing){token.Cancel();e.Cancel=true;label.Text="Cancelando…";}};window.FormClosed+=(s,e)=>{if(picture.Image!=null){picture.Image.Dispose();picture.Image=null;}};window.Controls.AddRange(new Control[]{label,picture,bar,cancel});window.Show(this);
    Task.Factory.StartNew(()=>{try{
     var result=Sam2Runner090.Track(snapshot,id,savedPath,points,token.Token,
      (percent,message)=>UI(()=>{if(window.IsDisposed)return;bar.Value=Math.Max(0,Math.Min(100,percent));label.Text=message;}),prior,
      (frameFile,maskFile,index)=>{try{var bitmap=Sam2ProgressImage01023(frameFile,maskFile);UI(()=>{if(window.IsDisposed){bitmap.Dispose();return;}var previous=picture.Image;picture.Image=bitmap;if(previous!=null)previous.Dispose();});}catch{}});
     if(prior!=null){result.target=prior.target;result.composite_inverted=prior.composite_inverted;foreach(var layer in result.layers){var old=Sam2MaskStore090.Layers(prior).FirstOrDefault(x=>x.object_id==layer.object_id);if(old!=null){layer.enabled=old.enabled;layer.overlay_visible=old.overlay_visible;layer.inverted=old.inverted;layer.feather=old.feather;layer.blend=old.blend;}}}
     UI(()=>{busy=false;sam2Token090=null;window.Close();window.Dispose();try{if(editor.Project.id!=snapshot.id||editor.Project.revision!=revision)throw new Exception("El proyecto cambió durante el recorte. Vuelve a calcularlo.");Edit(()=>editor.Commit(p=>Sam2MaskStore090.Put(MaskClip(p,id),result)));status.Text="Recorte inteligente listo · "+result.frames+" fotogramas";}catch(Exception ex){Error(ex);}});
    }catch(OperationCanceledException){UI(()=>{busy=false;sam2Token090=null;if(!window.IsDisposed){window.Close();window.Dispose();}status.Text="Recorte cancelado";});}
     catch(Exception ex){UI(()=>{busy=false;sam2Token090=null;if(!window.IsDisposed){window.Close();window.Dispose();}Error(ex);});}});
   }catch(Exception ex){busy=false;Error(ex);}
  }
 }
}
