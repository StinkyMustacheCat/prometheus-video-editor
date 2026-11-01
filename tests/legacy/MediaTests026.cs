using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using Prometheus;

class MediaTests026 {
 static int count;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);count++;}
 static void Reject(Action action,string expected){try{action();throw new Exception("Se aceptó una operación inválida");}catch(Exception ex){Check(ex.Message.Contains(expected),expected);}}
 static void Main(string[] args){
  Tools.Root=Path.GetFullPath(args[0]);string work=Path.GetFullPath(args[1]);Directory.CreateDirectory(work);
  string file=Path.Combine(work,"local-video.mp4");File.Copy(Path.Combine(Tools.Root,"demo","video.mp4"),file,true);
  var local=Media.Inspect(file,CancellationToken.None);
  Check(local.source.kind=="local"&&local.source.uri==file&&local.source.imported_at!=null,"origen local explícito y fecha de importación");
  Check(local.representations.Count==1&&local.representations[0].role=="original"&&local.representations[0].size_bytes==new FileInfo(file).Length,"representación original con versión ligera");
  Check(local.representations[0].sha256==null&&local.sha256==null,"importación no calcula hash completo obligatorio");
  Check(local.streams.Any(s=>s.kind=="video"&&s.width>0&&s.height>0&&s.nominal_fps!=null&&s.pixel_format!=null),"stream conserva datos técnicos de video");
  Check(local.representations[0].stream_ordinals.Count==local.streams.Count,"representación asigna ordinal físico a cada stream lógico");
  var p=Store.New();p.assets.Add(local);Media.Insert(p,local.id,p.Active.tracks.First(t=>t.kind=="video").id,0);
  string project=Path.Combine(work,"schema026.prometheus.json");Store.Save(p,project);var loaded=Store.Load(project);
  Check(loaded.schema_version==p.schema_version&&loaded.assets[0].id==local.id&&loaded.Active.clips.All(c=>c.asset_id==local.id),"guardar y abrir mantiene la identidad lógica de los clips");
  string json=File.ReadAllText(project);Check(json.Contains("representations")&&json.Contains("source")&&!json.Contains("\"locator\":null"),"proyecto serializa origen y representaciones");
  Media.VerifySources(loaded,CancellationToken.None,false);
  DateTime originalTime=File.GetLastWriteTimeUtc(file);File.SetLastWriteTimeUtc(file,originalTime.AddSeconds(3));
  Reject(()=>Media.Verify(local,CancellationToken.None),"cambió");File.SetLastWriteTimeUtc(file,originalTime);
  string moved=Path.Combine(work,"renamed-video.mp4");File.Copy(file,moved,true);var candidate=Media.Inspect(moved,CancellationToken.None);
  string previousId=loaded.assets[0].id;Media.RelinkLocal(loaded.assets[0],candidate);Store.Validate(loaded);
  Check(loaded.assets[0].id==previousId&&loaded.assets[0].source.uri==moved&&loaded.Active.clips.All(c=>c.asset_id==previousId),"reconectar cambia ubicación sin cambiar Asset ni Clip");
  var virtualWeb=new Asset{id=Store.Id(),name="Video web pendiente",source=new MediaSource{kind="web",uri="https://example.test/video",page_url="https://example.test/page",resolver_type="web-video"},presentation_origin=Rational.Time(0),streams=new List<StreamInfo>{new StreamInfo{id=Store.Id(),ordinal=0,kind="video",codec="h264",pts_origin=Rational.Time(0),time_base=new Rational(1,60),duration=Rational.Time(8),nominal_fps=new Rational(60,1)}}};
  var webProject=Store.New();webProject.assets.Add(virtualWeb);Media.Insert(webProject,virtualWeb.id,webProject.Active.tracks.First(t=>t.kind=="video").id,0);Store.Validate(webProject);
  Store.Save(webProject,Path.Combine(work,"virtual-web.prometheus.json"));
  Reject(()=>Media.RequirePath(virtualWeb,false),"sin representación local");
  virtualWeb.representations.Add(new MediaRepresentation{role="preview",locator=new Locator{path=file}});
  Check(Media.SelectRepresentation(virtualWeb,true)!=null&&Media.SelectRepresentation(virtualWeb,false)==null,"vista previa puede existir sin archivo final");
  using(var frame=Rendering.Frame(webProject,1,CancellationToken.None))Check(frame.Width==640&&frame.Height==360,"medio virtual reproduce desde una representación de preview");
  Reject(()=>Rendering.Build(webProject,true,false,640,360,60,"render.mp4",0),"sin representación local");
  var mgt=new Asset{id=Store.Id(),name="Título MGT",source=new MediaSource{kind="mgt",uri="title.mgt.json",relative=true,resolver_type="mgt-core"},presentation_origin=Rational.Time(0),recommended_duration=Rational.Time(5),streams=new List<StreamInfo>{new StreamInfo{id=Store.Id(),ordinal=0,kind="video",codec="mgt",pts_origin=Rational.Time(0),time_base=new Rational(1,60),duration=Rational.Time(5),nominal_fps=new Rational(60,1)}}};
  var mgtProject=Store.New();mgtProject.assets.Add(mgt);Media.Insert(mgtProject,mgt.id,mgtProject.Active.tracks.First(t=>t.kind=="video").id,0);Store.Validate(mgtProject);
  Check(mgtProject.Active.clips.Single().asset_id==mgt.id&&mgt.representations.Count==0,"MGT cabe como medio lógico sin render disponible");
  string mgtFile=Path.Combine(work,"virtual-mgt.prometheus.json");Store.Save(mgtProject,mgtFile);
  Check(Store.Load(mgtFile).assets[0].source.uri==Path.Combine(work,"title.mgt.json"),"origen MGT relativo se resuelve junto al proyecto");
  Console.WriteLine("PASS TOTAL "+count);
 }
}
