using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Prometheus {
 public static class GeneratedMedia {
  public const string Namespace="prometheus.generated";
  static Extension Data(Asset asset){return asset==null||asset.extensions==null?null:asset.extensions.FirstOrDefault(x=>x!=null&&x.@namespace==Namespace&&x.version=="1");}
  static string Value(Asset asset,string key){var ext=Data(asset);object result;return ext!=null&&ext.payload!=null&&ext.payload.TryGetValue(key,out result)&&result!=null?Convert.ToString(result):"";}
  public static string Kind(Asset asset){return asset!=null&&asset.source!=null&&asset.source.kind=="generated"?Value(asset,"kind"):null;}
  public static bool ValidStreams(Asset asset){string kind=Kind(asset);if(kind=="nested")return asset.streams.Count==1&&asset.streams[0].kind=="image"||asset.streams.Count==2&&asset.streams.Count(s=>s.kind=="image")==1&&asset.streams.Count(s=>s.kind=="audio")==1;return asset.streams.Count==1&&(asset.streams[0].kind=="image"||kind=="placeholder"&&asset.streams[0].kind=="audio");}
  public static bool IsPlaceholder(Asset asset){return Kind(asset)=="placeholder";}
  public static bool IsUntimedPlaceholder(Asset asset){return IsPlaceholder(asset);}
  public static string ColorHex(Asset asset){string value=Value(asset,"color_hex");return Regex.IsMatch(value,@"^[0-9a-fA-F]{6}$")?value.ToUpperInvariant():"40464F";}
  public static string Note(Asset asset){return Value(asset,"note");}
  public static bool IsText(Asset asset){return Kind(asset)=="text";}
  public static bool IsSubtitle(Asset asset){return IsText(asset)&&Value(asset,"role")=="subtitle";}
  public static string Text(Asset asset){return Value(asset,"text");}
  public static int TextSize(Asset asset){int size;return Int32.TryParse(Value(asset,"font_size"),out size)?Math.Max(16,Math.Min(1000,size)):72;}
  public static string TextFont(Asset asset){string value=Value(asset,"font_family");return String.IsNullOrWhiteSpace(value)?"Segoe UI":value;}
  public static void SetTextFont(Asset asset,string family){if(!IsText(asset)||String.IsNullOrWhiteSpace(family)||family.Length>120)throw new Exception("Fuente de texto inválida");Data(asset).payload["font_family"]=family.Trim();}
  public static string TextColor(Asset asset){string color=Value(asset,"color_hex");return Regex.IsMatch(color,@"^[0-9a-fA-F]{6}$")?color.ToUpperInvariant():"FFFFFF";}
  public static double TextStyleNumber(Asset asset,string key,double fallback){double result;return Double.TryParse(Value(asset,key),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out result)?result:fallback;}
  public static string TextStyleColor(Asset asset,string key,string fallback){string value=Value(asset,key);return Regex.IsMatch(value,@"^[0-9a-fA-F]{6}$")?value.ToUpperInvariant():fallback;}
  public static void SetTextStyleNumber(Asset asset,string key,double value){if(!IsText(asset)||Double.IsNaN(value)||Double.IsInfinity(value))throw new Exception("Estilo de texto inválido");var limits=new Dictionary<string,Tuple<double,double>>{{"stroke_px",Tuple.Create(0.0,20.0)},{"shadow_x",Tuple.Create(-100.0,100.0)},{"shadow_y",Tuple.Create(-100.0,100.0)},{"shadow_blur",Tuple.Create(0.0,30.0)},{"shadow_opacity",Tuple.Create(0.0,1.0)}};Tuple<double,double> range;if(!limits.TryGetValue(key,out range)||value<range.Item1||value>range.Item2)throw new Exception("Propiedad de texto fuera de rango");Data(asset).payload[key]=value.ToString("0.####",System.Globalization.CultureInfo.InvariantCulture);}
  public static void SetTextStyleColor(Asset asset,string key,string color){if(!IsText(asset)||(key!="stroke_color"&&key!="shadow_color")||!Regex.IsMatch(color??"",@"^[0-9a-fA-F]{6}$"))throw new Exception("Color de estilo inválido");Data(asset).payload[key]=color.ToUpperInvariant();}
  public static void SetText(Asset asset,string text,int size,string color){
   if(!IsText(asset))throw new Exception("Selecciona un asset de texto");
   text=(text??"").Trim();if(text.Length<1||text.Length>1000)throw new Exception("El texto debe tener entre 1 y 1000 caracteres");
   if(size<16||size>1000)throw new Exception("El tamaño de texto debe estar entre 16 y 1000");
   if(!Regex.IsMatch(color??"",@"^[0-9a-fA-F]{6}$"))throw new Exception("Color de texto inválido");
   var payload=Data(asset).payload;payload["text"]=text;payload["font_size"]=size;payload["color_hex"]=color.ToUpperInvariant();
  }
  public static void ValidateText(Asset asset){if(!IsText(asset))return;string text=Text(asset),role=Value(asset,"role");int size;
   if(String.IsNullOrWhiteSpace(text)||text.Length>1000||(role!="title"&&role!="subtitle")||!Int32.TryParse(Value(asset,"font_size"),out size)||size<16||size>1000||!Regex.IsMatch(Value(asset,"color_hex"),@"^[0-9a-fA-F]{6}$"))throw new Exception("Asset de texto inválido");
  }
  public static Asset CreateText(string text,double duration,bool subtitle=false){
   var asset=Create("text",subtitle?"Subtítulo":"Texto","FFFFFF","",duration);
   var payload=Data(asset).payload;payload["role"]=subtitle?"subtitle":"title";payload["text"]="Texto";payload["font_size"]=subtitle?64:96;
   SetText(asset,text,subtitle?64:96,"FFFFFF");string content=Text(asset);asset.name=subtitle?"Subtítulo · "+(content.Length>34?content.Substring(0,34)+"…":content):"Texto · "+(content.Length>34?content.Substring(0,34)+"…":content);
   return asset;
  }
  public static void Update(Project project,string assetId,string clipId,string name,string detail,double duration){
   var asset=project.assets.Find(a=>a.id==assetId);
   string kind=Kind(asset);
   if(kind!="solid"&&kind!="placeholder")throw new Exception("El medio no es un color sólido ni un espacio reservado");
   name=(name??"").Trim();
   if(name.Length<1||name.Length>120)throw new Exception("El nombre debe tener entre 1 y 120 caracteres");
   detail=(detail??"").Trim();
   if(kind=="placeholder"&&detail.Length>500)throw new Exception("La nota no puede superar 500 caracteres");
   if(kind=="solid"&&!Regex.IsMatch(detail,@"^[0-9a-fA-F]{6}$"))throw new Exception("El color debe tener seis dígitos hexadecimales");
   var rounded=Rational.Frame(duration);
   if(rounded.Seconds<1.0/60||rounded.Seconds>48*3600)throw new Exception("La duración debe estar entre un fotograma y 48 horas");
   Clip selected=null;
   if(!String.IsNullOrEmpty(clipId)){
    selected=project.Active.clips.Find(c=>c.id==clipId&&c.asset_id==assetId);
    if(selected==null)throw new Exception("El clip seleccionado ya no usa este medio");
    var track=project.Active.tracks.Find(t=>t.id==selected.track_id);
    if(track==null||track.locked)throw new Exception("La pista está bloqueada");
   }
   string oldName=asset.name;
   asset.name=name;if(selected==null)asset.recommended_duration=rounded;
   var ext=Data(asset);if(ext==null||ext.payload==null)throw new Exception("Faltan las propiedades del medio generado");
   ext.payload[kind=="solid"?"color_hex":"note"]=kind=="solid"?detail.ToUpperInvariant():detail;
   foreach(var clip in project.sequences.SelectMany(s=>s.clips).Where(c=>c.asset_id==assetId&&c.name==oldName))clip.name=name;
   if(selected!=null)selected.duration=rounded;
  }
  public static Asset CreateSolid(string name,string hex,double duration){return Create("solid",name,hex,"",duration);}
  public static Asset CreatePlaceholder(string name,string note,double duration){return Create("placeholder",name,"40464F",note,duration);}
  public static Asset CreateAudioPlaceholder(string name,string note,double duration){
   var asset=Create("placeholder",name,"40464F",note,duration);
   asset.streams.Clear();asset.streams.Add(new StreamInfo{id=Store.Id(),ordinal=0,kind="audio",codec="generated-silence",
    pts_origin=Rational.Time(0),time_base=new Rational(1,48000),duration=null,sample_rate=48000,channels=2,channel_layout="stereo"});
   return asset;
  }
  static Asset Create(string kind,string name,string hex,string note,double duration){
   name=(name??"").Trim();note=(note??"").Trim();
   if(name.Length==0||name.Length>120)throw new Exception("El nombre debe tener entre 1 y 120 caracteres");
   if(note.Length>500)throw new Exception("La nota no puede superar 500 caracteres");
   if(!Regex.IsMatch(hex??"",@"^[0-9a-fA-F]{6}$"))throw new Exception("Color inválido");
   var rounded=Rational.Frame(duration);if(rounded.Seconds<1.0/60||rounded.Seconds>48*3600)throw new Exception("La duración debe ser de al menos un fotograma y como máximo 48 horas");
   string id=Store.Id();var asset=new Asset{id=id,name=name,source=new MediaSource{kind="generated",uri="generated:"+id,resolver_type=kind,imported_at=DateTime.UtcNow.ToString("o")},presentation_origin=Rational.Time(0),recommended_duration=rounded,container_format="generated"};
   asset.streams.Add(new StreamInfo{id=Store.Id(),ordinal=0,kind="image",codec="generated",pts_origin=Rational.Time(0),time_base=new Rational(1,60),width=1920,height=1080});
   asset.extensions.Add(new Extension{@namespace=Namespace,version="1",payload=new Dictionary<string,object>{{"kind",kind},{"color_hex",hex.ToUpperInvariant()},{"note",note}}});
   return asset;
  }
 }
}
