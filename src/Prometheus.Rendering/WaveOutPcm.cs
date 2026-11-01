using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Prometheus {
 public sealed class WaveOutPcm:IDisposable {
  [StructLayout(LayoutKind.Sequential)]struct WaveFormat {public ushort format,channels;public uint samplesPerSecond,bytesPerSecond;public ushort blockAlign,bitsPerSample,extraSize;}
  [StructLayout(LayoutKind.Sequential)]struct WaveHeader {public IntPtr data;public uint length,recorded;public IntPtr user;public uint flags,loops;public IntPtr next,reserved;}
  [StructLayout(LayoutKind.Explicit,Size=12)]struct WaveTime {[FieldOffset(0)]public uint type;[FieldOffset(4)]public uint samples;}
  [DllImport("winmm.dll")]static extern int waveOutOpen(out IntPtr handle,uint device,ref WaveFormat format,IntPtr callback,IntPtr instance,uint flags);
  [DllImport("winmm.dll")]static extern int waveOutPrepareHeader(IntPtr handle,IntPtr header,uint size);
  [DllImport("winmm.dll")]static extern int waveOutWrite(IntPtr handle,IntPtr header,uint size);
  [DllImport("winmm.dll")]static extern int waveOutUnprepareHeader(IntPtr handle,IntPtr header,uint size);
  [DllImport("winmm.dll")]static extern int waveOutReset(IntPtr handle);
  [DllImport("winmm.dll")]static extern int waveOutClose(IntPtr handle);
  [DllImport("winmm.dll")]static extern int waveOutGetPosition(IntPtr handle,ref WaveTime time,uint size);
  sealed class Buffer {public IntPtr Data,Header;}
  readonly List<Buffer> queued=new List<Buffer>();
  readonly Process process;
  readonly CancellationToken token;
  readonly uint headerSize=(uint)Marshal.SizeOf(typeof(WaveHeader));
  readonly byte[] first=new byte[19200];
  readonly Thread worker;
  IntPtr device;
  int firstCount;
  volatile bool stopping,finished;
  volatile Exception error;
  bool started,disposed;
  string stderr="";
  static int ReadSome(Stream stream,byte[] buffer){int total=0;while(total<buffer.Length){int count=stream.Read(buffer,total,buffer.Length-total);if(count==0)break;total+=count;}return total;}
  public WaveOutPcm(string args,CancellationToken cancellation){
   token=cancellation;
   var format=new WaveFormat{format=1,channels=2,samplesPerSecond=48000,bytesPerSecond=192000,blockAlign=4,bitsPerSample=16,extraSize=0};
   int result=waveOutOpen(out device,0xffffffff,ref format,IntPtr.Zero,IntPtr.Zero,0);
   if(result!=0)throw new InvalidOperationException("Salida de audio no disponible (waveOut "+result+")");
   try{
    process=Tools.Start(Tools.FFmpeg,args);
    process.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)stderr+=e.Data+"\n";};process.BeginErrorReadLine();
    using(token.Register(()=>{try{process.Kill();}catch{}}))firstCount=ReadSome(process.StandardOutput.BaseStream,first);
    token.ThrowIfCancellationRequested();
    if(firstCount==0)throw new Exception("No se pudo preparar audio de vista previa: "+stderr);
    worker=new Thread(Pump){IsBackground=true,Name="Prometheus preview audio"};
   }catch{try{if(process!=null)process.Kill();}catch{}if(process!=null)process.Dispose();waveOutClose(device);device=IntPtr.Zero;throw;}
  }
  void Write(byte[] bytes,int count){
   AudioMonitorState.Process(bytes,count);
   var item=new Buffer{Data=Marshal.AllocHGlobal(count),Header=Marshal.AllocHGlobal((int)headerSize)};
   Marshal.Copy(bytes,0,item.Data,count);
   Marshal.StructureToPtr(new WaveHeader{data=item.Data,length=(uint)count,loops=1},item.Header,false);
   int result=waveOutPrepareHeader(device,item.Header,headerSize);
   if(result!=0){Marshal.FreeHGlobal(item.Header);Marshal.FreeHGlobal(item.Data);throw new Exception("No se pudo preparar el audio ("+result+")");}
   queued.Add(item);result=waveOutWrite(device,item.Header,headerSize);
   if(result!=0)throw new Exception("No se pudo reproducir audio ("+result+")");
  }
  void Reap(){
   for(int i=queued.Count-1;i>=0;i--){var item=queued[i];var header=(WaveHeader)Marshal.PtrToStructure(item.Header,typeof(WaveHeader));if((header.flags&1)==0)continue;
    waveOutUnprepareHeader(device,item.Header,headerSize);Marshal.FreeHGlobal(item.Header);Marshal.FreeHGlobal(item.Data);queued.RemoveAt(i);
   }
  }
  void Pump(){
   try{
    var bytes=new byte[19200];
    while(!stopping&&!token.IsCancellationRequested){Reap();if(queued.Count>=8){Thread.Sleep(5);continue;}int count=ReadSome(process.StandardOutput.BaseStream,bytes);if(count==0)break;Write(bytes,count);}
    while(!stopping&&!token.IsCancellationRequested&&queued.Count>0){Reap();Thread.Sleep(5);}
    if(!stopping&&!token.IsCancellationRequested){process.WaitForExit();if(process.ExitCode!=0)throw new Exception("Audio de vista previa: "+stderr);}
   }catch(Exception ex){if(!stopping&&!token.IsCancellationRequested)error=ex;}finally{finished=true;}
  }
  public void Start(){if(started)return;started=true;Write(first,firstCount);worker.Start();}
  public void Check(){if(error!=null)throw error;}
  public double? PlayedSeconds{get{if(!started||disposed||device==IntPtr.Zero)return null;var position=new WaveTime{type=2};int result=waveOutGetPosition(device,ref position,12);return result==0&&position.type==2?position.samples/48000.0:(double?)null;}}
  public bool Finished{get{return started&&finished;}}
  public void Dispose(){if(disposed)return;disposed=true;stopping=true;try{process.Kill();}catch{}if(device!=IntPtr.Zero)waveOutReset(device);if(started&&worker.IsAlive)worker.Join();foreach(var item in queued){try{waveOutUnprepareHeader(device,item.Header,headerSize);}catch{}Marshal.FreeHGlobal(item.Header);Marshal.FreeHGlobal(item.Data);}queued.Clear();if(device!=IntPtr.Zero){waveOutClose(device);device=IntPtr.Zero;}process.Dispose();}
 }
}
