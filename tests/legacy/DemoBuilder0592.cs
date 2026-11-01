using System;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

public static class DemoBuilder0592 {
 public static void Main(string[] args){
  string demo=args[0];var project=Store.New();project.name="Demostración";
  var video=Media.Inspect(Path.Combine(demo,"video.mp4"),CancellationToken.None);
  var image=Media.Inspect(Path.Combine(demo,"grafico.png"),CancellationToken.None);
  var voice=Media.Inspect(Path.Combine(demo,"voz.wav"),CancellationToken.None);
  project.assets.Add(video);project.assets.Add(image);project.assets.Add(voice);
  var videoTracks=project.Active.tracks.Where(t=>t.kind=="video").OrderBy(t=>t.order).ToList();
  var audioTracks=project.Active.tracks.Where(t=>t.kind=="audio").OrderBy(t=>t.order).ToList();
  Media.Insert(project,video.id,videoTracks[0].id,0);
  Media.Insert(project,image.id,videoTracks[1].id,0);
  Media.Insert(project,voice.id,audioTracks[1].id,0);
  Store.Save(project,Path.Combine(demo,"Demostracion.pthproj"));
  Console.WriteLine("Demo .pthproj creada");
 }
}
