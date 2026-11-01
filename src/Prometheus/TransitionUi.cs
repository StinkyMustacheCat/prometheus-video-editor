using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  readonly TransitionCatalogView transitionCatalogView=new TransitionCatalogView();
  readonly ContextMenuStrip transitionMenu=new ContextMenuStrip();
  string selectedTransitionId;
  string inspectedTransitionFile;long inspectedTransitionStamp;bool inspectedTransitionResult;
  void SetupTransitions(){
   transitionCatalogView.ImportRequested=ImportMgtTransition;
   transitionCatalogView.FilesDropped=ImportMgtTransitionFiles;
   transitionCatalogView.RemoveRequested=RemoveMgtTransitionFromCatalog;
   timeline.ImportMgtTransitionFiles=OfferMgtTransitionFiles;
   timeline.IsMgtTransitionFiles=IsMgtTransitionFiles;
   timeline.TransitionContext=ShowJunctionContext;
   timeline.JunctionContext=ShowJunctionContext;
   timeline.TransitionResizeRequested=(id,frames)=>{try{Edit(()=>editor.ResizeTransition(id,frames));}catch(Exception ex){Error(ex);}};
   timeline.TransitionSelected=id=>{selectedEffectTrackId=null;selectedTransitionId=id;timeline.SelectedTransitionId=id;instructionView.SetContext(editor.Project,timeline.Selection,id);ShowClip();};
   timeline.DropTransition=(contract,left,right)=>ApplyTransition(contract,left,right);
   transitionCatalogView.TransitionActivated=contract=>{
    var selected=editor.Project.Active.transitions.FirstOrDefault(t=>t.id==selectedTransitionId);
    if(selected==null){status.Text="Arrastra una transición al corte entre dos clips";return;}
    ApplyTransition(contract,selected.left_clip_id,selected.right_clip_id);
   };
   effectControlsView.TransitionDurationChanged=(id,frames)=>{try{Edit(()=>editor.ResizeTransition(id,frames));}catch(Exception ex){Error(ex);}};
   effectControlsView.TransitionRemoved=id=>{try{Edit(()=>{editor.RemoveTransition(id);selectedTransitionId=null;timeline.SelectedTransitionId=null;});}catch(Exception ex){Error(ex);}};
   effectControlsView.TransitionEnabledChanged=(id,enabled)=>{try{Edit(()=>editor.Commit(p=>{var t=p.Active.transitions.Find(x=>x.id==id);if(t!=null)foreach(var item in p.Active.transitions.Where(x=>x.id==id||t.group_id!=null&&x.group_id==t.group_id))item.enabled=enabled;}));}catch(Exception ex){Error(ex);}};
   effectControlsView.TransitionParameterChanged=(id,key,value)=>{try{Edit(()=>editor.Commit(p=>{var t=p.Active.transitions.Find(x=>x.id==id);if(t==null||t.transition_id!=TransitionContracts.Mgt)throw new Exception("Selecciona una transición MGT");t.parameters[key]=value;}));}catch(Exception ex){Error(ex);}};
  }
  void ApplyTransition(string contractId,string leftId,string rightId){
   try{
    var resource=editor.Project.mgt_transitions.FirstOrDefault(r=>r.id==contractId);
    int requested=resource==null?30:(int)Math.Round(resource.recommended_seconds*60);
    Edit(()=>{editor.SetTransition(leftId,rightId,contractId,requested);var item=TransitionContracts.At(editor.Project.Active,leftId,rightId);selectedTransitionId=item==null?null:item.id;timeline.SelectedTransitionId=selectedTransitionId;});
    var contract=TransitionContracts.All.FirstOrDefault(c=>c.id==contractId);status.Text="Transición aplicada: "+(resource==null?contract.name:resource.name);
   }catch(Exception ex){Error(ex);}
  }
  void ImportMgtTransition(){
   using(var dialog=new OpenFileDialog{Title="Importar transición MGT",Filter="Documentos JSON (*.json)|*.json|Todos los archivos (*.*)|*.*"}){
    if(dialog.ShowDialog(this)!=DialogResult.OK)return;
    ImportMgtTransitionFiles(new[]{dialog.FileName});
   }
  }
  void ImportMgtTransitionFiles(string[] files){
   if(files==null||files.Length==0)return;
   try{var resources=files.Select(path=>MgtTransitionResource.FromJson(System.IO.File.ReadAllText(path))).ToList();
    Edit(()=>editor.Commit(p=>{foreach(var resource in resources){var prior=p.mgt_transitions.FirstOrDefault(r=>r.external_id==resource.external_id&&r.document==resource.document);if(prior!=null){prior.catalog_hidden=false;continue;}p.mgt_transitions.Add(resource);}}));
    status.Text=resources.Count==1?"Transición MGT disponible: "+resources[0].name:"Transiciones MGT importadas: "+resources.Count;
   }catch(Exception ex){Error(ex);}
  }
  void OfferMgtTransitionFiles(string[] files){
   if(files==null||files.Length!=1)return;
   try{var resource=MgtTransitionResource.FromJson(System.IO.File.ReadAllText(files[0]));
    if(MessageBox.Show(this,"Se reconoció la transición «"+resource.name+"». ¿Añadirla a Transiciones MGT?","Transición MGT",MessageBoxButtons.OKCancel,MessageBoxIcon.Question)==DialogResult.OK)ImportMgtTransitionFiles(files);
   }catch(Exception ex){Error(ex);}
  }
  bool IsMgtTransitionFiles(string[] files){
   if(files==null||files.Length!=1)return false;
   try{var info=new System.IO.FileInfo(files[0]);if(!info.Exists||info.Length>16*1024*1024)return false;long stamp=info.LastWriteTimeUtc.Ticks^info.Length;if(String.Equals(inspectedTransitionFile,info.FullName,StringComparison.OrdinalIgnoreCase)&&stamp==inspectedTransitionStamp)return inspectedTransitionResult;
    inspectedTransitionFile=info.FullName;inspectedTransitionStamp=stamp;try{MgtTransitionResource.FromJson(System.IO.File.ReadAllText(info.FullName));inspectedTransitionResult=true;}catch{inspectedTransitionResult=false;}return inspectedTransitionResult;
   }catch{return false;}
  }
  void RemoveMgtTransitionFromCatalog(string id){
   try{Edit(()=>editor.Commit(p=>{var resource=p.mgt_transitions.FirstOrDefault(r=>r.id==id);if(resource==null)return;
     if(p.sequences.Any(sequence=>sequence.transitions.Any(t=>t.mgt_resource_id==id||t.transition_id==id)))resource.catalog_hidden=true;
     else p.mgt_transitions.Remove(resource);
    }));status.Text="Transición retirada de la galería";}catch(Exception ex){Error(ex);}
  }
  bool DeleteSelectedTransition(){
   var current=editor.Project.Active.transitions.FirstOrDefault(t=>t.id==selectedTransitionId);
   if(current==null)return false;
   Edit(()=>{editor.RemoveTransition(current.id);selectedTransitionId=null;timeline.SelectedTransitionId=null;});
   status.Text="Transición eliminada";
   return true;
  }
  void ShowJunctionContext(string leftId,string rightId,Point point){
   ClearMenu(transitionMenu);
   var join=transitionMenu.Items.Add("Unir clips",null,(s,e)=>JoinJunction(leftId,rightId));
   join.Enabled=editor.CanJoinCut(leftId,rightId);
   var left=editor.Project.Active.clips.Find(c=>c.id==leftId);
   var right=editor.Project.Active.clips.Find(c=>c.id==rightId);
   var defaultTransition=transitionMenu.Items.Add("Aplicar transición predeterminada",null,(s,e)=>ApplyTransition(TransitionContracts.Dissolve,leftId,rightId));
   defaultTransition.Enabled=TransitionContracts.MaxLinkedFrames(editor.Project,editor.Project.Active,left,right,TransitionContracts.Dissolve)>=2;
   var favorites=new ToolStripMenuItem("Aplicar transición de favoritas");
   favorites.DropDownItems.Add(new ToolStripMenuItem("Aún no hay favoritas"){Enabled=false});
   transitionMenu.Items.Add(favorites);
   var current=TransitionContracts.At(editor.Project.Active,leftId,rightId);
   if(current!=null){
    transitionMenu.Items.Add(new ToolStripSeparator());
    transitionMenu.Items.Add("Editar transición",null,(s,e)=>{selectedTransitionId=current.id;timeline.SelectedTransitionId=current.id;timeline.Invalidate();ShowClip();});
    transitionMenu.Items.Add("Eliminar transición",null,(s,e)=>{try{Edit(()=>{editor.RemoveTransition(current.id);selectedTransitionId=null;timeline.SelectedTransitionId=null;});}catch(Exception ex){Error(ex);}});
   }
   transitionMenu.Show(timeline,point);
  }
  void JoinJunction(string leftId,string rightId){
   try{Edit(()=>editor.JoinCut(leftId,rightId));timeline.SelectedJunctionLeft=null;timeline.SelectedJunctionRight=null;timeline.Invalidate();status.Text="Clips unidos";}
   catch(Exception ex){Error(ex);}
  }
 }
}
