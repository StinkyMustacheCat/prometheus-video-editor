using System;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  string editingVideoNestedId,editingAudioNestedId;
  void SetupNested(){
   timeline.OpenNested=OpenNested;
   foreach(var view in new[]{timelineView.NestedEditor,timelineView.NestedAudioEditor}){
    var local=view;
    local.CloseRequested=()=>{if(Object.ReferenceEquals(local,timelineView.NestedEditor))editingVideoNestedId=null;else editingAudioNestedId=null;local.Set(null,null);status.Text="Medio anidado replegado";};
    local.ChildChanged=(outerId,childId,start,duration,trackId)=>{try{Edit(()=>editor.Commit(p=>NestedMedia0910.UpdateDeepChild(p,outerId,childId,start,duration,trackId)));status.Text="Contenido interno actualizado · Ctrl+Z para deshacer";}catch(Exception ex){Error(ex);}};
    local.EditChildRequested=EditNestedChild;
    local.RemoveChildRequested=(outerId,childId)=>{try{Edit(()=>editor.Commit(p=>NestedMedia0910.RemoveDeepChild(p,outerId,childId)));status.Text="Componente retirado · Ctrl+Z para deshacer";}catch(Exception ex){Error(ex);}};
   }
  }
  void OpenNested(string clipId){var clip=editor.Project.Active.clips.Find(c=>c.id==clipId);if(!NestedMedia0910.IsNested(editor.Project,clip))return;bool audio=editor.Project.Active.tracks.Any(t=>t.id==clip.track_id&&t.kind=="audio");if(audio&&editingAudioNestedId==clipId){editingAudioNestedId=null;timelineView.NestedAudioEditor.Set(null,null);status.Text="Audio anidado replegado";return;}if(!audio&&editingVideoNestedId==clipId){editingVideoNestedId=null;timelineView.NestedEditor.Set(null,null);status.Text="Vídeo anidado replegado";return;}if(!audio&&NestedMedia0910.Read(clip).audio_orders.Count>0&&NestedMedia0910.Companion(editor.Project,clip)==null){Edit(()=>editor.Commit(p=>NestedMedia0910.EnsureAudioCompanion(p,clipId)));clip=editor.Project.Active.clips.Find(c=>c.id==clipId);}if(audio){editingAudioNestedId=clipId;timelineView.NestedAudioEditor.Set(editor.Project,clipId);}else{editingVideoNestedId=clipId;timelineView.NestedEditor.Set(editor.Project,clipId);}status.Text="Editando componentes del medio anidado · arrastra para mover o recortar · Listo para replegar";}
  void UnnestSelected(string clipId){try{List<string> revealed=null;Edit(()=>editor.Commit(p=>{revealed=NestedMedia0910.Unnest(p,clipId);}));editingVideoNestedId=null;editingAudioNestedId=null;timelineView.NestedEditor.Set(null,null);timelineView.NestedAudioEditor.Set(null,null);timeline.Selection=revealed;timeline.Selected=revealed.FirstOrDefault();timeline.Invalidate();ShowClip();status.Text="Medio desanidado · Ctrl+Z para deshacer";}catch(Exception ex){Error(ex);}}
  void NestSelected(List<string> ids){try{string id=null;Edit(()=>editor.Commit(p=>{var all=new HashSet<string>(ids);foreach(var group in p.Active.link_groups.Where(g=>g.clip_ids.Any(all.Contains)).ToList())foreach(var child in group.clip_ids)all.Add(child);id=NestedMedia0910.Create(p,all,"Medio anidado").id;}));editingVideoNestedId=null;editingAudioNestedId=null;timelineView.NestedEditor.Set(null,null);timelineView.NestedAudioEditor.Set(null,null);timeline.Selection=new List<string>{id};timeline.Selected=id;timeline.Invalidate();ShowClip();status.Text="Medio anidado creado · doble clic para editar · Ctrl+Z para deshacer";}catch(Exception ex){Error(ex);}}
  void ApplyBlurredBackground(string clipId){try{string id=null;Edit(()=>editor.Commit(p=>{id=NestedMedia0910.CreateBlurredBackground(p,clipId).id;}));timeline.Selection=new List<string>{id};timeline.Selected=id;editingVideoNestedId=null;editingAudioNestedId=null;timelineView.NestedEditor.Set(null,null);timelineView.NestedAudioEditor.Set(null,null);status.Text="Fondo desenfocado creado · Ctrl+Z para deshacer";}catch(Exception ex){Error(ex);}}
  void ApplyTextCascade01062(string clipId){try{string id=null;Edit(()=>editor.Commit(p=>{id=TextCascade01062.Apply(p,clipId).id;}));timeline.Selection=new List<string>{id};timeline.Selected=id;ShowClip();timeline.Invalidate();status.Text="Letras en cascada · ajusta el título anidado con doble clic";}catch(Exception ex){Error(ex);}}
  void EditNestedChild(string outerId,string childId){var found=NestedMedia0910.FindDeepChild(editor.Project,outerId,childId);if(found==null)return;var content=found.content;var child=found.child;bool audio=content.audio_orders.ContainsKey(child.track_id);var blur=child.effects.FirstOrDefault(e=>e.effect_id==EffectContracts.BlurId);var frame=ClipFraming.Read(child);
   using(var dialog=new Form{Text="Propiedades · "+child.name,ClientSize=new Size(360,audio?150:blur==null?190:230),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,BackColor=panel,ForeColor=text}){
    Func<string,int,decimal,decimal,decimal,NumericUpDown> addNumber=(label,top,min,max,value)=>{dialog.Controls.Add(new Label{Text=label,Left=18,Top=top+4,Width=170,Height=22});var number=new NumericUpDown{Left=195,Top=top,Width=140,Minimum=min,Maximum=max,Value=value,DecimalPlaces=2,Increment=.05M,BackColor=bg,ForeColor=text};dialog.Controls.Add(number);return number;};
    NumericUpDown x=null,y=null,sigma=null,gainBox=null;if(audio)gainBox=addNumber("Volumen · dB",22,-60,12,(decimal)child.gain_db);else{x=addNumber("Escala horizontal",22,.25M,4,(decimal)frame.scale);y=addNumber("Escala vertical",60,.25M,4,(decimal)frame.scaleY);if(blur!=null)sigma=addNumber("Desenfoque",98,.1M,20,(decimal)blur.parameters["sigma"].number.Value);}
    var accept=new Button{Text="Guardar",Left=175,Top=dialog.ClientSize.Height-47,Width=78,Height=28,DialogResult=DialogResult.OK};var cancel=new Button{Text="Cancelar",Left=265,Top=dialog.ClientSize.Height-47,Width=78,Height=28,DialogResult=DialogResult.Cancel};dialog.Controls.AddRange(new Control[]{accept,cancel});dialog.AcceptButton=accept;dialog.CancelButton=cancel;
    if(dialog.ShowDialog(this)==DialogResult.OK)try{double sx=x==null?1:(double)x.Value,sy=y==null?1:(double)y.Value;double? amount=sigma==null?(double?)null:(double)sigma.Value;double volume=gainBox==null?0:(double)gainBox.Value;Edit(()=>editor.Commit(p=>NestedMedia0910.UpdateDeepChildProperties(p,outerId,childId,sx,sy,amount,volume)));status.Text="Propiedades internas actualizadas · Ctrl+Z para deshacer";}catch(Exception ex){Error(ex);}
   }
  }
 }
}
