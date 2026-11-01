using System;
using System.Drawing;
using System.Linq;

namespace Prometheus {
 public partial class MainForm {
  string monitorTransformClipId;
  string selectedTransformParameter;
  void SetupMonitorTransform(){monitor.TransformEdited=EditMonitorTransform;monitor.ViewMoved=RefreshMonitorTransform;monitorView.ViewChanged=RefreshMonitorTransform;monitor.Resize+=(s,e)=>RefreshMonitorTransform();effectControlsView.TransformParameterSelected=SelectTransformParameter;keyframeView.ActiveTargetChanged=target=>{if(target=="transform.scale"||target=="transform.scale_y"||target=="transform.x"||target=="transform.y")SelectTransformParameter(target);};}
  void SelectTransformParameter(string target){selectedTransformParameter=target;monitor.TransformMode=target.StartsWith("transform.scale")?"scale":"position";keyframeView.SelectTarget(target);RefreshMonitorTransform();status.Text=target.StartsWith("transform.scale")?"Arrastra una esquina en el monitor para cambiar Escala":"Arrastra el medio en el monitor para cambiar Posición";}
  void RefreshMonitorTransform(){
   if(monitor==null||editor==null||timeline==null)return;
   var clip=selectedTransitionId==null&&selectedEffectTrackId==null?editor.Project.Active.clips.Find(c=>c.id==timeline.Selected):null;
   var track=clip==null?null:editor.Project.Active.tracks.Find(t=>t.id==clip.track_id);
   if(clip==null||track==null||track.kind!="video"||timeline.Cursor<clip.timeline_start.Seconds||timeline.Cursor>=clip.End){monitorTransformClipId=null;selectedTransformParameter=null;monitor.TransformMode="all";monitor.SetTransform(null,1,0,0,false);return;}
   var asset=editor.Project.assets.Find(a=>a.id==clip.asset_id);var stream=asset==null?null:asset.streams.Find(s=>s.id==clip.stream_id);
   if(stream==null){monitorTransformClipId=null;monitor.SetTransform(null,1,0,0,false);return;}
   var frame=ClipFraming.Read(clip);double scale=ClipKeyframes.Value(clip,"transform.scale",timeline.Cursor,frame.scale),scaleY=frame.scaleLinked?scale:ClipKeyframes.Value(clip,"transform.scale_y",timeline.Cursor,frame.scaleY);
   double x=ClipKeyframes.Value(clip,"transform.x",timeline.Cursor,frame.x),y=ClipKeyframes.Value(clip,"transform.y",timeline.Cursor,frame.y);
   var canvas=monitor.ImageBounds();double sourceWidth=stream.width.GetValueOrDefault(editor.Project.Active.width),sourceHeight=stream.height.GetValueOrDefault(editor.Project.Active.height);
   if(sourceWidth<1||sourceHeight<1){sourceWidth=editor.Project.Active.width;sourceHeight=editor.Project.Active.height;}
   double baseScale=frame.mode=="native"?canvas.Width/editor.Project.Active.width:frame.mode=="fill"?Math.Max(canvas.Width/sourceWidth,canvas.Height/sourceHeight):Math.Min(canvas.Width/sourceWidth,canvas.Height/sourceHeight);
   float width=(float)(sourceWidth*baseScale*scale),height=(float)(sourceHeight*baseScale*scaleY);
   float centerX=canvas.Left+canvas.Width/2+(float)(x*canvas.Width),centerY=canvas.Top+canvas.Height/2+(float)(y*canvas.Height);
   if(monitorTransformClipId!=clip.id){selectedTransformParameter=null;monitor.TransformMode="all";}
   monitorTransformClipId=clip.id;monitor.SetTransform(new RectangleF(centerX-width/2,centerY-height/2,width,height),scale,x,y,track.locked);
  }
  static void SetMonitorTransform(Project project,string clipId,double time,double scale,double x,double y,string gesture){
   var clip=project.Active.clips.Find(c=>c.id==clipId);if(clip==null)throw new Exception("Clip no disponible");
   var track=project.Active.tracks.Find(t=>t.id==clip.track_id);if(track==null||track.locked)throw new Exception("La pista está bloqueada");
   var frame=ClipFraming.Read(clip);double baseScale=frame.scale,baseX=frame.x,baseY=frame.y;
   if(gesture=="scale"){double oldX=ClipKeyframes.Value(clip,"transform.scale",time,frame.scale);double oldY=frame.scaleLinked?oldX:ClipKeyframes.Value(clip,"transform.scale_y",time,frame.scaleY);double nextY=Math.Max(0.25,Math.Min(4,oldY*scale/Math.Max(0.00001,oldX)));if(ClipKeyframes.Active(clip,"transform.scale")){CheckAnimationPosition(clip,time);SetAnimatedValue(clip,"transform.scale",time,scale);}else baseScale=scale;if(!frame.scaleLinked){if(ClipKeyframes.Active(clip,"transform.scale_y")){CheckAnimationPosition(clip,time);SetAnimatedValue(clip,"transform.scale_y",time,nextY);}else ClipFraming.SetScaleY(clip,nextY);}}
   else {if(ClipKeyframes.Active(clip,"transform.x")){CheckAnimationPosition(clip,time);SetAnimatedValue(clip,"transform.x",time,x);}else baseX=x;
    if(ClipKeyframes.Active(clip,"transform.y")){CheckAnimationPosition(clip,time);SetAnimatedValue(clip,"transform.y",time,y);}else baseY=y;}
   ClipFraming.Set(clip,frame.mode,baseScale,baseX,baseY);
  }
  void EditMonitorTransform(double scale,double x,double y,bool commit){
   string id=monitorTransformClipId;if(id==null)return;double time=timeline.Cursor;string gesture=monitor.TransformGesture;
   if(commit){try{Edit(()=>editor.Commit(p=>SetMonitorTransform(p,id,time,scale,x,y,gesture)));status.Text="Transformación actualizada desde el monitor";}catch(Exception ex){Error(ex);RefreshMonitorTransform();}}
   else PreviewProject(p=>SetMonitorTransform(p,id,time,scale,x,y,gesture));
  }
 }
}
