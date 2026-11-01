using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  bool maskToolActive;string activeMaskChoice;Bitmap maskOverlayCache01058;string maskOverlayKey01058;
  void BeginMaskTool(string choice){try{if(maskToolActive&&activeMaskChoice==choice){SetTool(Timeline.ToolMode.Select);return;}var clip=MaskClip(editor.Project,timeline.Selected);StopMaskPick01061();monitor.CancelMaskEdit(false);textToolActive=false;monitor.TextToolActive=false;timeline.ActiveTool=Timeline.ToolMode.Select;maskToolActive=true;activeMaskChoice=choice;UpdateMouseButtons();if(choice=="sam2")BeginSmartCut091(clip.id);else BeginMonitorMask(clip.id,choice);}catch(Exception ex){SetTool(Timeline.ToolMode.Select);Error(ex);}}
  void SetupMasks(){
   effectControlsView.MaskShapeRequested=(id,shape)=>BeginMonitorMask(id,shape);
   effectControlsView.MaskMonitorEditRequested=id=>BeginMonitorMask(id,null);
   effectControlsView.MaskEditRequested=id=>EditMask(id,null);
   effectControlsView.MaskRemoveRequested=id=>{try{Edit(()=>editor.Commit(p=>{var clip=MaskClip(p,id);if(VectorMaskStack0104.SelectedId(clip)==1)TemporalMasks0823.Put(clip,null);VectorMaskStack0104.RemoveSelected(clip);}));}catch(Exception ex){Error(ex);}};
   effectControlsView.VectorMaskSelected=(id,layerId)=>{try{Edit(()=>editor.Commit(p=>VectorMaskStack0104.Select(MaskClip(p,id),layerId)));}catch(Exception ex){Error(ex);}};
   effectControlsView.VectorMaskVisibleChanged=(id,layerId,value)=>{try{Edit(()=>editor.Commit(p=>VectorMaskStack0104.Update(MaskClip(p,id),layerId,layer=>layer.overlay_visible=value)));}catch(Exception ex){Error(ex);}};
   effectControlsView.VectorMaskBlendChanged=(id,layerId,value)=>{try{Edit(()=>editor.Commit(p=>VectorMaskStack0104.Update(MaskClip(p,id),layerId,layer=>layer.blend=value)));}catch(Exception ex){Error(ex);}};
   effectControlsView.VectorMaskCompositeInvertedChanged=(id,value)=>{try{Edit(()=>editor.Commit(p=>{var clip=MaskClip(p,id);var stack=VectorMaskStack0104.Get(clip);stack.composite_inverted=value;VectorMaskStack0104.Put(clip,stack);}));}catch(Exception ex){Error(ex);}};
   effectControlsView.MaskEnabledChanged=(id,value)=>ChangeMask(id,m=>m.enabled=value);
   effectControlsView.MaskInvertedChanged=(id,value)=>ChangeMask(id,m=>m.inverted=value);
   effectControlsView.MaskFeatherChanged=(id,value)=>ChangeMask(id,m=>m.feather=value);
   effectControlsView.MaskAnimationEnabledChanged=(id,value)=>{try{Edit(()=>editor.Commit(p=>{var clip=MaskClip(p,id);if(clip.mask==null)throw new Exception("Crea primero una máscara");var track=TemporalMasks0823.Get(clip);if(value){if(track==null||track.points.Count==0)TemporalMasks0823.Set(clip,timeline.Cursor,clip.mask);else{track.enabled=true;TemporalMasks0823.Put(clip,track);}}else if(track!=null){track.enabled=false;TemporalMasks0823.Put(clip,track);}}));}catch(Exception ex){Error(ex);}};
   effectControlsView.MaskKeyframeRequested=id=>{try{Edit(()=>editor.Commit(p=>{var clip=MaskClip(p,id);TemporalMasks0823.Set(clip,timeline.Cursor,TemporalMasks0823.At(clip,timeline.Cursor));}));}catch(Exception ex){Error(ex);}};
   effectControlsView.MaskInterpolationChanged=(id,value)=>{try{Edit(()=>editor.Commit(p=>{var clip=MaskClip(p,id);var track=TemporalMasks0823.Get(clip);if(track==null)throw new Exception("Activa la animación de máscara");track.interpolation=value;TemporalMasks0823.Put(clip,track);}));}catch(Exception ex){Error(ex);}};
   effectControlsView.Sam2PickRequested=BeginSam2Pick090;
   effectControlsView.Sam2TrackRequested=TrackDraft01061;
   effectControlsView.Sam2EnabledChanged=(id,value)=>{try{Edit(()=>editor.Commit(p=>{var clip=MaskClip(p,id);var track=Sam2MaskStore090.Get(clip);if(track==null)throw new Exception("No hay seguimiento SAM2");if(value&&!Sam2MaskStore090.Ready(clip))throw new Exception("La máscara cambió con el MGT o faltan sus datos; vuelve a calcularla");track.enabled=value;Sam2MaskStore090.Put(clip,track);}));RefreshActiveMaskPick01023(id);ShowSam2Overlay0104(id);}catch(Exception ex){Error(ex);ShowClip();}};
   effectControlsView.Sam2RemoveRequested=id=>{try{SetTool(Timeline.ToolMode.Select);Edit(()=>editor.Commit(p=>Sam2MaskStore090.Put(MaskClip(p,id),null)));}catch(Exception ex){Error(ex);}};
   effectControlsView.Sam2LayerEnabledChanged=(id,objectId,value)=>ChangeSam2Layer0104(id,objectId,layer=>layer.enabled=value);
   effectControlsView.Sam2LayerVisibleChanged=(id,objectId,value)=>ChangeSam2Layer0104(id,objectId,layer=>layer.overlay_visible=value);
   effectControlsView.Sam2LayerInvertedChanged=(id,objectId,value)=>ChangeSam2Layer0104(id,objectId,layer=>layer.inverted=value);
   effectControlsView.Sam2LayerFeatherChanged=(id,objectId,value)=>ChangeSam2Layer0104(id,objectId,layer=>layer.feather=value);

   effectControlsView.Sam2LayerBlendChanged=(id,objectId,value)=>ChangeSam2Layer0104(id,objectId,layer=>layer.blend=value);
   effectControlsView.Sam2CompositeInvertedChanged=(id,value)=>{try{Edit(()=>editor.Commit(p=>{var track=Sam2MaskStore090.Get(MaskClip(p,id));if(track==null)throw new Exception("No hay máscara inteligente");track.composite_inverted=value;Sam2MaskStore090.Put(MaskClip(p,id),track);}));RefreshActiveMaskPick01023(id);ShowSam2Overlay0104(id);}catch(Exception ex){Error(ex);}};
   effectControlsView.MaskTargetChanged=(id,sam2,target)=>{try{Edit(()=>editor.Commit(p=>TemporalMasks0823.SetTarget(MaskClip(p,id),target,sam2)));}catch(Exception ex){Error(ex);}};
  }
  void ChangeSam2Layer0104(string id,int objectId,Action<Sam2Layer0104> change){try{Edit(()=>editor.Commit(p=>{var clip=MaskClip(p,id);var track=Sam2MaskStore090.Get(clip);if(track==null)throw new Exception("No hay máscara inteligente");track.layers=Sam2MaskStore090.Layers(track);var layer=track.layers.FirstOrDefault(x=>x.object_id==objectId);if(layer==null)throw new Exception("La máscara ya no existe");change(layer);Sam2MaskStore090.Put(clip,track);}));RefreshActiveMaskPick01023(id);ShowSam2Overlay0104(id);}catch(Exception ex){Error(ex);}}
  void ShowSam2Overlay0104(string id){
   if(monitor.ObjectPickActive)return;monitor.ObjectMaskHasSelection=false;monitor.ObjectMaskContextRequested=null;if(!maskToolActive||playing||id==null||timeline.Selected!=id){monitor.ShowObjectOverlay(null);return;}
   var clip=editor.Project.Active.clips.Find(c=>c.id==id);if(clip==null){monitor.ShowObjectOverlay(null);return;}var data=Sam2MaskStore090.Get(clip);
   int width=Math.Max(2,Math.Min(960,editor.Project.Active.width)),height=Math.Max(2,(int)Math.Round(width*(double)editor.Project.Active.height/editor.Project.Active.width));var files=new System.Collections.Generic.List<string>();
   if(data!=null&&Sam2MaskStore090.Ready(clip)){int frame=data.static_preview?0:Sam2MaskStore090.FirstFrame(clip,data)+(int)Math.Round((timeline.Cursor-clip.timeline_start.Seconds)*data.fps);if(frame>=0&&frame<data.frames)foreach(var layer in Sam2MaskStore090.Layers(data).Where(x=>x.overlay_visible)){string file=Path.Combine(Sam2MaskStore090.LayerDirectory(data,layer),frame.ToString("D6")+".png");if(File.Exists(file))files.Add(file);}}
   foreach(var layer in VectorMaskStack0104.All(clip).Where(x=>x.overlay_visible&&x.mask!=null&&x.mask.enabled)){var mask=layer.id==1&&TemporalMasks0823.HasAnimation(clip)?TemporalMasks0823.At(clip,timeline.Cursor):layer.mask;if(mask!=null&&mask.enabled)files.Add(ClipMaskRaster.RenderFile(mask,width,height));}
   if(files.Count==0){monitor.ShowObjectOverlay(null);return;}
   string cacheKey=id+"|"+width+"x"+height+"|"+String.Join("|",files.Select(file=>file+":"+File.GetLastWriteTimeUtc(file).Ticks+":"+new FileInfo(file).Length));
   if(maskOverlayCache01058!=null&&cacheKey==maskOverlayKey01058){DisplayMaskOverlay01058(id,data,maskOverlayCache01058);return;}
   var pixels=new byte[width*height*4];
   foreach(string file in files)using(var source=new Bitmap(file))using(var bitmap=new Bitmap(width,height,PixelFormat.Format32bppArgb)){using(var graphics=Graphics.FromImage(bitmap))graphics.DrawImage(source,0,0,width,height);var rect=new Rectangle(0,0,width,height);var bits=bitmap.LockBits(rect,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);try{var raw=new byte[Math.Abs(bits.Stride)*height];Marshal.Copy(bits.Scan0,raw,0,raw.Length);for(int y=0;y<height;y++)for(int x=0;x<width;x++){int sourceIndex=y*bits.Stride+x*4,target=(y*width+x)*4;if(raw[sourceIndex+2]>127){pixels[target]=44;pixels[target+1]=156;pixels[target+2]=255;pixels[target+3]=92;}}}finally{bitmap.UnlockBits(bits);}}
   var overlay=new Bitmap(width,height,PixelFormat.Format32bppArgb);var overlayRect=new Rectangle(0,0,width,height);var overlayBits=overlay.LockBits(overlayRect,ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);try{Marshal.Copy(pixels,0,overlayBits.Scan0,pixels.Length);}finally{overlay.UnlockBits(overlayBits);}
   if(maskOverlayCache01058!=null)maskOverlayCache01058.Dispose();maskOverlayCache01058=overlay;maskOverlayKey01058=cacheKey;DisplayMaskOverlay01058(id,data,overlay);
  }
  void DisplayMaskOverlay01058(string id,Sam2MaskTrack090 data,Bitmap overlay){monitor.ShowObjectOverlay(overlay);if(data!=null){monitor.ObjectMaskHasSelection=true;monitor.ObjectMaskContextRequested=point=>ShowMaskTargetContext01061(id,point);}}
  static Clip MaskClip(Project project,string id){var clip=project.Active.clips.Find(c=>c.id==id);var track=clip==null?null:project.Active.tracks.Find(t=>t.id==clip.track_id);if(track==null||track.kind!="video"||track.locked)throw new Exception("Selecciona un clip visual desbloqueado");return clip;}
  void ChangeMask(string id,Action<ClipMask> change){try{Edit(()=>editor.Commit(p=>{var clip=MaskClip(p,id);int layerId=VectorMaskStack0104.SelectedId(clip);var mask=layerId==1&&TemporalMasks0823.HasAnimation(clip)?TemporalMasks0823.At(clip,timeline.Cursor):VectorMaskStack0104.Selected(clip);if(mask==null)throw new Exception("La máscara ya no existe");change(mask);if(layerId==1&&TemporalMasks0823.HasAnimation(clip))TemporalMasks0823.Set(clip,timeline.Cursor,mask);else VectorMaskStack0104.Update(clip,layerId,layer=>layer.mask=mask);}));}catch(Exception ex){Error(ex);ShowClip();}}
  void BeginMonitorMask(string id,string shape){
   try{var clip=MaskClip(editor.Project,id);int selected=VectorMaskStack0104.SelectedId(clip);var mask=shape==null?(selected==1?TemporalMasks0823.At(clip,timeline.Cursor):VectorMaskStack0104.Selected(clip)):ClipMasks.New(shape);if(mask==null)throw new Exception("No hay una máscara para editar");mask=Store.Json().Deserialize<ClipMask>(Store.Json().Serialize(Store.Fields(mask)));if(shape!=null)mask.points.Clear();string blend=(ModifierKeys&Keys.Shift)==Keys.Shift?"subtract":(ModifierKeys&Keys.Control)==Keys.Control?"intersect":"add";
    Stop();StopMaskPick01061();var unmasked=Store.Copy(editor.Project);var unmaskedClip=unmasked.Active.clips.Find(c=>c.id==id);unmaskedClip.mask=null;VectorMaskStack0104.Put(unmaskedClip,null);Sam2MaskStore090.Put(unmaskedClip,null);RequestFrameFor(unmasked);
    monitor.BeginMaskEdit(mask,result=>{try{var copy=Store.Json().Deserialize<ClipMask>(Store.Json().Serialize(Store.Fields(result)));Edit(()=>editor.Commit(p=>{var target=MaskClip(p,id);if(shape!=null)VectorMaskStack0104.Add(target,copy,blend);else if(selected==1&&TemporalMasks0823.HasAnimation(target))TemporalMasks0823.Set(target,timeline.Cursor,copy);else VectorMaskStack0104.Update(target,selected,layer=>layer.mask=copy);}));SetTool(Timeline.ToolMode.Select);status.Text="Máscara aplicada desde el monitor";}catch(Exception ex){SetTool(Timeline.ToolMode.Select);Error(ex);RequestFrame();}},()=>{SetTool(Timeline.ToolMode.Select);RequestFrame();status.Text="Edición de máscara cancelada";});
    status.Text=mask.shape=="polygon"?"Añade puntos en el monitor · Intro aplica · Esc cancela":"Arrastra la forma en el monitor · Esc cancela";
   }catch(Exception ex){Error(ex);}
  }
  void EditMask(string id,string shape){
   try{
    var current=MaskClip(editor.Project,id);int selected=VectorMaskStack0104.SelectedId(current);var proposed=shape==null?(selected==1?TemporalMasks0823.At(current,timeline.Cursor):VectorMaskStack0104.Selected(current)):ClipMasks.New(shape);
    if(proposed==null)throw new Exception("No hay una máscara para editar");
    Bitmap background=null;
    try{var snapshot=Store.Copy(editor.Project);var clean=snapshot.Active.clips.Find(c=>c.id==id);clean.mask=null;VectorMaskStack0104.Put(clean,null);Sam2MaskStore090.Put(clean,null);
     int[] size=SequenceCanvas.PreviewSize(snapshot.Active,640);double time=Math.Max(current.timeline_start.Seconds,Math.Min(timeline.Cursor,current.End-1.0/60));
     background=Rendering.FrameExact(snapshot,time,CancellationToken.None,size[0],size[1],true);
    }catch{background=null;}
    using(background)using(var dialog=new MaskEditorDialog(proposed,background)){
     if(dialog.ShowDialog(this)!=DialogResult.OK)return;
     var result=Store.Json().Deserialize<ClipMask>(Store.Json().Serialize(Store.Fields(dialog.Result)));
     Edit(()=>editor.Commit(p=>{var target=MaskClip(p,id);if(shape!=null)VectorMaskStack0104.Add(target,result,"add");else if(selected==1&&TemporalMasks0823.HasAnimation(target))TemporalMasks0823.Set(target,timeline.Cursor,result);else VectorMaskStack0104.Update(target,selected,layer=>layer.mask=result);}));
     status.Text="Máscara aplicada a "+current.name;
    }
   }catch(Exception ex){Error(ex);}
  }
 }
}
