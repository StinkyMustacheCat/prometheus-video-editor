using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 public static class BlendModes01034 {
  public const string Namespace="prometheus.clip-blend-mode";
  public static readonly string[] Modes={"normal","multiply","screen","overlay","darken","lighten","softlight","hardlight","difference","exclusion","addition","subtract"};
  public static string Read(Clip clip){
   if(clip==null||clip.extensions==null)return "normal";
   var item=clip.extensions.FirstOrDefault(x=>x.@namespace==Namespace);
   object value;string mode=item!=null&&item.payload!=null&&item.payload.TryGetValue("mode",out value)?Convert.ToString(value):"normal";
   return Modes.Contains(mode)?mode:"normal";
  }
  public static void Set(Clip clip,string mode){
   if(clip==null||!Modes.Contains(mode))throw new Exception("Modo de fusión no válido");
   clip.extensions.RemoveAll(x=>x.@namespace==Namespace);
   if(mode!="normal")clip.extensions.Add(new Extension{@namespace=Namespace,version="1",payload=new Dictionary<string,object>{{"mode",mode}}});
  }
 }
}
