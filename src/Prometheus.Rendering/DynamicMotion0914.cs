using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;

namespace Prometheus {
 public sealed class DynamicMotion0914 {
  public const string PreviewNamespace="prometheus.motion.preview-window";
  public EffectInstance Zoom,Pan;
  public bool HasZoom{get{return Zoom!=null;}}
  public bool HasPan{get{return Pan!=null;}}
  public double MaxZoom{get{return Zoom==null?1:1+Zoom.parameters["zoom_percent"].number.Value/100;}}
  public static DynamicMotion0914 Read(Project project,Clip clip){var effects=EffectContracts.Effective(project,clip).ToList();return new DynamicMotion0914{
   Zoom=effects.LastOrDefault(e=>e.effect_id==EffectContracts.ZoomInDynamicId||e.effect_id==EffectContracts.ZoomOutDynamicId),
   Pan=effects.LastOrDefault(e=>e.effect_id==EffectContracts.PanDynamicId)};}
  static double[] Range(Clip clip){var ext=clip.extensions==null?null:clip.extensions.FirstOrDefault(e=>e.@namespace==PreviewNamespace);object start,length;if(ext!=null&&ext.payload!=null&&ext.payload.TryGetValue("start",out start)&&ext.payload.TryGetValue("duration",out length))return new[]{Convert.ToDouble(start,CultureInfo.InvariantCulture),Convert.ToDouble(length,CultureInfo.InvariantCulture)};return new[]{clip.timeline_start.Seconds,clip.duration.Seconds};}
  public static void ShiftPreviewWindow(Clip clip,double from){var range=Range(clip);if(clip.extensions==null)clip.extensions=new List<Extension>();clip.extensions.RemoveAll(e=>e.@namespace==PreviewNamespace);clip.extensions.Add(new Extension{@namespace=PreviewNamespace,version="1",payload=new Dictionary<string,object>{{"start",range[0]-from},{"duration",range[1]}}});}
  static string Ease(EffectInstance effect,Clip clip){var range=Range(clip);string r="max(0\\,min(1\\,((t-"+Tools.N(range[0])+")/"+Tools.N(Math.Max(1.0/60,range[1]))+")))";string mode=effect.parameters["motion"].text;
   return mode=="Lineal"?r:mode=="Acelerado al final"?"("+r+"*"+r+"*"+r+")":"("+r+"*"+r+"*(3-2*"+r+"))";
  }
  public string Scale(Clip clip){if(Zoom==null)return "1";string amount=Tools.N(Zoom.parameters["zoom_percent"].number.Value/100),ease=Ease(Zoom,clip);return Zoom.effect_id==EffectContracts.ZoomInDynamicId?"(1+"+amount+"*"+ease+")":"(1+"+amount+"*(1-"+ease+"))";}
  public string OffsetX(Clip clip){if(Pan==null)return null;string direction=Pan.parameters["direction"].text;int sign=direction=="Izquierda → derecha"||direction=="Diagonal ↘"||direction=="Diagonal ↗"?1:direction=="Derecha → izquierda"||direction=="Diagonal ↙"||direction=="Diagonal ↖"?-1:0;return sign==0?null:Offset(clip,sign);}
  public string OffsetY(Clip clip){if(Pan==null)return null;string direction=Pan.parameters["direction"].text;int sign=direction=="Arriba → abajo"||direction=="Diagonal ↘"||direction=="Diagonal ↙"?1:direction=="Abajo → arriba"||direction=="Diagonal ↗"||direction=="Diagonal ↖"?-1:0;return sign==0?null:Offset(clip,sign);}
  string Offset(Clip clip,int sign){string amplitude=Tools.N(Pan.parameters["distance_percent"].number.Value/100),ease=Ease(Pan,clip);return "("+(sign<0?"-":"")+amplitude+"*("+ease+"-.5))";}
 }
}
