using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  InstructionPanelView instructionView=new InstructionPanelView();
  InstructionPlan currentInstructionPlan;
  InstructionMediaAdapter currentInstructionMedia;
  CancellationTokenSource instructionReviewToken;
  bool instructionApplying;
  void SetupInstructions(){
   instructionView.ErrorOccurred=Error;
   instructionView.CopyStateRequested=(scope,selected)=>{try{bool full=scope.StartsWith("full:");string actual=full?scope.Substring(5):scope;Clipboard.SetText(full?Instructions.CopyState(editor.Project,actual,selected):Instructions.CopyCompactState(editor.Project,actual,selected));status.Text=(full?"Estado completo copiado: ":"Contexto compacto copiado: ")+(actual=="io"?"tramo In/Out":actual=="selection"?"clips seleccionados":actual=="tracks"?"pistas elegidas":actual=="project"?"proyecto completo":"secuencia activa");}catch(Exception ex){Error(ex);}};
   instructionView.ReviewRequested=doc=>{
    if(instructionReviewToken!=null)instructionReviewToken.Cancel();instructionReviewToken=new CancellationTokenSource();var token=instructionReviewToken;
    currentInstructionPlan=null;currentInstructionMedia=null;instructionView.ShowReviewProgress();
    if(doc.project_id!=editor.Project.id){instructionView.ShowError("El proyecto no coincide con el de las instrucciones.");return;}
    try{
     var snapshot=Store.Copy(editor.Project);var encoded=Instructions.Encode(doc);var prepared=Instructions.Parse(encoded);var adapter=new InstructionMediaAdapter();
     status.Text=prepared.actions.Any(a=>a.op=="media.import_url"||a.op=="placeholder.fill_url"||a.op=="media.import_local")?"Consultando enlaces y revisando el lote…":"Revisando el lote…";
     Task.Factory.StartNew(()=>{try{
      if(prepared.actions.Any(a=>a.op=="media.import_url"||a.op=="placeholder.fill_url"||a.op=="media.import_local"))adapter.Prepare(snapshot,prepared,token.Token);
      token.Token.ThrowIfCancellationRequested();var reviewed=Instructions.Preview(snapshot,prepared,adapter);
      UI(()=>{if(token.IsCancellationRequested||instructionView.Document!=doc||Instructions.Encode(doc)!=encoded)return;if(editor.Project.id!=snapshot.id||editor.Project.revision!=snapshot.revision){instructionView.ShowError("El proyecto cambió durante la revisión; se comprobarán las tarjetas otra vez.");return;}currentInstructionPlan=reviewed;currentInstructionMedia=adapter;instructionView.ShowPlan(reviewed);status.Text="Instrucciones revisadas · "+reviewed.ChangedCount+" cambio(s)";});
     }catch(OperationCanceledException){}catch(Exception ex){UI(()=>{if(token.IsCancellationRequested||instructionView.Document!=doc||Instructions.Encode(doc)!=encoded)return;currentInstructionPlan=null;currentInstructionMedia=null;instructionView.ShowError(ex.Message);status.Text="No se pudo revisar el lote";Error(ex);});}});
    }catch(Exception ex){currentInstructionPlan=null;instructionView.ShowError(ex.Message);Error(ex);}
   };
   instructionView.ApplyRequested=doc=>{try{
    if(instructionApplying||busy)throw new Exception("Espera a que termine la tarea actual");
    if(currentInstructionPlan==null)throw new Exception("Revisa las instrucciones antes de aplicarlas");
    var media=currentInstructionMedia;var plan=currentInstructionPlan;var redownload=plan.RedownloadAssetIds.Distinct().ToList();bool changedMgt=plan.MgtPreviewAssetIds.Count>0;
    if(redownload.Count>0&&webResolveToken!=null)throw new Exception("Espera a que termine la descarga web actual antes de aplicar esta instrucción");
    Stop();var source=Store.Copy(editor.Project);string sourceState=Store.Encode(source);var encoded=Instructions.Encode(doc);var prepared=Instructions.Parse(encoded);instructionApplying=true;instructionView.ShowApplyProgress(0,prepared.actions.Count);status.Text="Aplicando "+prepared.actions.Count+" instrucciones…";
    Task.Factory.StartNew(()=>{try{var shadow=new Editor();shadow.Reset(source);Instructions.Apply(shadow,prepared,plan,media,(done,total)=>UI(()=>{if(instructionApplying)instructionView.ShowApplyProgress(done,total);}));var result=shadow.Project;
     UI(()=>{if(!instructionApplying)return;try{if(Store.Encode(editor.Project)!=sourceState)throw new Exception("El proyecto cambió durante la aplicación. Revisa las instrucciones otra vez.");Edit(()=>editor.Commit(p=>{p.name=result.name;p.assets=result.assets;p.effect_resources=result.effect_resources;p.sequences=result.sequences;p.active_sequence_id=result.active_sequence_id;p.extensions=result.extensions;}));instructionApplying=false;currentInstructionPlan=null;currentInstructionMedia=null;instructionView.Applied();if(media!=null)foreach(string id in media.AddedAssetIds){var asset=editor.Project.assets.Find(a=>a.id==id);if(asset!=null&&asset.source!=null&&asset.source.kind=="web"&&asset.streams.Any(s=>s.kind=="video"||s.kind=="audio")&&(prepareWebPreviewAutomatically||WebMedia.IsAudioOnly(asset)))BeginWebPreview(id);}if(changedMgt)foreach(var asset in editor.Project.assets.Where(a=>MgtMedia.IsMgt(a)&&editor.Project.sequences.Any(q=>q.clips.Any(c=>c.asset_id==a.id))&&!a.representations.Any(r=>r.role=="preview")))QueueMgtPreview(asset.id);if(redownload.Count>0)ResolveWebNow(null,false,"project",true,redownload);status.Text=redownload.Count>0?"Instrucciones aplicadas · redescarga web iniciada":"Instrucciones aplicadas en un paso";}catch(Exception ex){instructionApplying=false;currentInstructionPlan=null;currentInstructionMedia=null;instructionView.ShowError(ex.Message);Error(ex);}});
    }catch(Exception ex){UI(()=>{instructionApplying=false;currentInstructionPlan=null;currentInstructionMedia=null;instructionView.ShowError(ex.Message);Error(ex);});}});
   }catch(Exception ex){instructionApplying=false;instructionView.ShowError(ex.Message);Error(ex);}};
  }
 }
}
