using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Prometheus {
 public sealed class WaveformData {
  public readonly string Key;
  public readonly int BinsPerSecond;
  public readonly short[] Minimum,Maximum;
  public int Count { get { return Minimum.Length; } }
  public WaveformData(string key,int binsPerSecond,short[] minimum,short[] maximum){
   if(minimum==null||maximum==null||minimum.Length!=maximum.Length)throw new ArgumentException("Picos inválidos");
   Key=key;BinsPerSecond=binsPerSecond;Minimum=minimum;Maximum=maximum;
  }
 }

 public static class WaveformCache {
  const int SampleRate=8000,FramesPerBin=40,BinsPerSecond=SampleRate/FramesPerBin,Magic=0x31574650;

  public static string Identity(Asset asset,StreamInfo stream,MediaRepresentation representation){
   if(asset==null||stream==null||stream.kind!="audio"||representation==null||representation.locator==null)throw new ArgumentException("Se requiere un stream de audio local");
   string path=Path.GetFullPath(representation.locator.path);
   var file=new FileInfo(path);
   if(!file.Exists)throw new FileNotFoundException("Audio no disponible",path);
   int ordinal;
   if(representation.stream_ordinals==null||!representation.stream_ordinals.TryGetValue(stream.id,out ordinal))throw new Exception("El audio no está mapeado en esta representación");
   string description="PWF1|"+path.ToUpperInvariant()+"|"+file.Length+"|"+file.LastWriteTimeUtc.Ticks+"|"+ordinal;
   using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(description))).Replace("-","").ToLowerInvariant();
  }

  public static string CachePath(string folder,string key){return Path.Combine(folder,key+".pwf");}

  public static WaveformData LoadOrBuild(Asset asset,StreamInfo stream,MediaRepresentation representation,string folder,CancellationToken token){
   string key=Identity(asset,stream,representation),target=CachePath(folder,key);
   token.ThrowIfCancellationRequested();
   if(File.Exists(target))try{return Load(target,key);}catch(IOException){TryDelete(target);}catch(InvalidDataException){TryDelete(target);}
   Directory.CreateDirectory(folder);
   var minima=new List<short>();var maxima=new List<short>();
   short low=short.MaxValue,high=short.MinValue;int frames=0;
   int ordinal=representation.stream_ordinals[stream.id];
   string args="-hide_banner -loglevel error -nostdin -i "+Tools.Q(representation.locator.path)+" -map 0:"+ordinal+" -vn -ac 2 -ar "+SampleRate+" -f s16le pipe:1";
   using(var process=Tools.Start(Tools.FFmpeg,args,true)){
    var error=new StringBuilder();
    process.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)lock(error)error.AppendLine(e.Data);};
    process.BeginErrorReadLine();
    using(token.Register(()=>{try{if(!process.HasExited)process.Kill();}catch{}})){
     byte[] buffer=new byte[32768],frame=new byte[4];int filled=0,read;
     while((read=process.StandardOutput.BaseStream.Read(buffer,0,buffer.Length))>0){
      token.ThrowIfCancellationRequested();
      for(int i=0;i<read;i++){
       frame[filled++]=buffer[i];
       if(filled!=4)continue;
       filled=0;
       short left=(short)(frame[0]|frame[1]<<8),right=(short)(frame[2]|frame[3]<<8);
       if(left<low)low=left;if(right<low)low=right;
       if(left>high)high=left;if(right>high)high=right;
       if(++frames==FramesPerBin){minima.Add(low);maxima.Add(high);low=short.MaxValue;high=short.MinValue;frames=0;}
      }
     }
     process.WaitForExit();token.ThrowIfCancellationRequested();
     if(process.ExitCode!=0){lock(error)throw new Exception("No se pudo analizar el audio: "+error.ToString());}
    }
   }
   if(frames>0){minima.Add(low);maxima.Add(high);}
   if(minima.Count==0)throw new InvalidDataException("No se encontraron muestras de audio");
   if(Identity(asset,stream,representation)!=key)throw new IOException("El medio cambió durante el análisis de picos");
   var data=new WaveformData(key,BinsPerSecond,minima.ToArray(),maxima.ToArray());
   string temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{
    using(var output=new BinaryWriter(new FileStream(temp,FileMode.CreateNew,FileAccess.Write))){
     output.Write(Magic);output.Write(BinsPerSecond);output.Write(data.Count);
     for(int i=0;i<data.Count;i++){output.Write(data.Minimum[i]);output.Write(data.Maximum[i]);}
    }
    token.ThrowIfCancellationRequested();
    if(!File.Exists(target))try{File.Move(temp,target);}catch(UnauthorizedAccessException){if(!File.Exists(target))File.Copy(temp,target);}
   }finally{TryDelete(temp);}
   return data;
  }

  static WaveformData Load(string path,string key){
   using(var input=new BinaryReader(File.OpenRead(path))){
    int magic=input.ReadInt32(),rate=input.ReadInt32(),count=input.ReadInt32();
    if(magic!=Magic||rate!=BinsPerSecond||count<=0||count>100000000||input.BaseStream.Length!=12L+count*4L)throw new InvalidDataException("Caché de ondas inválida");
    var low=new short[count];var high=new short[count];
    for(int i=0;i<count;i++){low[i]=input.ReadInt16();high[i]=input.ReadInt16();}
    return new WaveformData(key,rate,low,high);
   }
  }
  static void TryDelete(string path){try{if(File.Exists(path))File.Delete(path);}catch{}}
 }
}
