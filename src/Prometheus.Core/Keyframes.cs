using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Prometheus {
 public sealed class KeyframePoint {public double source_seconds,value,in_tangent=1,out_tangent=1;}
 public sealed class KeyframeCurve {public string target,interpolation="linear";public bool enabled=true;public List<KeyframePoint> points=new List<KeyframePoint>();}
 public static class ClipKeyframes {
  static string N(double value){return value.ToString("0.########",CultureInfo.InvariantCulture);}
  public static KeyframeCurve Find(Clip clip,string target){return clip.animation==null?null:clip.animation.FirstOrDefault(c=>c.target==target);}
  public static bool Active(Clip clip,string target){var curve=Find(clip,target);return curve!=null&&curve.enabled;}
  public static KeyframeCurve Arm(Clip clip,string target){if(clip.animation==null)clip.animation=new List<KeyframeCurve>();var curve=Find(clip,target);if(curve==null){curve=new KeyframeCurve{target=target};clip.animation.Add(curve);}curve.enabled=true;return curve;}
  static List<KeyframePoint> Relevant(Clip clip,KeyframeCurve curve){
   var all=curve.points.OrderBy(point=>point.source_seconds).ToList();if(all.Count<3)return all;
   double margin=10*Math.Max(0.5,clip.speed.Seconds),begin=clip.source_in.Seconds-margin,end=clip.source_in.Seconds+clip.duration.Seconds*clip.speed.Seconds+margin;
   int first=all.FindLastIndex(point=>point.source_seconds<=begin),last=all.FindIndex(point=>point.source_seconds>=end);
   if(first<0)first=0;if(last<0)last=all.Count-1;return all.GetRange(first,last-first+1);
  }
  public static bool IsVarying(Clip clip,string target){var curve=Find(clip,target);if(curve==null||!curve.enabled||curve.points.Count==0)return false;var points=Relevant(clip,curve);return points.Count>1&&points.Any(point=>Math.Abs(point.value-points[0].value)>0.0000001);}
  public static double MaxVisibleValue(Clip clip,string target,double fallback){var curve=Find(clip,target);return curve==null||!curve.enabled||curve.points.Count==0?fallback:Relevant(clip,curve).Max(point=>point.value);}
  public static double SourceAt(Clip clip,double timelineSeconds){return clip.source_in.Seconds+(timelineSeconds-clip.timeline_start.Seconds)*clip.speed.Seconds;}
  public static bool At(Clip clip,string target,double timelineSeconds){var curve=Find(clip,target);double source=SourceAt(clip,timelineSeconds);return curve!=null&&curve.enabled&&curve.points.Any(p=>Math.Abs(p.source_seconds-source)<1.0/48000);}
  public static double Value(Clip clip,string target,double timelineSeconds,double fallback){return Evaluate(clip,target,timelineSeconds,fallback,true);}
  public static double StoredValue(Clip clip,string target,double timelineSeconds,double fallback){return Evaluate(clip,target,timelineSeconds,fallback,false);}
  static double Evaluate(Clip clip,string target,double timelineSeconds,double fallback,bool respectEnabled){
   var curve=Find(clip,target);if(curve==null||(respectEnabled&&!curve.enabled)||curve.points.Count==0)return fallback;
   double source=SourceAt(clip,timelineSeconds);var points=curve.points.OrderBy(p=>p.source_seconds).ToList();
   if(source<=points[0].source_seconds)return points[0].value;
   for(int i=1;i<points.Count;i++)if(source<=points[i].source_seconds){var a=points[i-1];var b=points[i];return Interpolate(curve,a,b,(source-a.source_seconds)/(b.source_seconds-a.source_seconds));}
   return points[points.Count-1].value;
  }
  static double Interpolate(KeyframeCurve curve,KeyframePoint a,KeyframePoint b,double u){
   if(curve.interpolation=="hold")return a.value;
   if(curve.interpolation=="linear")return a.value+(b.value-a.value)*u;
   double m0=curve.interpolation=="smooth"?0:a.out_tangent,m1=curve.interpolation=="smooth"?0:b.in_tangent;
   double u2=u*u,u3=u2*u;
   double result=a.value*(2*u3-3*u2+1)+b.value*(-2*u3+3*u2)+(b.value-a.value)*(m0*(u3-2*u2+u)+m1*(u3-u2));
   return Math.Max(Math.Min(a.value,b.value),Math.Min(Math.Max(a.value,b.value),result));
  }
  static string Segment(KeyframeCurve curve,KeyframePoint a,KeyframePoint b,string variable,double at,double until){
   if(curve.interpolation=="hold")return N(a.value);
   string u="(("+variable+"-"+N(at)+")/"+N(until-at)+")";
   if(curve.interpolation=="linear")return N(a.value)+"+("+N(b.value-a.value)+")*"+u;
   string u2="("+u+"*"+u+")",u3="("+u2+"*"+u+")";
   double m0=curve.interpolation=="smooth"?0:a.out_tangent,m1=curve.interpolation=="smooth"?0:b.in_tangent;
   string result=N(a.value)+"*(2*"+u3+"-3*"+u2+"+1)+"+N(b.value)+"*(-2*"+u3+"+3*"+u2+")+"+N(b.value-a.value)+"*("+N(m0)+"*("+u3+"-2*"+u2+"+"+u+")+"+N(m1)+"*("+u3+"-"+u2+"))";
   return "max("+N(Math.Min(a.value,b.value))+"\\,min("+N(Math.Max(a.value,b.value))+"\\,"+result+"))";
  }
  public static string Expression(Clip clip,string target,double fallback,string timeVariable){
   var curve=Find(clip,target);if(curve==null||!curve.enabled||curve.points.Count==0)return N(fallback);
   var points=Relevant(clip,curve);if(!IsVarying(clip,target))return N(points[0].value);
   string result=N(points[points.Count-1].value);
   for(int i=points.Count-2;i>=0;i--){KeyframePoint a=points[i],b=points[i+1];double at=clip.timeline_start.Seconds+(a.source_seconds-clip.source_in.Seconds)/clip.speed.Seconds;
    double until=clip.timeline_start.Seconds+(b.source_seconds-clip.source_in.Seconds)/clip.speed.Seconds;
    string value=Segment(curve,a,b,timeVariable,at,until);
    result="if(lte("+timeVariable+","+N(until)+"),"+value+","+result+")";
   }
   double first=clip.timeline_start.Seconds+(points[0].source_seconds-clip.source_in.Seconds)/clip.speed.Seconds;
   return "if(lte("+timeVariable+","+N(first)+"),"+N(points[0].value)+","+result+")";
  }
  public static void Set(Clip clip,string target,double timelineSeconds,double value){
   if(clip.animation==null)clip.animation=new List<KeyframeCurve>();
   double source=Math.Round(SourceAt(clip,timelineSeconds)*48000)/48000.0;
   if(source<0||Double.IsNaN(value)||Double.IsInfinity(value))throw new Exception("Fotograma clave fuera del medio");
   var curve=Arm(clip,target);
   var point=curve.points.FirstOrDefault(p=>Math.Abs(p.source_seconds-source)<1.0/48000);
   if(point==null)curve.points.Add(new KeyframePoint{source_seconds=source,value=value});else point.value=value;
   curve.points=curve.points.OrderBy(p=>p.source_seconds).ToList();
  }
  public static void Remove(Clip clip,string target,double timelineSeconds){
   var curve=Find(clip,target);if(curve==null)return;double source=SourceAt(clip,timelineSeconds);
   curve.points.RemoveAll(p=>Math.Abs(p.source_seconds-source)<1.0/48000);
   // The armed state survives deletion of the final point.
  }
  public static void Validate(Clip clip){
   if(clip.animation==null)throw new Exception("Faltan fotogramas clave");var used=new HashSet<string>();
   foreach(var curve in clip.animation){
    if(curve==null||String.IsNullOrWhiteSpace(curve.target)||!used.Add(curve.target)||!(new[]{"linear","hold","smooth","bezier"}).Contains(curve.interpolation)||curve.points==null)throw new Exception("Curva de fotogramas clave inválida");
    double min=Double.NegativeInfinity,max=Double.PositiveInfinity;
    if(curve.target=="transform.scale"||curve.target=="transform.scale_y"){min=0.25;max=4;}
    else if(curve.target=="transform.x"||curve.target=="transform.y"){min=-1;max=1;}
    else if(curve.target=="transform.opacity"){min=0;max=1;}
    else if(curve.target.StartsWith("effect:",StringComparison.Ordinal)){
     var bits=curve.target.Split(':');if(bits.Length!=3||bits[1].Length==0||bits[2].Length==0)throw new Exception("Destino de efecto inválido");
     var effect=clip.effects.FirstOrDefault(e=>e.id==bits[1]);var contract=effect==null?null:EffectContracts.Find(effect.effect_id,effect.contract_version);
     var spec=contract==null?null:contract.parameters.FirstOrDefault(p=>p.id==bits[2]&&p.kind=="number"&&p.animatable);
     if(spec!=null){min=spec.minimum.GetValueOrDefault(Double.NegativeInfinity);max=spec.maximum.GetValueOrDefault(Double.PositiveInfinity);}
    }else throw new Exception("Destino de fotograma clave desconocido");
    double previous=-1;
    foreach(var point in curve.points){if(point==null||Double.IsNaN(point.source_seconds)||Double.IsInfinity(point.source_seconds)||point.source_seconds<0||point.source_seconds<=previous||Double.IsNaN(point.value)||Double.IsInfinity(point.value)||point.value<min||point.value>max||Double.IsNaN(point.in_tangent)||Double.IsNaN(point.out_tangent)||point.in_tangent<0||point.in_tangent>3||point.out_tangent<0||point.out_tangent>3)throw new Exception("Fotograma clave inválido");previous=point.source_seconds;}
   }
  }
 }
}
