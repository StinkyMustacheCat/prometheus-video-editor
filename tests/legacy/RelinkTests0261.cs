using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Prometheus;

class RelinkTests0261 {
 static int count;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);count++;}
 static T Field<T>(object target,string name){return (T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
 [STAThread]static void Main(string[] args){
  Application.EnableVisualStyles();Tools.Root=Path.GetFullPath(args[0]);string work=Path.Combine(Path.GetFullPath(args[1]),"relink-"+Guid.NewGuid().ToString("N"));
  string found=Path.Combine(work,"found"),nested=Path.Combine(found,"nested"),duplicate=Path.Combine(found,"duplicate"),missing=Path.Combine(work,"missing");
  Directory.CreateDirectory(nested);Directory.CreateDirectory(duplicate);
  string video=Path.Combine(found,"video.mp4"),audio=Path.Combine(nested,"voz.wav");
  File.Copy(Path.Combine(Tools.Root,"demo","video.mp4"),video);File.Copy(Path.Combine(Tools.Root,"demo","voz.wav"),audio);
  var a=Media.Inspect(video,CancellationToken.None);var b=Media.Inspect(audio,CancellationToken.None);
  foreach(var asset in new[]{a,b}){string old=Path.Combine(missing,asset.name);asset.source.uri=old;asset.representations[0].locator.path=old;}
  var project=Store.New();project.assets.Add(a);project.assets.Add(b);
  Media.Insert(project,a.id,project.Active.tracks.First(t=>t.kind=="video").id,0);
  Media.Insert(project,b.id,project.Active.tracks.Last(t=>t.kind=="audio").id,0);
  string projectFile=Path.Combine(work,"Offline.prometheus.json");Store.Save(project,projectFile);
  Check(project.assets.All(x=>Media.SelectRepresentation(x,false)==null),"el proyecto parte con dos medios faltantes");
  File.Copy(video,Path.Combine(duplicate,"video.mp4"));using(var append=File.Open(Path.Combine(duplicate,"video.mp4"),FileMode.Append))append.WriteByte(1);
  var plan=Media.FindRelinks(project,found,CancellationToken.None);
  Check(plan.matches.Count==2&&plan.missing.Count==0&&plan.ambiguous.Count==0,"búsqueda recursiva identifica dos medios por nombre y tamaño");
  Check(plan.scanned_files>=3,"búsqueda recorre subcarpetas sin inspeccionar todos los archivos como medios");
  var editor=new Editor();editor.Reset(project);string videoId=a.id,audioId=b.id;
  editor.Commit(p=>{foreach(var match in plan.matches)Media.RelinkLocal(p.assets.Find(x=>x.id==match.Key),match.Value);});
  Check(editor.Project.assets.Any(x=>x.id==videoId&&x.source.uri==video)&&editor.Project.assets.Any(x=>x.id==audioId&&x.source.uri==audio),"reconecta varios medios en una sola operación");
  Check(editor.Project.Active.clips.All(x=>x.asset_id==videoId||x.asset_id==audioId),"la timeline conserva IDs y vínculos lógicos");
  editor.Undo();Check(editor.Project.assets.All(x=>Media.SelectRepresentation(x,false)==null),"Deshacer restaura las ubicaciones anteriores");
  editor.Redo();Media.VerifySources(editor.Project,CancellationToken.None,false);
  Store.Save(editor.Project,projectFile);var reopened=Store.Load(projectFile);
  Check(reopened.assets.All(x=>Media.SelectRepresentation(x,false)!=null),"guardar y reabrir conserva la reconexión");
  string export=Path.Combine(work,"reconnected.mp4");Rendering.Export(reopened,export,CancellationToken.None);
  Check(File.Exists(export)&&new FileInfo(export).Length>0,"proyecto reconectado exporta el montaje final");
  string validCopy=Path.Combine(duplicate,"voz.wav");File.Copy(audio,validCopy);
  var ambiguousProject=Store.New();var other=Media.Inspect(audio,CancellationToken.None);other.source.uri=Path.Combine(missing,"voz.wav");other.representations[0].locator.path=other.source.uri;ambiguousProject.assets.Add(other);
  var ambiguous=Media.FindRelinks(ambiguousProject,found,CancellationToken.None);
  Check(ambiguous.matches.Count==0&&ambiguous.ambiguous.Count==1,"copias igualmente compatibles requieren selección manual");
  using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();try{Media.FindRelinks(ambiguousProject,found,cancelled.Token);throw new Exception("No se canceló");}catch(OperationCanceledException){Check(true,"búsqueda admite cancelación");}}
  using(var form=new MainForm()){
   var workspace=Field<PanelWorkspace>(form,"workspace");workspace.PersistenceEnabled=false;
   typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Public|BindingFlags.Static).SetValue(null,true);
   form.Show();Application.DoEvents();form.LoadFixture(projectFile);
   var media=Field<MediaPanelView>(form,"mediaView");
   Check(media.Controls.OfType<Panel>().SelectMany(x=>x.Controls.OfType<Button>()).Any(x=>x.Text.Contains("Reconectar carpeta")),"Medios ofrece reconexión por carpeta");
   var method=typeof(MainForm).GetMethod("RelinkFromFolder",BindingFlags.Instance|BindingFlags.NonPublic);
   var offlineProject=Store.Load(Path.Combine(work,"Offline.prometheus.json"));
   foreach(var asset in offlineProject.assets){asset.source.uri=Path.Combine(missing,asset.name);asset.representations[0].locator.path=asset.source.uri;}
   var localEditor=Field<Editor>(form,"editor");localEditor.Reset(offlineProject);
   string unique=Path.Combine(work,"unique");Directory.CreateDirectory(unique);File.Copy(video,Path.Combine(unique,"video.mp4"));File.Copy(audio,Path.Combine(unique,"voz.wav"));
   method.Invoke(form,new object[]{unique,true});var until=DateTime.UtcNow.AddSeconds(15);
   while(Field<bool>(form,"busy")&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(20);}Application.DoEvents();
   Check(!Field<bool>(form,"busy")&&localEditor.Project.assets.All(x=>Media.SelectRepresentation(x,false)!=null),"la aplicación reconecta por carpeta sin cambiar de proyecto");
  }
  Console.WriteLine("PASS TOTAL "+count);
 }
}
