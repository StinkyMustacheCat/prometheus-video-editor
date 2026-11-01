using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Prometheus {
 // Preview files are derived data. The project owns the transition document and parameters,
 // never a path to a rendered preview. A fingerprint includes every visual source in its span.
 public static class MgtTransitionCache {
  public static readonly string Root=String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROMETHEUS_TRANSITION_CACHE_ROOT"))?
   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","tc"):
   Path.GetFullPath(Environment.GetEnvironmentVariable("PROMETHEUS_TRANSITION_CACHE_ROOT"));
  public static bool IsMgt(TransitionInstance t){return t!=null&&t.enabled&&t.transition_id==TransitionContracts.Mgt;}
  public static int RenderFps(int outputFps){return outputFps==25?60:outputFps;}
  public static int Frames(TransitionInstance t,int fps){return Math.Max(1,(int)Math.Round(t.duration_frames*fps/60.0));}
  public static int Cut(TransitionInstance t,int fps){return (int)Math.Round(t.cut_frame*fps/60.0);}
  public static int StartFrame(TransitionInstance t,int fps){return Cut(t,fps)-(Frames(t,fps)+1)/2;}
  public static double Start(TransitionInstance t,int fps){return StartFrame(t,fps)/(double)fps;}
  public static double End(TransitionInstance t,int fps){return (StartFrame(t,fps)+Frames(t,fps))/(double)fps;}
  static string Signature(Project project,TransitionInstance transition,string profile,int fps,int width,int height){
   double begin=Start(transition,fps),end=End(transition,fps);var seq=project.Active;
     var clips=seq.clips.Where(c=>c.enabled&&seq.tracks.Any(t=>t.id==c.track_id&&t.enabled&&t.kind=="video")&&c.timeline_start.Seconds<end+0.000001&&c.End>begin-0.000001).OrderBy(c=>c.id).ToList();
   var assets=project.assets.Where(a=>clips.Any(c=>c.asset_id==a.id)).OrderBy(a=>a.id).ToList();
   var resource=project.mgt_transitions.First(r=>r.id==transition.mgt_resource_id);
   var renderResource=new{resource.id,resource.name,resource.document,resource.external_id,resource.minimum_seconds,resource.maximum_seconds,resource.recommended_seconds};
   var visual=new Dictionary<string,object>{{"contract","mgt-transition-v1"},{"profile",profile},{"fps",fps},{"width",width},{"height",height},
    {"sequence",new[]{seq.id,seq.width.ToString(),seq.height.ToString(),seq.color_space}},
    {"transition",Store.Fields(transition)},{"resource",renderResource},
    {"clips",Store.Fields(clips)},{"tracks",Store.Fields(seq.tracks.Where(t=>t.kind=="video").OrderBy(t=>t.id).ToList())},
    {"assets",Store.Fields(assets)},{"resources",Store.Fields(project.effect_resources)}};
   var engine=Path.Combine(Path.GetDirectoryName(typeof(MgtEngine).Assembly.Location),"mgt-engine","motor","server.cjs");
   visual["engine_stamp"]=File.Exists(engine)?File.GetLastWriteTimeUtc(engine).Ticks.ToString():"missing";
   foreach(var asset in assets){var rep=Media.SelectRepresentation(asset,profile=="preview");if(rep!=null&&rep.locator!=null&&File.Exists(rep.locator.path))visual["file:"+asset.id]=new[]{new FileInfo(rep.locator.path).Length.ToString(),File.GetLastWriteTimeUtc(rep.locator.path).Ticks.ToString()};}
   byte[] bytes=Encoding.UTF8.GetBytes(Store.Json().Serialize(visual));using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
  }
  public static string DirectoryFor(Project project,TransitionInstance transition,string profile,int fps,int width,int height){return Path.Combine(Root,Signature(project,transition,profile,fps,width,height).Substring(0,40));}
  public static bool Ready(Project project,TransitionInstance transition,string profile,int fps,int width,int height){if(!IsMgt(transition))return false;string dir=DirectoryFor(project,transition,profile,fps,width,height);return File.Exists(Path.Combine(dir,"ready"))&&File.Exists(Path.Combine(dir,"000000.png"))&&File.Exists(Path.Combine(dir,(Frames(transition,fps)-1).ToString("D6")+".png"));}
  static double Handle(Project project,Clip clip,bool before){
   var asset=project.assets.Find(a=>a.id==clip.asset_id);var stream=asset==null?null:asset.streams.Find(s=>s.id==clip.stream_id);
   if(stream==null)return 0;
   if(stream.kind=="image"||GeneratedMedia.IsUntimedPlaceholder(asset))return 60;
   if(before)return Math.Max(0,clip.source_in.Seconds/clip.speed.Seconds);
   if(stream.duration==null)return 0;
   return Math.Max(0,(stream.duration.Seconds-clip.source_in.Seconds-clip.duration.Seconds*clip.speed.Seconds)/clip.speed.Seconds);
  }
  static Project Input(Project original,TransitionInstance transition,bool outgoing,int fps){
   var result=Store.Copy(original);var seq=result.Active;var left=seq.clips.Find(c=>c.id==transition.left_clip_id);var right=seq.clips.Find(c=>c.id==transition.right_clip_id);
   double begin=Start(transition,fps),end=End(transition,fps),cut=transition.cut_frame/60.0;
   if(left==null||right==null)throw new Exception("Faltan los clips de la transición.");
   seq.transitions.RemoveAll(t=>t.transition_id==TransitionContracts.Mgt||t.left_clip_id==(outgoing?right.id:left.id)||t.right_clip_id==(outgoing?right.id:left.id));
   if(outgoing){seq.clips.Remove(right);double extension=Math.Min(end-cut,Handle(original,left,false));left.duration=Rational.Frame(cut+extension-left.timeline_start.Seconds);}
   else{seq.clips.Remove(left);double delta=Math.Min(cut-begin,Handle(original,right,true));right.timeline_start=Rational.Frame(cut-delta);var source=result.assets.Find(a=>a.id==right.asset_id);var stream=source.streams.Find(s=>s.id==right.stream_id);if(stream.kind!="image"&&!GeneratedMedia.IsUntimedPlaceholder(source))right.source_in=Rational.Time(right.source_in.Seconds-delta*right.speed.Seconds);right.duration=Rational.Frame(right.duration.Seconds+delta);}
   seq.link_groups.RemoveAll(g=>{g.clip_ids.RemoveAll(id=>!seq.clips.Any(c=>c.id==id));return g.clip_ids.Count<2;});
   seq.edit_groups.RemoveAll(g=>{g.clip_ids.RemoveAll(id=>!seq.clips.Any(c=>c.id==id));return g.clip_ids.Count<2;});
   Store.Validate(result);return result;
  }
  static string Png(Bitmap image){using(var memory=new MemoryStream()){image.Save(memory,ImageFormat.Png);return Convert.ToBase64String(memory.ToArray());}}
  static Dictionary<string,object> Parameters(MgtTransitionResource resource,TransitionInstance transition){
   var map=new Dictionary<string,object>();var raw=Store.Json().Deserialize<Dictionary<string,object>>(resource.document);object definitions;
   if(raw.TryGetValue("parameters",out definitions))foreach(var item in definitions as object[]??new object[0]){var spec=item as Dictionary<string,object>;if(spec==null)continue;string id=Convert.ToString(spec["id"]);object value;map[id]=spec.TryGetValue("default",out value)?value:null;}
   foreach(var pair in transition.parameters){var value=pair.Value;map[pair.Key]=value.kind=="number"?(object)value.number.GetValueOrDefault():value.kind=="boolean"?(object)value.boolean.GetValueOrDefault():value.text;}
   return map;
  }
  public static string Ensure(Project project,TransitionInstance transition,string profile,int fps,int width,int height,CancellationToken cancel,Action<int> progress){
   if(!IsMgt(transition))throw new Exception("No es una transición MGT activa.");
   string destination=DirectoryFor(project,transition,profile,fps,width,height);if(Ready(project,transition,profile,fps,width,height))return destination;
   Directory.CreateDirectory(Root);string temp=destination+".partial-"+Store.Id();Directory.CreateDirectory(temp);
   var resource=project.mgt_transitions.First(r=>r.id==transition.mgt_resource_id);int frames=Frames(transition,fps),cut=Cut(transition,fps),first=StartFrame(transition,fps),pre=(frames+1)/2;
   string session=null;
   try{
    using(var engine=new MgtEngine(cancel)){
     var imported=engine.ImportTransition(resource.document,cancel);session=MgtEngine.String(imported,"transitionId");if(String.IsNullOrEmpty(session))throw new Exception("MGT no devolvió una sesión de transición.");
     var coverage=new{A=new{firstOffset=-pre,lastOffset=frames-pre-1},B=new{firstOffset=-pre,lastOffset=frames-pre-1}};
     var parameters=Parameters(resource,transition);
     var input=new{transitionId=session,profile,fps,width,height,frames,cutFrame=cut,available=coverage,@params=parameters};
     engine.PreflightTransition(input,cancel);
     var a=Input(project,transition,true,fps);var b=Input(project,transition,false,fps);
     var left=project.Active.clips.Find(c=>c.id==transition.left_clip_id);var right=project.Active.clips.Find(c=>c.id==transition.right_clip_id);
     double lastA=Math.Min(End(transition,fps),left.End+Handle(project,left,false))-1.0/60;
     double firstB=Math.Max(Start(transition,fps),right.timeline_start.Seconds-Handle(project,right,true));
     for(int i=0;i<frames;i++){
      cancel.ThrowIfCancellationRequested();double time=(first+i)/(double)fps;
      using(var imageA=Rendering.FrameExact(a,Math.Min(time,lastA),cancel,width,height,profile=="preview"))using(var imageB=Rendering.FrameExact(b,Math.Max(time,firstB),cancel,width,height,profile=="preview")){
       var request=new{transitionId=session,profile,fps,width,height,frames,cutFrame=cut,available=coverage,@params=parameters,frameIndex=i,offset=i-pre,inputs=new{A=Png(imageA),B=Png(imageB)}};
       var result=engine.TransitionFrame(request,cancel);if(result.Length<8||result[0]!=137||result[1]!=80)throw new Exception("MGT no devolvió un fotograma PNG válido.");
       File.WriteAllBytes(Path.Combine(temp,i.ToString("D6")+".png"),result);
      }
      if(progress!=null)progress((i+1)*100/frames);
     }
     engine.ReleaseTransition(session);session=null;
    }
    File.WriteAllText(Path.Combine(temp,"ready"),"mgt-transition-v1");
    if(Directory.Exists(destination)){string expected=Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;if(!Path.GetFullPath(destination).StartsWith(expected,StringComparison.OrdinalIgnoreCase))throw new Exception("Ruta de caché fuera del directorio esperado.");Directory.Delete(destination,true);}
    Directory.Move(temp,destination);return destination;
   }finally{if(Directory.Exists(temp))Directory.Delete(temp,true);}
  }
  public static Bitmap Apply(Project project,Bitmap image,double time,int fps,int width,int height){
   fps=60; // One canonical preview clock; playback may display only every second/fourth frame.
     foreach(var transition in project.Active.transitions.Where(t=>IsMgt(t)&&FFmpegTransitionAdapter.Visible(project.Active,t))){
    int index=(int)Math.Floor((time-Start(transition,fps))*fps+0.00001);
    if(index<0||index>=Frames(transition,fps))continue;
    if(Ready(project,transition,"preview",fps,width,height)){
     string file=Path.Combine(DirectoryFor(project,transition,"preview",fps,width,height),index.ToString("D6")+".png");
     if(File.Exists(file))using(var overlay=Image.FromFile(file))using(var g=Graphics.FromImage(image))g.DrawImage(overlay,0,0,width,height);
    }else using(var g=Graphics.FromImage(image))using(var font=new Font("Segoe UI",Math.Max(10,width/65f),FontStyle.Bold))using(var brush=new SolidBrush(Color.FromArgb(210,28,31,36))){
     string label="TRANSICIÓN PENDIENTE DE PROCESAR";var area=new RectangleF(width*0.09f,height*0.84f,width*0.82f,Math.Max(28,height*0.1f));g.FillRectangle(brush,area);g.DrawString(label,font,Brushes.WhiteSmoke,area,new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center});
    }
   }
   return image;
  }
 }
}
