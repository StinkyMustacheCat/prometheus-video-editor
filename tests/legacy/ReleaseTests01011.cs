using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class ReleaseTests01011 {
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static string ToneWithPause(string folder){
  string path=Path.Combine(folder,"speech-pause.wav");int rate=48000,samples=rate*3;
  using(var writer=new BinaryWriter(File.Create(path))){
   writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+samples*2);
   writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);
   writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(samples*2);
   for(int i=0;i<samples;i++){double t=i/(double)rate;writer.Write((short)(t>=1&&t<2?0:12000*Math.Sin(2*Math.PI*440*t)));}
  }return path;
 }
 static Project LinkedProject(string root,string wav,out string videoId,out string audioId){
  var p=Store.New();var image=Media.Inspect(Path.Combine(root,"demo","grafico.png"),CancellationToken.None);var audio=Media.Inspect(wav,CancellationToken.None);
  p.assets.Add(image);p.assets.Add(audio);var video=p.Active.tracks.First(t=>t.kind=="video"&&t.order==0);var sound=p.Active.tracks.First(t=>t.kind=="audio"&&t.order==0);
  Media.Insert(p,image.id,video.id,0);Media.Insert(p,audio.id,sound.id,0);
  var visual=p.Active.clips.First(c=>c.asset_id==image.id);visual.duration=Rational.Time(3);
  videoId=visual.id;audioId=p.Active.clips.First(c=>c.asset_id==audio.id).id;
  p.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=new List<string>{videoId,audioId}});Store.Validate(p);return p;
 }
 static void Main(string[] args){
  string root=Path.GetFullPath(args[0]),folder=Path.GetFullPath(args[1]);Directory.CreateDirectory(folder);Tools.Root=root;
  string wav=ToneWithPause(folder),videoId,audioId;var project=LinkedProject(root,wav,out videoId,out audioId);
  Markers.Add(project.Active,2.5,"Después");SequenceExportRange.SetIn(project.Active,0.5);SequenceExportRange.SetOut(project.Active,2.5);
  var found=SilenceAnalysis01010.Detect(project,audioId,-35,0.3,0.05,CancellationToken.None);
  Check(found.Count==1&&found[0][0]>1&&found[0][0]<1.2&&found[0][1]>1.8&&found[0][1]<2,"análisis detecta la pausa y conserva márgenes");
  var editor=new Editor();editor.Reset(project);editor.RemoveSilences(audioId,found,false);
  Check(editor.Project.Active.clips.Count==4&&editor.Project.Active.link_groups.Count==2,"quitar dejando hueco conserva las parejas vinculadas");
  Check(editor.Project.Active.clips.Any(c=>c.track_id==project.Active.tracks.First(t=>t.kind=="audio"&&t.order==0).id&&c.timeline_start.Seconds>1.8),"dejar hueco mantiene el tiempo posterior");
  Check(Math.Abs(Markers.Read(editor.Project.Active)[0].Seconds-2.5)<0.001,"dejar hueco no mueve marcadores");
  editor.Undo();Check(editor.Project.Active.clips.Count==2&&editor.Project.Active.link_groups.Count==1,"un deshacer revierte todos los silencios");
  editor.Redo();Check(editor.Project.Active.clips.Count==4,"rehacer restaura el lote");
  editor.Undo();editor.RemoveSilences(audioId,found,true);
  double removed=found[0][1]-found[0][0];
  Check(editor.Project.Active.clips.Count==4&&Math.Abs(editor.Project.Active.Length-(3-removed))<0.02,"rizo acorta la secuencia y conserva vínculos");
  Check(Math.Abs(Markers.Read(editor.Project.Active)[0].Seconds-(2.5-removed))<0.02&&Math.Abs(SequenceExportRange.Read(editor.Project.Active).OutFrame.Value/60.0-(2.5-removed))<0.02,"rizo desplaza marcadores y puntos de exportación");
  string projectFile=Path.Combine(folder,"silence-ripple.prometheus.json");Store.Save(editor.Project,projectFile);
  var opened=Store.Load(projectFile);Store.Validate(opened);Check(opened.Active.clips.Count==4&&opened.Active.link_groups.Count==2,"guardar y reabrir conserva cortes y vínculos");
  string export=Path.Combine(folder,"silence-ripple.mp4");Tools.Run(Tools.FFmpeg,Rendering.Build(opened,true,true,320,180,30,export,0,false),CancellationToken.None,true);
  Check(File.Exists(export)&&new FileInfo(export).Length>1000,"el montaje recortado exporta vídeo y audio");
  editor.Undo();editor.Project.Active.tracks.First(t=>t.kind=="video"&&t.order==0).locked=true;
  bool refused=false;try{editor.RemoveSilences(audioId,found,true);}catch(Exception){refused=true;}
  Check(refused&&editor.Project.Active.clips.Count==2,"rizo protege las pistas bloqueadas");
  string otherVideo,otherAudio;var moving=LinkedProject(root,wav,out otherVideo,out otherAudio);
  moving.Active.tracks.RemoveAll(t=>t.order>0);Store.Validate(moving);
  var moved=new Editor();moved.Reset(moving);moved.MoveSelectionAboveVideos(new List<string>{otherVideo},otherVideo,0,true);
  Check(moved.Project.Active.tracks.Count(t=>t.kind=="video")==2&&moved.Project.Active.clips.Find(c=>c.id==otherAudio).track_id==moving.Active.clips.Find(c=>c.id==otherAudio).track_id&&moved.Related(otherVideo).Contains(otherAudio),"vídeo crea pista superior y audio permanece en A1 vinculado");
  moved.Undo();Check(moved.Project.Active.tracks.Count(t=>t.kind=="video")==1&&moved.Related(otherVideo).Contains(otherAudio),"deshacer retira la nueva pista y mantiene el vínculo");
 }
}
