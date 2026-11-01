using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Prometheus {
 public sealed class ClipLabelOption {
  public readonly string Id,Name;
  public readonly Color Color;
  public ClipLabelOption(string id,string name,Color color){Id=id;Name=name;Color=color;}
 }
 public static class ClipLabels {
  public const string Namespace="prometheus.ui.clip-label";
  public static readonly ClipLabelOption[] Palette={
   new ClipLabelOption("coral","Coral",Color.FromArgb(226,160,143)),
   new ClipLabelOption("orange","Naranja",Color.FromArgb(233,176,108)),
   new ClipLabelOption("yellow","Amarillo",Color.FromArgb(231,210,132)),
   new ClipLabelOption("green","Verde",Color.FromArgb(171,212,151)),
   new ClipLabelOption("cyan","Cian",Color.FromArgb(147,207,210)),
   new ClipLabelOption("blue","Azul",Color.FromArgb(154,187,224)),
   new ClipLabelOption("violet","Violeta",Color.FromArgb(185,169,217)),
   new ClipLabelOption("pink","Rosa",Color.FromArgb(214,163,195)),
   new ClipLabelOption("gray","Gris",Color.FromArgb(183,184,185))
  };
  public static string Get(Clip clip){
   if(clip==null||clip.extensions==null)return null;
   var record=clip.extensions.LastOrDefault(x=>x!=null&&x.@namespace==Namespace);
   object value;if(record==null||record.payload==null||!record.payload.TryGetValue("id",out value))return null;
   string id=value as string;return Palette.Any(x=>x.Id==id)?id:null;
  }
  public static Color ColorFor(Clip clip,string kind){
   string id=Get(clip);var option=Palette.FirstOrDefault(x=>x.Id==id);
   return option!=null?option.Color:kind=="audio"?Color.FromArgb(139,176,201):Color.FromArgb(199,157,113);
  }
  public static Color ColorFor(Clip clip,Asset asset,string kind){
   string id=Get(clip);var option=Palette.FirstOrDefault(x=>x.Id==id);if(option!=null)return option.Color;
   string source=asset==null||asset.source==null?null:asset.source.kind;
   if(source=="web")return Color.FromArgb(224,158,82);
   if(source=="mgt")return Color.FromArgb(174,145,213);
   if(source=="local")return Color.FromArgb(130,173,211);
   return ColorFor(clip,kind);
  }
  public static void Set(Clip clip,string id){
   if(clip==null)throw new ArgumentNullException("clip");
   if(id!=null&&!Palette.Any(x=>x.Id==id))throw new ArgumentException("Color desconocido");
   if(clip.extensions==null)clip.extensions=new List<Extension>();
   clip.extensions.RemoveAll(x=>x!=null&&x.@namespace==Namespace);
   if(id!=null)clip.extensions.Add(new Extension{@namespace=Namespace,version="1",payload=new Dictionary<string,object>{{"id",id}}});
  }
  static readonly Dictionary<string,Bitmap> swatches=new Dictionary<string,Bitmap>();
  public static Image Swatch(ClipLabelOption option){
   Bitmap bitmap;if(swatches.TryGetValue(option.Id,out bitmap))return bitmap;
   bitmap=new Bitmap(16,16);using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.Transparent);using(var fill=new SolidBrush(option.Color))g.FillRectangle(fill,2,2,12,12);using(var border=new Pen(Color.FromArgb(90,95,102)))g.DrawRectangle(border,2,2,12,12);}
   swatches[option.Id]=bitmap;return bitmap;
  }
 }
}
