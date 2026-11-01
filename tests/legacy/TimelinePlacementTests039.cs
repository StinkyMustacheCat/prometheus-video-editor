using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class TimelinePlacementTests039 {
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Main(string[] args){
  Tools.Root=Path.GetFullPath(args[0]);string demo=Path.Combine(Tools.Root,"demo");
  var project=Store.New();var video=Media.Inspect(Path.Combine(demo,"video.mp4"),CancellationToken.None);
  var image=Media.Inspect(Path.Combine(demo,"grafico.png"),CancellationToken.None);
  var audio=Media.Inspect(Path.Combine(demo,"voz.wav"),CancellationToken.None);
  project.assets.AddRange(new[]{video,image,audio});
  var v1=project.Active.tracks.First(t=>t.kind=="video"&&t.order==0);
  var v2=project.Active.tracks.First(t=>t.kind=="video"&&t.order==1);
  var a1=project.Active.tracks.First(t=>t.kind=="audio"&&t.order==0);
  var a2=project.Active.tracks.First(t=>t.kind=="audio"&&t.order==1);
  var first=Media.PlaceInFreeTrack(project,video.id,v1.id,0);
  Check(first.ClipIds.Count==2&&first.PrimaryTrackId==v1.id&&project.Active.clips.Any(c=>c.track_id==a1.id),"A/V entra en pistas compatibles y queda vinculado");
  var second=Media.PlaceInFreeTrack(project,video.id,v1.id,0);
  Check(second.PrimaryTrackId==v2.id&&project.Active.clips.Any(c=>c.id==second.ClipIds[1]&&c.track_id==a2.id),"si el hueco no alcanza, vídeo y audio buscan pistas independientes");
  var third=Media.PlaceInFreeTrack(project,image.id,v1.id,0);
  Check(project.Active.tracks.Find(t=>t.id==third.PrimaryTrackId).order==2,"se crea pista visual al agotarse las disponibles");
  var audioOnly=Media.PlaceInFreeTrack(project,audio.id,a1.id,0);
  Check(project.Active.tracks.Find(t=>t.id==audioOnly.PrimaryTrackId).order==2,"audio local busca o crea solo pista de audio");
  int before=project.Active.clips.Count;
  Check(before==6,"la colocación conserva los clips existentes sin sobrescribir");
  Store.Validate(project);Check(true,"el proyecto completo supera la validación de estructura");
  var separate=Store.New();separate.assets.Add(video);var v3=Store.AddTrack(separate.Active,"video");
  var pair=Media.PlaceInFreeTrack(separate,video.id,v3.id,0);
  Check(separate.Active.clips.Find(c=>c.id==pair.PrimaryClipId).track_id==v3.id&&separate.Active.clips.Find(c=>c.id==pair.ClipIds[1]).track_id==separate.Active.tracks.First(t=>t.kind=="audio").id,"V3 y A1 pueden formar pareja sin orden coincidente");
 }
}
