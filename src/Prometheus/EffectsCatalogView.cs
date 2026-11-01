using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public sealed class EffectsCatalogView:Panel {
  readonly TextBox search=new TextBox();readonly TreeView tree=new TreeView();string preferredKind;
  public Action<string> EffectActivated;
  public EffectsCatalogView(){
   Dock=DockStyle.Fill;BackColor=Color.FromArgb(36,38,42);
   var title=new Label{Text="Buscar efectos",Dock=DockStyle.Top,Height=25,Padding=new Padding(10,5,0,0),ForeColor=Color.FromArgb(190,193,198)};
   search.Dock=DockStyle.Top;search.Height=27;search.Margin=new Padding(8);search.BackColor=Color.FromArgb(27,28,31);search.ForeColor=Color.WhiteSmoke;
   tree.Dock=DockStyle.Fill;tree.BorderStyle=BorderStyle.None;tree.BackColor=BackColor;tree.ForeColor=Color.WhiteSmoke;tree.Font=new Font("Segoe UI",9);tree.ItemHeight=27;tree.HideSelection=false;tree.ShowLines=false;tree.ShowRootLines=false;
   Controls.Add(tree);Controls.Add(search);Controls.Add(title);
   search.TextChanged+=(s,e)=>Rebuild();
   tree.ItemDrag+=(s,e)=>{var node=e.Item as TreeNode;string id=node==null?null:node.Tag as string;if(id==null)return;var data=new DataObject();data.SetData("Prometheus.EffectId",id);tree.DoDragDrop(data,DragDropEffects.Copy);};
   tree.NodeMouseDoubleClick+=(s,e)=>{string id=e.Node.Tag as string;if(id!=null&&EffectActivated!=null)EffectActivated(id);};
   Rebuild();
  }
  public void SetPreferredKind(string kind){if(kind==preferredKind)return;preferredKind=kind;Rebuild();}
  static bool Matches(EffectContract effect,string query){if(query.Length==0)return true;string aliases=effect.name+" "+effect.category+" "+(effect.input_kind=="audio"?"audio sonido voz música":"video imagen visual");if(effect.id==EffectContracts.PitchId)aliases+=" pitch tono voz semitonos";if(effect.id==EffectContracts.GlowId)aliases+=" brillo bloom glow resplandor";if(effect.id==EffectContracts.EqualizerId)aliases+=" eq ecualizador graves medios agudos";if(effect.id==EffectContracts.ZoomInDynamicId||effect.id==EffectContracts.ZoomOutDynamicId||effect.id==EffectContracts.PanDynamicId)aliases+=" movimiento automático relativo duración paneo zoom";return aliases.IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0;}
  void Rebuild(){
   string query=search.Text.Trim();tree.BeginUpdate();tree.Nodes.Clear();
   foreach(string kind in new[]{preferredKind,"video","audio"}.Where(k=>k=="video"||k=="audio").Distinct()){
    var matching=EffectContracts.All.Where(c=>c.input_kind==kind&&Matches(c,query)).ToList();if(matching.Count==0)continue;
    var root=tree.Nodes.Add(kind=="video"?"VÍDEO":"AUDIO");root.NodeFont=new Font(tree.Font,FontStyle.Bold);
    foreach(var category in matching.GroupBy(c=>c.category)){
     var folder=root.Nodes.Add(category.Key);folder.NodeFont=new Font(tree.Font,FontStyle.Bold);
     foreach(var effect in category){var row=folder.Nodes.Add("✦  "+effect.name);row.Tag=effect.id;row.ToolTipText="Arrastra al clip o a la cabecera de una pista";}
     folder.Expand();
    }root.Expand();
   }
   tree.EndUpdate();
  }
 }
}
