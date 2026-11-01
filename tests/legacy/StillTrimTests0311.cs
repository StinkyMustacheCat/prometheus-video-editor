using System;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class StillTrimTests0311 {
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Main(string[] args){
  Tools.Root=Path.GetFullPath(args[0]);
  var project=Store.New();var still=Media.Inspect(Path.Combine(Tools.Root,"demo","grafico.png"),CancellationToken.None);
  project.assets.Add(still);string track=project.Active.tracks.First(t=>t.kind=="video").id;
  Media.Insert(project,still.id,track,5);Media.Insert(project,still.id,track,12);
  var first=project.Active.clips.OrderBy(c=>c.timeline_start.Seconds).First();
  var editor=new Editor();editor.Reset(project);
  editor.Trim(first.id,true,0);
  Check(Math.Abs(editor.Project.Active.clips.Find(c=>c.id==first.id).timeline_start.Seconds)<0.000001,"imagen se extiende hacia la izquierda hasta el inicio");
  Check(Math.Abs(editor.Project.Active.clips.Find(c=>c.id==first.id).source_in.Seconds)<0.000001,"extender imagen no consume fuente temporal");
  editor.Trim(first.id,false,30);
  Check(Math.Abs(editor.Project.Active.clips.Find(c=>c.id==first.id).End-12)<0.000001,"el siguiente clip limita la extensión derecha sin sobrescritura");
  Check(editor.Project.Active.clips.Count==2,"ningún vecino se elimina durante la extensión");
  editor.Delete(editor.Project.Active.clips.First(c=>c.id!=first.id).id);
  editor.Trim(first.id,false,60);
  Check(Math.Abs(editor.Project.Active.clips.Find(c=>c.id==first.id).End-60)<0.000001,"imagen puede prolongarse mucho más allá de su duración recomendada");
  editor.Project.Active.tracks.Find(t=>t.id==track).locked=true;bool blocked=false;
  try{editor.Trim(first.id,false,70);}catch{blocked=true;}
  Check(blocked&&Math.Abs(editor.Project.Active.clips.Find(c=>c.id==first.id).End-60)<0.000001,"una pista bloqueada impide modificar la imagen");
  editor.Project.Active.tracks.Find(t=>t.id==track).locked=false;Store.Validate(editor.Project);
  var video=Media.Inspect(Path.Combine(Tools.Root,"demo","video.mp4"),CancellationToken.None);
  var separate=Store.New();separate.assets.Add(video);string v=separate.Active.tracks.First(t=>t.kind=="video").id;Media.Insert(separate,video.id,v,0);
  var videoClip=separate.Active.clips.First(c=>c.track_id==v);var movieEditor=new Editor();movieEditor.Reset(separate);
  movieEditor.Trim(videoClip.id,false,10);
  Check(Math.Abs(movieEditor.Project.Active.clips.Find(c=>c.id==videoClip.id).End-2)<0.000001,"vídeo conserva el límite de duración de su archivo");
 }
}
