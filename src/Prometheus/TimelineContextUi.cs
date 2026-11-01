using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  static void MenuDivider(ContextMenuStrip menu){if(menu.Items.Count>0&&!(menu.Items[menu.Items.Count-1] is ToolStripSeparator))menu.Items.Add(new ToolStripSeparator());}
  bool EditableClips(IEnumerable<string> ids){
   var requested=new HashSet<string>(ids);if(requested.Count==0)return false;
   foreach(string id in requested){var clip=editor.Project.Active.clips.Find(c=>c.id==id);if(clip==null)return false;var track=editor.Project.Active.tracks.Find(t=>t.id==clip.track_id);if(track==null||track.locked)return false;}
   return true;
  }
  bool ValidEditorAction(Action<Editor> action){try{var test=new Editor();test.Reset(Store.Copy(editor.Project));action(test);return true;}catch{return false;}}
  bool CanRippleDeleteClip(Clip selected,IEnumerable<string> affected){
   var ids=new HashSet<string>(affected);if(!EditableClips(ids))return false;
   return editor.Project.Active.clips.Where(c=>!ids.Contains(c.id)&&c.timeline_start.Seconds>=selected.End-0.000001).All(c=>{
    var track=editor.Project.Active.tracks.Find(t=>t.id==c.track_id);return track!=null&&!track.locked;
   });
  }
  void BuildGapContextMenu(string trackId,double at,Point point){
   var menu=gapMenu;ClearMenu(menu);
   double gapStart=timeline.SelectedGapStart,gapDuration=timeline.SelectedGapEnd-timeline.SelectedGapStart;
   if(timeline.SelectedGapTrack!=trackId||gapDuration<1.0/60-0.000001)return;
   var close=menu.Items.Add("Eliminar hueco",null,(s,e)=>{try{Edit(()=>editor.RippleDeleteGap(trackId,at));timeline.SelectedGapTrack=null;timeline.Invalidate();}catch(Exception ex){Error(ex);}});
   close.Enabled=ValidEditorAction(test=>test.RippleDeleteGap(trackId,at));
   close.ToolTipText=close.Enabled?"Cierra el espacio desplazando el contenido posterior":"Desbloquea las pistas posteriores para cerrar este hueco";
   var track=editor.Project.Active.tracks.Find(t=>t.id==trackId);
   if(track!=null&&!track.locked){
    menu.Items.Add("Convertir en espacio reservado",null,(s,e)=>CreateGenerated(true,trackId,gapStart,track.kind=="audio",gapDuration));
    menu.Items.Add("Importar enlace en este hueco…",null,(s,e)=>ShowImportLink(trackId,gapStart,true,null,gapDuration));
    if(track.kind=="video")menu.Items.Add("Crear color sólido en este hueco…",null,(s,e)=>CreateGenerated(false,trackId,gapStart,false,gapDuration));
   }
   if(menu.Items.Count>0)menu.Show(timeline,point);
  }
  void BuildClipContextMenu(Point point){
   var menu=clipMenu;ClearMenu(menu);
   string selectedId=timeline.Selected;var selected=editor.Project.Active.clips.Find(c=>c.id==selectedId);
   if(selected==null)return;
   var ids=timeline.Selection.Where(id=>editor.Project.Active.clips.Any(c=>c.id==id)).Distinct().ToList();if(!ids.Contains(selectedId))ids.Add(selectedId);
   var related=ids.SelectMany(editor.Related).Distinct().ToList();bool editable=EditableClips(related);
   var selectedAsset=editor.Project.assets.Find(a=>a.id==selected.asset_id);
   var selectedStream=selectedAsset==null?null:selectedAsset.streams.Find(stream=>stream.id==selected.stream_id);
   if(editable&&(selectedStream!=null&&selectedStream.kind=="audio"||related.Any(id=>{var linked=editor.Project.Active.clips.Find(c=>c.id==id);var source=linked==null?null:editor.Project.assets.Find(a=>a.id==linked.asset_id);return source!=null&&source.streams.Any(st=>st.id==linked.stream_id&&st.kind=="audio");})))
    menu.Items.Add("Detectar y recortar silencios…",null,(s,e)=>ShowSilenceCut01010(selectedId));
   if(editable&&selectedStream!=null&&selectedStream.kind!="image")menu.Items.Add("Velocidad/Duración…",null,(s,e)=>ChangeClipSpeed(selectedId));
   if(editable&&selectedStream!=null&&selectedStream.kind!="audio"&&timeline.Cursor>=selected.timeline_start.Seconds-0.000001&&timeline.Cursor<selected.End-0.000001)
    menu.Items.Add("Insertar fotograma",null,(s,e)=>InsertCaptureFrame01034());
   if(editable&&selectedStream!=null&&selectedStream.kind!="audio"){
    var fitMenu=new ToolStripMenuItem("Ajustar imagen");var visualIds=ids.ToArray();
    fitMenu.DropDownItems.Add("Escalar al lienzo",null,(s,e)=>ApplyCanvasFit(visualIds,"fit"));
    fitMenu.DropDownItems.Add("Encajar al ancho",null,(s,e)=>ApplyCanvasFit(visualIds,"width"));
    fitMenu.DropDownItems.Add("Encajar a la altura",null,(s,e)=>ApplyCanvasFit(visualIds,"height"));
    fitMenu.DropDownItems.Add("Adaptar al lienzo",null,(s,e)=>ApplyCanvasFit(visualIds,"adapt"));
    fitMenu.DropDownItems.Add("Deformar al lienzo",null,(s,e)=>ApplyCanvasFit(visualIds,"deform"));
    if(selectedAsset!=null&&GeneratedMedia.Kind(selectedAsset)==null&&selectedStream.width.HasValue&&selectedStream.height.HasValue&&Math.Abs((double)selectedStream.width.Value/selectedStream.height.Value-(double)editor.Project.Active.width/editor.Project.Active.height)>0.01&&ids.Count==1)fitMenu.DropDownItems.Add("Fondo desenfocado",null,(s,e)=>ApplyBlurredBackground(selectedId));
    menu.Items.Add(fitMenu);
   }
   if(NestedMedia0910.IsNested(editor.Project,selected)){bool audioNested=editor.Project.Active.tracks.Any(t=>t.id==selected.track_id&&t.kind=="audio");bool expanded=audioNested?editingAudioNestedId==selectedId:editingVideoNestedId==selectedId;menu.Items.Add(expanded?"Replegar medio anidado":"Editar medio anidado",null,(s,e)=>OpenNested(selectedId));if(editable)menu.Items.Add("Desanidar medios",null,(s,e)=>UnnestSelected(selectedId));}
   if(ids.Count>=1&&editable&&ids.Any(id=>editor.Project.Active.tracks.Any(t=>t.id==editor.Project.Active.clips.Find(c=>c.id==id).track_id&&t.kind=="video")))menu.Items.Add(ids.Count==1?"Anidar medio":"Anidar medios seleccionados",null,(s,e)=>NestSelected(ids));
   if(editable&&selectedAsset!=null&&GeneratedMedia.IsText(selectedAsset)&&!GeneratedMedia.IsSubtitle(selectedAsset))menu.Items.Add("Animar letras en cascada…",null,(s,e)=>ApplyTextCascade01062(selectedId));
   if(selectedAsset!=null&&selectedAsset.source!=null&&selectedAsset.source.kind=="generated")
    menu.Items.Add("Editar medio generado…",null,(s,e)=>ShowMediaSettings(selected.asset_id,selected.id));
   MenuDivider(menu);
   if(ids.Count>=2&&EditableClips(ids)){
    menu.Items.Add("Vincular selección",null,(s,e)=>{try{Edit(()=>editor.Link(ids));}catch(Exception ex){Error(ex);}});
    menu.Items.Add("Agrupar selección",null,(s,e)=>{try{Edit(()=>editor.Group(ids));timeline.Selection=editor.Project.Active.edit_groups.First(g=>g.clip_ids.Contains(selectedId)).clip_ids.ToList();timeline.Selected=selectedId;timeline.Invalidate();}catch(Exception ex){Error(ex);}});
   }
   if(editor.Project.Active.link_groups.Any(g=>g.clip_ids.Any(ids.Contains))&&EditableClips(related))menu.Items.Add("Separar vínculos de la selección",null,(s,e)=>{try{Edit(()=>editor.UnlinkMany(ids));timeline.Selected=selectedId;timeline.Selection=new List<string>{selectedId};ShowClip();timeline.Invalidate();}catch(Exception ex){Error(ex);}});
   if(editor.Project.Active.edit_groups.Any(g=>g.clip_ids.Any(ids.Contains)))menu.Items.Add("Desagrupar selección",null,(s,e)=>{try{Edit(()=>editor.UngroupMany(ids));timeline.Selected=selectedId;timeline.Selection=new List<string>{selectedId};ShowClip();timeline.Invalidate();}catch(Exception ex){Error(ex);}});
   MenuDivider(menu);menu.Items.Add("Copiar",null,(s,e)=>Copy());
   if(editable)menu.Items.Add("Cortar",null,(s,e)=>{try{CutToClipboard();}catch(Exception ex){Error(ex);}});
   if(clipboard!=null&&clipboard.Count>0&&clipboardProjectId==editor.Project.id)menu.Items.Add("Pegar",null,(s,e)=>{try{Paste();}catch(Exception ex){Error(ex);}});
   menu.Items.Add("Copiar atributos",null,(s,e)=>{try{CopyClipAttributes(selectedId);}catch(Exception ex){Error(ex);}});
   if(HasCopiedAttributes()&&EditableClips(ids))menu.Items.Add("Pegar atributos",null,(s,e)=>{try{PasteClipAttributes(ids);}catch(Exception ex){Error(ex);}});
   if(EditableClips(ids))menu.Items.Add("Restablecer atributos",null,(s,e)=>{try{Edit(()=>editor.Commit(p=>ClipAttributes.Reset(p,ids)));status.Text="Atributos restablecidos · Ctrl+Z para deshacer";}catch(Exception ex){Error(ex);}});
   if(editable){
    var deleteMenu=new ToolStripMenuItem("Eliminar");
    deleteMenu.DropDownItems.Add(ids.Count>1?"Eliminar clips seleccionados":"Eliminar clip",null,(s,e)=>{try{Delete();}catch(Exception ex){Error(ex);}});
    if(ids.Count==1&&CanRippleDeleteClip(selected,editor.Related(selectedId)))deleteMenu.DropDownItems.Add("Eliminar y cerrar espacio",null,(s,e)=>{try{Edit(()=>editor.RippleDelete(selectedId));}catch(Exception ex){Error(ex);}});
    bool cursorInside=timeline.Cursor>selected.timeline_start.Seconds+0.000001&&timeline.Cursor<selected.End-0.000001;
    if(cursorInside){deleteMenu.DropDownItems.Add("Eliminar desde el inicio hasta el lector",null,(s,e)=>{try{Edit(()=>editor.RippleTrim(selectedId,true,timeline.Cursor));}catch(Exception ex){Error(ex);}});deleteMenu.DropDownItems.Add("Eliminar desde el lector hasta el final",null,(s,e)=>{try{Edit(()=>editor.RippleTrim(selectedId,false,timeline.Cursor));}catch(Exception ex){Error(ex);}});}
    menu.Items.Add(deleteMenu);MenuDivider(menu);AddClipColorMenu(menu);
   }
   if(editable&&GeneratedMedia.IsPlaceholder(editor.Project.assets.Find(a=>a.id==selected.asset_id)))AddPlaceholderContext(menu,selectedId);
   MenuDivider(menu);menu.Items.Add("Ver información del medio",null,(s,e)=>ShowMediaInfo(selected.asset_id,selected.id));
   if(selectedAsset!=null&&selectedAsset.source!=null&&selectedAsset.source.kind=="web")menu.Items.Add("Redescargar fuente web",null,(s,e)=>RedownloadAssetWebNow(selectedAsset.id));
   if(menu.Items.Count>0)menu.Show(timeline,point);
  }
 }
}

