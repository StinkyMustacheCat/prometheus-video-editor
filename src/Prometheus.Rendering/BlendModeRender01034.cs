using System;
using System.Collections.Generic;

namespace Prometheus {
 public static class BlendModeRender01034 {
  // The layer's alpha limits the blend to the actual clip; empty pixels never alter the canvas.
  public static string Compose(List<string> graph,string below,string layer,string mode,int number,long firstFrame,long endFrame){
   if(mode=="normal")throw new ArgumentException("Normal uses the regular overlay path");
   string prefix="blend_01034_"+number;
   graph.Add("["+layer+"]format=rgba,split=2["+prefix+"_color]["+prefix+"_alpha_source]");
   graph.Add("["+prefix+"_alpha_source]alphaextract,split=3["+prefix+"_la_normal]["+prefix+"_la_blend]["+prefix+"_la_union]");
   graph.Add("["+below+"]format=rgba,split=3["+prefix+"_base]["+prefix+"_alpha_base]["+prefix+"_outside]");
   graph.Add("["+prefix+"_alpha_base]alphaextract,split=2["+prefix+"_ba_mix]["+prefix+"_ba_union]");
   graph.Add("["+prefix+"_base]format=rgb24,split=3["+prefix+"_base_normal]["+prefix+"_base_blend]["+prefix+"_base_behind]");
   graph.Add("["+prefix+"_color]format=rgb24,split=2["+prefix+"_layer_normal]["+prefix+"_layer_blend]");
   graph.Add("["+prefix+"_base_blend]["+prefix+"_layer_blend]blend=all_mode="+mode+"["+prefix+"_mixed]");
   graph.Add("["+prefix+"_base_normal]["+prefix+"_layer_normal]["+prefix+"_la_normal]maskedmerge["+prefix+"_normal]");
   graph.Add("["+prefix+"_base_behind]["+prefix+"_mixed]["+prefix+"_la_blend]maskedmerge["+prefix+"_blended]");
   graph.Add("["+prefix+"_normal]["+prefix+"_blended]["+prefix+"_ba_mix]maskedmerge["+prefix+"_rgb]");
   graph.Add("["+prefix+"_ba_union]["+prefix+"_la_union]blend=all_mode=screen["+prefix+"_alpha]");
   graph.Add("["+prefix+"_rgb]["+prefix+"_alpha]alphamerge["+prefix+"_active]");
   graph.Add("["+prefix+"_outside]format=rgba["+prefix+"_outside_rgba]");
   graph.Add("["+prefix+"_outside_rgba]["+prefix+"_active]overlay=eof_action=pass:repeatlast=0:format=auto:enable='gte(round(t*60),"+firstFrame+")*lt(round(t*60),"+endFrame+")'["+prefix+"_out]");
   return prefix+"_out";
  }
 }
}