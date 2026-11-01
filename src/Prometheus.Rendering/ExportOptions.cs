using System;

namespace Prometheus {
 public sealed class ExportOptions {
  public bool Video=true,UsePreview,SelectiveWeb;
  public long? StartFrame,EndFrame;
  public double RenderDuration;
  public int Width,Height,Fps=60,Crf=20,AudioBitrateKbps=192,TargetBitrateKbps=8000;
  public string Preset="veryfast",RateControl="crf",Acceleration="nvenc";
  public static ExportOptions For(Project project){return new ExportOptions{Width=project.Active.width,Height=project.Active.height};}
  public ExportOptions ForWindow(double duration){var copy=(ExportOptions)MemberwiseClone();copy.StartFrame=null;copy.EndFrame=null;copy.RenderDuration=duration;return copy;}
  public static int[] AtShortSide(Sequence sequence,int shortSide){
   if(shortSide<=0)throw new Exception("Resolución inválida");
   double scale=(double)shortSide/Math.Min(sequence.width,sequence.height);
   int width=Math.Max(2,(int)Math.Round(sequence.width*scale/2)*2);
   int height=Math.Max(2,(int)Math.Round(sequence.height*scale/2)*2);
   SequenceCanvas.Validate(width,height);
   return new[]{width,height};
  }
  public void Validate(Project project){
   if(project==null||project.Active==null)throw new Exception("No hay una secuencia activa");
   if(StartFrame.HasValue||EndFrame.HasValue){if(!StartFrame.HasValue||!EndFrame.HasValue||StartFrame.Value<0||EndFrame.Value<=StartFrame.Value||EndFrame.Value/60.0>project.Active.Length+0.000001)throw new Exception("El rango de entrada y salida no es válido");}
   if(SelectiveWeb&&(!Video||UsePreview))throw new Exception("La descarga por tramos requiere vídeo final sin vistas ligeras");
   if(Video){
    SequenceCanvas.Validate(Width,Height);
    if(Math.Abs((double)Width/Height-(double)project.Active.width/project.Active.height)>0.006)
     throw new Exception("La resolución debe conservar la proporción del lienzo. Cambia el lienzo en Ajustes para usar otro formato");
    if(Fps!=24&&Fps!=25&&Fps!=30&&Fps!=60)throw new Exception("La salida admite 24, 25, 30 o 60 FPS");
    if(Crf<15||Crf>32||RateControl!="crf"&&RateControl!="bitrate"||TargetBitrateKbps<500||TargetBitrateKbps>100000)
     throw new Exception("Calidad de vídeo fuera de rango");
    if(Preset!="ultrafast"&&Preset!="veryfast"&&Preset!="fast"&&Preset!="medium"&&Preset!="slow")throw new Exception("Preset de codificación inválido");
    if(AudioBitrateKbps<96||AudioBitrateKbps>320)throw new Exception("Bitrate de audio fuera de rango");
   }
  }
 }
}
