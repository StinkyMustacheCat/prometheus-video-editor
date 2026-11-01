using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Prometheus {
 public sealed class EffectParameterSpec {
  public string id,kind,unit;
  public bool required,animatable;
  public double? minimum,maximum;
  public string[] choices=new string[0];
  public EffectValue initial;
 }
 public sealed class EffectContract {
  public string id,version,input_kind,output_kind,name,category;
  public string[] capabilities=new string[0];
  public EffectParameterSpec[] parameters=new EffectParameterSpec[0];
 }
 public static class EffectContracts {
  public const string Lut3DId="prometheus.color.lut3d";
  public const string Lut3DVersion="1.0.0";
  public const string FlipId="prometheus.image.flip-horizontal";
  public const string BasicColorId="prometheus.color.basic";
  public const string BlurId="prometheus.image.gaussian-blur";
  public const string SharpenId="prometheus.image.sharpen";
  public const string FlipVerticalId="prometheus.image.flip-vertical";
  public const string VignetteId="prometheus.image.vignette";
  public const string HueId="prometheus.color.hue";
  public const string CropId="prometheus.image.crop";
  public const string HighPassId="prometheus.audio.highpass";
  public const string ChromaKeyId="prometheus.image.chroma-key";
  public const string ExposureId="prometheus.color.exposure";
  public const string MonochromeId="prometheus.color.monochrome";
  public const string VibranceId="prometheus.color.vibrance";
  public const string PixelizeId="prometheus.image.pixelize";
  public const string LowPassId="prometheus.audio.lowpass";
  public const string CompressorId="prometheus.audio.compressor";
  public const string WhiteBalanceId="prometheus.color.white-balance",LevelsId="prometheus.color.levels",ShadowsHighlightsId="prometheus.color.shadows-highlights",GlowId="prometheus.image.glow";
  public const string EqualizerId="prometheus.audio.eq3",LimiterId="prometheus.audio.limiter",DeEsserId="prometheus.audio.deesser",GateId="prometheus.audio.gate",PitchId="prometheus.audio.pitch",EchoId="prometheus.audio.echo",ChorusId="prometheus.audio.chorus",TremoloId="prometheus.audio.tremolo";
  public const string VoiceCleanId="prometheus.audio.voice-clean";
  public const string ZoomInDynamicId="prometheus.motion.zoom-in-dynamic",ZoomOutDynamicId="prometheus.motion.zoom-out-dynamic",PanDynamicId="prometheus.motion.pan-dynamic";
  static readonly EffectContract lut3d=new EffectContract{
   id=Lut3DId,version=Lut3DVersion,name="LUT 3D",category="Color",input_kind="video",output_kind="video",
   capabilities=new[]{"file-resource","sdr-rec709","preview","export"},
   parameters=new[]{
    new EffectParameterSpec{id="lut",kind="resource",required=true,animatable=false},
    new EffectParameterSpec{id="interpolation",kind="choice",required=true,animatable=false,
     choices=new[]{"tetrahedral","trilinear"},initial=new EffectValue{kind="choice",text="tetrahedral"}}
   }
  };
  static EffectParameterSpec Number(string id,double initial,double minimum,double maximum){return new EffectParameterSpec{id=id,kind="number",required=true,animatable=false,minimum=minimum,maximum=maximum,initial=new EffectValue{kind="number",number=initial}};}
  static EffectParameterSpec AnimatedNumber(string id,double initial,double minimum,double maximum){var spec=Number(id,initial,minimum,maximum);spec.animatable=true;return spec;}
  static EffectParameterSpec Choice(string id,string initial,params string[] choices){return new EffectParameterSpec{id=id,kind="choice",required=true,animatable=false,choices=choices,initial=new EffectValue{kind="choice",text=initial}};}
  static readonly EffectContract flip=new EffectContract{id=FlipId,version="1.0.0",name="Voltear horizontal",category="Imagen",input_kind="video",output_kind="video",capabilities=new[]{"preview","export"}};
  static readonly EffectContract basic=new EffectContract{id=BasicColorId,version="1.0.0",name="Corrección básica",category="Color",input_kind="video",output_kind="video",capabilities=new[]{"sdr-rec709","preview","export"},parameters=new[]{AnimatedNumber("brightness",0,-1,1),AnimatedNumber("contrast",1,0,2),AnimatedNumber("saturation",1,0,3)}};
  static readonly EffectContract blur=new EffectContract{id=BlurId,version="1.0.0",name="Desenfoque gaussiano",category="Imagen",input_kind="video",output_kind="video",capabilities=new[]{"preview","export"},parameters=new[]{Number("sigma",2,0.1,20)}};
  static readonly EffectContract sharpen=new EffectContract{id=SharpenId,version="1.0.0",name="Nitidez",category="Imagen",input_kind="video",output_kind="video",capabilities=new[]{"preview","export"},parameters=new[]{Number("amount",0.7,0,2)}};
  static readonly EffectContract flipVertical=new EffectContract{id=FlipVerticalId,version="1.0.0",name="Voltear vertical",category="Imagen",input_kind="video",output_kind="video",capabilities=new[]{"preview","export"}};
  static readonly EffectContract vignette=new EffectContract{id=VignetteId,version="1.0.0",name="Viñeta",category="Color",input_kind="video",output_kind="video",capabilities=new[]{"preview","export"},parameters=new[]{Number("amount",0.35,0,1)}};
  static readonly EffectContract hue=new EffectContract{id=HueId,version="1.0.0",name="Tono",category="Color",input_kind="video",output_kind="video",capabilities=new[]{"preview","export"},parameters=new[]{AnimatedNumber("degrees",0,-180,180)}};
  static readonly EffectContract crop=new EffectContract{id=CropId,version="1.0.0",name="Recorte de bordes",category="Imagen",input_kind="video",output_kind="video",capabilities=new[]{"preview","export","alpha"},parameters=new[]{Number("left",0,0,95),Number("top",0,0,95),Number("right",0,0,95),Number("bottom",0,0,95)}};
  static readonly EffectContract highPass=new EffectContract{id=HighPassId,version="1.0.0",name="Corte de graves (paso alto)",category="Audio",input_kind="audio",output_kind="audio",capabilities=new[]{"preview","export"},parameters=new[]{Number("frequency",80,20,2000)}};
  static readonly EffectContract chromaKey=new EffectContract{id=ChromaKeyId,version="1.0.0",name="Croma por color",category="Imagen",input_kind="video",output_kind="video",capabilities=new[]{"preview","export","alpha"},parameters=new[]{
   new EffectParameterSpec{id="color",kind="text",required=true,initial=new EffectValue{kind="text",text="#00FF00"}},Number("similarity",0.12,0.01,1),Number("blend",0.02,0,1)}};
  static readonly EffectContract exposure=new EffectContract{id=ExposureId,version="1.0.0",name="Exposición",category="Color",input_kind="video",output_kind="video",capabilities=new[]{"preview","export"},parameters=new[]{Number("stops",0,-3,3),Number("black",0,-0.5,0.5)}};
  static readonly EffectContract monochrome=new EffectContract{id=MonochromeId,version="1.0.0",name="Blanco y negro",category="Color",input_kind="video",output_kind="video",capabilities=new[]{"preview","export"}};
  static readonly EffectContract vibrance=new EffectContract{id=VibranceId,version="1.0.0",name="Intensidad de color",category="Color",input_kind="video",output_kind="video",capabilities=new[]{"preview","export"},parameters=new[]{Number("intensity",0,-2,2)}};
  static readonly EffectContract pixelize=new EffectContract{id=PixelizeId,version="1.0.0",name="Pixelar",category="Imagen",input_kind="video",output_kind="video",capabilities=new[]{"preview","export"},parameters=new[]{Number("block",16,2,256)}};
  static readonly EffectContract lowPass=new EffectContract{id=LowPassId,version="1.0.0",name="Corte de agudos (paso bajo)",category="Audio",input_kind="audio",output_kind="audio",capabilities=new[]{"preview","export"},parameters=new[]{Number("frequency",10000,100,20000)}};
  static readonly EffectContract compressor=new EffectContract{id=CompressorId,version="1.0.0",name="Compresor",category="Audio",input_kind="audio",output_kind="audio",capabilities=new[]{"preview","export"},parameters=new[]{Number("threshold_db",-18,-60,-1),Number("ratio",3,1,20),Number("attack_ms",20,0.01,2000),Number("release_ms",250,0.01,9000),Number("makeup_db",0,0,24)}};
  static readonly EffectContract whiteBalance=new EffectContract{id=WhiteBalanceId,version="1.0.0",name="Temperatura y matiz",category="Color",input_kind="video",output_kind="video",capabilities=new[]{"preview","export"},parameters=new[]{Number("temperature",6500,1000,40000),Number("tint",0,-1,1)}};
  static readonly EffectContract levels=new EffectContract{id=LevelsId,version="1.0.0",name="Niveles",category="Color",input_kind="video",output_kind="video",capabilities=new[]{"preview","export"},parameters=new[]{Number("black",0,-0.5,0.9),Number("white",1,0.1,1.5),Number("gamma",1,0.1,5)}};
  static readonly EffectContract shadowsHighlights=new EffectContract{id=ShadowsHighlightsId,version="1.0.0",name="Sombras y luces",category="Color",input_kind="video",output_kind="video",capabilities=new[]{"preview","export"},parameters=new[]{Number("shadows",0,-1,1),Number("highlights",0,-1,1)}};
  static readonly EffectContract glow=new EffectContract{id=GlowId,version="1.0.0",name="Resplandor suave",category="Imagen",input_kind="video",output_kind="video",capabilities=new[]{"preview","export"},parameters=new[]{Number("amount",0.35,0,1),Number("radius",5,1,30)}};
  static readonly EffectContract equalizer=new EffectContract{id=EqualizerId,version="1.0.0",name="Ecualizador de tres bandas",category="Audio",input_kind="audio",output_kind="audio",capabilities=new[]{"preview","export"},parameters=new[]{Number("low_db",0,-18,18),Number("mid_db",0,-18,18),Number("high_db",0,-18,18)}};
  static readonly EffectContract limiter=new EffectContract{id=LimiterId,version="1.0.0",name="Limitador",category="Audio",input_kind="audio",output_kind="audio",capabilities=new[]{"preview","export"},parameters=new[]{Number("ceiling_db",-1,-24,0)}};
  static readonly EffectContract deEsser=new EffectContract{id=DeEsserId,version="1.0.0",name="Reducción de sibilancias",category="Audio",input_kind="audio",output_kind="audio",capabilities=new[]{"preview","export"},parameters=new[]{Number("intensity",0.5,0,1)}};
  static readonly EffectContract gate=new EffectContract{id=GateId,version="1.0.0",name="Puerta de ruido",category="Audio",input_kind="audio",output_kind="audio",capabilities=new[]{"preview","export"},parameters=new[]{Number("threshold_db",-38,-60,-6),Number("ratio",3,1,20)}};
  static readonly EffectContract pitch=new EffectContract{id=PitchId,version="1.0.0",name="Tono sin cambiar duración",category="Audio",input_kind="audio",output_kind="audio",capabilities=new[]{"preview","export"},parameters=new[]{Number("semitones",0,-12,12)}};
  static readonly EffectContract echo=new EffectContract{id=EchoId,version="1.0.0",name="Eco",category="Audio",input_kind="audio",output_kind="audio",capabilities=new[]{"preview","export"},parameters=new[]{Number("delay_ms",350,10,1500),Number("decay",0.35,0,0.9)}};
  static readonly EffectContract chorus=new EffectContract{id=ChorusId,version="1.0.0",name="Coro",category="Audio",input_kind="audio",output_kind="audio",capabilities=new[]{"preview","export"},parameters=new[]{Number("depth",1.5,0.1,5),Number("speed",0.4,0.1,5)}};
  static readonly EffectContract tremolo=new EffectContract{id=TremoloId,version="1.0.0",name="Trémolo",category="Audio",input_kind="audio",output_kind="audio",capabilities=new[]{"preview","export"},parameters=new[]{Number("frequency",5,0.1,20),Number("depth",0.5,0,1)}};
  static readonly EffectContract voiceClean=new EffectContract{id=VoiceCleanId,version="1.0.0",name="Limpiar y nivelar voz",category="Audio · Voz",input_kind="audio",output_kind="audio",capabilities=new[]{"preview","export"},parameters=new[]{Number("strength",35,0,100)}};
  static readonly EffectContract zoomInDynamic=new EffectContract{id=ZoomInDynamicId,version="1.0.0",name="Zoom In dinámico",category="Movimiento",input_kind="video",output_kind="video",capabilities=new[]{"preview","export","relative-duration"},parameters=new[]{Number("zoom_percent",15,1,100),Choice("motion","Bézier","Lineal","Bézier","Acelerado al final")}};
  static readonly EffectContract zoomOutDynamic=new EffectContract{id=ZoomOutDynamicId,version="1.0.0",name="Zoom Out dinámico",category="Movimiento",input_kind="video",output_kind="video",capabilities=new[]{"preview","export","relative-duration"},parameters=new[]{Number("zoom_percent",15,1,100),Choice("motion","Bézier","Lineal","Bézier","Acelerado al final")}};
  static readonly EffectContract panDynamic=new EffectContract{id=PanDynamicId,version="1.0.0",name="Paneo dinámico",category="Movimiento",input_kind="video",output_kind="video",capabilities=new[]{"preview","export","relative-duration"},parameters=new[]{Number("distance_percent",15,1,100),Choice("direction","Izquierda → derecha","Izquierda → derecha","Derecha → izquierda","Arriba → abajo","Abajo → arriba","Diagonal ↘","Diagonal ↗","Diagonal ↙","Diagonal ↖"),Choice("motion","Bézier","Lineal","Bézier","Acelerado al final")}};
  static readonly EffectContract[] contracts={lut3d,basic,whiteBalance,levels,shadowsHighlights,blur,glow,sharpen,flip,flipVertical,vignette,hue,crop,zoomInDynamic,zoomOutDynamic,panDynamic,highPass,chromaKey,exposure,monochrome,vibrance,pixelize,lowPass,compressor,equalizer,limiter,deEsser,gate,pitch,echo,chorus,tremolo,voiceClean};
  public static IEnumerable<EffectContract> All {get{return contracts;}}
  public static EffectContract Find(string id,string version){return contracts.FirstOrDefault(c=>c.id==id&&c.version==version);}
  public static EffectInstance New(string effectId){var contract=contracts.FirstOrDefault(c=>c.id==effectId);if(contract==null||effectId==Lut3DId)throw new ArgumentException("Selecciona un efecto compatible");return new EffectInstance{id=Store.Id(),effect_id=contract.id,contract_version=contract.version,parameters=contract.parameters.ToDictionary(p=>p.id,p=>new EffectValue{kind=p.initial.kind,number=p.initial.number,text=p.initial.text,boolean=p.initial.boolean,resource_id=p.initial.resource_id})};}
  public static IEnumerable<EffectInstance> Effective(Project project,Clip clip){
   var track=project.Active.tracks.First(t=>t.id==clip.track_id);
   if(clip.inherit_track_effects&&track.effects_enabled)foreach(var effect in track.effects.Where(e=>e.enabled))yield return effect;
   foreach(var effect in clip.effects.Where(e=>e.enabled))yield return effect;
  }
  public static EffectInstance NewLut3D(string resourceId){
   if(String.IsNullOrWhiteSpace(resourceId))throw new ArgumentException("Falta el archivo LUT");
   return new EffectInstance{id=Store.Id(),effect_id=Lut3DId,contract_version=Lut3DVersion,
    parameters=new Dictionary<string,EffectValue>{
     {"lut",new EffectValue{kind="resource",resource_id=resourceId}},
     {"interpolation",new EffectValue{kind="choice",text="tetrahedral"}}
    }};
  }
  static void ValidateValue(EffectValue value){
   if(value==null||String.IsNullOrWhiteSpace(value.kind))throw new Exception("Parámetro de efecto inválido");
   if(value.number.HasValue&&(Double.IsNaN(value.number.Value)||Double.IsInfinity(value.number.Value)))throw new Exception("Número de efecto inválido");
   switch(value.kind){
    case "number":if(!value.number.HasValue)throw new Exception("Número de efecto ausente");break;
    case "boolean":if(!value.boolean.HasValue)throw new Exception("Valor lógico de efecto ausente");break;
    case "text":case "choice":if(value.text==null)throw new Exception("Texto de efecto ausente");break;
    case "resource":if(String.IsNullOrWhiteSpace(value.resource_id))throw new Exception("Recurso de efecto ausente");break;
    default:throw new Exception("Tipo de parámetro de efecto desconocido: "+value.kind);
   }
  }
  public static void Validate(Project project,Clip clip,string trackKind){Validate(project,clip.effects,trackKind);}
  public static void Validate(Project project,List<EffectInstance> effects,string trackKind){
   if(effects==null)throw new Exception("Falta la lista de efectos");
   var ids=new HashSet<string>();
   foreach(var effect in effects){
    if(effect==null||String.IsNullOrWhiteSpace(effect.id)||!ids.Add(effect.id)||
     String.IsNullOrWhiteSpace(effect.effect_id)||String.IsNullOrWhiteSpace(effect.contract_version)||
     !Regex.IsMatch(effect.contract_version,@"^\d+\.\d+\.\d+$")||effect.parameters==null)
     throw new Exception("Instancia de efecto inválida");
    foreach(var pair in effect.parameters){if(String.IsNullOrWhiteSpace(pair.Key))throw new Exception("Parámetro de efecto sin nombre");ValidateValue(pair.Value);}
    var contract=Find(effect.effect_id,effect.contract_version);
    if(contract==null)continue; // Conserva contratos que esta versión todavía no puede procesar.
    if(contract.input_kind!=trackKind)throw new Exception("El efecto no admite esta pista");
    if(effect.parameters.Count!=contract.parameters.Length||contract.parameters.Any(spec=>!effect.parameters.ContainsKey(spec.id)))
     throw new Exception("Parámetros incompletos para "+effect.effect_id);
    foreach(var spec in contract.parameters){
     var value=effect.parameters[spec.id];
     if(value.kind!=spec.kind)throw new Exception("Tipo incorrecto para "+spec.id);
     if(value.number.HasValue&&(spec.minimum.HasValue&&value.number<spec.minimum||spec.maximum.HasValue&&value.number>spec.maximum))
      throw new Exception("Parámetro fuera de límites: "+spec.id);
     if(spec.kind=="choice"&&!spec.choices.Contains(value.text))throw new Exception("Opción de efecto desconocida: "+value.text);
     if(spec.kind=="resource"&&!project.effect_resources.Any(r=>r.id==value.resource_id&&r.kind=="lut3d"))
      throw new Exception("Referencia LUT no encontrada en el proyecto");
    }
    if(effect.effect_id==CropId&&(effect.parameters["left"].number.Value+effect.parameters["right"].number.Value>=99||effect.parameters["top"].number.Value+effect.parameters["bottom"].number.Value>=99))
     throw new Exception("El recorte no puede eliminar toda la imagen");
    if(effect.effect_id==ChromaKeyId&&!Regex.IsMatch(effect.parameters["color"].text??"",@"^#[0-9A-Fa-f]{6}$"))
     throw new Exception("El color de croma debe usar #RRGGBB");
   }
  }
 }
}
