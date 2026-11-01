using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  void RevealPlacedClip(PlacementResult placement){
   if(placement==null||placement.PrimaryClipId==null)return;
   timeline.Selection.Clear();timeline.Selection.AddRange(placement.ClipIds);
   timeline.Selected=placement.PrimaryClipId;timeline.ActiveTrack=placement.PrimaryTrackId;
   timeline.RevealTrack(placement.PrimaryTrackId);
   timeline.Invalidate();ShowClip();
  }
  void PlaceAssetAt(string assetId,string trackId,double at){
   PlacementResult result=null;
   Edit(()=>editor.Commit(p=>result=Media.PlaceInFreeTrack(p,assetId,trackId,at)));
   RevealPlacedClip(result);
   var chosen=editor.Project.Active.tracks.Find(t=>t.id==result.PrimaryTrackId);
   status.Text="Medio añadido en "+(chosen==null?"pista compatible":chosen.name);
  }
  void AddTimelineInsertMenu(ContextMenuStrip menu,string trackId,double at){
   var track=editor.Project.Active.tracks.Find(t=>t.id==trackId);
   if(track==null||track.locked||busy)return;
   if(menu.Items.Count>0)MenuDivider(menu);
   menu.Items.Add("Importar archivo…",null,(s,e)=>{using(var picker=new OpenFileDialog{Filter="Medios compatibles|*.mp4;*.mov;*.wav;*.png;*.jpg;*.jpeg|Todos los archivos|*.*",Multiselect=true})if(picker.ShowDialog(this)==DialogResult.OK)StartImport(picker.FileNames,trackId,at,true);});
   if(track.kind=="video"){
    menu.Items.Add("Importar enlace…",null,(s,e)=>ShowImportLink(trackId,at,true));
    menu.Items.Add("Crear color sólido…",null,(s,e)=>CreateGenerated(false,trackId,at));
    menu.Items.Add("Crear espacio reservado…",null,(s,e)=>CreateGenerated(true,trackId,at));
   }else{
    menu.Items.Add("Importar enlace de audio…",null,(s,e)=>ShowImportLink(trackId,at,true));
    menu.Items.Add("Crear audio reservado…",null,(s,e)=>CreateGenerated(true,trackId,at,true));
   }
  }
  void BuildEmptyContextMenu(string trackId,double at,Point point){
   var menu=gapMenu;ClearMenu(menu);AddTimelineInsertMenu(menu,trackId,at);if(menu.Items.Count>0)menu.Show(timeline,point);
  }
 }
}
