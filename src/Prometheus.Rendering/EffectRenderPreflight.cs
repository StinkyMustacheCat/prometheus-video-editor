using System;
using System.IO;
using System.Linq;

namespace Prometheus {
 public static class EffectRenderPreflight {
  public static void Inspect(Project project,ExportPreflightReport report,bool videoOutput){
   if(!videoOutput)return;project=NestedMedia0910.Flatten(project);
   var used=project.Active.clips.Where(c=>c.enabled&&project.Active.tracks.Any(t=>t.id==c.track_id&&t.enabled&&t.kind=="video"))
    .SelectMany(c=>EffectContracts.Effective(project,c).Where(e=>e.effect_id==EffectContracts.Lut3DId)
     .Select(e=>new{Clip=c,ResourceId=e.parameters["lut"].resource_id}))
    .GroupBy(x=>x.ResourceId);
   foreach(var group in used){
    if(report.checks.Any(c=>c.resource_id==group.Key&&c.severity=="error"))continue;
    var resource=project.effect_resources.FirstOrDefault(r=>r.id==group.Key);
    if(resource==null||!File.Exists(resource.uri))continue; // El comprobador general ya informa del archivo ausente.
    try{FFmpegEffectAdapter.VerifyLutFile(resource.uri);}
    catch(Exception ex){
     var clip=group.OrderBy(x=>x.Clip.timeline_start.Seconds).First().Clip;
     report.checks.RemoveAll(c=>c.code=="ready");
     report.checks.Add(new ExportCheck{severity="error",code="lut-invalid",resource_id=group.Key,
      asset_id=clip.asset_id,timeline_seconds=clip.timeline_start.Seconds,
      message="El LUT de «"+clip.name+"» no se puede procesar: "+ex.Message});
    }
   }
  }
 }
}

