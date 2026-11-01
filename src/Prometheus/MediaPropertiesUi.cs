using System;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  void ShowMediaSettings(string assetId){ShowMediaSettings(assetId,null);}
  void ShowMediaSettings(string assetId,string clipId){
   var asset=editor.Project.assets.Find(a=>a.id==assetId);if(asset==null)return;
   using(var dialog=new Form{Text="Editar medio · "+asset.name,Width=380,Height=510,MinimumSize=new Size(320,350),StartPosition=FormStartPosition.CenterParent,BackColor=panel,ForeColor=text}){
    var view=new InspectorPanelView{Dock=DockStyle.Fill};
    var close=new Button{Text="Cerrar",Dock=DockStyle.Bottom,Height=32,DialogResult=DialogResult.OK};
    dialog.Controls.Add(view);dialog.Controls.Add(close);dialog.AcceptButton=close;
    Action refresh=()=>{var current=editor.Project.assets.Find(a=>a.id==assetId);if(current!=null){var clip=clipId==null?null:editor.Project.Active.clips.Find(c=>c.id==clipId);if(clip!=null)view.ShowGeneratedClip(current,clip);else view.ShowAsset(current);dialog.Text="Editar medio · "+current.name;}};
    view.AssetNameChanged=(id,name)=>{RenameAssetFromProperties(id,name);refresh();};
    view.GeneratedChanged=(id,targetClipId,name,detail,duration)=>{UpdateGeneratedFromProperties(id,targetClipId,name,detail,duration);refresh();};
    view.RelinkRequested=id=>{dialog.Close();Relink(id);};
    view.GenerateProxyRequested=id=>{dialog.Close();try{BeginProxy(id);}catch(Exception ex){Error(ex);}};
    view.ProxyPreferenceChanged=(id,enabled)=>{ChangeProxyPreference(id,enabled);refresh();};
    view.PrepareWebPreviewRequested=id=>{dialog.Close();BeginWebPreview(id);};
    view.OpenWebSourceRequested=OpenWebSource;
    view.CopyWebSourceRequested=CopyWebSource;
    refresh();dialog.ShowDialog(this);
   }
  }
  void RenameAssetFromProperties(string assetId,string name){
   try{
    name=(name??"").Trim();
    if(name.Length<1||name.Length>120)throw new Exception("El nombre debe tener entre 1 y 120 caracteres");
    Edit(()=>editor.Commit(p=>{
     var asset=p.assets.Find(a=>a.id==assetId);
     if(asset==null)throw new Exception("El medio ya no está en el proyecto");
     string previous=asset.name;asset.name=name;
     foreach(var clip in p.sequences.SelectMany(s=>s.clips).Where(c=>c.asset_id==assetId&&c.name==previous))clip.name=name;
    }));
    if(mediaView.SelectedAssetId==assetId)ShowAsset();
    status.Text="Nombre del medio actualizado";
   }catch(Exception ex){Error(ex);}
  }
  void UpdateGeneratedFromProperties(string assetId,string clipId,string name,string detail,double duration){
   try{
    Edit(()=>editor.Commit(p=>GeneratedMedia.Update(p,assetId,clipId,name,detail,duration)));
    if(clipId==null&&mediaView.SelectedAssetId==assetId)ShowAsset();
    status.Text=clipId==null?"Propiedades del medio actualizadas":"Espacio o color actualizado en la línea de tiempo";
   }catch(Exception ex){Error(ex);}
  }
 }
}
