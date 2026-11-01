using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Prometheus;

class MgtFollowupTests {
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("OK "+message);}
 static Asset Asset(double duration){
  var asset=new Asset{id=Store.Id(),name="Animación",source=new MediaSource{kind="mgt",uri="test.mgt.json"},container_format="mgt.json",presentation_origin=Rational.Time(0),recommended_duration=Rational.Time(duration)};
  asset.streams.Add(new StreamInfo{id=Store.Id(),ordinal=0,kind="video",codec="mgt-prores",pts_origin=Rational.Time(0),time_base=new Rational(1,60),duration=Rational.Time(duration),nominal_fps=new Rational(60,1),width=640,height=360});
  asset.extensions.Add(new Extension{@namespace=MgtMedia.Namespace,version="1",payload=new Dictionary<string,object>{{"parameters_json","[]"},{"overrides_json","{}"}}});
  return asset;
 }
 static int Main(string[] args){
  try{
   Check(MgtMedia.IsJsonFile("desencriptador.mgt (1).json"),"nombre duplicado reconocido");
   Check(MgtMedia.IsFile("animacion.mgt.json")&&MgtMedia.IsFile("proyecto.mgtproj"),"formatos habituales reconocidos");
   Check(!MgtMedia.IsFile("texto.json")&&!MgtMedia.IsFile("video.mgt (a).json"),"nombres ajenos rechazados");
   Check(MgtMedia.LooksLikeDocument("{\"version\":2,\"render\":{},\"name\":\"Demo\"}"),"MGT pegado detectado");
   Check(!MgtMedia.LooksLikeDocument("{\"format\":\"prometheus-instructions\",\"version\":1}"),"instrucciones no confundidas con MGT");
   var project=Store.New();var original=Asset(10);project.assets.Add(original);var video=project.Active.tracks.First(t=>t.kind=="video");Media.Insert(project,original.id,video.id,0);Store.Validate(project);
   var clip=project.Active.clips.Single();var shorter=Asset(6);MgtMedia.Relink(original,shorter);MgtMedia.ReconcileClipDurations(project,original);Store.Validate(project);
   Check(Math.Abs(ShortTail.MissingStart(project,clip).Value-6)<0.000001,"fuente acortada deja franjas desde 6 s");
   var longer=Asset(20);MgtMedia.Relink(original,longer);MgtMedia.ReconcileClipDurations(project,original);Store.Validate(project);
   Check(!ShortTail.Has(clip)&&Math.Abs(original.streams[0].duration.Seconds-20)<0.000001,"fuente ampliada deja extender el clip");
   if(args.Length>0){
    var inspected=Media.Inspect(args[0],System.Threading.CancellationToken.None);
    Check(MgtMedia.IsMgt(inspected),"nombre duplicado importado por motor MGT");
    var reservedProject=Store.New();var placeholder=GeneratedMedia.CreatePlaceholder("MGT pendiente","",5);
    reservedProject.assets.Add(placeholder);reservedProject.assets.Add(inspected);
    Media.Insert(reservedProject,placeholder.id,reservedProject.Active.tracks.First(t=>t.kind=="video").id,0);
    var reservedClip=reservedProject.Active.clips.Single();
    PlaceholderReplacement.Replace(reservedProject,reservedClip.id,inspected.id,0);Store.Validate(reservedProject);
    Check(ShortTail.MissingStart(reservedProject,reservedClip).HasValue,"MGT corto conectado a espacio reservado conserva franjas");
    var savePath=Path.Combine(Path.GetDirectoryName(args[0]),"mgt-followup-test.pthproj");
    ProjectResourceTransfer.Materialize(reservedProject,savePath);Store.Save(reservedProject,savePath);
    var loaded=Store.Load(savePath);Store.Validate(loaded);
    Check(loaded.assets.First(MgtMedia.IsMgt).source.uri.EndsWith(".mgt.json",StringComparison.OrdinalIgnoreCase),"copia gestionada conserva extensión MGT canónica");
   }
   return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
 }
}
