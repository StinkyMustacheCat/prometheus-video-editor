using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Generic;

namespace Prometheus {
 public static class FFmpegEffectAdapter {
  public static bool Supports(EffectInstance instance){
   return instance!=null&&EffectContracts.Find(instance.effect_id,instance.contract_version)!=null;
  }
  public static IEnumerable<EffectInstance> ActiveEffects(Project project,Clip clip){
   return EffectContracts.Effective(project,clip);
  }
  // FFmpeg parses option values and then the enclosing filtergraph. Escape both layers.
  static string PathOption(string path){
   string first=Path.GetFullPath(path).Replace('\\','/').Replace("\\","\\\\").Replace(":","\\:").Replace("'","\\'");
   return first.Replace("\\","\\\\").Replace("'","\\'").Replace(",","\\,")
    .Replace("[","\\[").Replace("]","\\]").Replace(";","\\;");
  }
  public static void VerifyLutFile(string path){
   if(String.IsNullOrWhiteSpace(path)||!File.Exists(path)||!Path.GetExtension(path).Equals(".cube",StringComparison.OrdinalIgnoreCase))
    throw new Exception("Selecciona un archivo LUT .cube disponible");
   string filter="lut3d=file="+PathOption(path)+":interp=tetrahedral";
   Tools.Run(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -f lavfi -i "+Tools.Q("color=c=gray:s=2x2:d=0.1")+
    " -vf "+Tools.Q(filter)+" -frames:v 1 -f null NUL",CancellationToken.None);
  }
  public static string VideoFilters(Project project,Clip clip){
   return VideoFilters(project,clip,ActiveEffects(project,clip));
  }
  public static string VideoFilters(Project project,Clip clip,IEnumerable<EffectInstance> selected){
   string result="";
   foreach(var effect in selected){
    if(!Supports(effect))throw new Exception("Efecto no disponible para render: "+effect.effect_id+" ("+effect.contract_version+")");
    if(effect.effect_id==EffectContracts.Lut3DId){
     var value=effect.parameters["lut"];
     var resource=project.effect_resources.FirstOrDefault(r=>r.id==value.resource_id);
     if(resource==null||!File.Exists(resource.uri))throw new Exception("Falta el archivo LUT del clip: "+clip.name);
     if(!Path.GetExtension(resource.uri).Equals(".cube",StringComparison.OrdinalIgnoreCase))throw new Exception("El LUT debe ser un archivo .cube");
     result+=",lut3d=file="+PathOption(resource.uri)+":interp="+effect.parameters["interpolation"].text;
    }
    else if(effect.effect_id==EffectContracts.FlipId)result+=",hflip";
    else if(effect.effect_id==EffectContracts.BasicColorId)result+=",eq=brightness='"+ClipKeyframes.Expression(clip,"effect:"+effect.id+":brightness",effect.parameters["brightness"].number.Value,"t")+"':contrast='"+ClipKeyframes.Expression(clip,"effect:"+effect.id+":contrast",effect.parameters["contrast"].number.Value,"t")+"':saturation='"+ClipKeyframes.Expression(clip,"effect:"+effect.id+":saturation",effect.parameters["saturation"].number.Value,"t")+"':eval=frame";
    else if(effect.effect_id==EffectContracts.BlurId)result+=",gblur=sigma="+Tools.N(effect.parameters["sigma"].number.Value);
    else if(effect.effect_id==EffectContracts.SharpenId)result+=",unsharp=luma_msize_x=5:luma_msize_y=5:luma_amount="+Tools.N(effect.parameters["amount"].number.Value)+":chroma_amount=0";
    else if(effect.effect_id==EffectContracts.FlipVerticalId)result+=",vflip";
    else if(effect.effect_id==EffectContracts.HueId)result+=",hue=h='"+ClipKeyframes.Expression(clip,"effect:"+effect.id+":degrees",effect.parameters["degrees"].number.Value,"t")+"'";
    else if(effect.effect_id==EffectContracts.VignetteId){double amount=effect.parameters["amount"].number.Value;if(amount>0)result+=",vignette=angle="+Tools.N(Math.PI/2-amount*1.2)+":eval=init";}
    else if(effect.effect_id==EffectContracts.CropId){
     double left=effect.parameters["left"].number.Value/100,top=effect.parameters["top"].number.Value/100,right=effect.parameters["right"].number.Value/100,bottom=effect.parameters["bottom"].number.Value/100;
     if(left+right>0||top+bottom>0)result+=",format=rgba,crop=w=iw*"+Tools.N(1-left-right)+":h=ih*"+Tools.N(1-top-bottom)+":x=iw*"+Tools.N(left)+":y=ih*"+Tools.N(top)+":exact=1,pad=w=iw/"+Tools.N(1-left-right)+":h=ih/"+Tools.N(1-top-bottom)+":x=ow*"+Tools.N(left)+":y=oh*"+Tools.N(top)+":color=black@0";
    }
    else if(effect.effect_id==EffectContracts.ChromaKeyId){
     string color=effect.parameters["color"].text.Substring(1);
     result+=",format=rgba,colorkey=color=0x"+color+":similarity="+Tools.N(effect.parameters["similarity"].number.Value)+":blend="+Tools.N(effect.parameters["blend"].number.Value);
    }
    else if(effect.effect_id==EffectContracts.ExposureId)result+=",exposure=exposure="+Tools.N(effect.parameters["stops"].number.Value)+":black="+Tools.N(effect.parameters["black"].number.Value);
    else if(effect.effect_id==EffectContracts.MonochromeId)result+=",hue=s=0";
    else if(effect.effect_id==EffectContracts.VibranceId)result+=",vibrance=intensity="+Tools.N(effect.parameters["intensity"].number.Value);
    else if(effect.effect_id==EffectContracts.PixelizeId){int block=(int)Math.Round(effect.parameters["block"].number.Value);result+=",pixelize=width="+block+":height="+block;}
    else if(effect.effect_id==EffectContracts.WhiteBalanceId){double temperature=effect.parameters["temperature"].number.Value,tint=effect.parameters["tint"].number.Value;result+=",colortemperature=temperature="+Tools.N(temperature)+":pl=0.5,colorbalance=gm="+Tools.N(-tint*0.4)+":rm="+Tools.N(tint*0.2)+":bm="+Tools.N(tint*0.2)+":pl=1";}
    else if(effect.effect_id==EffectContracts.LevelsId){double black=effect.parameters["black"].number.Value,white=effect.parameters["white"].number.Value,gamma=effect.parameters["gamma"].number.Value;result+=",colorlevels=rimin="+Tools.N(black)+":gimin="+Tools.N(black)+":bimin="+Tools.N(black)+":rimax="+Tools.N(Math.Min(1,white))+":gimax="+Tools.N(Math.Min(1,white))+":bimax="+Tools.N(Math.Min(1,white))+",eq=gamma="+Tools.N(gamma);}
    else if(effect.effect_id==EffectContracts.ShadowsHighlightsId){double shadows=effect.parameters["shadows"].number.Value,highlights=effect.parameters["highlights"].number.Value;result+=",colorbalance=rs="+Tools.N(shadows*0.5)+":gs="+Tools.N(shadows*0.5)+":bs="+Tools.N(shadows*0.5)+":rh="+Tools.N(highlights*0.5)+":gh="+Tools.N(highlights*0.5)+":bh="+Tools.N(highlights*0.5)+":pl=0";}
    else if(effect.effect_id==EffectContracts.GlowId){double amount=effect.parameters["amount"].number.Value;result+=",unsharp=luma_msize_x=7:luma_msize_y=7:luma_amount="+Tools.N(-amount*1.5)+":chroma_amount=0,eq=brightness="+Tools.N(amount*0.04);}
   }
   return result;
  }
 }
}
