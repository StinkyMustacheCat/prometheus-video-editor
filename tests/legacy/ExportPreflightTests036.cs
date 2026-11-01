using System;
using System.IO;
using System.Linq;
using Prometheus;

class ExportPreflightTests036 {
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static Asset Source(string kind,string uri,double seconds){var asset=new Asset{id=Store.Id(),name=kind+" test",source=new MediaSource{kind=kind,uri=uri},presentation_origin=Rational.Time(0),recommended_duration=Rational.Frame(seconds)};asset.streams.Add(new StreamInfo{id=Store.Id(),ordinal=0,kind="video",codec="h264",pts_origin=Rational.Time(0),time_base=new Rational(1,60),duration=Rational.Frame(seconds),width=1280,height=720});return asset;}
 static void Main(){
  string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preflight-test-036.bin");File.WriteAllBytes(file,new byte[]{1,2,3});
  try{
   var p=Store.New();string track=p.Active.tracks.First(t=>t.kind=="video").id;
   var local=Source("local",file,10);local.representations.Add(new MediaRepresentation{role="original",locator=new Locator{path=file},size_bytes=3});p.assets.Add(local);Media.Insert(p,local.id,track,0);
   var ready=ExportPreflight.Inspect(p,true,0);Check(ready.CanExport&&ready.Errors==0,"medio local disponible pasa preflight");
   var pending=ExportPreflight.Inspect(p,true,1);Check(!pending.CanExport&&pending.checks.Any(c=>c.code=="pending"),"importación pendiente impide exportar");
   File.WriteAllBytes(file,new byte[]{1,2,3,4});var changed=ExportPreflight.Inspect(p,true,0);Check(!changed.CanExport&&changed.checks.Any(c=>c.code=="media-unavailable"),"archivo modificado se detecta antes de renderizar");
   var web=Source("web","https://www.youtube.com/watch?v=test",10);p.assets.Add(web);Media.Insert(p,web.id,track,11);var remote=ExportPreflight.Inspect(p,true,0);Check(remote.checks.Any(c=>c.code=="web-pending"),"remoto pendiente se avisa como resolución automática");
   var placeholder=GeneratedMedia.CreatePlaceholder("Pendiente","",3);p.assets.Add(placeholder);Media.Insert(p,placeholder.id,track,22);var blocked=ExportPreflight.Inspect(p,true,0);Check(blocked.checks.Any(c=>c.code=="placeholder"),"placeholder bloquea video antes de exportar");
   var audioOnly=ExportPreflight.Inspect(p,false,0);Check(!audioOnly.checks.Any(c=>c.code=="placeholder"),"placeholder visual no bloquea exportación WAV");
  }finally{if(File.Exists(file))File.Delete(file);}
 }
}
