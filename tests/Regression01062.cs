using System;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using Prometheus;

class Regression01062 {
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Main(){
  string runtime=Environment.GetEnvironmentVariable("PROMETHEUS_TEST_RUNTIME");
  if(String.IsNullOrWhiteSpace(runtime)||!File.Exists(Path.Combine(runtime,"runtime","ffmpeg.exe"))||!File.Exists(Path.Combine(runtime,"runtime","ffprobe.exe")))throw new Exception("PROMETHEUS_TEST_RUNTIME debe apuntar a un paquete con runtime\\ffmpeg.exe y runtime\\ffprobe.exe.");
  Tools.Root=runtime;
  var p=Store.New();var video=p.Active.tracks.First(t=>t.kind=="video");
  var solid=GeneratedMedia.CreateSolid("Solo","FF7744",4);p.assets.Add(solid);Media.Insert(p,solid.id,video.id,0);
  var original=p.Active.clips.First(c=>c.asset_id==solid.id);int trackCount=p.Active.tracks.Count;
  var outer=NestedMedia0910.Create(p,new[]{original.id},"Solo anidado");Store.Validate(p);
  Check(NestedMedia0910.IsNested(p,outer)&&p.Active.tracks.Count==trackCount&&outer.track_id==video.id,"Anidar un medio conserva su pista");
  var otherTrack=Store.AddTrack(p.Active,"video");var blue=GeneratedMedia.CreateSolid("Azul","0000FF",4);p.assets.Add(blue);Media.Insert(p,blue.id,otherTrack.id,0);
  var outside=p.Active.clips.First(c=>c.asset_id==blue.id);
  var context=Instructions.CopyCompactState(p,"tracks",new[]{video.id});
  Check(context.Contains("context_track_ids")&&context.Contains(video.id)&&!context.Contains(outside.id),"Contexto compacto limita los clips a la pista elegida");
  var doc=Instructions.New(p);doc.context_sequence_id=p.Active.id;doc.context_track_ids=new List<string>{video.id};doc.actions.Add(new InstructionAction{op="clip.delete",sequence_id=p.Active.id,clip_id=outside.id});
  var plan=Instructions.Preview(p,doc);Check(plan.Cards[0].status=="invalid","Instrucciones impiden alterar otra pista");
  string image=Path.Combine(Path.GetTempPath(),"prometheus-blur-01062-"+Guid.NewGuid().ToString("N")+".png");using(var bitmap=new Bitmap(8,8)){using(var graphics=Graphics.FromImage(bitmap))graphics.Clear(Color.Coral);bitmap.Save(image,ImageFormat.Png);}
  try{var blurProject=Store.New();var bottom=blurProject.Active.tracks.Where(t=>t.kind=="video").OrderBy(t=>t.order).First();var photo=new Asset{id=Store.Id(),name="Foto",source=new MediaSource{kind="local",uri=image,imported_at=DateTime.UtcNow.ToString("o")},presentation_origin=Rational.Time(0),recommended_duration=Rational.Time(3),container_format="png"};photo.streams.Add(new StreamInfo{id=Store.Id(),ordinal=0,kind="image",codec="png",pts_origin=Rational.Time(0),time_base=new Rational(1,60),width=8,height=8});blurProject.assets.Add(photo);Media.Insert(blurProject,photo.id,bottom.id,0);int beforeTracks=blurProject.Active.tracks.Count;var blurred=NestedMedia0910.CreateBlurredBackground(blurProject,blurProject.Active.clips.First(c=>c.asset_id==photo.id).id);Store.Validate(blurProject);Check(blurProject.Active.tracks.Count==beforeTracks&&blurred.track_id==bottom.id,"Fondo desenfocado no deja pista residual");}finally{File.Delete(image);}
  var bulk=Store.New();var chosen=bulk.Active.tracks.First();string firstName=chosen.name;var batch=Instructions.New(bulk);for(int i=0;i<115;i++)batch.actions.Add(new InstructionAction{op="track.rename",sequence_id=bulk.Active.id,track_id=chosen.id,name="Pista "+i});var preview=Instructions.Preview(bulk,batch);int reported=0;var bulkEditor=new Editor();bulkEditor.Reset(bulk);Instructions.Apply(bulkEditor,batch,preview,null,(done,total)=>{if(done==total)reported=done;});Check(reported==115&&bulkEditor.Project.Active.tracks.First(t=>t.id==chosen.id).name=="Pista 114","Lote de 115 instrucciones progresa y se aplica");bulkEditor.Undo();Check(bulkEditor.Project.Active.tracks.First(t=>t.id==chosen.id).name==firstName,"Lote completo se deshace una sola vez");
  var titleProject=Store.New();var titleAsset=GeneratedMedia.CreateText("Frameworks\nRuntimes",2.5);titleProject.assets.Add(titleAsset);var titleTrack=titleProject.Active.tracks.First(t=>t.kind=="video");Media.Insert(titleProject,titleAsset.id,titleTrack.id,0);var titleClip=titleProject.Active.clips.First(c=>c.asset_id==titleAsset.id);var animated=TextCascade01062.Apply(titleProject,titleClip.id);Store.Validate(titleProject);var letters=NestedMedia0910.Read(animated).clips;Check(letters.Count==18&&letters.All(c=>c.animation.Count>=2)&&titleProject.Active.tracks.Count==Store.New().Active.tracks.Count,"Título de dos líneas genera letras animadas sin pistas residuales");
  Rendering.GeneratedCacheRoot=Path.Combine(Path.GetTempPath(),"prometheus-01062-cache-"+Guid.NewGuid().ToString("N"));
  try{using(var titleFrame=Rendering.FrameExact(titleProject,1.2,CancellationToken.None,320,180,true)){Check(titleFrame!=null&&titleFrame.Width==320,"El título animado se compone en el monitor");}}
  finally{if(Directory.Exists(Rendering.GeneratedCacheRoot))Directory.Delete(Rendering.GeneratedCacheRoot,true);}
 }
}
