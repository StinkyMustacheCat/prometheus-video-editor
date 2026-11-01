using System;

namespace Prometheus {
 public static class FFmpegAudioEffectAdapter {
  // Runs after source trimming and speed conversion, before clip/track volume and timeline delay.
  public static string AudioFilters(Project project,Clip clip){
   string filters="";
   foreach(var effect in EffectContracts.Effective(project,clip)){
    if(effect.effect_id==EffectContracts.HighPassId&&effect.contract_version=="1.0.0")
     filters+=",highpass=f="+Tools.N(effect.parameters["frequency"].number.Value);
    else if(effect.effect_id==EffectContracts.LowPassId&&effect.contract_version=="1.0.0")
     filters+=",lowpass=f="+Tools.N(effect.parameters["frequency"].number.Value);
    else if(effect.effect_id==EffectContracts.CompressorId&&effect.contract_version=="1.0.0"){
     double threshold=Math.Pow(10,effect.parameters["threshold_db"].number.Value/20.0);
     double makeup=Math.Pow(10,effect.parameters["makeup_db"].number.Value/20.0);
     filters+=",acompressor=threshold="+Tools.N(threshold)+":ratio="+Tools.N(effect.parameters["ratio"].number.Value)+":attack="+Tools.N(effect.parameters["attack_ms"].number.Value)+":release="+Tools.N(effect.parameters["release_ms"].number.Value)+":makeup="+Tools.N(makeup);
    }
    else if(effect.effect_id==EffectContracts.EqualizerId){filters+=",bass=g="+Tools.N(effect.parameters["low_db"].number.Value)+",equalizer=f=1000:t=q:w=1:g="+Tools.N(effect.parameters["mid_db"].number.Value)+",treble=g="+Tools.N(effect.parameters["high_db"].number.Value);}
    else if(effect.effect_id==EffectContracts.LimiterId){double ceiling=Math.Pow(10,effect.parameters["ceiling_db"].number.Value/20);filters+=",alimiter=limit="+Tools.N(ceiling)+":level=false:latency=true";}
    else if(effect.effect_id==EffectContracts.DeEsserId)filters+=",deesser=i="+Tools.N(effect.parameters["intensity"].number.Value)+":m=0.6:f=0.5";
    else if(effect.effect_id==EffectContracts.GateId)filters+=",agate=threshold="+Tools.N(Math.Pow(10,effect.parameters["threshold_db"].number.Value/20))+":ratio="+Tools.N(effect.parameters["ratio"].number.Value);
    else if(effect.effect_id==EffectContracts.PitchId)filters+=",rubberband=tempo=1:pitch="+Tools.N(Math.Pow(2,effect.parameters["semitones"].number.Value/12))+":formant=preserved";
    else if(effect.effect_id==EffectContracts.EchoId)filters+=",aecho=in_gain=0.8:out_gain=0.6:delays="+Tools.N(effect.parameters["delay_ms"].number.Value)+":decays="+Tools.N(effect.parameters["decay"].number.Value);
    else if(effect.effect_id==EffectContracts.ChorusId)filters+=",chorus=in_gain=0.65:out_gain=0.7:delays=40:decays=0.4:speeds="+Tools.N(effect.parameters["speed"].number.Value)+":depths="+Tools.N(effect.parameters["depth"].number.Value);
    else if(effect.effect_id==EffectContracts.TremoloId)filters+=",tremolo=f="+Tools.N(effect.parameters["frequency"].number.Value)+":d="+Tools.N(effect.parameters["depth"].number.Value);
    else if(effect.effect_id==EffectContracts.VoiceCleanId){
     double strength=effect.parameters["strength"].number.Value;
     filters+=",highpass=f=75,afftdn=nr="+Tools.N(3+strength*.17)+":nf=-50,adeclick=t="+Tools.N(4-strength*.03)+",acompressor=threshold=0.1:ratio=2.5:attack=15:release=180:makeup=1.2,alimiter=limit=0.84:level=false:latency=true";
    }
    else throw new Exception("Efecto de audio no disponible para render: "+effect.effect_id);
   }
   return filters;
  }
 }
}
