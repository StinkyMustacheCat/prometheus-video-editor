using System;
using System.Collections.Generic;

namespace Prometheus {
 public static class NestedRenderAdapter01034 {
  public static string Open(List<string> graph,int width,int height,int fps,double length,int ordinal){
   string label="nested_clear_"+ordinal;
   graph.Add("color=c=black@0:s="+width+"x"+height+":r="+fps+":d="+Tools.N(length)+",format=rgba["+label+"]");
   return label;
  }
  public static string Close(List<string> graph,Project project,Clip outer,string inner,string below,int width,int height,int fps,int ordinal){
   var frame=ClipFraming.Read(outer);var motion=DynamicMotion0914.Read(project,outer);
   string sx=ClipKeyframes.Expression(outer,"transform.scale",frame.scale,"t");
   string sy=frame.scaleLinked?sx:ClipKeyframes.Expression(outer,"transform.scale_y",frame.scaleY,"t");
   if(motion.HasZoom){string zoom=motion.Scale(outer);sx="("+sx+")*("+zoom+")";sy="("+sy+")*("+zoom+")";}
   int fixedWidth=Math.Max(4,2*((int)Math.Ceiling(width*ClipKeyframes.MaxVisibleValue(outer,"transform.scale",frame.scale)*motion.MaxZoom/2)+2));
   int fixedHeight=Math.Max(4,2*((int)Math.Ceiling(height*(frame.scaleLinked?ClipKeyframes.MaxVisibleValue(outer,"transform.scale",frame.scale):ClipKeyframes.MaxVisibleValue(outer,"transform.scale_y",frame.scaleY))*motion.MaxZoom/2)+2));
   string transform=",scale=w='min("+(fixedWidth-4)+"\\,max(2\\,round("+width+"*("+sx+"))))':h='min("+(fixedHeight-4)+"\\,max(2\\,round("+height+"*("+sy+"))))':eval=frame,format=rgba,pad=w="+fixedWidth+":h="+fixedHeight+":x='(ow-iw)/2':y='(oh-ih)/2':color=black@0:eval=frame";
   string opacity=ClipKeyframes.IsVarying(outer,"transform.opacity")?",geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='alpha(X,Y)*("+ClipKeyframes.Expression(outer,"transform.opacity",frame.opacity,"T")+")'":frame.opacity<0.999999?",colorchannelmixer=aa="+Tools.N(frame.opacity):"";
   string processed="nested_processed_"+ordinal,result="nested_composed_"+ordinal;
   graph.Add("["+inner+"]null"+FFmpegEffectAdapter.VideoFilters(project,outer)+transform+",setsar=1"+opacity+"["+processed+"]");
   string x="(W-w)/2+("+ClipKeyframes.Expression(outer,"transform.x",frame.x,"t")+")*W";
   string y="(H-h)/2+("+ClipKeyframes.Expression(outer,"transform.y",frame.y,"t")+")*H";
   if(motion.HasPan){var dx=motion.OffsetX(outer);var dy=motion.OffsetY(outer);if(dx!=null)x="("+x+")+"+dx+"*W";if(dy!=null)y="("+y+")+"+dy+"*H";}
   long start=(long)Math.Round(outer.timeline_start.Seconds*60),end=(long)Math.Round(outer.End*60);
   if(BlendModes01034.Read(outer)=="normal")graph.Add("["+below+"]["+processed+"]overlay=x='"+x+"':y='"+y+"':eof_action=pass:repeatlast=0:format=auto:enable='gte(round(t*60),"+start+")*lt(round(t*60),"+end+")'["+result+"]");
   else {string empty="nested_blend_empty_"+ordinal,placed="nested_blend_placed_"+ordinal;graph.Add("color=c=black@0:s="+width+"x"+height+":r="+fps+":d="+Tools.N(project.Active.Length)+",format=rgba["+empty+"]");graph.Add("["+empty+"]["+processed+"]overlay=x='"+x+"':y='"+y+"':eof_action=pass:repeatlast=0:format=auto:enable='gte(round(t*60),"+start+")*lt(round(t*60),"+end+")'["+placed+"]");result=BlendModeRender01034.Compose(graph,below,placed,BlendModes01034.Read(outer),100000+ordinal,start,end);}
   return result;
  }
 }
}
