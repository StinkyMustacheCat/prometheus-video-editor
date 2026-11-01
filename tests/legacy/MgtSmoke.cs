using System;
using System.IO;
using System.Threading;
using Prometheus;
class MgtSmoke {
 static int Main(string[] args){
  try{
   var testCache=Environment.GetEnvironmentVariable("PROMETHEUS_MGT_CACHE_ROOT");if(!String.IsNullOrEmpty(testCache))Rendering.GeneratedCacheRoot=Path.Combine(testCache,"generated");
   var asset=MgtMedia.Inspect(args[0],CancellationToken.None);
   Console.WriteLine("IMPORT "+asset.name+" "+asset.recommended_duration.Seconds+" "+asset.streams.Count);
   var pending=Store.New();pending.assets.Add(asset);Media.Insert(pending,asset.id,pending.Active.tracks.Find(t=>t.kind=="video").id,0);
   using(var placeholder=Rendering.Frame(pending,0.1,CancellationToken.None,320,180))Console.WriteLine("PENDING_FRAME "+placeholder.Width);
   MgtMedia.Resolve(asset,true,CancellationToken.None,p=>{if(p%20==0)Console.WriteLine("PREVIEW "+p);});
   Console.WriteLine("FILE "+Media.RequirePath(asset,true));
   MgtMedia.Resolve(asset,false,CancellationToken.None,null);
   Console.WriteLine("FINAL "+Media.RequirePath(asset,false));
   var project=Store.New();project.assets.Add(asset);Media.Insert(project,asset.id,project.Active.tracks.Find(t=>t.kind=="video").id,0);
   Store.Validate(project);
   using(var frame=Rendering.Frame(project,0.5,CancellationToken.None,320,180))Console.WriteLine("FRAME "+frame.Width+"x"+frame.Height);
   Rendering.Export(project,args[1],CancellationToken.None);
   Console.WriteLine("EXPORTED "+new FileInfo(args[1]).Length);
   string saved=Path.ChangeExtension(args[1],".pthproj");
   ProjectResourceTransfer.Materialize(project,saved);Store.Save(project,saved);
   var loaded=Store.Load(saved);Store.Validate(loaded);
   Console.WriteLine("REOPENED "+loaded.assets[0].source.uri+" "+Media.RequirePath(loaded.assets[0],false));
   if(loaded.assets[0].container_format=="mgt.json"){
    string earlier=Media.RequirePath(loaded.assets[0],true);
    MgtMedia.SetOverride(loaded.assets[0],"title","Prueba cambiada");
    MgtMedia.Resolve(loaded.assets[0],true,CancellationToken.None,null);
    if(earlier==Media.RequirePath(loaded.assets[0],true))throw new Exception("El parámetro MGT no cambió el render.");
    Console.WriteLine("OVERRIDE_RENDERED");
   }
   return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
 }
}
