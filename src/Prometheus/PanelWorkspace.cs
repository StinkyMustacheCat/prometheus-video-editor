using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Prometheus {
 // UI layout is deliberately separate from Project and the timeline undo history.
 public class PanelWorkspace:Panel {
  readonly ToolTip shelfHints=new ToolTip();
  static class NativeMethods {
   [DllImport("user32.dll")]public static extern IntPtr SendMessage(IntPtr handle,int message,IntPtr wparam,IntPtr lparam);
   [DllImport("user32.dll")]public static extern bool RedrawWindow(IntPtr handle,IntPtr update,IntPtr region,uint flags);
  }
  public class PanelEntry {
   public string Id,Title,Icon;
   public Control Content;
   public Form Floating;
   public string State="closed";
   public Rectangle FloatingBounds;
  }
  public class LayoutNode {
   public string Id;
   public string Direction;
   public double Ratio=0.5;
   public LayoutNode First,Second;
  }
  public class LayoutSnapshot {
   public LayoutNode Root;
   public string Preset;
   public Dictionary<string,string> States=new Dictionary<string,string>();
   public Dictionary<string,int[]> FloatingBounds=new Dictionary<string,int[]>();
  }
  readonly Dictionary<string,PanelEntry> entries=new Dictionary<string,PanelEntry>();
  LayoutNode root;
  public string CurrentPreset {get;private set;}
  Control view;
  readonly Color surface=Color.FromArgb(27,28,31),header=Color.FromArgb(42,44,49);
  readonly Panel contentHost=new Panel{BackColor=Color.FromArgb(27,28,31)};
  readonly FlowLayoutPanel shelf=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,AutoScroll=true,Padding=new Padding(5,12,0,0)};
  const int EdgeGutter=8;
  readonly Dictionary<string,Panel> edgeTargets=new Dictionary<string,Panel>();
  readonly Dictionary<string,Control> dockHeaders=new Dictionary<string,Control>();
  readonly List<KeyValuePair<SplitContainer,LayoutNode>> dockDividers=new List<KeyValuePair<SplitContainer,LayoutNode>>();
  sealed class FloatingDockTarget {public string Kind,Id,Edge;public LayoutNode Divider;public Control Highlight;}
  Control activeFloatingHighlight;
  Button panelsButton,designsButton;ContextMenuStrip designMenu;
  public string LayoutPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","layout-v1.json");
  bool restoring,shuttingDown;
  public bool PersistenceEnabled=true;
  public string LastLayoutError;
  public Action<string> DropFeedback;
  public Action<string,ContextMenuStrip> PopulatePanelMenu;
  public Control PanelShelf {get{return shelf;}}
  public IEnumerable<PanelEntry> Entries {get{return entries.Values;}}
  public LayoutNode Root {get{return root;}}
  public PanelWorkspace(){BackColor=surface;Controls.Add(contentHost);CreateEdgeTargets();RefreshShelf();}
  void CreateEdgeTargets(){
   foreach(string edge in new[]{"izquierda","derecha","arriba","abajo"}){
    string zone=edge;var target=new Panel{BackColor=surface,AllowDrop=true};edgeTargets[zone]=target;Controls.Add(target);
    target.DragEnter+=(s,e)=>{if(e.Data.GetDataPresent("Prometheus.PanelId")){e.Effect=DragDropEffects.Move;target.BackColor=Color.FromArgb(255,156,44);}};
    target.DragOver+=(s,e)=>{if(e.Data.GetDataPresent("Prometheus.PanelId")){e.Effect=DragDropEffects.Move;if(DropFeedback!=null)DropFeedback("Soltar para acoplar al borde "+zone);}};
    target.DragLeave+=(s,e)=>target.BackColor=surface;
    target.DragDrop+=(s,e)=>{target.BackColor=surface;string id=e.Data.GetData("Prometheus.PanelId") as string;if(id!=null)DockAtEdge(id,zone);};
   }
   PositionEdgeTargets();
  }
  void PositionEdgeTargets(){
   if(edgeTargets.Count<4)return;
   int width=Math.Max(0,ClientSize.Width),height=Math.Max(0,ClientSize.Height),size=EdgeGutter;
   contentHost.SetBounds(size,size,Math.Max(0,width-size*2),Math.Max(0,height-size*2));
   edgeTargets["izquierda"].SetBounds(0,0,size,height);
   edgeTargets["derecha"].SetBounds(Math.Max(0,width-size),0,size,height);
   edgeTargets["arriba"].SetBounds(size,0,Math.Max(0,width-size*2),size);
   edgeTargets["abajo"].SetBounds(size,Math.Max(0,height-size),Math.Max(0,width-size*2),size);
  }
  protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);PositionEdgeTargets();}
  public void Register(string id,string title,string icon,Control content){
   if(entries.ContainsKey(id)||content==null)throw new ArgumentException("Panel repetido o vacío");
   entries.Add(id,new PanelEntry{Id=id,Title=title,Icon=icon,Content=content});
  }
  public void UseDefaultLayout(){
   foreach(var entry in entries.Values)Detach(entry);
   root=PresetTree("equilibrado");CurrentPreset="equilibrado";
   foreach(var entry in entries.Values)entry.State=Contains(root,entry.Id)?"docked":"closed";
   Rebuild();RefreshShelf();
  }
  static LayoutNode PresetTree(string preset){
   if(preset=="edicion-completa")return new LayoutNode{Direction="horizontal",Ratio=0.50,
    First=new LayoutNode{Direction="vertical",Ratio=0.18,
     First=new LayoutNode{Id="efectos"},Second=new LayoutNode{Direction="vertical",Ratio=0.245,
      First=new LayoutNode{Id="propiedades"},Second=new LayoutNode{Direction="vertical",Ratio=0.68,
       First=new LayoutNode{Id="monitor"},Second=new LayoutNode{Id="color"}}}},
    Second=new LayoutNode{Direction="vertical",Ratio=0.18,
     First=new LayoutNode{Id="medios"},Second=new LayoutNode{Direction="vertical",Ratio=0.81,
      First=new LayoutNode{Id="timeline"},Second=new LayoutNode{Id="mezclador"}}}};
   if(preset=="montaje")return new LayoutNode{Direction="vertical",Ratio=0.25,
    First=new LayoutNode{Direction="horizontal",Ratio=0.72,First=new LayoutNode{Id="medios"},Second=new LayoutNode{Id="propiedades"}},
    Second=new LayoutNode{Direction="horizontal",Ratio=0.55,First=new LayoutNode{Id="monitor"},Second=new LayoutNode{Id="timeline"}}};
   if(preset=="broll")return new LayoutNode{Direction="vertical",Ratio=0.28,
    First=new LayoutNode{Id="medios"},
    Second=new LayoutNode{Direction="horizontal",Ratio=0.53,
     First=new LayoutNode{Direction="vertical",Ratio=0.78,First=new LayoutNode{Id="monitor"},Second=new LayoutNode{Id="propiedades"}},
     Second=new LayoutNode{Id="timeline"}}};
   if(preset=="revision")return new LayoutNode{Direction="horizontal",Ratio=0.60,
    First=new LayoutNode{Direction="vertical",Ratio=0.78,First=new LayoutNode{Id="monitor"},Second=new LayoutNode{Id="propiedades"}},
    Second=new LayoutNode{Direction="vertical",Ratio=0.28,First=new LayoutNode{Id="medios"},Second=new LayoutNode{Id="timeline"}}};
   return new LayoutNode{Direction="horizontal",Ratio=0.50,
    First=new LayoutNode{Direction="vertical",Ratio=0.19,
     First=new LayoutNode{Id="medios"},
     Second=new LayoutNode{Direction="vertical",Ratio=0.75,
      First=new LayoutNode{Id="monitor"},Second=new LayoutNode{Id="propiedades"}}},
    Second=new LayoutNode{Id="timeline"}};
  }
  public void ApplyPreset(string preset){
   if(preset!="equilibrado"&&preset!="montaje"&&preset!="broll"&&preset!="revision"&&preset!="edicion-completa")throw new ArgumentException("Diseño desconocido");
   foreach(var entry in entries.Values)Detach(entry);
   root=PresetTree(preset);CurrentPreset=preset;
   foreach(var entry in entries.Values)entry.State=Contains(root,entry.Id)?"docked":"closed";
   Rebuild();RefreshShelf();SaveLayout();
  }
  static LayoutNode Remove(LayoutNode node,string id){
   if(node==null)return null;
   if(node.Id!=null)return node.Id==id?null:node;
   node.First=Remove(node.First,id);node.Second=Remove(node.Second,id);
   return node.First==null?node.Second:node.Second==null?node.First:node;
  }
  static bool Contains(LayoutNode node,string id){return node!=null&&(node.Id==id||Contains(node.First,id)||Contains(node.Second,id));}
  static bool HasNode(LayoutNode node,LayoutNode target){return node!=null&&(Object.ReferenceEquals(node,target)||HasNode(node.First,target)||HasNode(node.Second,target));}
  static string OtherLeaf(LayoutNode node,string excluded){if(node==null)return null;if(node.Id!=null)return node.Id==excluded?null:node.Id;return OtherLeaf(node.First,excluded)??OtherLeaf(node.Second,excluded);}
  static LayoutNode Insert(LayoutNode node,string target,string id,string edge){
   if(node.Id==target){
    if(edge=="centro"){node.Id=id;return node;}
    var fresh=new LayoutNode{Id=id};
    var split=new LayoutNode{Direction=edge=="izquierda"||edge=="derecha"?"vertical":"horizontal",Ratio=0.5};
    if(edge=="izquierda"||edge=="arriba"){split.First=fresh;split.Second=node;}
    else{split.First=node;split.Second=fresh;}
    return split;
   }
   if(node.First!=null)node.First=Insert(node.First,target,id,edge);
   if(node.Second!=null)node.Second=Insert(node.Second,target,id,edge);
   return node;
  }
  static LayoutNode Replace(LayoutNode node,string oldId,string newId){
   if(node==null)return null;
   if(node.Id==oldId){node.Id=newId;return node;}
   node.First=Replace(node.First,oldId,newId);node.Second=Replace(node.Second,oldId,newId);return node;
  }
  void Detach(PanelEntry entry){
   if(entry.Content.Parent!=null)entry.Content.Parent.Controls.Remove(entry.Content);
   if(entry.Floating!=null){var form=entry.Floating;entry.Floating=null;string previous=entry.State;entry.State="detaching";form.Close();form.Dispose();entry.State=previous;}
  }
  public void Minimize(string id){
   var entry=entries[id];
   bool docked=entry.State=="docked";
   if(docked)root=Remove(root,id);
   Detach(entry);entry.State="minimized";CurrentPreset="personalizado";
   if(docked)Rebuild();RefreshShelf();SaveLayout();
  }
  public void ClosePanel(string id){
   var entry=entries[id];
   if(entry.State=="docked")root=Remove(root,id);
   Detach(entry);entry.State="closed";CurrentPreset="personalizado";Rebuild();RefreshShelf();SaveLayout();
  }
  public void FloatPanel(string id){
   var entry=entries[id];
   if(entry.State=="floating"&&entry.Floating!=null){entry.Floating.Activate();return;}
   bool wasDocked=entry.State=="docked";
   if(wasDocked)root=Remove(root,id);
   Detach(entry);entry.State="floating";if(!restoring)CurrentPreset="personalizado";if(wasDocked)Rebuild();
   var form=new FloatingPanelWindow(entry.Title);
   if(entry.FloatingBounds.Width>100&&entry.FloatingBounds.Height>100)form.Bounds=entry.FloatingBounds;
   entry.Floating=form;
   var panel=BuildLeaf(entry);panel.Dock=DockStyle.Fill;form.ContentHost.Controls.Add(panel);
   form.Move+=(s,e)=>entry.FloatingBounds=form.Bounds;
   form.Resize+=(s,e)=>entry.FloatingBounds=form.Bounds;
   form.FormClosed+=(s,e)=>{if(entry.State=="floating"&&!shuttingDown){entry.Floating=null;entry.State="closed";RefreshShelf();SaveLayout();}};
   form.Show(FindForm());RefreshShelf();SaveLayout();
  }
  public void DockPanel(string id,string target,string edge){
   if(id==target||!entries.ContainsKey(id))return;
   var entry=entries[id];
   if(entry.State=="docked")root=Remove(root,id);
   Detach(entry);
   if(root==null)root=new LayoutNode{Id=id};
   else if(target!=null&&Contains(root,target)){
    if(edge=="centro"){
     var displaced=entries[target];Detach(displaced);displaced.State="minimized";
    }
    root=Insert(root,target,id,edge);
   }else root=new LayoutNode{Direction="vertical",Ratio=0.5,First=root,Second=new LayoutNode{Id=id}};
   entry.State="docked";CurrentPreset="personalizado";Rebuild();RefreshShelf();SaveLayout();
  }
  public void DockBetween(string id,LayoutNode divider){
   if(divider==null||divider.Id!=null||!entries.ContainsKey(id))return;
   string left=OtherLeaf(divider.First,id),right=OtherLeaf(divider.Second,id);
   if(left==null&&right==null)return;
   var entry=entries[id];
   if(entry.State=="docked")root=Remove(root,id);
   Detach(entry);
   if(HasNode(root,divider)&&divider.First!=null&&divider.Second!=null){
    var oldSecond=divider.Second;
    divider.Second=new LayoutNode{Direction=divider.Direction,Ratio=0.333,
     First=new LayoutNode{Id=id},Second=oldSecond};
    divider.Ratio=Math.Max(0.2,Math.Min(0.8,divider.Ratio*0.8));
   }else if(right!=null&&Contains(root,right))root=Insert(root,right,id,divider.Direction=="horizontal"?"arriba":"izquierda");
   else if(left!=null&&Contains(root,left))root=Insert(root,left,id,divider.Direction=="horizontal"?"abajo":"derecha");
   else root=root==null?new LayoutNode{Id=id}:new LayoutNode{Direction="vertical",Ratio=0.5,First=root,Second=new LayoutNode{Id=id}};
   entry.State="docked";CurrentPreset="personalizado";Rebuild();RefreshShelf();SaveLayout();
  }
  public void DockAtEdge(string id,string edge){
   if(!entries.ContainsKey(id)||!(new[]{"izquierda","derecha","arriba","abajo"}).Contains(edge))return;
   var entry=entries[id];if(entry.State=="docked")root=Remove(root,id);Detach(entry);
   var fresh=new LayoutNode{Id=id};
   if(root==null)root=fresh;
   else{
    bool first=edge=="izquierda"||edge=="arriba";
    root=new LayoutNode{Direction=edge=="izquierda"||edge=="derecha"?"vertical":"horizontal",Ratio=first?0.25:0.75,
     First=first?fresh:root,Second=first?root:fresh};
   }
   entry.State="docked";CurrentPreset="personalizado";Rebuild();RefreshShelf();SaveLayout();
  }
  public void ReplacePanel(string current,string replacement){
   if(current==replacement||!entries.ContainsKey(current)||!entries.ContainsKey(replacement))return;
   var original=entries[current];var next=entries[replacement];
   if(original.State!="docked")return;
   if(next.State=="docked")root=Remove(root,replacement);
   Detach(next);Detach(original);root=Replace(root,current,replacement);
   original.State="minimized";next.State="docked";CurrentPreset="personalizado";Rebuild();RefreshShelf();SaveLayout();
  }
  void RefreshShelf(){
   if(shelf.IsDisposed)return;
   shelf.SuspendLayout();
   foreach(Control old in shelf.Controls.Cast<Control>().Where(c=>c!=panelsButton&&c!=designsButton).ToList()){shelf.Controls.Remove(old);old.Dispose();}
   if(panelsButton!=null)shelf.Controls.Remove(panelsButton);
   if(designsButton!=null)shelf.Controls.Remove(designsButton);
   foreach(var entry in entries.Values.Where(p=>p.State=="minimized")){
    var button=new Button{Text=entry.Icon,Width=32,Height=30,Margin=new Padding(2,0,2,0),FlatStyle=FlatStyle.Flat,
     BackColor=Color.FromArgb(55,57,62),ForeColor=Color.Gainsboro};
    button.FlatAppearance.BorderColor=Color.FromArgb(85,88,94);
    shelfHints.SetToolTip(button,"Abrir "+entry.Title+" como panel flotante");
    button.Click+=(s,e)=>FloatPanel(entry.Id);shelf.Controls.Add(button);
   }
   if(panelsButton==null){panelsButton=new Button{Text="▦ Paneles",Width=94,Height=30,Margin=new Padding(8,0,2,0),FlatStyle=FlatStyle.Flat,
    BackColor=Color.FromArgb(55,57,62),ForeColor=Color.Gainsboro};panelsButton.Click+=(s,e)=>ShowPanelList(panelsButton);}
   if(designsButton==null){designsButton=new Button{Text="▤ Diseños",Width=90,Height=30,Margin=new Padding(2,0,2,0),FlatStyle=FlatStyle.Flat,
    BackColor=Color.FromArgb(55,57,62),ForeColor=Color.Gainsboro};designsButton.Click+=(s,e)=>ShowDesignMenu(designsButton);}
   shelf.Controls.Add(panelsButton);shelf.Controls.Add(designsButton);shelf.ResumeLayout();
  }
  void ShowMenuInside(ToolStripDropDown menu,Control anchor){
   var host=anchor.FindForm()??FindForm();
   var bounds=host==null?Screen.FromControl(anchor).WorkingArea:host.RectangleToScreen(host.ClientRectangle);
   menu.MaximumSize=new Size(bounds.Width,bounds.Height);
   var preferred=menu.GetPreferredSize(Size.Empty);
   var desired=anchor.PointToScreen(new Point(0,anchor.Height));
   int width=Math.Min(bounds.Width,Math.Max(1,preferred.Width)),height=Math.Min(bounds.Height,Math.Max(1,preferred.Height));
   int x=Math.Max(bounds.Left,Math.Min(bounds.Right-width,desired.X));
   int y=desired.Y+height<=bounds.Bottom?desired.Y:anchor.PointToScreen(Point.Empty).Y-height;
   y=Math.Max(bounds.Top,Math.Min(bounds.Bottom-height,y));
   menu.Show(new Point(x,y));
  }
  static Bitmap DesignThumbnail(string key){
   var image=new Bitmap(64,36);using(var g=Graphics.FromImage(image)){
    g.Clear(Color.FromArgb(24,25,28));
    Action<float,float,float,float,Color> box=(x,y,w,h,color)=>{using(var fill=new SolidBrush(color))g.FillRectangle(fill,2+x*60,2+y*32,w*60-1,h*32-1);};
    Color media=Color.FromArgb(206,144,83),monitor=Color.FromArgb(102,147,170),properties=Color.FromArgb(135,135,143),timeline=Color.FromArgb(216,158,88);
    if(key=="equilibrado"){box(0,0,.20f,.52f,media);box(.20f,0,.59f,.52f,monitor);box(.79f,0,.21f,.52f,properties);box(0,.52f,1,.48f,timeline);}
    else if(key=="montaje"){box(0,0,.25f,.72f,media);box(0,.72f,.25f,.28f,properties);box(.25f,0,.75f,.55f,monitor);box(.25f,.55f,.75f,.45f,timeline);}
    else if(key=="broll"){box(0,0,.28f,1,media);box(.28f,0,.56f,.53f,monitor);box(.84f,0,.16f,.53f,properties);box(.28f,.53f,.72f,.47f,timeline);}
    else if(key=="edicion-completa"){box(0,0,.18f,.50f,Color.FromArgb(132,113,150));box(.18f,0,.20f,.50f,properties);box(.38f,0,.42f,.50f,monitor);box(.80f,0,.20f,.50f,Color.FromArgb(112,150,138));box(0,.50f,.18f,.50f,media);box(.18f,.50f,.66f,.50f,timeline);box(.84f,.50f,.16f,.50f,Color.FromArgb(110,151,194));}
    else{box(0,0,.78f,.60f,monitor);box(.78f,0,.22f,.60f,properties);box(0,.60f,.28f,.40f,media);box(.28f,.60f,.72f,.40f,timeline);}
    using(var border=new Pen(Color.FromArgb(155,160,166)))g.DrawRectangle(border,1,1,61,33);
   }return image;
  }
  void ShowDesignMenu(Control anchor){
   if(designMenu!=null&&!designMenu.IsDisposed)designMenu.Close();
   var menu=new ContextMenuStrip{BackColor=header,ForeColor=Color.Gainsboro,ShowImageMargin=true};designMenu=menu;
   var images=new List<Bitmap>();
   var designs=new[]{new[]{"equilibrado","Equilibrado","Medios, monitor y controles de efectos arriba; línea de tiempo abajo"},new[]{"montaje","Montaje","Medios y controles de efectos a la izquierda; monitor y línea de tiempo a la derecha"},new[]{"broll","B-roll","Medios altos a la izquierda; monitor y línea de tiempo a la derecha"},new[]{"revision","Revisión","Monitor arriba; medios y línea de tiempo abajo"},new[]{"edicion-completa","Edición completa","Efectos, controles de efectos, monitor y color arriba; medios, línea de tiempo y mezclador abajo"}};
   foreach(var design in designs){string key=design[0];var item=(ToolStripMenuItem)menu.Items.Add(design[1],null,(s,e)=>ApplyPreset(key));
    var image=DesignThumbnail(key);images.Add(image);item.Image=image;item.ImageScaling=ToolStripItemImageScaling.None;item.Checked=CurrentPreset==key;item.ToolTipText=design[2];}
   menu.Closed+=(s,e)=>{if(Object.ReferenceEquals(designMenu,menu))designMenu=null;DisposeMenuLater(menu);};
   menu.Disposed+=(s,e)=>{foreach(var image in images)image.Dispose();};
   ShowMenuInside(menu,anchor);
  }
  void ShowPanelList(Control anchor){
   var panels=entries.Values.ToList();
   var menu=new ToolStripDropDown{AutoClose=true,BackColor=header};
   var list=new ListBox{Width=270,Height=Math.Max(36,Math.Min(300,panels.Count*28+4)),
    BackColor=header,ForeColor=Color.Gainsboro,BorderStyle=BorderStyle.None,IntegralHeight=false};
   foreach(var entry in panels)list.Items.Add(entry.Icon+"  "+entry.Title+"  · "+(entry.State=="docked"?"acoplado":entry.State=="floating"?"flotante":entry.State=="minimized"?"minimizado":"cerrado"));
   list.DoubleClick+=(s,e)=>{if(list.SelectedIndex<0||list.SelectedIndex>=panels.Count)return;
    var entry=panels[list.SelectedIndex];menu.Close();if(entry.State=="docked"){entry.Content.Focus();entry.Content.Invalidate();}
    else if(entry.State=="floating"&&entry.Floating!=null)entry.Floating.Activate();else FloatPanel(entry.Id);};
   menu.Items.Add(new ToolStripControlHost(list){AutoSize=false,Size=list.Size,Margin=Padding.Empty,Padding=Padding.Empty});
   menu.Closed+=(s,e)=>DisposeMenuLater(menu);ShowMenuInside(menu,anchor);
  }
  void ShowPanelMenu(PanelEntry entry,Control anchor){
   var menu=new ContextMenuStrip();
   if(PopulatePanelMenu!=null)PopulatePanelMenu(entry.Id,menu);
   menu.Items.Add("Acoplar al área de trabajo",null,(s,e)=>DockPanel(entry.Id,null,"derecha")).Enabled=entry.State!="docked";
   var borders=new ToolStripMenuItem("Acoplar en borde");
   foreach(string edge in new[]{"izquierda","derecha","arriba","abajo"}){string zone=edge;borders.DropDownItems.Add(Char.ToUpper(edge[0])+edge.Substring(1),null,(s,e)=>DockAtEdge(entry.Id,zone));}
   menu.Items.Add(borders);
   menu.Items.Add("Flotar",null,(s,e)=>FloatPanel(entry.Id));
   menu.Items.Add("Minimizar",null,(s,e)=>Minimize(entry.Id));
   var substitute=new ToolStripMenuItem("Sustituir panel");
   foreach(var candidate in entries.Values.Where(p=>p.Id!=entry.Id)){
    var target=candidate;substitute.DropDownItems.Add(target.Icon+"  "+target.Title,null,(s,e)=>ReplacePanel(entry.Id,target.Id));
   }
   substitute.Enabled=entry.State=="docked";menu.Items.Add(substitute);
   menu.Items.Add("Cerrar",null,(s,e)=>ClosePanel(entry.Id));
   var host=anchor.FindForm();if(host!=null&&anchor.PointToScreen(Point.Empty).X>host.RectangleToScreen(host.ClientRectangle).Left+host.ClientSize.Width/2){
    substitute.DropDownDirection=ToolStripDropDownDirection.Left;borders.DropDownDirection=ToolStripDropDownDirection.Left;}
   menu.Closed+=(s,e)=>DisposeMenuLater(menu);ShowMenuInside(menu,anchor);
  }
  void DisposeMenuLater(IDisposable menu){
   if(IsHandleCreated&&!IsDisposed)BeginInvoke((Action)(()=>menu.Dispose()));
   else menu.Dispose();
  }
  void DragHeader(PanelEntry entry,Control bar,MouseEventArgs down){
   if(down.Button!=MouseButtons.Left)return;
   if(entry.State=="floating"&&entry.Floating!=null){DragFloatingHeader(entry,bar,down.Location);return;}
   Point origin=down.Location;
   MouseEventHandler move=null,up=null;
   up=(s,e)=>{bar.MouseMove-=move;bar.MouseUp-=up;};bar.MouseUp+=up;
   move=(s,e)=>{if(e.Button!=MouseButtons.Left){bar.MouseMove-=move;return;}
    if(Math.Abs(e.X-origin.X)+Math.Abs(e.Y-origin.Y)<SystemInformation.DragSize.Width/2)return;
    bar.MouseMove-=move;bar.MouseUp-=up;var data=new DataObject();data.SetData("Prometheus.PanelId",entry.Id);
    var effect=bar.DoDragDrop(data,DragDropEffects.Move);
    if(effect==DragDropEffects.None&&entry.State=="docked")FloatPanel(entry.Id);
   };
   bar.MouseMove+=move;
  }
  FloatingDockTarget FindFloatingDockTarget(Point screen){
   if(!Visible||!RectangleToScreen(ClientRectangle).Contains(screen))return null;
   foreach(var edge in edgeTargets){
    if(edge.Value.Visible&&edge.Value.RectangleToScreen(edge.Value.ClientRectangle).Contains(screen))
     return new FloatingDockTarget{Kind="edge",Edge=edge.Key,Highlight=edge.Value};
   }
   foreach(var pair in dockHeaders){
    var control=pair.Value;
    if(control.IsDisposed||!control.Visible||!control.RectangleToScreen(control.ClientRectangle).Contains(screen))continue;
    Point point=control.PointToClient(screen);
    string zone=point.X<control.Width*0.18?"izquierda":point.X>control.Width*0.82?"derecha":point.Y<6?"arriba":point.Y>control.Height-6?"abajo":"centro";
    return new FloatingDockTarget{Kind="panel",Id=pair.Key,Edge=zone,Highlight=control};
   }
   foreach(var pair in dockDividers){
    var split=pair.Key;
    if(split.IsDisposed||!split.Visible)continue;
    Point point=split.PointToClient(screen);
    if(!split.ClientRectangle.Contains(point))continue;
    int center=split.SplitterDistance+split.SplitterWidth/2;
    bool over=split.Orientation==Orientation.Vertical?Math.Abs(point.X-center)<=8:Math.Abs(point.Y-center)<=8;
    if(over)return new FloatingDockTarget{Kind="divider",Divider=pair.Value,Highlight=split};
   }
   return null;
  }
  void HighlightFloatingTarget(FloatingDockTarget target){
   Control next=target==null?null:target.Highlight;
   if(activeFloatingHighlight!=null&&activeFloatingHighlight!=next&&!activeFloatingHighlight.IsDisposed)
    activeFloatingHighlight.BackColor=edgeTargets.Values.Contains(activeFloatingHighlight)?surface:activeFloatingHighlight is SplitContainer?surface:header;
   activeFloatingHighlight=next;
   if(next!=null&&!next.IsDisposed)next.BackColor=target.Kind=="panel"?Color.FromArgb(130,82,37):Color.FromArgb(255,156,44);
   if(target!=null&&DropFeedback!=null)DropFeedback(target.Kind=="edge"?"Soltar para acoplar al borde "+target.Edge:
    target.Kind=="divider"?"Soltar para colocar el panel entre espacios":"Soltar para acoplar sobre otro panel");
  }
  void DragFloatingHeader(PanelEntry entry,Control bar,Point origin){
   var form=entry.Floating;
   bool dragging=false;
   MouseEventHandler move=null,up=null;
   move=(s,e)=>{
    Point current=bar.PointToScreen(e.Location);
    int dx=e.X-origin.X,dy=e.Y-origin.Y;
    if(!dragging){if(Math.Abs(dx)+Math.Abs(dy)<SystemInformation.DragSize.Width/2)return;dragging=true;}
    if(form.IsDisposed||entry.State!="floating")return;
    form.Location=new Point(form.Left+dx,form.Top+dy);
    HighlightFloatingTarget(FindFloatingDockTarget(current));
   };
   up=(s,e)=>{
    bar.MouseMove-=move;bar.MouseUp-=up;
    Point current=bar.PointToScreen(e.Location);
    var target=dragging?FindFloatingDockTarget(current):null;
    HighlightFloatingTarget(null);
    if(target==null||entry.State!="floating")return;
    if(target.Kind=="edge")DockAtEdge(entry.Id,target.Edge);
    else if(target.Kind=="divider")DockBetween(entry.Id,target.Divider);
    else DockPanel(entry.Id,target.Id,target.Edge);
   };
   bar.MouseMove+=move;bar.MouseUp+=up;
  }
  void AttachDockTarget(PanelEntry entry,Control bar){
   bar.AllowDrop=true;
   bar.DragEnter+=(s,e)=>{if(e.Data.GetDataPresent("Prometheus.PanelId"))e.Effect=DragDropEffects.Move;};
   bar.DragOver+=(s,e)=>{if(e.Data.GetDataPresent("Prometheus.PanelId")){e.Effect=DragDropEffects.Move;bar.BackColor=Color.FromArgb(130,82,37);if(DropFeedback!=null)DropFeedback("Soltar sobre "+entry.Title+" para acoplar o sustituir");}};
   bar.DragLeave+=(s,e)=>bar.BackColor=header;
   bar.DragDrop+=(s,e)=>{bar.BackColor=header;var id=e.Data.GetData("Prometheus.PanelId") as string;
    if(id==null)return;var p=bar.PointToClient(new Point(e.X,e.Y));
    string zone=p.X<bar.Width*0.18?"izquierda":p.X>bar.Width*0.82?"derecha":p.Y<6?"arriba":p.Y>bar.Height-6?"abajo":"centro";
    DockPanel(id,entry.Id,zone);};
  }
  public void SaveLayout(){
   if(restoring||!PersistenceEnabled)return;
   try{
    var snap=new LayoutSnapshot{Root=root,Preset=CurrentPreset};
    foreach(var p in entries.Values){snap.States[p.Id]=p.State;if(p.FloatingBounds.Width>0)snap.FloatingBounds[p.Id]=new[]{p.FloatingBounds.X,p.FloatingBounds.Y,p.FloatingBounds.Width,p.FloatingBounds.Height};}
    Directory.CreateDirectory(Path.GetDirectoryName(LayoutPath));
    string temp=LayoutPath+".tmp";File.WriteAllText(temp,Store.Json().Serialize(Store.Fields(snap)));
    if(File.Exists(LayoutPath))File.Replace(temp,LayoutPath,null);else File.Move(temp,LayoutPath);
    LastLayoutError=null;
   }catch(Exception ex){LastLayoutError=ex.ToString();}
  }
  public void LoadLayout(){
   if(!PersistenceEnabled||!File.Exists(LayoutPath))return;
   try{
    var snap=Store.Json().Deserialize<LayoutSnapshot>(File.ReadAllText(LayoutPath));
    if(snap==null||snap.Root==null)return;
    var ids=new HashSet<string>();Action<LayoutNode> validate=null;
    validate=n=>{if(n==null)throw new Exception("Panel ausente");if(n.Id!=null){if(!entries.ContainsKey(n.Id)||!ids.Add(n.Id))throw new Exception("Panel inválido");return;}if(n.Direction!="horizontal"&&n.Direction!="vertical")throw new Exception("Disposición inválida");validate(n.First);validate(n.Second);};
    validate(snap.Root);restoring=true;root=snap.Root;CurrentPreset=String.IsNullOrEmpty(snap.Preset)?"personalizado":snap.Preset;
    foreach(var entry in entries.Values){string state;
     entry.State=ids.Contains(entry.Id)?"docked":snap.States!=null&&snap.States.TryGetValue(entry.Id,out state)?state:"closed";
     int[] bounds;if(snap.FloatingBounds!=null&&snap.FloatingBounds.TryGetValue(entry.Id,out bounds)&&bounds.Length==4)entry.FloatingBounds=new Rectangle(bounds[0],bounds[1],bounds[2],bounds[3]);
    }
    Rebuild();
    foreach(var entry in entries.Values.Where(p=>p.State=="floating").ToList())FloatPanel(entry.Id);
    restoring=false;RefreshShelf();
   }catch{restoring=false;UseDefaultLayout();}
  }
  public void Shutdown(){shuttingDown=true;SaveLayout();}
  protected override void Dispose(bool disposing){if(disposing)shelfHints.Dispose();base.Dispose(disposing);}
  public void Rebuild(){
   contentHost.SuspendLayout();
   bool redraw=contentHost.IsHandleCreated;
   if(redraw)NativeMethods.SendMessage(contentHost.Handle,11,IntPtr.Zero,IntPtr.Zero);
   try{
    HighlightFloatingTarget(null);dockHeaders.Clear();dockDividers.Clear();
    foreach(var entry in entries.Values)if(entry.Floating==null&&entry.Content.Parent!=null)
     entry.Content.Parent.Controls.Remove(entry.Content);
    if(view!=null){contentHost.Controls.Remove(view);view.Dispose();view=null;}
    if(root!=null){view=Build(root);view.Dock=DockStyle.Fill;contentHost.Controls.Add(view);}
    foreach(var target in edgeTargets.Values)target.BringToFront();PositionEdgeTargets();
   }finally{
    contentHost.ResumeLayout(true);
    if(redraw){NativeMethods.SendMessage(contentHost.Handle,11,new IntPtr(1),IntPtr.Zero);NativeMethods.RedrawWindow(contentHost.Handle,IntPtr.Zero,IntPtr.Zero,0x0001|0x0004|0x0080|0x0100);}
    else contentHost.Invalidate(true);
   }
  }
  Control Build(LayoutNode node){
   if(node.Id!=null){
    PanelEntry entry;
    if(!entries.TryGetValue(node.Id,out entry))return new Panel{BackColor=surface};
    return BuildLeaf(entry);
   }
   var split=new SplitContainer{Dock=DockStyle.Fill,BackColor=surface,SplitterWidth=5,
    Orientation=node.Direction=="horizontal"?Orientation.Horizontal:Orientation.Vertical};
   dockDividers.Add(new KeyValuePair<SplitContainer,LayoutNode>(split,node));
   split.SplitterWidth=10;split.AllowDrop=true;
   split.DragEnter+=(s,e)=>{if(e.Data.GetDataPresent("Prometheus.PanelId"))e.Effect=DragDropEffects.Move;};
   split.DragOver+=(s,e)=>{if(!e.Data.GetDataPresent("Prometheus.PanelId"))return;
    var point=split.PointToClient(new Point(e.X,e.Y));int at=split.SplitterDistance;
    bool over=split.Orientation==Orientation.Vertical?Math.Abs(point.X-at-split.SplitterWidth/2)<=8:Math.Abs(point.Y-at-split.SplitterWidth/2)<=8;
    e.Effect=over?DragDropEffects.Move:DragDropEffects.None;
    split.BackColor=over?Color.FromArgb(255,156,44):surface;
    if(over&&DropFeedback!=null)DropFeedback("Soltar aquí para colocar el panel entre los dos espacios");};
   split.DragLeave+=(s,e)=>split.BackColor=surface;
   split.DragDrop+=(s,e)=>{split.BackColor=surface;string id=e.Data.GetData("Prometheus.PanelId") as string;
    if(id!=null)DockBetween(id,node);};
   split.Panel1MinSize=80;split.Panel2MinSize=80;
   split.Panel1.Controls.Add(Build(node.First));split.Panel2.Controls.Add(Build(node.Second));
   split.Panel1.Controls[0].Dock=DockStyle.Fill;split.Panel2.Controls[0].Dock=DockStyle.Fill;
   bool ready=false,userMoved=false;
   split.SizeChanged+=(s,e)=>{if(ready)return;int length=split.Orientation==Orientation.Horizontal?split.Height:split.Width;
    if(length<200)return;int max=length-split.Panel2MinSize-split.SplitterWidth;
    split.SplitterDistance=Math.Max(split.Panel1MinSize,Math.Min(max,(int)(length*node.Ratio)));ready=true;};
   split.SplitterMoving+=(s,e)=>userMoved=true;
   split.SplitterMoved+=(s,e)=>{if(!ready||!userMoved)return;userMoved=false;int length=split.Orientation==Orientation.Horizontal?split.Height:split.Width;
    if(length>0)node.Ratio=(double)split.SplitterDistance/length;CurrentPreset="personalizado";SaveLayout();};
   return split;
  }
  Control BuildLeaf(PanelEntry entry){
    var holder=new Panel{BackColor=surface,Margin=new Padding(2)};
    var bar=new Panel{Dock=DockStyle.Top,Height=29,BackColor=header,Padding=new Padding(6,3,4,3)};
    var title=new Label{Dock=DockStyle.Fill,Text=entry.Icon+"  "+entry.Title,ForeColor=Color.Gainsboro,TextAlign=ContentAlignment.MiddleLeft};
    var button=new Button{Text="☰",Dock=DockStyle.Right,Width=29,FlatStyle=FlatStyle.Flat,
     BackColor=header,ForeColor=Color.Gainsboro};
    button.FlatAppearance.BorderSize=0;
    button.Click+=(s,e)=>ShowPanelMenu(entry,button);
    var actions=new Panel{Dock=DockStyle.Right,Width=entry.State=="floating"?87:29,BackColor=header};
    button.Dock=DockStyle.Left;
    if(entry.State=="floating"){
     var minimize=new Button{Text="−",Width=29,FlatStyle=FlatStyle.Flat,
      BackColor=header,ForeColor=Color.Gainsboro,TabStop=false};
     minimize.FlatAppearance.BorderSize=0;minimize.Dock=DockStyle.Left;
     minimize.Click+=(s,e)=>Minimize(entry.Id);actions.Controls.Add(minimize);
     var close=new Button{Text="×",Width=29,FlatStyle=FlatStyle.Flat,
      BackColor=header,ForeColor=Color.Gainsboro,TabStop=false};
     close.FlatAppearance.BorderSize=0;close.FlatAppearance.MouseOverBackColor=Color.FromArgb(160,55,49);
     close.Dock=DockStyle.Left;
     close.Click+=(s,e)=>ClosePanel(entry.Id);actions.Controls.Add(close);
    }else dockHeaders[entry.Id]=bar;
    actions.Controls.Add(button);
    bar.Controls.Add(title);bar.Controls.Add(actions);
    title.MouseDown+=(s,e)=>DragHeader(entry,title,e);
    AttachDockTarget(entry,bar);AttachDockTarget(entry,title);
    entry.Content.Dock=DockStyle.Fill;
    holder.Controls.Add(entry.Content);
    holder.Controls.Add(bar);
    PanelScrollbarOverlay.AddFor(holder,entry.Content);
    return holder;
  }
 }
}
