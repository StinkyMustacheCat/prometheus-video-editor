using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public sealed class TransitionCatalogView:Panel {
  readonly TextBox search=new TextBox();readonly TreeView tree=new TreeView();
  public Action<string> TransitionActivated;
  public Action ImportRequested;
  public Action<string> RemoveRequested;
  public Action<string[]> FilesDropped;
  List<MgtTransitionResource> imported=new List<MgtTransitionResource>();
  public TransitionCatalogView(){
   Dock=DockStyle.Fill;BackColor=Color.FromArgb(36,38,42);
   var title=new Label{Text="Arrastra una transición a un corte entre clips",Dock=DockStyle.Top,Height=30,Padding=new Padding(10,7,0,0),ForeColor=Color.FromArgb(190,193,198),AutoEllipsis=true};
   search.Dock=DockStyle.Top;search.Height=27;search.BackColor=Color.FromArgb(27,28,31);search.ForeColor=Color.WhiteSmoke;
   tree.Dock=DockStyle.Fill;tree.BorderStyle=BorderStyle.None;tree.BackColor=BackColor;tree.ForeColor=Color.WhiteSmoke;tree.Font=new Font("Segoe UI",9);tree.ItemHeight=28;tree.HideSelection=false;tree.ShowLines=false;tree.ShowRootLines=false;tree.ShowNodeToolTips=true;
   Controls.Add(tree);Controls.Add(search);Controls.Add(title);
   var import=new Button{Text="+ Importar transición MGT",Dock=DockStyle.Bottom,Height=32,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(49,51,56),ForeColor=Color.Gainsboro};
   import.Click+=(s,e)=>{if(ImportRequested!=null)ImportRequested();};Controls.Add(import);
   search.TextChanged+=(s,e)=>Rebuild();
   tree.ItemDrag+=(s,e)=>{var node=e.Item as TreeNode;string id=node==null?null:node.Tag as string;if(id==null)return;var data=new DataObject();data.SetData("Prometheus.TransitionId",id);tree.DoDragDrop(data,DragDropEffects.Copy);};
   tree.NodeMouseDoubleClick+=(s,e)=>{string id=e.Node.Tag as string;if(id!=null&&TransitionActivated!=null)TransitionActivated(id);};
   AllowDrop=true;tree.AllowDrop=true;
   DragEventHandler drag=(s,e)=>{var files=e.Data.GetData(DataFormats.FileDrop) as string[];e.Effect=files!=null&&files.Length>0?DragDropEffects.Copy:DragDropEffects.None;};
   DragEnter+=drag;DragOver+=drag;tree.DragEnter+=drag;tree.DragOver+=drag;
   DragEventHandler drop=(s,e)=>{var files=e.Data.GetData(DataFormats.FileDrop) as string[];if(files!=null&&FilesDropped!=null)FilesDropped(files);};
   DragDrop+=drop;tree.DragDrop+=drop;
   tree.NodeMouseClick+=(s,e)=>{if(e.Button!=MouseButtons.Right)return;var id=e.Node.Tag as string;if(id==null||!imported.Any(r=>r.id==id))return;tree.SelectedNode=e.Node;var menu=new ContextMenuStrip();menu.Items.Add("Quitar de Transiciones MGT",null,(a,b)=>{if(RemoveRequested!=null)RemoveRequested(id);});menu.Closed+=(a,b)=>{if(tree.IsHandleCreated&&!tree.IsDisposed)tree.BeginInvoke((Action)(()=>{if(!menu.IsDisposed)menu.Dispose();}));else menu.Dispose();};menu.Show(tree,e.Location);};
   Rebuild();
  }
  public void SetProject(Project project){imported=project==null?new List<MgtTransitionResource>():project.mgt_transitions.ToList();Rebuild();}
  void Rebuild(){
   string query=search.Text.Trim();tree.BeginUpdate();tree.Nodes.Clear();
   foreach(var group in TransitionContracts.All.GroupBy(c=>c.category)){
    var matches=group.Where(c=>query.Length==0||c.name.IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0).ToList();if(matches.Count==0)continue;
    var folder=tree.Nodes.Add(group.Key);folder.NodeFont=new Font(tree.Font,FontStyle.Bold);
    foreach(var contract in matches){var row=folder.Nodes.Add("⇄  "+contract.name);row.Tag=contract.id;row.ToolTipText=contract.needs_incoming_handle?"Necesita margen anterior en el clip entrante":"No necesita margen adicional";}
    folder.Expand();
   }
   var mgt=imported.Where(x=>!x.catalog_hidden&&(query.Length==0||x.name.IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0)).ToList();
   if(mgt.Count>0){var folder=tree.Nodes.Add("Transiciones MGT");folder.NodeFont=new Font(tree.Font,FontStyle.Bold);foreach(var resource in mgt){var row=folder.Nodes.Add("◇  "+resource.name);row.Tag=resource.id;row.ToolTipText="Usa los fotogramas disponibles y procesa una vista previa con Enter";}folder.Expand();}
   tree.EndUpdate();
  }
 }
}
