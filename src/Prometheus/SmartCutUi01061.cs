using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  CancellationTokenSource maskCandidates01061;
  System.Windows.Forms.Timer maskCandidateDelay0107;
  string pickingMaskClip01061;
  int pickingMaskFrame01061,maskPickGeneration01061;
  void StopMaskPick01061(){maskPickGeneration01061++;if(maskCandidateDelay0107!=null){maskCandidateDelay0107.Stop();maskCandidateDelay0107.Dispose();maskCandidateDelay0107=null;}if(maskCandidates01061!=null){maskCandidates01061.Cancel();maskCandidates01061.Dispose();maskCandidates01061=null;}pickingMaskClip01061=null;monitor.ProcessingStatus=null;monitor.CancelObjectPick(false);monitor.Invalidate();}
  void BeginSmartCut091(string id){BeginSmartCut091(id,true);}
  void BeginSmartCut091(string id,bool stopPlayback){
   try{
    if(busy)throw new Exception("Espera a que termine la tarea actual");var clip=MaskClip(editor.Project,id);var asset=editor.Project.assets.First(a=>a.id==clip.asset_id);var stream=asset.streams.First(s=>s.id==clip.stream_id);
    if(stream.kind!="video"&&stream.kind!="image")throw new Exception("Recorte inteligente necesita una imagen o un clip de vídeo");if(stopPlayback&&!Sam2SourceSettings01034.Confirm(this,asset))return;
    if(stopPlayback)Stop();StopMaskPick01061();monitor.TextToolActive=false;pickingMaskClip01061=id;
    int frame=stream.kind=="image"?0:Math.Max(0,Math.Min((int)Math.Ceiling(clip.duration.Seconds*60)-1,(int)Math.Round((timeline.Cursor-clip.timeline_start.Seconds)*60)));pickingMaskFrame01061=frame;
    var prior=Sam2MaskStore090.Get(clip);bool selected=prior!=null&&prior.static_preview&&prior.anchor_frame==frame&&Sam2MaskStore090.Ready(clip);monitor.ObjectMaskHasSelection=selected;
    monitor.BeginObjectPickWithKeys((x,y,keys)=>{
     if(pickingMaskClip01061!=id||pickingMaskFrame01061!=frame)return;
     bool subtract=selectedOrDraft01061(id,frame)&&(keys&Keys.Alt)==Keys.Alt;var candidate=monitor.ObjectCandidateAt(x,y);
     if(candidate!=null){using(var mask=new Bitmap(candidate.Mask))AddDraftMask01061(id,frame,x,y,subtract,mask);return;}
     status.Text="Calculando selección en el punto elegido…";monitor.ProcessingStatus="Calculando selección…";monitor.Invalidate();
     var snapshot=Store.Copy(editor.Project);var task=new CancellationTokenSource();int generation=maskPickGeneration01061;
     Task.Factory.StartNew(()=>{try{using(var mask=Sam2Runner090.Preview(snapshot,id,frame,new List<Sam2Prompt090>{new Sam2Prompt090{frame=frame,x=x,y=y,positive=true,object_id=1}},task.Token)){var copy=new Bitmap(mask);UI(()=>{using(copy){if(pickingMaskClip01061!=id||generation!=maskPickGeneration01061)return;monitor.ProcessingStatus=null;AddDraftMask01061(id,frame,x,y,subtract,copy);}});}}catch(Exception ex){UI(()=>{if(pickingMaskClip01061==id&&generation==maskPickGeneration01061){monitor.ProcessingStatus=null;status.Text="No se pudo detectar esa zona: "+ex.Message;monitor.Invalidate();}});}finally{task.Dispose();}});
    },()=>{SetTool(Timeline.ToolMode.Select);status.Text="Selección de máscara cerrada";});
    monitor.ObjectMaskContextRequested=point=>ShowMaskTargetContext01061(id,point);
    if(selected)using(var composed=ComposeDraft01061(prior)){monitor.ShowObjectPreview(composed);}
    monitor.ProcessingStatus=null;monitor.Invalidate();status.Text="Detente un momento sobre el fotograma para analizar sus objetos";
    int run=++maskPickGeneration01061;var idle=new System.Windows.Forms.Timer{Interval=1400};maskCandidateDelay0107=idle;
    idle.Tick+=(sender,args)=>{idle.Stop();if(maskCandidateDelay0107==idle)maskCandidateDelay0107=null;idle.Dispose();if(!maskToolActive||pickingMaskClip01061!=id||run!=maskPickGeneration01061)return;monitor.ProcessingStatus="Analizando fotograma…";monitor.Invalidate();status.Text="Analizando objetos del fotograma…";var token=new CancellationTokenSource();maskCandidates01061=token;var project=Store.Copy(editor.Project);
     Task.Factory.StartNew(()=>{try{var candidates=Sam2Runner090.Candidates(project,id,frame,token.Token);UI(()=>{if(pickingMaskClip01061!=id||run!=maskPickGeneration01061||token.IsCancellationRequested){foreach(var c in candidates)c.Dispose();return;}monitor.SetObjectCandidates(candidates);monitor.ProcessingStatus=null;monitor.Invalidate();status.Text=candidates.Count==0?"No se detectaron objetos · haz clic para calcular una zona":"Objetos listos · clic selecciona · Alt cambia a restar · clic derecho: Asignar a";});}catch(OperationCanceledException){}catch(Exception ex){UI(()=>{if(pickingMaskClip01061==id&&run==maskPickGeneration01061){monitor.ProcessingStatus=null;monitor.Invalidate();status.Text="Vista automática no disponible · clic calcula una zona: "+ex.Message;}});}});
    };idle.Start();
    if(stopPlayback)monitor.Focus();
   }catch(Exception ex){Error(ex);}
  }
  void MaskCursorChanged0107(){if(!maskToolActive||activeMaskChoice!="sam2"||pickingMaskClip01061==null)return;var clip=editor.Project.Active.clips.Find(c=>c.id==pickingMaskClip01061);if(clip==null){StopMaskPick01061();return;}var asset=editor.Project.assets.Find(a=>a.id==clip.asset_id);var stream=asset==null?null:asset.streams.Find(s=>s.id==clip.stream_id);int frame=stream!=null&&stream.kind=="image"?0:Math.Max(0,Math.Min((int)Math.Ceiling(clip.duration.Seconds*60)-1,(int)Math.Round((timeline.Cursor-clip.timeline_start.Seconds)*60)));if(frame!=pickingMaskFrame01061)BeginSmartCut091(clip.id,false);}
  void MaskSelectionChanged0107(){if(!maskToolActive||activeMaskChoice!="sam2"||pickingMaskClip01061==null||timeline.Selected==pickingMaskClip01061)return;StopMaskPick01061();var clip=editor.Project.Active.clips.Find(c=>c.id==timeline.Selected);var track=clip==null?null:editor.Project.Active.tracks.Find(t=>t.id==clip.track_id);if(track==null||track.kind!="video"||track.locked){SetTool(Timeline.ToolMode.Select);return;}BeginSmartCut091(clip.id);}
  bool selectedOrDraft01061(string id,int frame){var clip=editor.Project.Active.clips.Find(c=>c.id==id);var data=clip==null?null:Sam2MaskStore090.Get(clip);return data!=null&&data.static_preview&&data.anchor_frame==frame&&Sam2MaskStore090.Ready(clip);}
  void RefreshActiveMaskPick01023(string id){if(!monitor.ObjectPickActive||pickingMaskClip01061!=id)return;var clip=editor.Project.Active.clips.Find(c=>c.id==id);var data=clip==null?null:Sam2MaskStore090.Get(clip);if(data==null||!data.static_preview||!data.enabled||!Sam2MaskStore090.Ready(clip)||!Sam2MaskStore090.Layers(data).Any(layer=>layer.enabled)){monitor.ObjectMaskHasSelection=false;monitor.ShowObjectPreview(null);return;}using(var composed=ComposeDraft01061(data))monitor.ShowObjectPreview(composed);monitor.ObjectMaskHasSelection=true;}
  void AddDraftMask01061(string id,int frame,double x,double y,bool subtract,Bitmap mask){
   string directory=null;try{
    var clip=MaskClip(editor.Project,id);var prior=Sam2MaskStore090.Get(clip);bool keep=prior!=null&&prior.static_preview&&prior.anchor_frame==frame&&Sam2MaskStore090.Ready(clip)&&prior.width==mask.Width&&prior.height==mask.Height;
    directory=Path.Combine(Sam2MaskStore090.DirectoryFor(projectFile,editor.Project.id,id),"draft-"+Store.Id().Substring(0,12));Directory.CreateDirectory(directory);
    var layers=new List<Sam2Layer0104>();var prompts=new List<Sam2Prompt090>();if(keep){foreach(var old in Sam2MaskStore090.Layers(prior)){string target=Path.Combine(directory,"object-"+old.object_id);Directory.CreateDirectory(target);File.Copy(Path.Combine(Sam2MaskStore090.LayerDirectory(prior,old),"000000.png"),Path.Combine(target,"000000.png"));layers.Add(Store.Json().Deserialize<Sam2Layer0104>(Store.Json().Serialize(Store.Fields(old))));}prompts.AddRange(prior.prompts);}
    int objectId=layers.Count==0?1:layers.Max(layer=>layer.object_id)+1;if(objectId>64)throw new Exception("Esta máscara ya tiene demasiadas zonas");string folder=Path.Combine(directory,"object-"+objectId);Directory.CreateDirectory(folder);mask.Save(Path.Combine(folder,"000000.png"),ImageFormat.Png);
    layers.Add(new Sam2Layer0104{object_id=objectId,blend=keep&&subtract?"subtract":"add",overlay_visible=true});prompts.Add(new Sam2Prompt090{frame=frame,x=x,y=y,positive=true,object_id=objectId});
    var data=new Sam2MaskTrack090{directory=directory,frames=1,width=mask.Width,height=mask.Height,fps=60,static_preview=true,anchor_frame=frame,source_in=clip.source_in.Seconds,duration=clip.duration.Seconds,speed=clip.speed.Seconds,target=keep?prior.target:"unassigned",composite_inverted=keep&&prior.composite_inverted,layers=layers,prompts=prompts};
    using(var composed=ComposeDraft01061(data)){composed.Save(Path.Combine(directory,"000000.png"),ImageFormat.Png);Edit(()=>editor.Commit(p=>Sam2MaskStore090.Put(MaskClip(p,id),data)));monitor.ObjectMaskHasSelection=true;monitor.ShowObjectPreview(composed);}
    monitor.ProcessingStatus=null;monitor.Invalidate();status.Text="Máscara seleccionada en este fotograma · ▶ Seguir en Controles de efectos";
   }catch(Exception ex){if(directory!=null)try{Directory.Delete(directory,true);}catch{}Error(ex);}
  }
  static Bitmap ComposeDraft01061(Sam2MaskTrack090 data){
   int width=data.width,height=data.height;var result=new byte[width*height];bool first=true;
   foreach(var layer in Sam2MaskStore090.Layers(data).Where(x=>x.enabled))using(var image=new Bitmap(Path.Combine(Sam2MaskStore090.LayerDirectory(data,layer),"000000.png")))using(var scaled=new Bitmap(width,height,PixelFormat.Format32bppArgb)){
    using(var graphics=Graphics.FromImage(scaled))graphics.DrawImage(image,0,0,width,height);var bounds=new Rectangle(0,0,width,height);var bits=scaled.LockBits(bounds,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);try{var raw=new byte[bits.Stride*height];Marshal.Copy(bits.Scan0,raw,0,raw.Length);var values=new byte[width*height];for(int y=0;y<height;y++)for(int x=0;x<width;x++)values[y*width+x]=raw[y*bits.Stride+x*4+2];int radius=(int)Math.Round(Math.Min(100,layer.feather*Math.Min(width,height)));if(radius>0)values=BlurMask01034(values,width,height,radius);for(int i=0;i<values.Length;i++){int value=layer.inverted?255-values[i]:values[i];if(first||layer.blend=="add")result[i]=(byte)Math.Max(result[i],value);else if(layer.blend=="subtract")result[i]=(byte)Math.Max(0,result[i]-value);else result[i]=(byte)Math.Min(result[i],value);}}finally{scaled.UnlockBits(bits);}first=false;
   }
   var output=new Bitmap(width,height,PixelFormat.Format32bppArgb);var rect=new Rectangle(0,0,width,height);var outputBits=output.LockBits(rect,ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);try{var raw=new byte[outputBits.Stride*height];for(int y=0;y<height;y++)for(int x=0;x<width;x++){byte value=data.composite_inverted?(byte)(255-result[y*width+x]):result[y*width+x];int index=y*outputBits.Stride+x*4;raw[index]=raw[index+1]=raw[index+2]=value;raw[index+3]=255;}Marshal.Copy(raw,0,outputBits.Scan0,raw.Length);}finally{output.UnlockBits(outputBits);}return output;
  }
  static byte[] BlurMask01034(byte[] source,int width,int height,int radius){
   // Three separable box passes approximate the Gaussian softness used by the final render.
   for(int pass=0;pass<3;pass++){
    var horizontal=new byte[source.Length];
    for(int y=0;y<height;y++){int row=y*width,sum=0;for(int x=-radius;x<=radius;x++)sum+=source[row+Math.Max(0,Math.Min(width-1,x))];int size=radius*2+1;for(int x=0;x<width;x++){horizontal[row+x]=(byte)(sum/size);sum-=source[row+Math.Max(0,x-radius)];sum+=source[row+Math.Min(width-1,x+radius+1)];}}
    var vertical=new byte[source.Length];
    for(int x=0;x<width;x++){int sum=0;for(int y=-radius;y<=radius;y++)sum+=horizontal[Math.Max(0,Math.Min(height-1,y))*width+x];int size=radius*2+1;for(int y=0;y<height;y++){vertical[y*width+x]=(byte)(sum/size);sum-=horizontal[Math.Max(0,y-radius)*width+x];sum+=horizontal[Math.Min(height-1,y+radius+1)*width+x];}}
    source=vertical;
   }
   return source;
  }
  void TrackDraft01061(string id){try{var clip=MaskClip(editor.Project,id);var data=Sam2MaskStore090.Get(clip);if(data==null||!data.static_preview||!Sam2MaskStore090.Ready(clip))throw new Exception("Selecciona primero el objeto en un fotograma");var asset=editor.Project.assets.Find(a=>a.id==clip.asset_id);var stream=asset==null?null:asset.streams.Find(s=>s.id==clip.stream_id);if(stream!=null&&stream.kind=="image"){status.Text="La máscara de esta imagen permanece fija durante todo el clip";return;}StopMaskPick01061();RunSmartCut091(id,data.prompts.Select(p=>new Sam2Prompt090{frame=p.frame,x=p.x,y=p.y,positive=p.positive,object_id=p.object_id}).ToList(),data);}catch(Exception ex){Error(ex);}}
  void ShowMaskTargetContext01061(string id,Point point){var clip=editor.Project.Active.clips.Find(c=>c.id==id);if(clip==null||Sam2MaskStore090.Get(clip)==null)return;var menu=new ContextMenuStrip();var assign=new ToolStripMenuItem("Asignar a");assign.DropDownItems.Add("Sin asignar",null,(s,e)=>AssignMaskTarget01061(id,"unassigned"));assign.DropDownItems.Add("Opacidad",null,(s,e)=>AssignMaskTarget01061(id,"opacity"));foreach(var effect in EffectContracts.Effective(editor.Project,clip)){var contract=EffectContracts.Find(effect.effect_id,effect.contract_version);if(contract==null||contract.input_kind!="video"||effect.effect_id==EffectContracts.ZoomInDynamicId||effect.effect_id==EffectContracts.ZoomOutDynamicId||effect.effect_id==EffectContracts.PanDynamicId)continue;string effectId=effect.id;assign.DropDownItems.Add(contract.name,null,(s,e)=>AssignMaskTarget01061(id,"effect:"+effectId));}menu.Items.Add(assign);var data=Sam2MaskStore090.Get(clip);var asset=editor.Project.assets.Find(a=>a.id==clip.asset_id);var stream=asset==null?null:asset.streams.Find(s=>s.id==clip.stream_id);if(data.static_preview&&stream!=null&&stream.kind!="image")menu.Items.Add("▶ Seguir objeto",null,(s,e)=>TrackDraft01061(id));menu.Closed+=(s,e)=>BeginInvoke((Action)(()=>menu.Dispose()));menu.Show(monitor,point);}
  void AssignMaskTarget01061(string id,string target){try{Edit(()=>editor.Commit(p=>TemporalMasks0823.SetTarget(MaskClip(p,id),target,true)));status.Text="Máscara asignada · Ctrl+Z para deshacer";}catch(Exception ex){Error(ex);}}
 }
}
