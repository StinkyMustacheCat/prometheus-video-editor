using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 public sealed class VectorMaskLayer0104 {
  public int id=1;
  public ClipMask mask;
  public string blend="add";
  public bool overlay_visible;
 }
 public sealed class VectorMaskStackData0104 {
  public int selected_id=1;
  public string first_blend="add";
  public bool first_overlay_visible;
  public bool composite_inverted;
  public List<VectorMaskLayer0104> extras=new List<VectorMaskLayer0104>();
 }
 public static class VectorMaskStack0104 {
  public const string Namespace="prometheus.mask.vector-stack.v1";
  public static VectorMaskStackData0104 Get(Clip clip){var item=clip.extensions==null?null:clip.extensions.FirstOrDefault(x=>x.@namespace==Namespace);return item==null?new VectorMaskStackData0104():Store.Json().Deserialize<VectorMaskStackData0104>(Store.Json().Serialize(item.payload));}
  public static void Put(Clip clip,VectorMaskStackData0104 data){clip.extensions.RemoveAll(x=>x.@namespace==Namespace);if(data!=null&&(data.extras.Count>0||data.selected_id!=1||data.first_blend!="add"||data.first_overlay_visible||data.composite_inverted))clip.extensions.Add(new Extension{@namespace=Namespace,version="1",payload=(Dictionary<string,object>)Store.Fields(data)});}
  public static List<VectorMaskLayer0104> All(Clip clip){var data=Get(clip);var list=new List<VectorMaskLayer0104>();if(clip.mask!=null)list.Add(new VectorMaskLayer0104{id=1,mask=clip.mask,blend=data.first_blend,overlay_visible=data.first_overlay_visible});if(data.extras!=null)list.AddRange(data.extras);return list;}
  public static int SelectedId(Clip clip){var data=Get(clip);return All(clip).Any(x=>x.id==data.selected_id)?data.selected_id:1;}
  public static ClipMask Selected(Clip clip){var layer=All(clip).FirstOrDefault(x=>x.id==SelectedId(clip));return layer==null?null:layer.mask;}
  public static void Select(Clip clip,int id){var data=Get(clip);if(!All(clip).Any(x=>x.id==id))throw new Exception("La máscara ya no existe");data.selected_id=id;Put(clip,data);}
  public static int Add(Clip clip,ClipMask mask,string blend){ClipMasks.Validate(mask);if(clip.mask==null){clip.mask=mask;return 1;}var data=Get(clip);int id=All(clip).Max(x=>x.id)+1;data.extras.Add(new VectorMaskLayer0104{id=id,mask=mask,blend=blend});data.selected_id=id;Put(clip,data);return id;}
  public static void Update(Clip clip,int id,Action<VectorMaskLayer0104> change){var data=Get(clip);var layer=id==1?new VectorMaskLayer0104{id=1,mask=clip.mask,blend=data.first_blend,overlay_visible=data.first_overlay_visible}:data.extras.FirstOrDefault(x=>x.id==id);if(layer==null)throw new Exception("La máscara ya no existe");change(layer);ClipMasks.Validate(layer.mask);if(id==1){clip.mask=layer.mask;data.first_blend=layer.blend;data.first_overlay_visible=layer.overlay_visible;}Put(clip,data);}
  public static void RemoveSelected(Clip clip){var data=Get(clip);int id=SelectedId(clip);if(id==1){if(data.extras.Count==0){clip.mask=null;Put(clip,null);return;}var first=data.extras[0];clip.mask=first.mask;data.first_blend=first.blend;data.first_overlay_visible=first.overlay_visible;data.extras.RemoveAt(0);foreach(var layer in data.extras)layer.id--;data.selected_id=1;}else{data.extras.RemoveAll(x=>x.id==id);data.selected_id=1;}Put(clip,data);}
  public static void Validate(Clip clip){var data=Get(clip);if(data.extras==null||data.extras.Count>31||data.selected_id<1||data.first_blend!="add"&&data.first_blend!="subtract"&&data.first_blend!="intersect")throw new Exception("Capas vectoriales inválidas");var ids=new HashSet<int>();foreach(var layer in data.extras){if(layer==null||layer.id<2||!ids.Add(layer.id)||layer.blend!="add"&&layer.blend!="subtract"&&layer.blend!="intersect")throw new Exception("Capa de máscara inválida");ClipMasks.Validate(layer.mask);}}
 }
}
