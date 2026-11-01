using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  void ShowSequenceCanvasSettings(){
   var sequence=editor.Project.Active;
   using(var dialog=new Form{Text="Lienzo de la secuencia",ClientSize=new Size(480,350),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,BackColor=panel,ForeColor=text}){
    var ratios=new[]{new[]{16,9},new[]{9,16},new[]{1,1},new[]{4,3},new[]{3,4},new[]{21,9}};
    var sides=new[]{720,1080,1440,0,2160};
    var heading=new Label{Text="Formato del montaje",Left=18,Top=16,Width=430,Height=24,Font=new Font("Segoe UI",10,FontStyle.Bold)};
    var aspectLabel=new Label{Text="Relación de aspecto",Left=18,Top=48,Width=200,Height=20};
    var aspect=new ComboBox{Left=18,Top=72,Width=210,DropDownStyle=ComboBoxStyle.DropDownList,BackColor=bg,ForeColor=text};
    aspect.Items.AddRange(new object[]{"Horizontal · 16:9","Vertical · 9:16","Cuadrado · 1:1","Clásico · 4:3","Vertical · 3:4","Ultrapanorámico · 21:9","Personalizada"});
    var resolutionLabel=new Label{Text="Resolución de trabajo",Left=248,Top=48,Width=200,Height=20};
    var resolution=new ComboBox{Left=248,Top=72,Width=214,DropDownStyle=ComboBoxStyle.DropDownList,BackColor=bg,ForeColor=text};
    resolution.Items.AddRange(new object[]{"720p","1080p","1440p","2K · 2048 px de ancho","4K UHD","Personalizada"});
    var width=new NumericUpDown{Left=18,Top=148,Width=145,Minimum=320,Maximum=7680,Increment=2,Value=sequence.width,BackColor=bg,ForeColor=text};
    var height=new NumericUpDown{Left=188,Top=148,Width=145,Minimum=320,Maximum=4320,Increment=2,Value=sequence.height,BackColor=bg,ForeColor=text};
    var by=new Label{Text="×",Left=168,Top=151,Width=20,Height=23,TextAlign=ContentAlignment.MiddleCenter};
    var labels=new Label{Text="Dimensiones finales del lienzo (px)",Left=18,Top=122,Width=350,Height=21};
    bool syncing=false;int ratioIndex=Array.FindIndex(ratios,r=>Math.Abs((double)sequence.width/sequence.height-(double)r[0]/r[1])<.001);
    int resolutionIndex=-1;if(ratioIndex>=0)for(int i=0;i<sides.Length;i++){try{var size=SequenceCanvas.Preset(ratios[ratioIndex][0],ratios[ratioIndex][1],sides[i]==0?1080:sides[i],sides[i]==0);if(size[0]==sequence.width&&size[1]==sequence.height){resolutionIndex=i;break;}}catch{}}
    aspect.SelectedIndex=ratioIndex<0?6:ratioIndex;resolution.SelectedIndex=resolutionIndex<0?5:resolutionIndex;
    Action sync=()=>{if(syncing||aspect.SelectedIndex<0||aspect.SelectedIndex>=ratios.Length||resolution.SelectedIndex<0||resolution.SelectedIndex>=sides.Length)return;try{var shape=ratios[aspect.SelectedIndex];var size=SequenceCanvas.Preset(shape[0],shape[1],sides[resolution.SelectedIndex]==0?1080:sides[resolution.SelectedIndex],sides[resolution.SelectedIndex]==0);syncing=true;width.Value=size[0];height.Value=size[1];}catch(Exception ex){MessageBox.Show(dialog,ex.Message,"Lienzo",MessageBoxButtons.OK,MessageBoxIcon.Information);}finally{syncing=false;}};
    aspect.SelectedIndexChanged+=(s,e)=>sync();resolution.SelectedIndexChanged+=(s,e)=>sync();
    EventHandler changed=(s,e)=>{if(syncing)return;syncing=true;aspect.SelectedIndex=6;resolution.SelectedIndex=5;syncing=false;};width.ValueChanged+=changed;height.ValueChanged+=changed;
    var note=new Label{Text="La relación y la resolución de trabajo son independientes. La exportación puede usar otra resolución de la misma relación. La secuencia permanece a 60 FPS.",Left=18,Top=195,Width=445,Height=61,ForeColor=Color.FromArgb(170,174,180)};
    var save=new Button{Text="Aplicar",Left=274,Top=287,Width=87,DialogResult=DialogResult.OK};
    var cancel=new Button{Text="Cancelar",Left=371,Top=287,Width=87,DialogResult=DialogResult.Cancel};
    dialog.Controls.AddRange(new Control[]{heading,aspectLabel,aspect,resolutionLabel,resolution,labels,width,by,height,note,save,cancel});dialog.AcceptButton=save;dialog.CancelButton=cancel;
    if(dialog.ShowDialog(this)!=DialogResult.OK)return;
    int chosenWidth=(int)width.Value,chosenHeight=(int)height.Value;
    if(chosenWidth==sequence.width&&chosenHeight==sequence.height)return;
    bool changedAspect=Math.Abs((double)chosenWidth/chosenHeight-(double)sequence.width/sequence.height)>.001;
    bool adapt=false;if(changedAspect&&sequence.clips.Any(c=>sequence.tracks.Any(t=>t.id==c.track_id&&t.kind=="video"))){var response=MessageBox.Show(this,"Cambió la relación del lienzo. ¿Aplicar Adaptar al lienzo automáticamente a los medios visuales?\n\nSí: ajustar escalas · No: conservar el encuadre · Cancelar: volver", "Adaptar medios",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);if(response==DialogResult.Cancel)return;adapt=response==DialogResult.Yes;}
    int adjusted=0,skipped=0;Edit(()=>editor.Commit(p=>{SequenceCanvas.Set(p.Active,chosenWidth,chosenHeight);if(changedAspect)foreach(var clip in p.Active.clips){var sam2=Sam2MaskStore090.Get(clip);if(sam2!=null&&sam2.enabled){sam2.enabled=false;Sam2MaskStore090.Put(clip,sam2);}if(adapt)try{if(CanvasFit0913.Apply(p,clip,"adapt"))adjusted++;}catch{skipped++;}}}));
    status.Text="Lienzo "+chosenWidth+" × "+chosenHeight+(adapt?" · "+adjusted+" medios adaptados"+(skipped>0?" · "+skipped+" requieren ajuste manual":""):" · revisa el encuadre");
   }
  }
  void UpdateClipFraming(string clipId,string mode,double scale,double x,double y){
   try{var ids=timeline.Selection.Contains(clipId)?timeline.Selection.ToList():new System.Collections.Generic.List<string>{clipId};Edit(()=>editor.Commit(p=>{
    foreach(string id in ids){var clip=p.Active.clips.Find(c=>c.id==id);if(clip==null)continue;
     var track=p.Active.tracks.Find(t=>t.id==clip.track_id);if(track==null||track.locked)throw new Exception("La pista está bloqueada");if(track.kind=="video")ClipFraming.Set(clip,ClipFraming.Read(clip).mode,scale,x,y);}
   }));status.Text="Transformación actualizada";}
   catch(Exception ex){Error(ex);}
  }
  void PreviewClipFraming(string clipId,string mode,double scale,double x,double y){PreviewProject(p=>{var clip=p.Active.clips.Find(c=>c.id==clipId);if(clip!=null)ClipFraming.Set(clip,mode,scale,x,y);});}
  void PreviewScaleY(string clipId,double scaleY){PreviewProject(p=>{var clip=p.Active.clips.Find(c=>c.id==clipId);if(clip!=null)ClipFraming.SetScaleY(clip,scaleY);});}
  void UpdateScaleY(string clipId,double scaleY){try{Edit(()=>editor.Commit(p=>{var clip=p.Active.clips.Find(c=>c.id==clipId);if(clip==null||p.Active.tracks.Find(t=>t.id==clip.track_id).locked)throw new Exception("Clip no disponible o pista bloqueada");ClipFraming.SetScaleY(clip,scaleY);}));status.Text="Escala vertical actualizada";}catch(Exception ex){Error(ex);}}
  void UpdateScaleLink(string clipId,bool linked){try{if(busy)throw new Exception("Espera a que termine la tarea actual");Stop();editor.Commit(p=>{var clip=p.Active.clips.Find(c=>c.id==clipId);if(clip==null||p.Active.tracks.Find(t=>t.id==clip.track_id).locked)throw new Exception("Clip no disponible o pista bloqueada");ClipFraming.SetScaleLinked(clip,linked);});dirty=true;projectLabel.Text="Prometheus · "+editor.Project.name+" *";Text="Prometheus 1.0.0 · "+editor.Project.name+" · "+editor.Project.Active.name+" *";timeline.Project=editor.Project;timeline.Invalidate();var selected=editor.Project.Active.clips.Find(c=>c.id==clipId);keyframeView.SetClip(selected,timeline.Cursor);RefreshMonitorTransform();var frame=ClipFraming.Read(selected);effectControlsView.ShowClip(editor.Project,selected);RequestFrame();status.Text=linked?"Escalas enlazadas":"Escalas independientes";}catch(Exception ex){Error(ex);}}
  void SetClipFit(string clipId,bool fit){try{var ids=timeline.Selection.Contains(clipId)?timeline.Selection.ToList():new System.Collections.Generic.List<string>{clipId};Edit(()=>editor.Commit(p=>{foreach(string id in ids){var clip=p.Active.clips.Find(c=>c.id==id);if(clip==null)continue;var track=p.Active.tracks.Find(t=>t.id==clip.track_id);if(track==null||track.locked)throw new Exception("Pista bloqueada");if(track.kind!="video")continue;var frame=ClipFraming.Read(clip);ClipFraming.Set(clip,fit?"fit":"native",frame.scale,frame.x,frame.y);}}));status.Text=fit?"Ajustar al lienzo activo":"Tamaño original activo";}catch(Exception ex){Error(ex);}}
  void ApplyCanvasFit(string[] ids,string operation){try{int changed=0;Edit(()=>editor.Commit(p=>{foreach(string id in ids){var clip=p.Active.clips.Find(c=>c.id==id);if(clip!=null&&CanvasFit0913.Apply(p,clip,operation))changed++;}}));status.Text=changed+" medio(s) ajustados · Ctrl+Z para deshacer";}catch(Exception ex){Error(ex);}}
  void PreviewClipOpacity(string clipId,double opacity){PreviewProject(p=>{var clip=p.Active.clips.Find(c=>c.id==clipId);if(clip!=null)ClipFraming.SetOpacity(clip,opacity);});}
  void UpdateClipOpacity(string clipId,double opacity){try{var ids=timeline.Selection.Contains(clipId)?timeline.Selection.ToList():new System.Collections.Generic.List<string>{clipId};Edit(()=>editor.Commit(p=>{foreach(string id in ids){var clip=p.Active.clips.Find(c=>c.id==id);if(clip==null)continue;var track=p.Active.tracks.Find(t=>t.id==clip.track_id);if(track==null||track.locked)throw new Exception("Pista bloqueada");if(track.kind=="video")ClipFraming.SetOpacity(clip,opacity);}}));status.Text="Opacidad actualizada";}catch(Exception ex){Error(ex);}}
 }
}
