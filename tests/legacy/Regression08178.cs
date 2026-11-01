using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Prometheus;

class Regression08178 {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static Clip Clip(Asset asset,StreamInfo stream,string track,double at,double duration){return new Clip{id=Store.Id(),asset_id=asset.id,stream_id=stream.id,track_id=track,timeline_start=Rational.Time(at),duration=Rational.Time(duration),source_in=Rational.Time(0),name="Prueba"};}
 static void Main(string[] args){
  Tools.Root=args[0];var asset=Media.Inspect(System.IO.Path.Combine(args[0],"demo","video.mp4"),CancellationToken.None);
  var video=asset.streams.First(s=>s.kind=="video");var audio=asset.streams.First(s=>s.kind=="audio");
  var editor=new Editor();editor.Commit(p=>{p.assets.Add(asset);var v=p.Active.tracks.First(t=>t.kind=="video");p.Active.clips.Add(Clip(asset,video,v.id,0,1));p.Active.clips.Add(Clip(asset,video,v.id,3,1));});
  string first=editor.Project.Active.clips[0].id,second=editor.Project.Active.clips[1].id;
  bool limited=false;editor.Commit(p=>limited=ClipSpeed08174.Change(p,first,25));
  Check(limited&&Math.Abs(editor.Project.Active.clips.First(c=>c.id==first).duration.Seconds-3)<0.0001,"velocidad conserva clips posteriores y limita extensión");
  Check(Math.Abs(editor.Project.Active.clips.First(c=>c.id==second).timeline_start.Seconds-3)<0.0001,"medio posterior no se desplaza");
  editor.Group(new[]{first,second});string v2=editor.Project.Active.tracks.Where(t=>t.kind=="video").OrderBy(t=>t.order).Last().id;
  editor.MoveSelection(new List<string>{first,second},first,0,v2);
  Check(editor.Project.Active.clips.All(c=>c.track_id==v2),"grupo se mueve a otra pista compatible");
  var roll=new Editor();roll.Commit(p=>{p.assets.Add(asset);var videos=p.Active.tracks.Where(t=>t.kind=="video").OrderBy(t=>t.order).ToList();var a=p.Active.tracks.First(t=>t.kind=="audio");
   var lv=Clip(asset,video,videos[0].id,0,1);var rv=Clip(asset,video,videos[0].id,1,1);var la=Clip(asset,audio,a.id,0,1);var ra=Clip(asset,audio,a.id,1,1);var lv2=Clip(asset,video,videos[1].id,0,1);var rv2=Clip(asset,video,videos[1].id,1,1);
   p.Active.clips.AddRange(new[]{lv,rv,la,ra,lv2,rv2});p.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=new List<string>{lv.id,la.id}});p.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=new List<string>{rv.id,ra.id}});p.Active.edit_groups.Add(new EditGroup{id=Store.Id(),number=1,clip_ids=new List<string>{lv.id,la.id,lv2.id}});p.Active.edit_groups.Add(new EditGroup{id=Store.Id(),number=2,clip_ids=new List<string>{rv.id,ra.id,rv2.id}});});
  var left=roll.Project.Active.clips.First(c=>c.track_id==roll.Project.Active.tracks.First(t=>t.kind=="video").id&&c.timeline_start.Seconds==0);var right=roll.Project.Active.clips.First(c=>c.track_id==left.track_id&&c.timeline_start.Seconds==1);
  roll.RollEditLinked(left.id,right.id,1.25,true);
  Check(roll.Project.Active.clips.Count(c=>Math.Abs(c.End-1.25)<0.0001)==3,"Roll Edit mueve la unión equivalente de todo el grupo");
  var web=new Asset{id=Store.Id(),name="Fuente web",source=new MediaSource{kind="web",uri="https://example.com/video"},presentation_origin=Rational.Time(0)};web.streams.Add(new StreamInfo{id=Store.Id(),ordinal=0,kind="video",codec="virtual",time_base=new Rational(1,60),duration=Rational.Frame(5),pts_origin=Rational.Time(0)});
  var history=new Editor();history.Commit(p=>p.assets.Add(web));for(int i=0;i<60;i++){int number=i;history.Commit(p=>p.name="Paso "+number);}
  Check(history.UpdateDerivedAsset(web.id,web.source.uri,p=>p.assets.First(a=>a.id==web.id).representations.Add(new MediaRepresentation{role="preview",locator=new Locator{path="C:\\cache\\preview.mp4"}})),"medio preparado se conecta sin rehacer el historial");
  history.Undo();history.Redo();Check(history.Project.assets.First(a=>a.id==web.id).representations.Count==1,"deshacer y rehacer conservan medio preparado sin duplicados");
 }
}
