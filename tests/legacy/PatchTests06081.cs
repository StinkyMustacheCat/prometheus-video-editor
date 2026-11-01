using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Prometheus;

class PatchTests06081 {
 static void Check(bool condition,string label){if(!condition)throw new Exception(label);Console.WriteLine("PASS "+label);}
 static void Seed(InstructionMediaAdapter adapter,string url,string kind,Asset asset){
  var field=typeof(InstructionMediaAdapter).GetField("prepared",BindingFlags.NonPublic|BindingFlags.Instance);
  ((Dictionary<string,Asset>)field.GetValue(adapter))[kind+"|"+url]=asset;
 }
 static Project CutPlaceholders(){
  var project=Store.New();var video=GeneratedMedia.CreatePlaceholder("Plano pendiente","",12);var audio=GeneratedMedia.CreateAudioPlaceholder("Sonido pendiente","",12);
  project.assets.Add(video);project.assets.Add(audio);
  string videoTrack=project.Active.tracks.First(t=>t.kind=="video").id,audioTrack=project.Active.tracks.First(t=>t.kind=="audio").id;
  for(int i=0;i<120;i++){
   double at=i/10.0;
   project.Active.clips.Add(new Clip{id=Store.Id(),name=video.name,asset_id=video.id,stream_id=video.streams[0].id,track_id=videoTrack,timeline_start=Rational.Frame(at),source_in=Rational.Time(at),duration=Rational.Frame(0.1)});
   project.Active.clips.Add(new Clip{id=Store.Id(),name=audio.name,asset_id=audio.id,stream_id=audio.streams[0].id,track_id=audioTrack,timeline_start=Rational.Time(at),source_in=Rational.Time(at),duration=Rational.Time(0.1)});
  }
  Store.Validate(project);return project;
 }
 static void LongPlayback(){
  var project=CutPlaceholders();
  string command=Rendering.Build(project,true,false,64,36,15,"raw",0,true).Replace("-an -c:v rawvideo","-frames:v 1 -an -c:v rawvideo");
  Check(command.Length>32767,"el proyecto reproduce un comando que excedía el límite de Windows");
  using(var process=Tools.Start(Tools.FFmpeg,command)){
   Check(process.StartInfo.Arguments.Contains("-/filter_complex"),"el gráfico largo se entrega a FFmpeg por archivo temporal");
   string errors="";process.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)errors+=e.Data+"\n";};process.BeginErrorReadLine();
   byte[] first=new byte[64*36*4];int total=0;while(total<first.Length){int count=process.StandardOutput.BaseStream.Read(first,total,first.Length-total);if(count==0)break;total+=count;}
   try{process.Kill();}catch{}process.WaitForExit();
   Check(total==first.Length,"FFmpeg produce un fotograma con muchos espacios reservados: "+errors);
  }
  string audio=Rendering.Build(project,false,true,64,36,15,"audio-raw",0,true);
  Check(audio.Contains("asplit=120"),"el audio reservado cortado comparte una entrada sin perder segmentos");
  using(var process=Tools.Start(Tools.FFmpeg,audio)){
   byte[] samples=new byte[19200];int count=process.StandardOutput.BaseStream.Read(samples,0,samples.Length);
   try{process.Kill();}catch{}process.WaitForExit();
   Check(count>0,"FFmpeg produce audio con muchos cortes de un espacio reservado");
  }
 }
 static void ImageInstructions(string root){
  string file=Path.Combine(root,"image.png");using(var bitmap=new Bitmap(40,30)){using(var graphics=Graphics.FromImage(bitmap))graphics.Clear(Color.MediumPurple);bitmap.Save(file);}
  var image=Media.Inspect(file,CancellationToken.None);const string url="https://2.bp.blogspot.com/-LpgABnjhe1k/UJadAdRb6XI/AAAAAAAAODg/zxeACHFnBYo/s1600/pato+COLOR+04.jpg";
  image.source=new MediaSource{kind="web",uri=url,resolver_type="image-http"};
  var project=Store.New();var adapter=new InstructionMediaAdapter();Seed(adapter,url,"video",image);Seed(adapter,url,"image",image);
  string videoTrack=project.Active.tracks.First(t=>t.kind=="video").id;
  var doc=Instructions.New(project);doc.actions.Add(new InstructionAction{op="media.import_url",url=url,source_kind="video",track_id=videoTrack,at_seconds=2,duration_seconds=4});
  var plan=Instructions.Preview(project,doc,adapter);Check(plan.Result.Active.clips.Count==1&&plan.Result.Active.clips[0].asset_id==image.id,"un enlace de imagen entra en pista visual mediante Instrucciones");
  var explicitImage=Instructions.New(project);explicitImage.actions.Add(new InstructionAction{op="media.import_url",url=url,source_kind="image",track_id=videoTrack,at_seconds=2,duration_seconds=4});
  var explicitPlan=Instructions.Preview(project,explicitImage,adapter);Check(explicitPlan.Result.Active.clips.Count==1,"source_kind image también se acepta");
  var placeholder=GeneratedMedia.CreatePlaceholder("Pendiente","",4);project.assets.Add(placeholder);Media.Insert(project,placeholder.id,videoTrack,0);
  string id=project.Active.clips.Single().id;var fill=Instructions.New(project);fill.actions.Add(new InstructionAction{op="placeholder.fill_url",clip_id=id,url=url});
  var filled=Instructions.Preview(project,fill,adapter);Check(filled.Result.Active.clips.Single().asset_id==image.id,"una imagen por enlace rellena un espacio reservado");
  var audioPlaceholder=GeneratedMedia.CreateAudioPlaceholder("Sonido pendiente","",4);project.assets.Add(audioPlaceholder);
  string audioTrack=project.Active.tracks.First(t=>t.kind=="audio").id;Media.Insert(project,audioPlaceholder.id,audioTrack,0);
  string audioId=project.Active.clips.First(c=>c.track_id==audioTrack).id;
  project.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=new List<string>{id,audioId}});Store.Validate(project);
  var linkedFill=Instructions.New(project);linkedFill.actions.Add(new InstructionAction{op="placeholder.fill_url",clip_id=id,url=url});
  var linkedPlan=Instructions.Preview(project,linkedFill,adapter);
  Check(linkedPlan.Result.Active.clips.First(c=>c.id==id).asset_id==image.id&&GeneratedMedia.IsPlaceholder(linkedPlan.Result.assets.Find(a=>a.id==linkedPlan.Result.Active.clips.First(c=>c.id==audioId).asset_id)),"una imagen rellena solo el componente visual de un par vinculado");
 }
 static void DragAndCut(string root){
  string path=Path.Combine(root,"drop.png");using(var bitmap=new Bitmap(40,30)){bitmap.Save(path);}
  var project=Store.New();var placeholder=GeneratedMedia.CreatePlaceholder("Pendiente","",10);var audioPlaceholder=GeneratedMedia.CreateAudioPlaceholder("Audio pendiente","",10);var image=Media.Inspect(path,CancellationToken.None);
  project.assets.Add(placeholder);project.assets.Add(audioPlaceholder);project.assets.Add(image);
  string videoTrack=project.Active.tracks.First(t=>t.kind=="video").id,audioTrack=project.Active.tracks.First(t=>t.kind=="audio").id;
  Media.Insert(project,placeholder.id,videoTrack,0);Media.Insert(project,audioPlaceholder.id,audioTrack,0);
  var visual=project.Active.clips.First(c=>c.track_id==videoTrack);var sound=project.Active.clips.First(c=>c.track_id==audioTrack);
  project.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=new List<string>{visual.id,sound.id}});Store.Validate(project);
  int filledAsset=0,filledFile=0,filledWeb=0,generic=0;
  using(var form=new Form{Width=1100,Height=520})using(var timeline=new Timeline{Project=project,Dock=DockStyle.Fill}){
   form.Controls.Add(timeline);form.Show();Application.DoEvents();
   timeline.CanFillPlaceholderAsset=(clipId,assetId)=>PlaceholderReplacement.Accepts(project,project.Active.clips.Find(c=>c.id==clipId),project.assets.Find(a=>a.id==assetId));
   timeline.CanFillPlaceholderFiles=(clipId,files)=>MediaDropCompatibility.CanFillPlaceholder(files,project,project.Active.clips.Find(c=>c.id==clipId));
   timeline.CanFillPlaceholderWeb=(clipId,web)=>clipId==visual.id&&WebMedia.IsImageUrl(web.Url);
   timeline.FillPlaceholderAsset=(clipId,assetId)=>{Check(clipId==visual.id&&assetId==image.id,"la imagen apunta al espacio correcto");filledAsset++;};
   timeline.FillPlaceholderFiles=(clipId,files)=>{Check(clipId==visual.id&&files.Single()==path,"el archivo apunta al espacio correcto");filledFile++;};
   timeline.FillPlaceholderWeb=(clipId,web)=>{Check(clipId==visual.id&&web.Url.EndsWith(".jpg"),"el enlace apunta al espacio correcto");filledWeb++;};
   timeline.DropAsset=(assetId,trackId,at)=>generic++;
   var enter=typeof(Timeline).GetMethod("OnDragEnter",BindingFlags.NonPublic|BindingFlags.Instance);
   var drop=typeof(Timeline).GetMethod("OnDragDrop",BindingFlags.NonPublic|BindingFlags.Instance);
   Func<Clip,System.Drawing.Point> point=clip=>{var bounds=timeline.ClipBounds(clip);return timeline.PointToScreen(new System.Drawing.Point((int)bounds.Left+20,(int)bounds.Top+10));};
   Action<IDataObject,Clip,bool> drag=(data,clip,allowed)=>{var location=point(clip);var e=new DragEventArgs(data,0,location.X,location.Y,DragDropEffects.Copy,DragDropEffects.Copy);enter.Invoke(timeline,new object[]{e});Check((e.Effect==DragDropEffects.Copy)==allowed,"el arrastre indica compatibilidad antes de soltar");drop.Invoke(timeline,new object[]{e});};
   var assetData=new DataObject();assetData.SetData("Prometheus.AssetId",image.id);drag(assetData,visual,true);
   var fileData=new DataObject();fileData.SetData(DataFormats.FileDrop,new[]{path});drag(fileData,visual,true);
   var webData=new DataObject();webData.SetData(DataFormats.UnicodeText,"https://example.com/picture.jpg");drag(webData,visual,true);
   drag(assetData,sound,false);
   Check(filledAsset==1&&filledFile==1&&filledWeb==1&&generic==0,"los tres arrastres rellenan el espacio sin insertar otro clip");
   form.Close();
  }
  typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  using(var form=new MainForm()){
   form.Show();Application.DoEvents();
   var editor=(Editor)typeof(MainForm).GetField("editor",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
   editor.Reset(project);
   typeof(MainForm).GetMethod("RefreshProject",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);
   var timeline=(Timeline)typeof(MainForm).GetField("timeline",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
   timeline.Cursor=3;timeline.Selected=visual.id;timeline.Selection.Add(visual.id);
   typeof(MainForm).GetField("playing",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,true);
   typeof(MainForm).GetMethod("Cut",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);
   Check((bool)typeof(MainForm).GetField("playing",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form),"dividir no detiene la reproducción");
   Check(editor.Project.Active.clips.Count==4,"dividir corta también el espacio de audio vinculado a esa posición");
   typeof(MainForm).GetField("playing",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,false);
   Check(timeline.FillPlaceholderAsset!=null&&timeline.FillPlaceholderFiles!=null&&timeline.FillPlaceholderWeb!=null,"la línea de tiempo conecta los tres destinos con Prometheus");
   timeline.FillPlaceholderAsset(visual.id,image.id);
   var replaced=editor.Project.Active.clips.Find(c=>c.id==visual.id);
   Check(replaced.asset_id==image.id&&Math.Abs(replaced.duration.Seconds-3)<0.001&&replaced.timeline_start.Seconds==0,"soltar una imagen conserva la posición y duración del espacio reservado");
   typeof(MainForm).GetField("dirty",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,false);
   form.Close();
  }
 }
 [STAThread]static void Main(){
  Tools.Root=AppDomain.CurrentDomain.BaseDirectory;
  string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"prometheus-06081-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);Rendering.GeneratedCacheRoot=Path.Combine(root,"generated");
  try{LongPlayback();ImageInstructions(root);DragAndCut(root);Console.WriteLine("PatchTests06081 OK");}
  finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }
}
