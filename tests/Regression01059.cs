using System;
using System.Linq;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using Prometheus;

class Regression01059 {
 static void Check(bool yes,string name){if(!yes)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Main(){
  string runtime=Environment.GetEnvironmentVariable("PROMETHEUS_TEST_RUNTIME");
  if(String.IsNullOrWhiteSpace(runtime)||!File.Exists(Path.Combine(runtime,"runtime","ffmpeg.exe"))||!File.Exists(Path.Combine(runtime,"runtime","ffprobe.exe")))throw new Exception("PROMETHEUS_TEST_RUNTIME debe apuntar a un paquete con runtime\\ffmpeg.exe y runtime\\ffprobe.exe.");
  Tools.Root=runtime;
  var project=Store.New();var audio=project.Active.tracks.First(t=>t.kind=="audio");
  var asset=GeneratedMedia.CreateAudioPlaceholder("Audio reservado","",5);project.assets.Add(asset);Media.Insert(project,asset.id,audio.id,0);
  var clip=project.Active.clips.First(c=>c.asset_id==asset.id);var editor=new Editor();editor.Reset(project);
  int before=project.Active.tracks.Count(t=>t.kind=="audio");editor.MoveSelectionBelowAudios(new List<string>{clip.id},clip.id,0,true);
  Check(editor.Project.Active.tracks.Count(t=>t.kind=="audio")==before+1&&editor.Project.Active.clips.First(c=>c.id==clip.id).track_id!=audio.id,"Arrastrar audio crea pista inferior");
  editor.Undo();Check(editor.Project.Active.tracks.Count(t=>t.kind=="audio")==before,"La pista creada se deshace con el movimiento");
  var render=Store.New();var lanes=render.Active.tracks.Where(t=>t.kind=="video").OrderBy(t=>t.order).ToList();
  var blue=GeneratedMedia.CreateSolid("Azul","0000FF",4);var red=GeneratedMedia.CreateSolid("Rojo","FF0000",2);render.assets.Add(blue);render.assets.Add(red);
  Media.Insert(render,blue.id,lanes[0].id,0);Media.Insert(render,red.id,lanes[1].id,0);BlendModes01034.Set(render.Active.clips.First(c=>c.asset_id==red.id),"multiply");Store.Validate(render);
  using(var frame=Rendering.FrameExact(render,2.5,CancellationToken.None,320,180,true)){Color pixel=frame.GetPixel(160,90);Check(pixel.B>200&&pixel.R<60&&pixel.G<60,"La mezcla termina al salir del clip superior");}
 }
}
