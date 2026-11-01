using System;
using System.Linq;
using Prometheus;

class InstructionShelfTests058 {
 static void Check(bool okay,string message){if(!okay)throw new Exception(message);}
 static Project Fixture(out ShelfItem shelf){
  var project=Store.New();var asset=GeneratedMedia.CreateSolid("Bandeja de prueba","334455",8);
  ShelfStore.Add(project,asset);shelf=ShelfStore.Read(project).Single();Store.Validate(project);return project;
 }
 static void Main(){
  ShelfItem item;var project=Fixture(out item);var document=Instructions.New(project);
  document.actions.Add(new InstructionAction{op="media.import_shelf",shelf_id=item.id,sequence_id=project.active_sequence_id});
  var parsed=Instructions.Parse(Instructions.Encode(document));var adapter=new InstructionMediaAdapter();var preview=Instructions.Preview(project,parsed,adapter);
  Check(preview.Cards.Single().status=="valid","Bandeja se revisa en verde");
  Check(preview.Result.assets.Count==project.assets.Count+1&&ShelfStore.Read(preview.Result).Count==0,"Revisión promueve medio y limpia Bandeja solo en simulación");
  Check(project.assets.Count==0&&ShelfStore.Read(project).Count==1,"Revisión no modifica el proyecto vivo");
  var editor=new Editor();editor.Reset(project);Instructions.Apply(editor,parsed,preview,adapter);
  Check(editor.Project.assets.Count==1&&ShelfStore.Read(editor.Project).Count==0,"Aplicación promueve en un paso");
  editor.Undo();Check(editor.Project.assets.Count==0&&ShelfStore.Read(editor.Project).Count==1,"Deshacer restaura el elemento de Bandeja");
  ShelfItem second;var timelineProject=Fixture(out second);var track=timelineProject.Active.tracks.First(t=>t.kind=="video");
  var timelineDoc=Instructions.New(timelineProject);timelineDoc.actions.Add(new InstructionAction{op="media.import_shelf",shelf_id=second.id,track_id=track.id,at_seconds=3,placement="empty"});
  var timelineAdapter=new InstructionMediaAdapter();var timelinePlan=Instructions.Preview(timelineProject,timelineDoc,timelineAdapter);
  Check(timelinePlan.Cards.Single().status=="valid"&&timelinePlan.Result.Active.clips.Count>0,"Importar desde Bandeja a pista crea clip: "+timelinePlan.Cards.Single().summary);
  var missing=Instructions.New(timelineProject);missing.actions.Add(new InstructionAction{op="media.import_shelf",shelf_id="no-existe"});
  Check(Instructions.Preview(timelineProject,missing,new InstructionMediaAdapter()).Cards.Single().status=="invalid","ID ausente queda rojo");
  var state=Instructions.CopyState(timelineProject,false);Check(state.Contains("shelf_items")&&state.Contains(second.id),"Estado copiado expone IDs de Bandeja");
  EditorActionCatalog.Validate(timelineProject,new EditorActionRequest{id="playhead.step_frames",frames=-5,project_id=timelineProject.id});
  bool rejected=false;try{EditorActionCatalog.Validate(timelineProject,new EditorActionRequest{id="playhead.seek",seconds=Double.NaN});}catch{rejected=true;}Check(rejected,"Navegación rechaza tiempo inválido");
  Console.WriteLine("InstructionShelfTests058 OK");
 }
}
