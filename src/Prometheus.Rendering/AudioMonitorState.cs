using System;
using System.Threading;

namespace Prometheus {
 // Levels come from the PCM actually sent to the output device. Monitoring never changes a project.
 public static class AudioMonitorState {
  static long leftPeakBits,rightPeakBits,leftRmsBits,rightRmsBits;
  static int muteLeft,muteRight;
  public static bool MuteLeft {get{return Volatile.Read(ref muteLeft)!=0;}set{Volatile.Write(ref muteLeft,value?1:0);}}
  public static bool MuteRight {get{return Volatile.Read(ref muteRight)!=0;}set{Volatile.Write(ref muteRight,value?1:0);}}
  static double Read(ref long bits){return BitConverter.Int64BitsToDouble(Interlocked.Read(ref bits));}
  static void Put(ref long bits,double value){Interlocked.Exchange(ref bits,BitConverter.DoubleToInt64Bits(value));}
  public static double LeftPeak {get{return Read(ref leftPeakBits);}}
  public static double RightPeak {get{return Read(ref rightPeakBits);}}
  public static double LeftRms {get{return Read(ref leftRmsBits);}}
  public static double RightRms {get{return Read(ref rightRmsBits);}}
  public static void Reset(){Put(ref leftPeakBits,0);Put(ref rightPeakBits,0);Put(ref leftRmsBits,0);Put(ref rightRmsBits,0);}
  public static void Process(byte[] pcm,int count){
   double peakL=0,peakR=0,sumL=0,sumR=0;int frames=count/4;
   bool ml=MuteLeft,mr=MuteRight;
   for(int i=0;i<frames;i++){
    int at=i*4;short l=(short)(pcm[at]|pcm[at+1]<<8),r=(short)(pcm[at+2]|pcm[at+3]<<8);
    if(ml){l=0;pcm[at]=pcm[at+1]=0;}if(mr){r=0;pcm[at+2]=pcm[at+3]=0;}
    double a=Math.Abs((double)l)/32768.0,b=Math.Abs((double)r)/32768.0;
    peakL=Math.Max(peakL,a);peakR=Math.Max(peakR,b);sumL+=a*a;sumR+=b*b;
   }
   Put(ref leftPeakBits,peakL);Put(ref rightPeakBits,peakR);
   Put(ref leftRmsBits,frames==0?0:Math.Sqrt(sumL/frames));Put(ref rightRmsBits,frames==0?0:Math.Sqrt(sumR/frames));
  }
 }
}
