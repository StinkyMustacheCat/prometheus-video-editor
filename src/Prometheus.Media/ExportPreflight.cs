using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Prometheus {
 public sealed class ExportCheck {
  public string severity,code,message,asset_id,resource_id;
  public double? timeline_seconds;
 }
 public sealed class ExportPreflightReport {
  public List<ExportCheck> checks=new List<ExportCheck>();
  public bool CanExport {get{return !checks.Any(c=>c.severity=="error");}}
  public int Errors {get{return checks.Count(c=>c.severity=="error");}}
  public int Warnings {get{return checks.Count(c=>c.severity=="warning");}}
 }
 public static class ExportPreflight {
  public static ExportPreflightReport Inspect(Project project,bool videoOutput,int pendingImports){
   return Inspect(project,videoOutput,pendingImports,project.Active.width,project.Active.height,false);
  }
  public static ExportPreflightReport Inspect(Project project,bool videoOutput,int pendingImports,int outputWidth,int outputHeight,bool usePreview){
   return Inspect(project,videoOutput,pendingImports,outputWidth,outputHeight,usePreview,false);
  }
  public static ExportPreflightReport Inspect(Project project,bool videoOutput,int pendingImports,int outputWidth,int outputHeight,bool usePreview,bool selectiveWeb){
   var report=new ExportPreflightReport();Store.Validate(project);project=NestedMedia0910.Flatten(project);
   if(project.Active.Length<=0)Add(report,"error","empty","La secuencia no contiene clips.",null,null);
   if(pendingImports>0)Add(report,"error","pending",pendingImports+" medios siguen preparándose. Espera o descártalos.",null,null);
   var used=project.Active.clips.Where(c=>c.enabled&&project.Active.tracks.Any(t=>t.id==c.track_id&&t.enabled)&&(videoOutput||project.Active.tracks.Find(t=>t.id==c.track_id).kind=="audio")).ToList();
   if(!videoOutput&&used.Count==0)Add(report,"error","no-audio","No hay clips de audio activos para exportar como WAV.",null,null);
   var checkedResources=new HashSet<string>();
   foreach(var clip in used.Where(c=>project.Active.tracks.Find(t=>t.id==c.track_id).kind=="video")){
    foreach(var effect in EffectContracts.Effective(project,clip)){
     if(EffectContracts.Find(effect.effect_id,effect.contract_version)==null){
      Add(report,"error","effect-unsupported","El efecto "+effect.effect_id+" del clip «"+clip.name+"» no tiene motor compatible.",clip.asset_id,clip.timeline_start.Seconds);
      continue;
     }
     if(effect.effect_id!=EffectContracts.Lut3DId)continue;
     string resourceId=effect.parameters["lut"].resource_id;
     if(!checkedResources.Add(resourceId))continue;
     var resource=project.effect_resources.FirstOrDefault(r=>r.id==resourceId);
     if(resource==null||!File.Exists(resource.uri)){
      Add(report,"error","lut-missing","Falta el LUT del clip «"+clip.name+"». Usa «Localizar LUT» para reconectarlo.",clip.asset_id,clip.timeline_start.Seconds,resourceId);
      continue;
     }
     if(!Path.GetExtension(resource.uri).Equals(".cube",StringComparison.OrdinalIgnoreCase))
      Add(report,"error","lut-invalid","El LUT de «"+clip.name+"» debe ser un archivo .cube.",clip.asset_id,clip.timeline_start.Seconds,resourceId);
    }
   }
   foreach(var group in used.GroupBy(c=>c.asset_id)){
    var asset=project.assets.Find(a=>a.id==group.Key);if(asset==null)continue;
    double first=group.Min(c=>c.timeline_start.Seconds);
    if(GeneratedMedia.IsPlaceholder(asset)){
     Add(report,"warning","placeholder","Espacio reservado sin conectar: "+asset.name+". Se exportará como referencia visual o silencio.",asset.id,first);continue;
    }
    if(asset.source!=null&&asset.source.kind=="generated")continue;
    if(asset.source!=null&&asset.source.kind=="web"&&Media.SelectRepresentation(asset,false)==null&&!usePreview){
     bool canSelect=selectiveWeb&&RangeMedia.CanResolveSelective(asset);
     if(selectiveWeb&&RangeMedia.CoversUsed(project,asset)){Add(report,"info","web-ranges-cached","Los tramos usados ya están en caché: "+asset.name+".",asset.id,first);continue;}
     Add(report,"warning",canSelect?"web-range-pending":"web-pending",canSelect?"Se intentarán solo los tramos usados; si falla, se obtendrá el medio completo: "+asset.name+".":"Se descargará el medio completo antes de renderizar: "+asset.name+".",asset.id,first);continue;
    }
    if(MgtMedia.IsMgt(asset)&&!File.Exists(asset.source.uri)){Add(report,"error","mgt-source-missing","Falta el archivo MGT original: "+asset.name+".",asset.id,first);continue;}
    if(MgtMedia.IsMgt(asset)&&Media.SelectRepresentation(asset,usePreview)==null){
     Add(report,"warning","mgt-render-pending","La composición MGT se resolverá antes de exportar: "+asset.name+".",asset.id,first);
     continue;
    }
    if(usePreview&&Media.SelectRepresentation(asset,true)==null){
     Add(report,"error","preview-unavailable","Falta una vista ligera o archivo local para: "+asset.name+".",asset.id,first);continue;
    }
    try{Media.Verify(asset,CancellationToken.None,usePreview);}
    catch(Exception ex){Add(report,"error","media-unavailable",ex.Message,asset.id,first);continue;}
    if(videoOutput&&asset.streams.Any(s=>s.kind=="video"&&s.width.HasValue&&s.height.HasValue&&s.width.Value<outputWidth&&s.height.Value<outputHeight))
     Add(report,"warning","small-source","La fuente se ampliará a "+outputWidth+" × "+outputHeight+": "+asset.name+".",asset.id,first);
   }
   foreach(var clip in used){double? missing=ShortTail.MissingStart(project,clip);if(missing.HasValue){
    var asset=project.assets.Find(a=>a.id==clip.asset_id);
    Add(report,"warning","short-tail","El medio termina antes que el clip: "+clip.name+". El tramo rayado se exportará negro o en silencio.",asset==null?null:asset.id,missing.Value);
   }}
   if(usePreview&&report.CanExport)Add(report,"warning","preview-export","Se usarán vistas ligeras o proxys cuando estén disponibles; la calidad puede ser menor que la final.",null,null);
   if(report.checks.Count==0)Add(report,"info","ready","Medios disponibles. Listo para exportar.",null,null);
   return report;
  }
  static void Add(ExportPreflightReport report,string severity,string code,string message,string assetId,double? at,string resourceId=null){report.checks.Add(new ExportCheck{severity=severity,code=code,message=message,asset_id=assetId,resource_id=resourceId,timeline_seconds=at});}
 }
}

