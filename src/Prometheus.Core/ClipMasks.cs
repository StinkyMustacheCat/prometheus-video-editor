using System;
using System.Collections.Generic;

namespace Prometheus {
 public sealed class MaskVertex {public double x,y;}
 public sealed class ClipMask {public string shape="rectangle";public bool enabled=true,inverted;public double feather;public List<MaskVertex> points=new List<MaskVertex>();}
 public static class ClipMasks {
  public static ClipMask New(string shape){
   var mask=new ClipMask{shape=shape};
   if(shape=="rectangle"||shape=="ellipse")mask.points.AddRange(new[]{new MaskVertex{x=0.2,y=0.2},new MaskVertex{x=0.8,y=0.8}});
   else if(shape=="polygon")mask.points.AddRange(new[]{new MaskVertex{x=0.25,y=0.25},new MaskVertex{x=0.75,y=0.25},new MaskVertex{x=0.5,y=0.75}});
   else throw new Exception("Forma de máscara desconocida");
   return mask;
  }
  public static void Validate(ClipMask mask){
   if(mask==null)return;
   if(mask.shape!="rectangle"&&mask.shape!="ellipse"&&mask.shape!="polygon"||mask.points==null||mask.feather<0||mask.feather>0.2||Double.IsNaN(mask.feather)||
    mask.points.Count<(mask.shape=="polygon"?3:2)||mask.points.Count>(mask.shape=="polygon"?64:2))throw new Exception("Máscara inválida");
   foreach(var point in mask.points)if(point==null||Double.IsNaN(point.x)||Double.IsNaN(point.y)||point.x<0||point.x>1||point.y<0||point.y>1)throw new Exception("Punto de máscara fuera del lienzo");
   if(mask.shape!="polygon"&&(mask.points[0].x>=mask.points[1].x||mask.points[0].y>=mask.points[1].y))throw new Exception("La máscara no tiene superficie");
  }
 }
}
