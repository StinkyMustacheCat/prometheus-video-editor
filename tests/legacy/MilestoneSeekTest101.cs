using System;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;
class MilestoneSeekTest101 {
 static int Main(string[] args){try{
  var p=Store.New();SequenceCanvas.Set(p.Active,640,360);
  var asset=Media.Inspect(Path.GetFullPath(args[0]),CancellationToken.None);p.assets.Add(asset);
  Media.Insert(p,asset.id,p.Active.tracks.First(t=>t.kind=="video").id,0);
  using(var image=Rendering.FrameExact(p,.6,CancellationToken.None,640,360,false))image.Save(Path.GetFullPath(args[1]));
  NestedMedia0910.Create(p,p.Active.clips.Select(c=>c.id),"Comprobación de búsqueda");
  using(var image=Rendering.FrameExact(p,.6,CancellationToken.None,640,360,false))image.Save(Path.GetFullPath(args[2]));
  if(!File.ReadAllBytes(args[1]).SequenceEqual(File.ReadAllBytes(args[2])))throw new Exception("El fotograma de un medio anidado cambió al buscar dentro de él");
  Console.WriteLine("PASS seek dentro del medio anidado");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
