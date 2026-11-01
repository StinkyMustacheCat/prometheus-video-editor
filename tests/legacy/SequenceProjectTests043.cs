using System;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class SequenceProjectTests043 {
 static void Check(bool good,string label){if(!good)throw new Exception(label);Console.WriteLine("PASS "+label);}
 static void Main(){
  var editor=new Editor();var project=editor.Project;
  string first=project.Active.id;
  var solid=GeneratedMedia.CreateSolid("Fondo","FF9933",5);project.assets.Add(solid);
  Media.Insert(project,solid.id,project.Active.tracks.First(t=>t.kind=="video").id,0);
  Markers.Add(project.Active,2,"Corte");SequenceExportRange.SetIn(project.Active,1);SequenceExportRange.SetOut(project.Active,4);
  editor.CreateSequence("Entrevista");string second=editor.Project.Active.id;
  Check(editor.Project.sequences.Count==2&&editor.Project.Active.clips.Count==0&&editor.Project.assets.Count==1,"secuencia nueva vacía comparte biblioteca");
  editor.SwitchSequence(first);Check(editor.Project.Active.clips.Count==1,"cambio restaura el montaje original");
  editor.DuplicateSequence(first,"Montaje alternativo");string third=editor.Project.Active.id;
  Check(editor.Project.Active.clips.Count==1&&editor.Project.Active.clips[0].id!=editor.Project.sequences[0].clips[0].id,"duplicar copia clips con IDs nuevos");
  Check(Markers.Read(editor.Project.Active).Count==1&&SequenceExportRange.Read(editor.Project.Active).Complete,"duplicar conserva marcadores e I/O");
  editor.RenameSequence(third,"Versión corta");Check(editor.Project.Active.name=="Versión corta","renombrar secuencia");
  editor.SwitchSequence(second);editor.DeleteSequence(second);
  Check(editor.Project.sequences.Count==2&&editor.Project.Active!=null,"eliminar activa elige una secuencia restante");
  bool blocked=false;try{editor.RenameSequence(first,"Versión corta");}catch{blocked=true;}Check(blocked,"nombres repetidos se rechazan");
  var history=new Editor();string historyFirst=history.Project.Active.id;
  history.CreateSequence("Otra");string historySecond=history.Project.Active.id;
  history.SwitchSequence(historyFirst);history.Commit(p=>p.Active.name="Primera editada");
  history.SwitchSequence(historySecond);history.Commit(p=>p.Active.name="Segunda editada");
  history.Undo();Check(history.Project.Active.id==historySecond&&history.Project.Active.name=="Otra"&&history.Project.sequences[0].name=="Primera editada","deshacer conserva edición anterior de otra secuencia");
  history.Undo();Check(history.Project.Active.id==historyFirst&&history.Project.Active.name=="Secuencia 01","deshacer vuelve a la secuencia del cambio");
  history.Redo();history.Redo();Check(history.Project.sequences[0].name=="Primera editada"&&history.Project.sequences[1].name=="Segunda editada","rehacer recupera cambios de ambas secuencias");
  string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"prometheus-043-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   string projectPath=Path.Combine(root,"Prueba.pthproj");
   Sequences.WriteView(editor.Project.Active,3,1,90);
   Store.Save(editor.Project,projectPath);
   var loaded=Store.Load(projectPath);
   Check(loaded.sequences.Count==2&&loaded.Active.name=="Versión corta"&&Sequences.ReadView(loaded.Active).Zoom==90,"guardado conserva secuencias, activa y vista");
   Tools.Root=AppDomain.CurrentDomain.BaseDirectory;
   using(var frame=Rendering.Frame(loaded,1,CancellationToken.None,320,180))Check(frame.Width==320&&frame.Height==180,"vista de secuencia guardada no bloquea reproducción");
   ProjectStorage.Ensure(projectPath);
   Check(Directory.Exists(ProjectStorage.DataDirectory(projectPath))&&!Directory.Exists(ProjectStorage.WebDirectory(projectPath))&&!Directory.Exists(ProjectStorage.ProxyDirectory(projectPath)),"carpetas de medios solo cuando se necesitan");
   string image=Path.Combine(root,"medio.png");File.WriteAllBytes(image,new byte[]{1,2,3});
   var asset=loaded.assets[0];asset.source=new MediaSource{kind="local",uri=image};asset.representations.Add(new MediaRepresentation{role="original",locator=new Locator{path=image}});
   Store.Save(loaded,projectPath);
   var relative=Store.Decode(File.ReadAllText(projectPath));
   Check(relative.assets[0].source.relative&&relative.assets[0].representations[0].locator.relative,"medios internos se guardan con ruta relativa");
   var reopened=Store.Load(projectPath);
   Check(reopened.assets[0].source.uri==image&&reopened.assets[0].representations[0].locator.path==image,"al abrir se resuelven rutas relativas");
  }finally{Directory.Delete(root,true);}
 }
}
