using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 // Shared, typed command vocabulary. Input devices (keyboard, future voice) send the same requests.
 // Editing instructions still use their separate reviewed, undoable contract.
 public sealed class EditorActionRequest {
  public string id,project_id,sequence_id,clip_id,marker_id;
  public double? seconds;
  public int? frames;
 }
 public sealed class EditorActionDefinition {
  public readonly string id,argument,description;
  public readonly bool changesProject;
  public EditorActionDefinition(string name,string arg,string label){id=name;argument=arg;description=label;changesProject=false;}
 }
 public static class EditorActionCatalog {
  public static readonly EditorActionDefinition[] All={
   new EditorActionDefinition("transport.play",null,"Reproducir"),
   new EditorActionDefinition("transport.pause",null,"Pausar en la posición actual"),
   new EditorActionDefinition("transport.toggle",null,"Alternar reproducción"),
   new EditorActionDefinition("playhead.seek","seconds","Ubicar lector en tiempo absoluto"),
   new EditorActionDefinition("playhead.step_frames","frames","Mover lector fotogramas, con signo"),
   new EditorActionDefinition("playhead.step_seconds","seconds","Mover lector segundos, con signo"),
   new EditorActionDefinition("playhead.start",null,"Ir al inicio de secuencia"),
   new EditorActionDefinition("playhead.end",null,"Ir al final de secuencia"),
   new EditorActionDefinition("playhead.boundary.next",null,"Siguiente límite de clip"),
   new EditorActionDefinition("playhead.boundary.previous",null,"Límite anterior de clip"),
   new EditorActionDefinition("playhead.junction.next",null,"Siguiente corte entre clips contiguos"),
   new EditorActionDefinition("playhead.junction.previous",null,"Corte anterior entre clips contiguos"),
   new EditorActionDefinition("playhead.marker.next",null,"Siguiente marcador"),
   new EditorActionDefinition("playhead.marker.previous",null,"Marcador anterior"),
   new EditorActionDefinition("playhead.marker.seek","marker_id","Ir al marcador indicado"),
   new EditorActionDefinition("selection.clip.at_playhead",null,"Seleccionar clip bajo lector"),
   new EditorActionDefinition("selection.clip.by_id","clip_id","Seleccionar clip por ID"),
   new EditorActionDefinition("selection.linked",null,"Incluir vinculados en la selección"),
   new EditorActionDefinition("selection.clear",null,"Limpiar selección"),
   new EditorActionDefinition("state.current_time",null,"Consultar tiempo actual"),
   new EditorActionDefinition("state.selected_clip",null,"Consultar clip seleccionado"),
   new EditorActionDefinition("state.active_sequence",null,"Consultar secuencia activa")
  };
  public static void Validate(Project project,EditorActionRequest request){
   if(project==null||request==null||String.IsNullOrWhiteSpace(request.id))throw new Exception("Acción de editor vacía");
   var definition=All.FirstOrDefault(x=>x.id==request.id);if(definition==null)throw new Exception("Acción de editor desconocida: "+request.id);
   if(request.project_id!=null&&request.project_id!=project.id)throw new Exception("El proyecto de la acción no coincide");
   if(request.sequence_id!=null&&request.sequence_id!=project.active_sequence_id)throw new Exception("La secuencia de la acción no está activa");
   if(definition.argument=="seconds"&&(!request.seconds.HasValue||Double.IsNaN(request.seconds.Value)||Double.IsInfinity(request.seconds.Value)))throw new Exception("La acción necesita segundos válidos");
   if(definition.argument=="frames"&&!request.frames.HasValue)throw new Exception("La acción necesita fotogramas");
   if(definition.argument=="clip_id"&&String.IsNullOrWhiteSpace(request.clip_id))throw new Exception("La acción necesita clip_id");
   if(definition.argument=="marker_id"&&String.IsNullOrWhiteSpace(request.marker_id))throw new Exception("La acción necesita marker_id");
   if(request.id=="playhead.seek"&&request.seconds.Value<0)throw new Exception("El tiempo no puede ser negativo");
  }
 }
}
