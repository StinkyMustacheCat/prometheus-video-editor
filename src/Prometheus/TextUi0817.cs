using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  bool textToolActive;
  void ActivateTextTool(){if(maskToolActive){maskToolActive=false;StopMaskPick01061();monitor.CancelMaskEdit(false);monitor.ShowObjectOverlay(null);}timeline.ActiveTool=Timeline.ToolMode.Select;textToolActive=true;monitor.TextToolActive=true;UpdateMouseButtons();monitor.Focus();status.Text="Texto: haz clic en el monitor, escribe y pulsa Ctrl+Intro para terminar";}
  void CreateTitleAt08171(string value,double x,double y){if(busy)return;var asset=GeneratedMedia.CreateText(value,5);var captionTrack=SubtitleTrack0817.TrackId(editor.Project.Active);
   var preferred=editor.Project.Active.tracks.FirstOrDefault(t=>t.id==timeline.ActiveTrack&&t.id!=captionTrack&&t.kind=="video"&&!t.locked)??editor.Project.Active.tracks.FirstOrDefault(t=>t.id!=captionTrack&&t.kind=="video"&&!t.locked);
   if(preferred==null)throw new Exception("No hay una pista de vídeo disponible");PlacementResult placed=null;
   Edit(()=>editor.Commit(p=>{p.assets.Add(asset);placed=Media.PlaceInFreeTrack(p,asset.id,preferred.id,timeline.Cursor);ClipFraming.Set(p.Active.clips.Find(c=>c.id==placed.PrimaryClipId),"fit",1,x,y);}));
   RevealPlacedClip(placed);status.Text="Texto añadido · cambia su tamaño y color en Controles de efectos";
  }
  void CreateTitle(){
   using(var dialog=new Form{Text="Crear texto",ClientSize=new Size(480,255),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,BackColor=panel,ForeColor=text}){
    var label=new Label{Text="Texto en pantalla",Left=18,Top=16,Width=440,Height=24};
    var content=new TextBox{Left=18,Top=44,Width=440,Height=115,Multiline=true,MaxLength=1000,Text="Nuevo texto"};
    var durationLabel=new Label{Text="Duración (HH:MM:SS:FF)",Left=18,Top=172,Width=215,Height=22};
    var duration=new TextBox{Left=245,Top=170,Width=126,Text="00:00:05:00"};
    var create=new Button{Text="Crear",Left=282,Top=211,Width=82,DialogResult=DialogResult.OK};
    var cancel=new Button{Text="Cancelar",Left=373,Top=211,Width=84,DialogResult=DialogResult.Cancel};
    dialog.Controls.AddRange(new Control[]{label,content,durationLabel,duration,create,cancel});dialog.AcceptButton=create;dialog.CancelButton=cancel;
    if(dialog.ShowDialog(this)!=DialogResult.OK)return;
    double seconds;if(!MonitorPanelView.TryParseTimecode(duration.Text,out seconds)||seconds<=0)throw new Exception("Escribe una duración válida, por ejemplo 00:00:05:00");
    var asset=GeneratedMedia.CreateText(content.Text,seconds);
    string captionTrack=SubtitleTrack0817.TrackId(editor.Project.Active);
    var track=editor.Project.Active.tracks.FirstOrDefault(t=>t.id==timeline.ActiveTrack&&t.id!=captionTrack&&t.kind=="video"&&!t.locked)??editor.Project.Active.tracks.FirstOrDefault(t=>t.id!=captionTrack&&t.kind=="video"&&!t.locked);
    if(track==null)throw new Exception("No hay una pista de vídeo disponible");PlacementResult placed=null;
    Edit(()=>editor.Commit(p=>{p.assets.Add(asset);placed=Media.PlaceInFreeTrack(p,asset.id,track.id,timeline.Cursor);}));
    mediaView.Reveal(asset.id);if(placed!=null)RevealPlacedClip(placed);
    status.Text="Texto añadido · selecciónalo para editar sus propiedades";
   }
  }
  void UpdateTextAsset(string assetId,string value,int size,string color){
   Edit(()=>editor.Commit(p=>{var asset=p.assets.Find(a=>a.id==assetId);if(asset==null)throw new Exception("El texto ya no existe");GeneratedMedia.SetText(asset,value,size,color);
    string old=asset.name;asset.name=(GeneratedMedia.IsSubtitle(asset)?"Subtítulo · ":"Texto · ")+(value.Length>34?value.Substring(0,34)+"…":value);
    foreach(var clip in p.sequences.SelectMany(s=>s.clips).Where(c=>c.asset_id==assetId&&c.name==old))clip.name=asset.name;
   }));
  }
  void UpdateTextStyleNumber(string assetId,string key,double value){try{Edit(()=>editor.Commit(p=>{var asset=p.assets.Find(a=>a.id==assetId);if(asset==null)throw new Exception("El texto ya no existe");GeneratedMedia.SetTextStyleNumber(asset,key,value);}));}catch(Exception ex){Error(ex);}}
  void UpdateTextStyleColor(string assetId,string key,string value){try{Edit(()=>editor.Commit(p=>{var asset=p.assets.Find(a=>a.id==assetId);if(asset==null)throw new Exception("El texto ya no existe");GeneratedMedia.SetTextStyleColor(asset,key,value);}));}catch(Exception ex){Error(ex);}}
  void UpdateTextFont(string assetId,string family){try{Edit(()=>editor.Commit(p=>{var asset=p.assets.Find(a=>a.id==assetId);if(asset==null)throw new Exception("El texto ya no existe");GeneratedMedia.SetTextFont(asset,family);}));}catch(Exception ex){Error(ex);}}
 }
}
