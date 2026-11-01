using System;
using System.Collections.Generic;
using System.Linq;
using Prometheus;

class InstructionTests08181 {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static Clip Add(Project p,string track,double at,double duration,string name){var asset=GeneratedMedia.CreateSolid(name,"204060",duration);p.assets.Add(asset);var clip=new Clip{id=Store.Id(),name=name,asset_id=asset.id,stream_id=asset.streams[0].id,track_id=track,timeline_start=Rational.Frame(at),duration=Rational.Frame(duration),source_in=Rational.Time(0)};p.Active.clips.Add(clip);return clip;}
 static void Main(){
  var project=Store.New();string firstTrack=project.Active.tracks.First(t=>t.kind=="video").id;string titleTrack=Store.AddTrack(project.Active,"video").id;
  var before=Add(project,firstTrack,0,5,"Anterior");var inside=Add(project,firstTrack,5,5,"Dentro");SequenceExportRange.SetIn(project.Active,5);SequenceExportRange.SetOut(project.Active,10);Store.Validate(project);
  string copied=Instructions.CopyState(project,"io",null);
  Check(copied.Contains("\"scope\":\"io\"")&&copied.Contains(inside.id)&&!copied.Contains(before.id),"Contexto I/O filtra clips sin desplazar tiempos");
  Check(copied.Contains("context_in_frame")&&copied.Contains("clip.set_transform")&&copied.Contains("subtitle.create"),"Contexto expone marcas y catálogo completo");
  var doc=Instructions.New(project);doc.context_sequence_id=project.Active.id;doc.context_in_frame=300;doc.context_out_frame=600;
  doc.actions.Add(new InstructionAction{op="clip.set_transform",clip_id=before.id,parameter_id="scale",number=1.5});
  doc.actions.Add(new InstructionAction{op="clip.set_transform",clip_id=inside.id,parameter_id="scale",number=1.5});
  doc.actions.Add(new InstructionAction{op="marker.create",at_seconds=6,name="Punto",result="mark"});
  doc.actions.Add(new InstructionAction{op="marker.rename",marker_id="$mark",name="Revisado"});
  doc.actions.Add(new InstructionAction{op="text.create",track_id=titleTrack,at_seconds=6,duration_seconds=2,text="Hola",result="title"});
  doc.actions.Add(new InstructionAction{op="text.set_content",clip_id="$title",text="Mundo"});
  doc.actions.Add(new InstructionAction{op="subtitle.create",at_seconds=8,duration_seconds=1,text="Frase",result="cue"});
  var parsed=Instructions.Parse(Instructions.Encode(doc));var plan=Instructions.Preview(project,parsed);
  foreach(var card in plan.Cards)Console.WriteLine(card.status+": "+card.summary+" "+String.Join(" | ",card.details));
  Check(plan.Cards[0].status=="invalid"&&plan.Cards.Skip(1).All(c=>c.status=="valid"),"Revisión protege lo anterior al punto In y acepta cambios internos");
  Check(Math.Abs(ClipFraming.Read(plan.Result.Active.clips.Find(c=>c.id==inside.id)).scale-1.5)<0.001,"Transformación aplicada dentro del tramo");
  Check(Markers.Read(plan.Result.Active).Any(m=>m.Name=="Revisado"),"Marcador creado y referenciado");
  Check(plan.Result.assets.Any(a=>GeneratedMedia.IsText(a)&&GeneratedMedia.Text(a)=="Mundo"),"Texto creado y editado");
  var editor=new Editor();editor.Reset(project);Instructions.Apply(editor,parsed,plan);Check(editor.Project.Active.clips.Count==project.Active.clips.Count+2,"Lote aplicado con texto y subtítulo");editor.Undo();Check(editor.Project.Active.clips.Count==project.Active.clips.Count,"Un deshacer restaura el lote");
  var overrideDoc=Instructions.New(project);overrideDoc.context_sequence_id=project.Active.id;overrideDoc.context_in_frame=300;overrideDoc.context_out_frame=600;overrideDoc.allow_outside_context=true;overrideDoc.actions.Add(new InstructionAction{op="clip.set_transform",clip_id=before.id,parameter_id="scale",number=1.5});
  Check(Instructions.Preview(project,overrideDoc).Cards[0].status=="valid","Excepción explícita permite cambios fuera del tramo");
  var older=Instructions.New(project);older.actions.Add(new InstructionAction{op="marker.create",at_seconds=4});Check(Instructions.Parse(Instructions.Encode(older)).version==1,"Documentos v1 siguen siendo compatibles");
  Console.WriteLine("InstructionTests08181 OK");
 }
}
