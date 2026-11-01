using System;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using Prometheus;

class PreV1Regression01058 {
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Main(){
  string runtime=Environment.GetEnvironmentVariable("PROMETHEUS_TEST_RUNTIME");
  if(String.IsNullOrWhiteSpace(runtime)||!File.Exists(Path.Combine(runtime,"runtime","ffmpeg.exe"))||!File.Exists(Path.Combine(runtime,"runtime","ffprobe.exe")))throw new Exception("PROMETHEUS_TEST_RUNTIME debe apuntar a un paquete con runtime\\ffmpeg.exe y runtime\\ffprobe.exe.");
  Tools.Root=runtime;
  var p=Store.New();var video=p.Active.tracks.First(t=>t.kind=="video");
  var first=GeneratedMedia.CreateSolid("Primero","FF0000",5);p.assets.Add(first);Media.Insert(p,first.id,video.id,0);
  var second=GeneratedMedia.CreateSolid("Segundo","0000FF",5);p.assets.Add(second);Media.Insert(p,second.id,video.id,10);
  var left=p.Active.clips.First(c=>c.asset_id==first.id);var right=p.Active.clips.First(c=>c.asset_id==second.id);
  p.Active.edit_groups.Add(new EditGroup{id=Store.Id(),number=1,clip_ids=new List<string>{left.id,right.id}});Store.Validate(p);
  var window=Rendering.PreviewWindow(p,0,2);Store.Validate(window);Check(window.Active.edit_groups.Count==0,"La ventana de monitor elimina grupos incompletos");
  var export=SequenceExportRange.Window(p,0,120);Store.Validate(export);Check(export.Active.edit_groups.Count==0,"La ventana de exportación elimina grupos incompletos");
  left.effects.Add(EffectContracts.New(EffectContracts.ZoomInDynamicId));
  string originalEffect=left.effects[0].id;
  var copied=Store.Copy(p);string newSequence=Sequences.Duplicate(copied,p.Active.id,"Copia");Store.Validate(copied);
  var duplicated=copied.Active.clips.First(c=>c.asset_id==first.id);
  Check(duplicated.effects[0].id!=originalEffect&&copied.Active.edit_groups.Count==1,"La secuencia duplica efectos y grupos con identidad nueva");
  var pasted=Store.Copy(p);var newIds=ClipboardCore1020.Paste(pasted,new[]{left},new[]{video},p.id,p.Active.id,null,null,20);Store.Validate(pasted);
  Check(pasted.Active.clips.First(c=>c.id==newIds[0]).effects[0].id!=originalEffect,"Pegar clip duplica identidad del efecto");
  var reset=Store.Copy(p);var target=reset.Active.clips.First(c=>c.id==left.id);target.mask=ClipMasks.New("rectangle");var temporal=new ManualMaskTrack();temporal.points.Add(new ManualMaskKeyframe{source_seconds=0,mask=ClipMasks.New("rectangle")});TemporalMasks0823.Put(target,temporal);ClipAttributes.Reset(reset,new[]{target.id});Store.Validate(reset);
  Check(target.mask==null&&TemporalMasks0823.Get(target)==null,"Restablecer atributos quita la máscara temporal");
  var nested=Store.New();var nestedTrack=nested.Active.tracks.First(t=>t.kind=="video");
  var childA=GeneratedMedia.CreateSolid("A","00FF00",5);var childB=GeneratedMedia.CreateSolid("B","0000FF",5);
  nested.assets.Add(childA);nested.assets.Add(childB);Media.Insert(nested,childA.id,nestedTrack.id,0);Media.Insert(nested,childB.id,nestedTrack.id,5);
  nested.Active.clips.First(c=>c.asset_id==childA.id).effects.Add(EffectContracts.New(EffectContracts.ZoomInDynamicId));
  NestedMedia0910.Create(nested,nested.Active.clips.Select(c=>c.id).ToList(),"Anidado");Store.Validate(nested);
  var nestedWindow=Rendering.PreviewWindow(nested,1,1.0/60);var flattened=NestedMedia0910.Flatten(nestedWindow);
  var flatChild=flattened.Active.clips.First(c=>c.asset_id==childA.id);var motion=flatChild.extensions.FirstOrDefault(e=>e.@namespace=="prometheus.motion.preview-window");
  Check(motion!=null&&Convert.ToDouble(motion.payload["start"])<0&&Convert.ToDouble(motion.payload["duration"])>4,"El zoom del medio anidado conserva su tiempo original");
  var editor=new Editor();editor.Reset(Store.Copy(p));editor.Commit(project=>project.Active.clips.First(c=>c.id==left.id).name="Editado");
  var prior=editor.Project;var relocated=Store.Copy(prior);relocated.assets.First(a=>a.id==first.id).source.uri="generated:ubicacion-nueva";
  editor.SyncMaterializedPaths(prior,relocated);editor.Undo();
  Check(editor.Project.assets.First(a=>a.id==first.id).source.uri=="generated:ubicacion-nueva","Deshacer conserva las rutas materializadas");
  editor.Redo();Check(editor.Project.assets.First(a=>a.id==first.id).source.uri=="generated:ubicacion-nueva","Rehacer conserva las rutas materializadas");
  var render=Store.New();var lane=render.Active.tracks.First(t=>t.kind=="video");
  var red=GeneratedMedia.CreateSolid("Rojo","FF0000",5);var blue=GeneratedMedia.CreateSolid("Azul","0000FF",5);
  render.assets.Add(red);render.assets.Add(blue);Media.Insert(render,red.id,lane.id,0);Media.Insert(render,blue.id,lane.id,5);
  using(var frame=Rendering.FrameExact(render,5-0.5/60,CancellationToken.None,320,180,true)){
   var pixel=frame.GetPixel(160,90);Check(pixel.R>pixel.B,"Fotograma antes del corte conserva el clip saliente");
  }
  using(var frame=Rendering.FrameExact(render,5,CancellationToken.None,320,180,true)){
   var pixel=frame.GetPixel(160,90);Check(pixel.B>pixel.R,"Fotograma en el corte muestra el clip entrante");
  }
  var transition=TransitionContracts.New(render,render.Active.clips[0],render.Active.clips[1],TransitionContracts.DipBlack,30);
  render.Active.transitions.Add(transition);Store.Validate(render);
  using(var frame=Rendering.FrameExact(render,5,CancellationToken.None,320,180,false))
   Check(frame.Width==320&&frame.Height==180,"Fotograma final atraviesa una transición sin fallar la validación");
 }
}
