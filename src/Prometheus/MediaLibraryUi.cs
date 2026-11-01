using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  string AskLibraryName(string title,string current){
   using(var dialog=new Form{Text=title,Width=390,Height=155,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,StartPosition=FormStartPosition.CenterParent,BackColor=panel,ForeColor=text}){
    var input=new TextBox{Text=current??"",MaxLength=64,Left=15,Top=16,Width=350};
    var ok=new Button{Text="Guardar",DialogResult=DialogResult.OK,Left=190,Top=58,Width=80};
    var cancel=new Button{Text="Cancelar",DialogResult=DialogResult.Cancel,Left=280,Top=58,Width=80};
    dialog.Controls.Add(input);dialog.Controls.Add(ok);dialog.Controls.Add(cancel);dialog.AcceptButton=ok;dialog.CancelButton=cancel;
    return dialog.ShowDialog(this)==DialogResult.OK?input.Text.Trim():null;
   }
  }
  void CreateLibraryFolder(){string name=AskLibraryName("Nueva carpeta de medios","");if(name!=null)Edit(()=>editor.Commit(p=>MediaLibrary.CreateFolder(p,name)));}
  void RenameLibraryFolder(string id){var folder=MediaLibrary.Folders(editor.Project).FirstOrDefault(x=>x.id==id);if(folder==null)return;string name=AskLibraryName("Renombrar carpeta",folder.name);if(name!=null)Edit(()=>editor.Commit(p=>MediaLibrary.RenameFolder(p,id,name)));}
  void DeleteLibraryFolder(string id){Edit(()=>editor.Commit(p=>MediaLibrary.DeleteFolder(p,id)));status.Text="Carpeta eliminada · sus medios siguen en el proyecto";}
  void MoveLibraryAsset(string assetId,string folderId){Edit(()=>editor.Commit(p=>MediaLibrary.Move(p,assetId,folderId)));status.Text="Medio organizado en la biblioteca";}
  void DeleteLibraryAsset(string id){
   var asset=editor.Project.assets.Find(x=>x.id==id);if(asset==null)return;
   int nestedUses=editor.Project.sequences.Sum(s=>s.clips.Where(c=>NestedMedia0910.IsNested(editor.Project,c)).Sum(c=>NestedMedia0910.Read(c).clips.Count(inner=>inner.asset_id==id)));
   if(nestedUses>0){status.Text="Este medio se usa dentro de "+nestedUses+" componente(s) anidado(s). Quítalo allí antes de eliminarlo.";return;}
   int uses=editor.Project.sequences.Sum(s=>s.clips.Count(c=>c.asset_id==id));
   if(uses>0&&!App.Capture&&MessageBox.Show(this,"Este medio aparece en "+uses+" clip(s). Se quitarán de la línea de tiempo y de la biblioteca. El archivo original seguirá en disco.","Eliminar del proyecto",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning)!=DialogResult.OK)return;
   Edit(()=>editor.Commit(p=>{
    p.assets.RemoveAll(a=>a.id==id);
    foreach(var seq in p.sequences){var removed=seq.clips.Where(c=>c.asset_id==id).Select(c=>c.id).ToList();seq.clips.RemoveAll(c=>c.asset_id==id);foreach(var group in seq.link_groups)group.clip_ids.RemoveAll(removed.Contains);seq.link_groups.RemoveAll(g=>g.clip_ids.Count<2);}
   }));status.Text="Medio eliminado del proyecto · archivo original conservado";
  }
  void OpenLibraryAssetFolder(string id){
   var asset=editor.Project.assets.Find(x=>x.id==id);if(asset==null)return;
   var rep=Media.SelectRepresentation(asset,false)??Media.SelectRepresentation(asset,true);
   string path=rep!=null&&rep.locator!=null?rep.locator.path:asset.source!=null&&asset.source.kind=="local"?asset.source.uri:null;
   if(String.IsNullOrWhiteSpace(path)||!File.Exists(path)){status.Text="Este medio todavía no tiene un archivo local disponible";return;}
   Process.Start(Path.GetDirectoryName(Path.GetFullPath(path)));
  }
 }
}
