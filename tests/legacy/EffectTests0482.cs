using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class EffectTests0482 {
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static Color Pixel(Project project,int x,int y){using(var frame=Rendering.Frame(project,0.5,CancellationToken.None,320,320))return frame.GetPixel(x,y);}
 static void Wav(string path,double frequency){
  int samples=48000;using(var stream=File.Create(path))using(var writer=new BinaryWriter(stream)){
   writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+samples*4);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)2);writer.Write(48000);writer.Write(48000*4);writer.Write((short)4);writer.Write((short)16);writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(samples*4);
   for(int i=0;i<samples;i++){short sample=(short)(Math.Sin(2*Math.PI*frequency*i/48000)*12000);writer.Write(sample);writer.Write(sample);}
  }
 }
 static double[] Rms(Project p){
  string args=Rendering.Build(p,false,true,320,320,60,"audio-raw",0,false);
  using(var process=Tools.Start(Tools.FFmpeg,args)){
   byte[] data;using(var output=new MemoryStream()){process.StandardOutput.BaseStream.CopyTo(output);data=output.ToArray();}
   string error=process.StandardError.ReadToEnd();process.WaitForExit();if(process.ExitCode!=0)throw new Exception(error);
   double[] sum={0,0};int count=data.Length/4;
   for(int i=0;i<count;i++){short left=BitConverter.ToInt16(data,i*4),right=BitConverter.ToInt16(data,i*4+2);sum[0]+=left*(double)left;sum[1]+=right*(double)right;}
   return new[]{Math.Sqrt(sum[0]/count),Math.Sqrt(sum[1]/count)};
  }
 }
 static void Main(){
  Tools.Root=AppDomain.CurrentDomain.BaseDirectory;string folder=Path.Combine(Tools.Root,"effect-tests-0482-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  try{
   var p=Store.New();p.Active.width=320;p.Active.height=320;
   var baseAsset=GeneratedMedia.CreateSolid("Fondo","0000FF",1);p.assets.Add(baseAsset);Media.Insert(p,baseAsset.id,p.Active.tracks.First(t=>t.kind=="video").id,0);
   string image=Path.Combine(folder,"chroma.png");using(var bitmap=new Bitmap(320,320)){using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.Lime);g.FillRectangle(Brushes.Red,120,120,80,80);}bitmap.Save(image);}
   var foreground=Media.Inspect(image,CancellationToken.None);p.assets.Add(foreground);Media.Insert(p,foreground.id,p.Active.tracks.Last(t=>t.kind=="video").id,0);
   var clip=p.Active.clips.Single(c=>c.asset_id==foreground.id);clip.effects.Add(EffectContracts.New(EffectContracts.ChromaKeyId));
   Check(Pixel(p,20,20).B>180&&Pixel(p,160,160).R>180,"croma revela la pista inferior y conserva el primer plano");
   string movie=Path.Combine(folder,"chroma.mp4");Rendering.Export(p,movie,CancellationToken.None);Check(File.Exists(movie)&&new FileInfo(movie).Length>1000,"croma funciona en la exportación");
   string exportedFrame=Path.Combine(folder,"frame.png");Tools.Run(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -y -ss 0.5 -i "+Tools.Q(movie)+" -frames:v 1 "+Tools.Q(exportedFrame),CancellationToken.None);
   using(var bitmap=new Bitmap(exportedFrame))Check(bitmap.GetPixel(20,20).B>160&&bitmap.GetPixel(160,160).R>160,"exportación conserva el croma visible en el monitor");
   var restoredPath=Path.Combine(folder,"project.prometheus.json");Store.Save(p,restoredPath);var reopened=Store.Load(restoredPath);Check(reopened.Active.clips.Any(c=>c.effects.Any(e=>e.effect_id==EffectContracts.ChromaKeyId)),"contrato de croma se conserva al guardar");
   var bad=EffectContracts.New(EffectContracts.ChromaKeyId);bad.parameters["color"].text="texto";clip.effects.Add(bad);bool invalid=false;try{Store.Validate(p);}catch{invalid=true;}clip.effects.Remove(bad);Check(invalid,"contrato rechaza colores de croma inválidos");
   string low=Path.Combine(folder,"low.wav"),high=Path.Combine(folder,"high.wav");Wav(low,100);Wav(high,997);
   Func<string,Project> audioProject=file=>{var q=Store.New();var asset=Media.Inspect(file,CancellationToken.None);q.assets.Add(asset);Media.Insert(q,asset.id,q.Active.tracks.First(t=>t.kind=="audio").id,0);return q;};
   var lowProject=audioProject(low);var highProject=audioProject(high);var lowClip=lowProject.Active.clips.Single();var highClip=highProject.Active.clips.Single();
   double beforeLow=Rms(lowProject)[0],beforeHigh=Rms(highProject)[0];lowClip.effects.Add(EffectContracts.New(EffectContracts.HighPassId));highClip.effects.Add(EffectContracts.New(EffectContracts.HighPassId));lowClip.effects[0].parameters["frequency"].number=400;highClip.effects[0].parameters["frequency"].number=400;
   double afterLow=Rms(lowProject)[0],afterHigh=Rms(highProject)[0];Check(afterLow<beforeLow*0.25&&afterHigh>beforeHigh*0.75,"filtro de graves atenúa bajas frecuencias y conserva la voz alta");
   highClip.balance=1;var balanced=Rms(highProject);Check(balanced[0]<balanced[1]*0.1,"balance desplaza audio al canal derecho");
   highProject.Active.tracks.First(t=>t.kind=="audio").effects.Add(EffectContracts.New(EffectContracts.HighPassId));highProject.Active.tracks.First(t=>t.kind=="audio").effects_enabled=false;Check(FFmpegAudioEffectAdapter.AudioFilters(highProject,highClip).Split(new[]{"highpass"},StringSplitOptions.None).Length==2,"Fx de pista apagado excluye su filtro de audio");
   string wavExport=Path.Combine(folder,"audio.wav");Rendering.Export(highProject,wavExport,CancellationToken.None);Check(File.Exists(wavExport)&&new FileInfo(wavExport).Length>1000,"efecto de audio y balance exportan WAV");
   var exportedAudio=audioProject(wavExport);var exportedLevels=Rms(exportedAudio);Check(exportedLevels[0]<exportedLevels[1]*0.1,"exportación conserva el balance aplicado en el monitor de audio");
   string saved=Path.Combine(folder,"audio.prometheus.json");Store.Save(highProject,saved);var opened=Store.Load(saved);Check(Math.Abs(opened.Active.clips.Single().balance-1)<0.0001&&opened.Active.clips.Single().effects.Count==1,"audio y efectos sobreviven al guardado");
  }finally{try{Directory.Delete(folder,true);}catch{}}
 }
}


