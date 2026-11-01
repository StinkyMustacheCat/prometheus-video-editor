using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Prometheus {
 public static class MediaGeometry {
  public static double[] DisplaySize(StreamInfo stream,double fallbackWidth,double fallbackHeight){
   double width=stream==null?fallbackWidth:stream.width.GetValueOrDefault((int)fallbackWidth),height=stream==null?fallbackHeight:stream.height.GetValueOrDefault((int)fallbackHeight);
   if(width<1||height<1){width=fallbackWidth;height=fallbackHeight;}
   if(stream!=null&&!String.IsNullOrWhiteSpace(stream.sample_aspect_ratio)){
    var parts=stream.sample_aspect_ratio.Split(':');int numerator,denominator;
    if(parts.Length==2&&Int32.TryParse(parts[0],out numerator)&&Int32.TryParse(parts[1],out denominator)&&numerator>0&&denominator>0)width*=numerator/(double)denominator;
   }
   if(stream!=null&&stream.display_rotation.HasValue&&Math.Abs(stream.display_rotation.Value%180)==90){double swap=width;width=height;height=swap;}
   return new[]{width,height};
  }
 }
 public static class SequenceCanvas {
  public static void Set(Sequence sequence,int width,int height){
   Validate(width,height);
   sequence.width=width;sequence.height=height;
  }
  public static void Validate(int width,int height){
   if(width<320||height<320||width>7680||height>4320||width%2!=0||height%2!=0||(long)width*height>12000000)
    throw new Exception("El lienzo debe medir al menos 320 píxeles por lado, usar dimensiones pares y no superar 12 millones de píxeles");
  }
  public static int[] Preset(int aspectWidth,int aspectHeight,int shortSide,bool twoK=false){
   if(aspectWidth<=0||aspectHeight<=0||shortSide<320)throw new Exception("Formato o resolución inválidos");
   double scale=twoK?2048.0/Math.Max(aspectWidth,aspectHeight):(double)shortSide/Math.Min(aspectWidth,aspectHeight);
   int width=Math.Max(2,(int)Math.Round(aspectWidth*scale/2)*2),height=Math.Max(2,(int)Math.Round(aspectHeight*scale/2)*2);
   Validate(width,height);return new[]{width,height};
  }
  public static int[] PreviewSize(Sequence sequence,int longestSide){
   Validate(sequence.width,sequence.height);
   double factor=Math.Min(1.0,(double)longestSide/Math.Max(sequence.width,sequence.height));
   int width=Math.Max(2,(int)Math.Round(sequence.width*factor/2)*2);
   int height=Math.Max(2,(int)Math.Round(sequence.height*factor/2)*2);
   return new[]{width,height};
  }
 }
 public sealed class ClipFrame {public string mode="fit";public double scale=1,scaleY=1,x,y,opacity=1;public bool scaleLinked=true;}
 public static class ClipFraming {
  public const string Namespace="prometheus.clip.framing";
  static Extension Find(Clip clip){return clip.extensions.FirstOrDefault(e=>e!=null&&e.@namespace==Namespace);}
  static double Number(Extension ext,string key,double fallback){object value;if(ext==null||ext.payload==null||!ext.payload.TryGetValue(key,out value)||value==null)return fallback;return Convert.ToDouble(value,CultureInfo.InvariantCulture);}
  public static ClipFrame Read(Clip clip){
   var ext=Find(clip);if(ext==null)return new ClipFrame();
   object raw;string mode=ext.payload!=null&&ext.payload.TryGetValue("mode",out raw)?Convert.ToString(raw):"fit";
   bool linked=true;object link;if(ext.payload!=null&&ext.payload.TryGetValue("scale_linked",out link)&&link!=null)linked=Convert.ToBoolean(link,CultureInfo.InvariantCulture);
   double sx=Number(ext,"scale",1);return new ClipFrame{mode=mode,scale=sx,scaleY=linked?sx:Number(ext,"scale_y",sx),scaleLinked=linked,x=Number(ext,"x",0),y=Number(ext,"y",0),opacity=Number(ext,"opacity",1)};
  }
  public static void Validate(Clip clip){var frame=Read(clip);if(frame.mode!="fit"&&frame.mode!="fill"&&frame.mode!="native"||Double.IsNaN(frame.scale)||frame.scale<0.25||frame.scale>4||Double.IsNaN(frame.scaleY)||frame.scaleY<0.25||frame.scaleY>4||Double.IsNaN(frame.x)||frame.x< -1||frame.x>1||Double.IsNaN(frame.y)||frame.y< -1||frame.y>1||Double.IsNaN(frame.opacity)||frame.opacity<0||frame.opacity>1)throw new Exception("Transformación de clip inválida");}
  public static void Set(Clip clip,string mode,double scale,double x,double y){
   var ext=Find(clip);if(ext==null){ext=new Extension{@namespace=Namespace,version="1",payload=new Dictionary<string,object>()};clip.extensions.Add(ext);}
   ext.payload["mode"]=mode;ext.payload["scale"]=scale;ext.payload["x"]=x;ext.payload["y"]=y;if(Read(clip).scaleLinked)ext.payload["scale_y"]=scale;
   Validate(clip);
  }
  public static void SetScaleY(Clip clip,double scaleY){var frame=Read(clip);if(frame.scaleLinked)throw new Exception("Desenlaza las escalas para cambiar Escala Y");Set(clip,frame.mode,frame.scale,frame.x,frame.y);Find(clip).payload["scale_y"]=scaleY;Validate(clip);}
  public static void SetScaleLinked(Clip clip,bool linked){var frame=Read(clip);if(frame.scaleLinked==linked)return;Set(clip,frame.mode,frame.scale,frame.x,frame.y);var ext=Find(clip);ext.payload["scale_linked"]=linked;if(linked){ext.payload["scale_y"]=frame.scale;clip.animation.RemoveAll(curve=>curve.target=="transform.scale_y");}else{ext.payload["scale_y"]=frame.scale;var source=ClipKeyframes.Find(clip,"transform.scale");if(source!=null){var copy=Store.Json().Deserialize<KeyframeCurve>(Store.Json().Serialize(Store.Fields(source)));copy.target="transform.scale_y";clip.animation.RemoveAll(curve=>curve.target==copy.target);clip.animation.Add(copy);}}Validate(clip);}
  public static void SetOpacity(Clip clip,double opacity){var frame=Read(clip);Set(clip,frame.mode,frame.scale,frame.x,frame.y);Find(clip).payload["opacity"]=opacity;Validate(clip);}
 }
 public static class CanvasFit0913 {
  public static bool Apply(Project project,Clip clip,string operation){
   var track=project.Active.tracks.FirstOrDefault(t=>t.id==clip.track_id);if(track==null||track.kind!="video"||track.locked)return false;
   var asset=project.assets.FirstOrDefault(a=>a.id==clip.asset_id);if(asset==null||GeneratedMedia.Kind(asset)!=null)return false;var stream=asset.streams.FirstOrDefault(s=>s.id==clip.stream_id);
   if(stream==null||!stream.width.HasValue||!stream.height.HasValue||stream.width.Value<2||stream.height.Value<2)return false;
   var displayed=MediaGeometry.DisplaySize(stream,stream.width.Value,stream.height.Value);
   double canvasW=project.Active.width,canvasH=project.Active.height,sourceW=displayed[0],sourceH=displayed[1];
   double fit=Math.Min(canvasW/sourceW,canvasH/sourceH),basisW=sourceW*fit,basisH=sourceH*fit;
   double x=1,y=1;
   if(operation=="width")x=y=canvasW/basisW;
   else if(operation=="height")x=y=canvasH/basisH;
   else if(operation=="deform"){x=canvasW/basisW;y=canvasH/basisH;}
   else if(operation=="adapt"){
    double needed=Math.Max(canvasW/basisW,canvasH/basisH);
    double distortion=Math.Min(needed,1+Math.Min(.10,.14*Math.Log(Math.Max(1,needed))));
    double uniform=needed/distortion;
    x=canvasW/basisW>canvasH/basisH?uniform*distortion:uniform;
    y=canvasH/basisH>canvasW/basisW?uniform*distortion:uniform;
   }else if(operation!="fit")throw new Exception("Ajuste de lienzo desconocido");
   if(x<.25||x>4||y<.25||y>4)throw new Exception("Este medio requiere un recorte excesivo; usa Fondo desenfocado o ajusta la escala manualmente");
   if(Math.Abs(x-y)<.00001){if(!ClipFraming.Read(clip).scaleLinked)ClipFraming.SetScaleLinked(clip,true);ClipFraming.Set(clip,"fit",x,0,0);}
   else{if(ClipFraming.Read(clip).scaleLinked)ClipFraming.SetScaleLinked(clip,false);ClipFraming.Set(clip,"fit",x,0,0);ClipFraming.SetScaleY(clip,y);}
   return true;
  }
 }
}
