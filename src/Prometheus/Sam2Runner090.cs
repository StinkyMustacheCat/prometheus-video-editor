using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Prometheus {
 public static class Sam2Worker01023 {
  static readonly object gate=new object(),errorsGate=new object();
  static Process process;static System.Threading.Timer idleTimer;static DateTime lastUse;static string errors="";
  const int IdleMinutes=4;
  static void Stop(){if(process==null)return;try{if(!process.HasExited)process.Kill();}catch{}try{process.Dispose();}catch{}process=null;}
  static void Expire(object state){lock(gate)if(process!=null&&DateTime.UtcNow-lastUse>TimeSpan.FromMinutes(IdleMinutes))Stop();}
  static void Ensure(string python,string script){if(process!=null&&!process.HasExited)return;Stop();lock(errorsGate)errors="";
   var info=new ProcessStartInfo(python,Tools.Q(script)+" --server"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
   process=new Process{StartInfo=info};process.ErrorDataReceived+=(s,e)=>{if(e.Data==null)return;lock(errorsGate){errors+=e.Data+"\n";if(errors.Length>4000)errors=errors.Substring(errors.Length-4000);}};
   process.Start();process.BeginErrorReadLine();try{process.PriorityClass=ProcessPriorityClass.BelowNormal;}catch{}
   if(idleTimer==null)idleTimer=new System.Threading.Timer(Expire,null,60000,60000);
  }
  public static void Shutdown(){lock(gate){Stop();if(idleTimer!=null){idleTimer.Dispose();idleTimer=null;}}}
  public static void Run(string python,string script,string config,CancellationToken token,Action<int,int,int> progress=null){lock(gate){token.ThrowIfCancellationRequested();Ensure(python,script);var active=process;lastUse=DateTime.UtcNow;
    using(token.Register(()=>{try{active.Kill();}catch{}}))try{
     active.StandardInput.WriteLine(Store.Json().Serialize(new Dictionary<string,string>{{"config",config}}));active.StandardInput.Flush();
     string line;while((line=active.StandardOutput.ReadLine())!=null){token.ThrowIfCancellationRequested();if(line=="COMPLETE"){lastUse=DateTime.UtcNow;return;}if(line.StartsWith("ERROR ",StringComparison.Ordinal))throw new Exception("SAM2: "+line.Substring(6));if(progress!=null&&line.StartsWith("PROGRESS ",StringComparison.Ordinal)){var parts=line.Split(' ');int current,total,frame;if(parts.Length>=4&&Int32.TryParse(parts[1],out current)&&Int32.TryParse(parts[2],out total)&&Int32.TryParse(parts[3],out frame))progress(current,total,frame);}}
     string failure;lock(errorsGate)failure=errors;throw new IOException("El proceso de SAM2 terminó antes de responder. "+failure);
    }catch{Stop();token.ThrowIfCancellationRequested();throw;}
   }}
 }
 public sealed class Sam2Candidate0103:IDisposable {
  public Bitmap Mask,Overlay;public int Area;
  public bool Contains(double x,double y){if(Mask==null)return false;int px=Math.Max(0,Math.Min(Mask.Width-1,(int)(x*Mask.Width))),py=Math.Max(0,Math.Min(Mask.Height-1,(int)(y*Mask.Height)));return Mask.GetPixel(px,py).R>127;}
  public void Dispose(){if(Mask!=null){Mask.Dispose();Mask=null;}if(Overlay!=null){Overlay.Dispose();Overlay=null;}}
 }
 public static class Sam2Runner090 {
  public sealed class CandidateRecord {public string file{get;set;}public int area{get;set;}}
  sealed class CachedCandidate {public byte[] mask,overlay;public int area;}
  static readonly object CandidateGate=new object();
  static readonly Dictionary<string,List<CachedCandidate>> CandidateCache=new Dictionary<string,List<CachedCandidate>>();
  static readonly Queue<string> CandidateOrder=new Queue<string>();
  static Project CandidateProject(Project project,string clipId){var isolated=Store.Copy(project);isolated.revision=0;isolated.sequences.RemoveAll(s=>s.id!=isolated.active_sequence_id);isolated.Active.clips.RemoveAll(c=>c.id!=clipId);isolated.Active.link_groups.Clear();isolated.Active.edit_groups.Clear();isolated.Active.transitions.Clear();var clean=isolated.Active.clips[0];clean.mask=null;TemporalMasks0823.Put(clean,null);Sam2MaskStore090.Put(clean,null);isolated.assets.RemoveAll(a=>a.id!=clean.asset_id);return isolated;}
  static string CandidateKey(Project isolated,string clipId,int frame){
   var asset=isolated.assets.First(a=>a.id==isolated.Active.clips[0].asset_id);
   var source=new StringBuilder(Store.Encode(isolated)).Append('|').Append(clipId).Append('|').Append(frame).Append('|').Append(Sam2SourceSettings01034.UseProxy);
   foreach(var representation in asset.representations){string path=representation.locator==null?null:representation.locator.path;if(String.IsNullOrWhiteSpace(path))continue;var info=new FileInfo(path);source.Append('|').Append(path).Append('|').Append(info.Exists?info.Length:-1).Append('|').Append(info.Exists?info.LastWriteTimeUtc.Ticks:0);}
   using(var hash=SHA256.Create())return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(source.ToString())));
  }
  static Bitmap DecodeCandidate(byte[] bytes){using(var stream=new MemoryStream(bytes))using(var image=new Bitmap(stream))return new Bitmap(image);}
  static List<Sam2Candidate0103> MaterializeCandidates(List<CachedCandidate> entries){return entries.Select(item=>new Sam2Candidate0103{Mask=DecodeCandidate(item.mask),Overlay=DecodeCandidate(item.overlay),Area=item.area}).ToList();}
  public static List<Sam2Candidate0103> Candidates(Project project,string clipId,int frame,CancellationToken token){
   var clip=project.Active.clips.FirstOrDefault(c=>c.id==clipId);if(clip==null)throw new Exception("El clip ya no existe");
   var isolated=CandidateProject(project,clipId);
   string cacheKey=CandidateKey(isolated,clipId,frame);
   lock(CandidateGate){List<CachedCandidate> saved;if(CandidateCache.TryGetValue(cacheKey,out saved))return MaterializeCandidates(saved);}
   string runtime=Path.Combine(Tools.Root,"runtime","sam2"),python=Path.Combine(Tools.Root,"runtime","whisper","python","python.exe"),script=Path.Combine(runtime,"run_sam2.py");
   string work=Path.Combine(Path.GetTempPath(),"Prometheus-SAM2-candidates-"+Store.Id()),frames=Path.Combine(work,"frames"),output=Path.Combine(work,"masks");Directory.CreateDirectory(frames);Directory.CreateDirectory(output);
   try{
    bool useLight=Sam2SourceSettings01034.For(isolated,clipId);
    int width=960,height=Math.Max(2,(int)Math.Round(width*(double)project.Active.height/project.Active.width/2)*2);
    var asset=project.assets.First(a=>a.id==clip.asset_id);var stream=asset.streams.First(s=>s.id==clip.stream_id);
    using(var image=Rendering.FrameExact(isolated,clip.timeline_start.Seconds+(stream.kind=="image"?0:frame/60.0),token,width,height,useLight))image.Save(Path.Combine(frames,"000000.jpg"),ImageFormat.Jpeg);
    string configFile=Path.Combine(work,"config.json");File.WriteAllText(configFile,Store.Json().Serialize(new Dictionary<string,object>{{"frames",frames},{"output",output},{"candidates",true}}));
    try{Sam2Worker01023.Run(python,script,configFile,token);}catch(IOException){Tools.Run(python,Tools.Q(script)+" --config "+Tools.Q(configFile),token,true);}
    var records=Store.Json().Deserialize<List<CandidateRecord>>(File.ReadAllText(Path.Combine(output,"candidates.json")));
    token.ThrowIfCancellationRequested();
    var compressed=records.Select(record=>new CachedCandidate{mask=File.ReadAllBytes(Path.Combine(output,record.file)),overlay=File.ReadAllBytes(Path.Combine(output,"overlay-"+record.file)),area=record.area}).ToList();
    var result=MaterializeCandidates(compressed);
    lock(CandidateGate){if(!CandidateCache.ContainsKey(cacheKey)){CandidateCache[cacheKey]=compressed;CandidateOrder.Enqueue(cacheKey);while(CandidateOrder.Count>6)CandidateCache.Remove(CandidateOrder.Dequeue());}}
    return result;
   }finally{try{Directory.Delete(work,true);}catch{}}
  }
  public static Bitmap Preview(Project project,string clipId,int frame,List<Sam2Prompt090> prompts,CancellationToken token){
   var clip=project.Active.clips.FirstOrDefault(c=>c.id==clipId);if(clip==null)throw new Exception("El clip ya no existe");
   var points=prompts.Where(p=>p.frame==frame).ToList();if(points.Count==0||!points.Any(p=>p.positive))throw new Exception("Añade al menos un punto positivo en este fotograma");
   string runtime=Path.Combine(Tools.Root,"runtime","sam2"),python=Path.Combine(Tools.Root,"runtime","whisper","python","python.exe"),script=Path.Combine(runtime,"run_sam2.py");
   string work=Path.Combine(Path.GetTempPath(),"Prometheus-SAM2-preview-"+Store.Id()),frames=Path.Combine(work,"frames"),output=Path.Combine(work,"masks");Directory.CreateDirectory(frames);Directory.CreateDirectory(output);
   try{
    var isolated=Store.Copy(project);isolated.Active.clips.RemoveAll(c=>c.id!=clipId);isolated.Active.link_groups.Clear();isolated.Active.edit_groups.Clear();isolated.Active.transitions.Clear();var clean=isolated.Active.clips[0];clean.mask=null;TemporalMasks0823.Put(clean,null);Sam2MaskStore090.Put(clean,null);bool useLight=Sam2SourceSettings01034.For(isolated,clipId);
    int width=960,height=Math.Max(2,(int)Math.Round(width*(double)project.Active.height/project.Active.width/2)*2);
    var asset=project.assets.First(a=>a.id==clip.asset_id);var stream=asset.streams.First(s=>s.id==clip.stream_id);
    using(var image=Rendering.FrameExact(isolated,clip.timeline_start.Seconds+(stream.kind=="image"?0:frame/60.0),token,width,height,useLight))image.Save(Path.Combine(frames,"000000.jpg"),ImageFormat.Jpeg);
    var config=new Dictionary<string,object>{{"frames",frames},{"output",output},{"preview",true},{"prompts",points.Select(p=>new Dictionary<string,object>{{"frame",0},{"x",p.x},{"y",p.y},{"positive",p.positive},{"object_id",p.object_id}}).ToArray()}};
    string configFile=Path.Combine(work,"config.json");File.WriteAllText(configFile,Store.Json().Serialize(config));
    try{Sam2Worker01023.Run(python,script,configFile,token);}catch(IOException){Tools.Run(python,Tools.Q(script)+" --config "+Tools.Q(configFile),token,true);}
    string mask=Path.Combine(output,"000000.png");if(!File.Exists(mask))throw new Exception("SAM2 no devolvió una vista de la selección");
    using(var file=new Bitmap(mask))return new Bitmap(file);
   }finally{try{Directory.Delete(work,true);}catch{}}
  }
  public static Sam2MaskTrack090 Track(Project project,string clipId,string projectFile,List<Sam2Prompt090> prompts,CancellationToken token,Action<int,string> progress,Sam2MaskTrack090 seed=null,Action<string,string,int> frameReady=null){
   var clip=project.Active.clips.FirstOrDefault(c=>c.id==clipId);if(clip==null)throw new Exception("El clip ya no existe");
   var asset=project.assets.FirstOrDefault(a=>a.id==clip.asset_id);var stream=asset==null?null:asset.streams.FirstOrDefault(s=>s.id==clip.stream_id);
   if(stream==null||stream.kind!="video")throw new Exception("El seguimiento SAM2 requiere un clip de vídeo");
   if(Media.SelectRepresentation(asset,false)==null&&!(MgtMedia.IsMgt(asset)&&Media.SelectRepresentation(asset,true)!=null)&&(!Sam2SourceSettings01034.UseProxy||Media.SelectRepresentation(asset,true)==null))throw new Exception("Prepara o reconecta el medio antes de crear la máscara SAM2");
   string runtime=Path.Combine(Tools.Root,"runtime","sam2"),python=Path.Combine(Tools.Root,"runtime","whisper","python","python.exe"),script=Path.Combine(runtime,"run_sam2.py");
   if(!File.Exists(python)||!File.Exists(script)||!File.Exists(Path.Combine(runtime,"sam2.1_hiera_small.pt")))throw new Exception("Falta el motor local SAM2");
   string work=Path.Combine(Path.GetTempPath(),"Prometheus-SAM2-"+Store.Id());string framesDir=Path.Combine(work,"frames");string output=Path.Combine(Sam2MaskStore090.DirectoryFor(projectFile,project.id,clip.id),"run-"+Store.Id().Substring(0,12));
   Directory.CreateDirectory(framesDir);Directory.CreateDirectory(output);
   try{
    if(progress!=null)progress(1,"Preparando vídeo del clip…");
    var isolated=Store.Copy(project);var sequence=isolated.Active;sequence.clips.RemoveAll(c=>c.id!=clipId);sequence.link_groups.Clear();sequence.edit_groups.Clear();sequence.transitions.Clear();bool useLight=Sam2SourceSettings01034.For(isolated,clipId);
    var clean=sequence.clips[0];clean.mask=null;TemporalMasks0823.Put(clean,null);Sam2MaskStore090.Put(clean,null);
    var window=Rendering.PreviewWindow(isolated,clip.timeline_start.Seconds,clip.duration.Seconds);
    int width=Math.Max(2,(int)Math.Round(Math.Min(960,project.Active.width)/(double)project.Active.width*project.Active.width/2)*2);
    int height=Math.Max(2,(int)Math.Round(width*(double)project.Active.height/project.Active.width/2)*2);
    var options=ExportOptions.For(window);options.Width=width;options.Height=height;options.Crf=16;options.Preset="veryfast";
    string source=Path.Combine(work,"clip.mp4");
    Tools.Run(Tools.FFmpeg,Rendering.Build(window,true,false,width,height,60,source,0,useLight,1,options),token,true);
    if(progress!=null)progress(20,"Extrayendo fotogramas para SAM2…");
    Tools.Run(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -y -i "+Tools.Q(source)+" -q:v 2 -start_number 0 "+Tools.Q(Path.Combine(framesDir,"%06d.jpg")),token,true);
    int count=Directory.GetFiles(framesDir,"*.jpg").Length;if(count<1)throw new Exception("No se pudieron extraer fotogramas del clip");
    foreach(var point in prompts)point.frame=Math.Max(0,Math.Min(count-1,point.frame));
    var seedMasks=new List<Dictionary<string,object>>();if(seed!=null&&seed.static_preview)foreach(var layer in Sam2MaskStore090.Layers(seed)){string file=Path.Combine(Sam2MaskStore090.LayerDirectory(seed,layer),"000000.png");if(File.Exists(file))seedMasks.Add(new Dictionary<string,object>{{"frame",Math.Max(0,Math.Min(count-1,seed.anchor_frame))},{"object_id",layer.object_id},{"path",file}});}
    var config=new Dictionary<string,object>{{"frames",framesDir},{"output",output},{"prompts",seedMasks.Count>0?new object[0]:prompts.Select(p=>new Dictionary<string,object>{{"frame",p.frame},{"x",p.x},{"y",p.y},{"positive",p.positive},{"object_id",p.object_id}}).Cast<object>().ToArray()},{"seed_masks",seedMasks.ToArray()}};
    string configFile=Path.Combine(work,"config.json");File.WriteAllText(configFile,Store.Json().Serialize(config));
    if(progress!=null)progress(25,"Siguiendo el objeto con SAM2…");
    DateTime lastVisual=DateTime.MinValue;
    Action<int,int,int> update=(current,total,index)=>{if(total>0&&progress!=null)progress(25+(int)(current*70.0/total),"SAM2 · "+current+" / "+total+" fotogramas");if(frameReady!=null&&DateTime.UtcNow-lastVisual>TimeSpan.FromMilliseconds(300)){string sourceFrame=Path.Combine(framesDir,index.ToString("D6")+".jpg"),maskFrame=Path.Combine(output,index.ToString("D6")+".png");if(File.Exists(sourceFrame)&&File.Exists(maskFrame)){lastVisual=DateTime.UtcNow;frameReady(sourceFrame,maskFrame,index);}}};
    try{Sam2Worker01023.Run(python,script,configFile,token,update);}catch(IOException){
     using(var process=Tools.Start(python,Tools.Q(script)+" --config "+Tools.Q(configFile),true)){
      string errors="";process.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)errors+=e.Data+"\n";};process.BeginErrorReadLine();
      using(token.Register(()=>{try{process.Kill();}catch{}})){
       string line;while((line=process.StandardOutput.ReadLine())!=null){token.ThrowIfCancellationRequested();if(line.StartsWith("PROGRESS ")){var parts=line.Split(' ');int current,total,index;if(parts.Length>=4&&Int32.TryParse(parts[1],out current)&&Int32.TryParse(parts[2],out total)&&Int32.TryParse(parts[3],out index))update(current,total,index);}}
       process.WaitForExit();token.ThrowIfCancellationRequested();if(process.ExitCode!=0)throw new Exception("SAM2 no completó el seguimiento: "+(errors.Length>1200?errors.Substring(errors.Length-1200):errors));
      }
     }
    }
    if(Directory.GetFiles(output,"*.png").Length!=count)throw new Exception("La máscara SAM2 está incompleta");
    if(progress!=null)progress(100,"Máscara SAM2 lista");
    return new Sam2MaskTrack090{directory=output,frames=count,width=width,height=height,fps=60,source_in=clip.source_in.Seconds,duration=clip.duration.Seconds,speed=clip.speed.Seconds,prompts=prompts,layers=(seedMasks.Count>0?seedMasks.Select(x=>Convert.ToInt32(x["object_id"])):prompts.Select(p=>p.object_id)).Distinct().OrderBy(x=>x).Select(x=>new Sam2Layer0104{object_id=x}).ToList()};
   }finally{try{Directory.Delete(work,true);}catch{}}
  }
 }
}
