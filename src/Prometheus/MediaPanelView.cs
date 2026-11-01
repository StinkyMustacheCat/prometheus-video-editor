using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public sealed partial class MediaPanelView:Panel {
  sealed class Row {public Asset asset;public string pendingId,label;}
  readonly List<Asset> assets=new List<Asset>();
  readonly List<MediaFolder> folders=new List<MediaFolder>();
  readonly List<Row> rows=new List<Row>(),pending=new List<Row>();
  readonly ListBox library=new ListBox();
  readonly TreeView folderTree=new TreeView();
  readonly Panel folderPane=new Panel();
  readonly TextBox search=new TextBox();
  readonly Button filterButton=new Button();
  readonly Panel filterPopup=new Panel();
  FilterDismissal filterDismissal;
  Form filterHost;
  readonly Panel listHeader=new Panel();
  readonly TableLayoutPanel viewPane=new TableLayoutPanel();
  readonly System.Windows.Forms.Timer viewHold=new System.Windows.Forms.Timer();
  readonly ContextMenuStrip viewMenu=new ContextMenuStrip();
  Button viewButton,foldersButton,shelfButton;
  int shelfCount;
  bool viewHoldOpened;
  readonly int[] viewSizes={58,164,26};
  readonly ContextMenuStrip menu=new ContextMenuStrip(),folderMenu=new ContextMenuStrip(),addMenu=new ContextMenuStrip(),resolveMenu=new ContextMenuStrip();
  readonly ToolTip hints=new ToolTip();
  Button addButton,resolveButton;
  int viewMode; // 0: miniaturas y detalles, 1: cuadrícula, 2: lista compacta
  string selectedFilter="Todos",sortColumn="Nombre";
  bool ascending=true,updatingTree;
  Point dragStart;
  public Action ImportRequested,ImportLinkRequested,ShelfRequested,ResolveUsedRequested,ResolveProjectRequested,RedownloadAllRequested,InsertRequested,SelectionChanged,ReconnectFolderRequested,FolderCreateRequested,CreateSolidRequested,CreatePlaceholderRequested,CreateAudioPlaceholderRequested,CreateTextRequested;
  public Action<string> FolderRenameRequested,FolderDeleteRequested,RemoveAssetRequested,OpenAssetFolderRequested,OpenAssetPageRequested,CopyAssetUrlRequested,RelinkAssetRequested,RetryPendingRequested,DiscardPendingRequested,ResolveAssetRequested,RedownloadAssetRequested,ViewAssetInfoRequested,EditAssetRequested;
  public Action<string,string> MoveAssetRequested;
  public Action<string[]> FilesDropped;
  public Action<WebDropInfo> WebDropped;
  public Action<Exception> ErrorOccurred;
  public Func<bool> CanAcceptFiles;
  public ListBox Library{get{return library;}}
  public int SelectedIndex{get{return library.SelectedIndex;}}
  public string SelectedAssetId{get{int i=SelectedIndex;return i>=0&&i<rows.Count&&rows[i].asset!=null?rows[i].asset.id:null;}}

  public MediaPanelView(){
   Dock=DockStyle.Fill;BackColor=Color.FromArgb(36,38,42);
   var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,BackColor=BackColor};
   layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));
   var header=new Panel{Dock=DockStyle.Fill};
   var searchRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Padding=new Padding(4,4,4,4)};
   searchRow.RowStyles.Add(new RowStyle(SizeType.Percent,100));
   searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,30));searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,28));
   filterButton.Dock=DockStyle.Fill;filterButton.Margin=Padding.Empty;filterButton.FlatStyle=FlatStyle.Flat;filterButton.BackColor=Color.FromArgb(49,51,56);filterButton.ForeColor=Color.FromArgb(223,225,228);filterButton.FlatAppearance.BorderColor=Color.FromArgb(66,68,74);filterButton.Paint+=DrawFilterButton;filterButton.Click+=(s,e)=>ShowFilterMenu();hints.SetToolTip(filterButton,"Filtrar");
   foldersButton=Button("",()=>folderPane.Visible=!folderPane.Visible);foldersButton.Dock=DockStyle.Fill;foldersButton.Margin=new Padding(0,0,2,0);foldersButton.Paint+=(s,e)=>DrawToolIcon(e.Graphics,foldersButton.ClientRectangle,"folders");hints.SetToolTip(foldersButton,"Mostrar u ocultar carpetas");
   var searchWrap=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(2,0,2,0),BackColor=Color.FromArgb(48,50,55)};
   searchWrap.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,26));searchWrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
   var magnifier=new Panel{Dock=DockStyle.Fill};magnifier.Paint+=(s,e)=>{e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using(var pen=new Pen(Color.FromArgb(220,225,230),1.7f)){e.Graphics.DrawEllipse(pen,5,7,10,10);e.Graphics.DrawLine(pen,14,16,20,22);}};hints.SetToolTip(magnifier,"Buscar medios por nombre");
   search.Dock=DockStyle.Fill;search.BorderStyle=BorderStyle.None;search.Margin=new Padding(0,6,3,0);search.BackColor=searchWrap.BackColor;search.ForeColor=Color.FromArgb(235,236,238);search.TextChanged+=(s,e)=>Rebuild();
   search.Enter+=(s,e)=>{if(search.Text=="Buscar…"){search.Text="";search.ForeColor=Color.FromArgb(235,236,238);}};
   search.Leave+=(s,e)=>{if(search.Text.Length==0){search.Text="Buscar…";search.ForeColor=Color.FromArgb(133,138,145);}};
   search.Text="Buscar…";search.ForeColor=Color.FromArgb(133,138,145);
   searchWrap.Controls.Add(magnifier,0,0);searchWrap.Controls.Add(search,1,0);searchRow.Controls.Add(foldersButton,0,0);searchRow.Controls.Add(searchWrap,1,0);searchRow.Controls.Add(filterButton,2,0);header.Controls.Add(searchRow);layout.Controls.Add(header,0,0);

   var content=new Panel{Dock=DockStyle.Fill};
   folderPane.Dock=DockStyle.Left;folderPane.Width=78;folderPane.BackColor=Color.FromArgb(31,33,37);content.Resize+=(s,e)=>folderPane.Width=Math.Min(78,Math.Max(48,content.Width/3));
   folderTree.Dock=DockStyle.Fill;folderTree.BorderStyle=BorderStyle.None;folderTree.HideSelection=false;folderTree.BackColor=folderPane.BackColor;folderTree.ForeColor=Color.FromArgb(225,226,228);folderTree.AfterSelect+=(s,e)=>{if(!updatingTree)Rebuild();};
   folderTree.NodeMouseClick+=(s,e)=>{folderTree.SelectedNode=e.Node;if(e.Button==MouseButtons.Right)ShowFolderMenu(e.Node,e.Location);};
   folderTree.AllowDrop=true;folderTree.DragEnter+=(s,e)=>e.Effect=e.Data.GetDataPresent("Prometheus.AssetId")?DragDropEffects.Move:DragDropEffects.None;
   folderTree.DragOver+=(s,e)=>{var node=folderTree.GetNodeAt(folderTree.PointToClient(new Point(e.X,e.Y)));e.Effect=e.Data.GetDataPresent("Prometheus.AssetId")&&node!=null&&node.Tag!=null&&(string)node.Tag!="all"?DragDropEffects.Move:DragDropEffects.None;};
   folderTree.DragDrop+=(s,e)=>{var node=folderTree.GetNodeAt(folderTree.PointToClient(new Point(e.X,e.Y)));string id=e.Data.GetData("Prometheus.AssetId") as string;if(node!=null&&id!=null&&(string)node.Tag!="all"&&MoveAssetRequested!=null)Run(()=>MoveAssetRequested(id,(string)node.Tag=="root"?null:(string)node.Tag));};folderPane.Controls.Add(folderTree);
   library.Dock=DockStyle.Fill;library.BackColor=BackColor;library.ForeColor=Color.FromArgb(223,225,228);library.BorderStyle=BorderStyle.None;library.IntegralHeight=false;library.DrawMode=DrawMode.OwnerDrawFixed;library.ItemHeight=58;
   library.HandleCreated+=(s,e)=>QueueVisibleThumbnails();library.Resize+=(s,e)=>QueueVisibleThumbnails();
   library.DrawItem+=DrawRow;library.SelectedIndexChanged+=(s,e)=>Run(SelectionChanged);library.DoubleClick+=(s,e)=>Run(InsertRequested);
   library.MouseDown+=(s,e)=>{dragStart=e.Location;if(e.Button==MouseButtons.Right){int i=library.IndexFromPoint(e.Location);if(i>=0&&i<rows.Count)library.SelectedIndex=i;ShowLibraryMenu(i,e.Location);}};
   library.MouseMove+=(s,e)=>{if(e.Button!=MouseButtons.Left){int index=library.IndexFromPoint(e.Location);hints.SetToolTip(library,index>=0&&index<rows.Count?rows[index].label:"");return;}if(SelectedAssetId==null||(CanAcceptFiles!=null&&!CanAcceptFiles()))return;var threshold=SystemInformation.DragSize;if(new Rectangle(dragStart.X-threshold.Width/2,dragStart.Y-threshold.Height/2,threshold.Width,threshold.Height).Contains(e.Location))return;var data=new DataObject();data.SetData("Prometheus.AssetId",SelectedAssetId);library.DoDragDrop(data,DragDropEffects.Copy|DragDropEffects.Move);};
   listHeader.Dock=DockStyle.Top;listHeader.Height=27;listHeader.BackColor=Color.FromArgb(43,46,51);listHeader.ForeColor=Color.FromArgb(223,225,228);listHeader.Visible=false;
   listHeader.Paint+=DrawListHeader;listHeader.MouseDown+=(s,e)=>SortListAt(e.X);
   viewPane.Dock=DockStyle.Fill;viewPane.ColumnCount=1;viewPane.RowCount=2;viewPane.Margin=Padding.Empty;viewPane.Padding=Padding.Empty;viewPane.RowStyles.Add(new RowStyle(SizeType.Absolute,0));viewPane.RowStyles.Add(new RowStyle(SizeType.Percent,100));viewPane.Controls.Add(listHeader,0,0);viewPane.Controls.Add(library,0,1);
   content.Controls.Add(viewPane);content.Controls.Add(folderPane);folderPane.Visible=false;layout.Controls.Add(content,0,1);

   var toolbar=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Padding=new Padding(1,2,1,0)};
   addButton=Tool(toolbar,"plus","Añadir o crear medio",ShowAddMenu);
   viewButton=Tool(toolbar,"grid","Alternar miniaturas, cuadrícula y lista · mantener para elegir · derecho para tamaño",()=>{});
   viewButton.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left){viewHoldOpened=false;viewHold.Start();}else if(e.Button==MouseButtons.Right)ShowViewSizeMenu();};
   viewButton.MouseUp+=(s,e)=>{if(e.Button!=MouseButtons.Left)return;viewHold.Stop();if(!viewHoldOpened&&viewButton.ClientRectangle.Contains(e.Location))ToggleView();};
   viewHold.Interval=430;viewHold.Tick+=(s,e)=>{viewHold.Stop();if(Control.MouseButtons==MouseButtons.Left){viewHoldOpened=true;ShowViewMenu();}};
   shelfButton=Tool(toolbar,"shelf","Bandeja de medios",()=>Run(ShelfRequested));
   resolveButton=Tool(toolbar,"resolve","Resolver y redescargar medios web",ShowResolveMenu);
   Tool(toolbar,"search","Reconectar carpeta",()=>Run(ReconnectFolderRequested));
   layout.Controls.Add(toolbar,0,2);Controls.Add(layout);
   filterPopup.Visible=false;filterPopup.BackColor=Color.FromArgb(42,44,49);filterPopup.BorderStyle=BorderStyle.FixedSingle;filterPopup.AutoScroll=true;Controls.Add(filterPopup);
   RebuildFolderTree();AcceptFiles(this);AcceptFiles(header);AcceptFiles(searchRow);AcceptFiles(library);
  }
  Button Button(string title,Action action){var b=new Button{Text=title,Height=29,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(49,51,56),ForeColor=Color.FromArgb(223,225,228),Margin=new Padding(2,0,2,0)};b.FlatAppearance.BorderColor=Color.FromArgb(66,68,74);b.Click+=(s,e)=>Run(action);return b;}
  Button Tool(Control target,string kind,string tip,Action action){var b=Button("",action);b.Width=29;b.Height=27;b.Paint+=(s,e)=>{DrawToolIcon(e.Graphics,new Rectangle(0,0,b.Width,b.Height),kind);if(kind=="shelf"&&shelfCount>0){using(var fill=new SolidBrush(Color.FromArgb(229,118,61)))e.Graphics.FillEllipse(fill,17,1,11,11);using(var font=new Font("Segoe UI",6,FontStyle.Bold))TextRenderer.DrawText(e.Graphics,shelfCount>9?"9+":shelfCount.ToString(),font,new Rectangle(16,0,13,12),Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);}};target.Controls.Add(b);hints.SetToolTip(b,tip);return b;}
  public void SetShelfCount(int count){shelfCount=Math.Max(0,count);if(shelfButton!=null)shelfButton.Invalidate();}
  static void DrawToolIcon(Graphics g,Rectangle r,string kind){
   g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using(var pen=new Pen(Color.FromArgb(230,231,233),1.7f)){
    if(kind=="plus"){g.DrawLine(pen,14,5,14,22);g.DrawLine(pen,6,13,22,13);}
    else if(kind=="folders"){g.DrawRectangle(pen,6,8,17,13);g.DrawLine(pen,6,8,11,8);g.DrawLine(pen,11,8,13,6);g.DrawLine(pen,13,6,21,6);g.DrawLine(pen,21,6,23,8);}
    else if(kind=="grid"){foreach(int x in new[]{8,18})foreach(int y in new[]{6,16})g.DrawRectangle(pen,x,y,7,7);}
    else if(kind=="shelf"){g.DrawLine(pen,5,10,23,10);g.DrawLine(pen,5,10,8,21);g.DrawLine(pen,23,10,20,21);g.DrawLine(pen,8,21,20,21);g.DrawLine(pen,8,16,20,16);}
    else if(kind=="resolve"){g.DrawArc(pen,8,6,17,17,35,290);g.DrawLine(pen,23,7,26,12);g.DrawLine(pen,23,7,19,10);}
    else if(kind=="search"){g.DrawEllipse(pen,8,6,13,13);g.DrawLine(pen,19,18,26,25);}
   }
  }
  void Run(Action action){try{if(action!=null)action();}catch(Exception ex){if(ErrorOccurred!=null)ErrorOccurred(ex);else throw;}}
  void DrawFilterButton(object sender,PaintEventArgs e){e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using(var pen=new Pen(Color.FromArgb(226,230,234),1.6f)){e.Graphics.DrawLine(pen,6,7,21,7);e.Graphics.DrawLine(pen,6,7,12,14);e.Graphics.DrawLine(pen,21,7,15,14);e.Graphics.DrawLine(pen,12,14,12,21);e.Graphics.DrawLine(pen,12,21,16,18);}}
  void ShowAddMenu(){addMenu.Close();addMenu.Items.Clear();addMenu.Items.Add("Importar archivos…",null,(s,e)=>Run(ImportRequested));addMenu.Items.Add("Importar enlace…",null,(s,e)=>Run(ImportLinkRequested));addMenu.Items.Add(new ToolStripSeparator());addMenu.Items.Add("Crear texto…",null,(s,e)=>Run(CreateTextRequested));addMenu.Items.Add("Crear color sólido…",null,(s,e)=>Run(CreateSolidRequested));addMenu.Items.Add("Crear espacio reservado…",null,(s,e)=>Run(CreatePlaceholderRequested));addMenu.Items.Add("Crear audio reservado…",null,(s,e)=>Run(CreateAudioPlaceholderRequested));if(addButton!=null)addMenu.Show(addButton,new Point(0,-addMenu.GetPreferredSize(Size.Empty).Height));}
  void ShowResolveMenu(){resolveMenu.Close();resolveMenu.Items.Clear();resolveMenu.Items.Add("Resolver medios web de la línea de tiempo",null,(s,e)=>Run(ResolveUsedRequested));resolveMenu.Items.Add("Resolver medios web del proyecto",null,(s,e)=>Run(ResolveProjectRequested));resolveMenu.Items.Add(new ToolStripSeparator());resolveMenu.Items.Add("Redescargar todos los medios web",null,(s,e)=>Run(RedownloadAllRequested));if(resolveButton!=null)resolveMenu.Show(resolveButton,new Point(0,-resolveMenu.GetPreferredSize(Size.Empty).Height));}
  sealed class FilterDismissal:IMessageFilter {
   readonly MediaPanelView owner;
   public FilterDismissal(MediaPanelView owner){this.owner=owner;}
   public bool PreFilterMessage(ref Message message){
    if(!owner.filterPopup.Visible)return false;
    if(message.Msg==0x100&&(Keys)(int)message.WParam==Keys.Escape){owner.HideFilterMenu();return true;}
    if(message.Msg!=0x201&&message.Msg!=0x204&&message.Msg!=0x207)return false;
    Point cursor=Control.MousePosition;
    Rectangle popup=owner.filterPopup.RectangleToScreen(owner.filterPopup.ClientRectangle);
    Rectangle button=owner.filterButton.RectangleToScreen(owner.filterButton.ClientRectangle);
    if(!popup.Contains(cursor)&&!button.Contains(cursor))owner.HideFilterMenu();
    return false;
   }
  }
  void HideFilterMenu(){
   filterPopup.Visible=false;
   if(filterDismissal!=null){Application.RemoveMessageFilter(filterDismissal);filterDismissal=null;}
   if(filterHost!=null){filterHost.Deactivate-=OnFilterHostDeactivate;filterHost=null;}
  }
  void OnFilterHostDeactivate(object sender,EventArgs e){HideFilterMenu();}
  Rectangle VisibleFilterBounds(){
   Rectangle bounds=filterButton.RectangleToScreen(filterButton.ClientRectangle);
   for(Control parent=filterButton.Parent;parent!=null&&parent!=this;parent=parent.Parent)bounds=Rectangle.Intersect(bounds,parent.RectangleToScreen(parent.ClientRectangle));
   return new Rectangle(PointToClient(bounds.Location),bounds.Size);
  }
  void ShowFilterMenu(){
   if(filterPopup.Visible){HideFilterMenu();return;}
   foreach(Control item in filterPopup.Controls.Cast<Control>().ToArray())item.Dispose();
   string[] options={"Todos","Video","Audio","Imagen","Web pendientes","Web resueltos"};
   for(int i=options.Length-1;i>=0;i--){
    string value=options[i];var row=new Label{Dock=DockStyle.Top,Height=25,Text=(value==selectedFilter?"✓  ":"    ")+value,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(6,0,0,0),ForeColor=Color.FromArgb(234,235,237),BackColor=filterPopup.BackColor,Cursor=Cursors.Hand};
    row.MouseEnter+=(s,e)=>row.BackColor=Color.FromArgb(69,72,79);
    row.MouseLeave+=(s,e)=>row.BackColor=filterPopup.BackColor;
    row.Click+=(s,e)=>{selectedFilter=value;HideFilterMenu();filterButton.Invalidate();Rebuild();};
    filterPopup.Controls.Add(row);
   }
   filterPopup.Width=Math.Min(166,Math.Max(80,Width-8));filterPopup.Height=Math.Min(options.Length*25+2,Math.Max(28,Height-8));
   Rectangle button=VisibleFilterBounds();
   int x=Math.Max(4,Math.Min(Width-filterPopup.Width-4,button.Right-filterPopup.Width));
   int y=button.Bottom+filterPopup.Height+4<=Height?button.Bottom:button.Top-filterPopup.Height>=4?button.Top-filterPopup.Height:Math.Max(4,Height-filterPopup.Height-4);
   filterPopup.Location=new Point(x,y);filterPopup.Visible=true;filterPopup.BringToFront();
   filterDismissal=new FilterDismissal(this);Application.AddMessageFilter(filterDismissal);
   filterHost=FindForm();if(filterHost!=null)filterHost.Deactivate+=OnFilterHostDeactivate;
  }
  protected override void OnParentChanged(EventArgs e){HideFilterMenu();base.OnParentChanged(e);}
  protected override void OnLocationChanged(EventArgs e){HideFilterMenu();base.OnLocationChanged(e);}
  protected override void OnSizeChanged(EventArgs e){HideFilterMenu();base.OnSizeChanged(e);}
  void ToggleView(){SetViewMode((viewMode+1)%3);}
  void SetViewMode(int mode){viewMode=mode;library.BeginUpdate();try{library.MultiColumn=viewMode==1;library.ItemHeight=viewMode==1?130:viewSizes[viewMode];if(viewMode==1)library.ColumnWidth=viewSizes[1];viewPane.RowStyles[0].Height=viewMode==2?27:0;listHeader.Visible=viewMode==2;library.Invalidate();listHeader.Invalidate();viewPane.PerformLayout();}finally{library.EndUpdate();}viewButton.Invalidate();Rebuild();}
  void ShowViewMenu(){viewMenu.Items.Clear();string[] names={"Miniaturas y detalles","Cuadrícula","Lista"};for(int i=0;i<names.Length;i++){int mode=i;var item=new ToolStripMenuItem(names[i]){Checked=i==viewMode};item.Click+=(s,e)=>SetViewMode(mode);viewMenu.Items.Add(item);}viewMenu.Show(viewButton,new Point(0,viewButton.Height));}
  void ShowViewSizeMenu(){var popup=new ContextMenuStrip();int mode=viewMode;var label=new ToolStripLabel("Tamaño de "+new[]{"miniaturas","cuadrícula","lista"}[mode]);var slider=new TrackBar{Minimum=mode==2?22:mode==1?120:42,Maximum=mode==2?42:mode==1?240:100,Value=viewSizes[mode],TickStyle=TickStyle.None,Width=170};slider.ValueChanged+=(s,e)=>{viewSizes[mode]=slider.Value;if(viewMode==mode){library.BeginUpdate();try{library.ItemHeight=mode==1?130:viewSizes[mode];if(mode==1)library.ColumnWidth=viewSizes[mode];}finally{library.EndUpdate();}library.Invalidate();}};popup.Items.Add(label);popup.Items.Add(new ToolStripControlHost(slider));popup.Closed+=(s,e)=>{if(IsHandleCreated)BeginInvoke(new Action(()=>popup.Dispose()));else popup.Dispose();};popup.Show(viewButton,new Point(0,viewButton.Height));}
  int TypeColumn(int width){return Math.Max(92,(int)(width*0.58));}
  int DurationColumn(int width){return Math.Max(TypeColumn(width)+72,(int)(width*0.82));}
  void DrawListHeader(object sender,PaintEventArgs e){int type=TypeColumn(listHeader.Width),duration=DurationColumn(listHeader.Width);e.Graphics.Clear(listHeader.BackColor);using(var rule=new Pen(Color.FromArgb(68,72,78))){e.Graphics.DrawLine(rule,0,listHeader.Height-1,listHeader.Width,listHeader.Height-1);e.Graphics.DrawLine(rule,type-7,4,type-7,listHeader.Height-5);e.Graphics.DrawLine(rule,duration-7,4,duration-7,listHeader.Height-5);}string arrow=ascending?" ▲":" ▼";Color ink=listHeader.ForeColor;TextRenderer.DrawText(e.Graphics,"Nombre"+(sortColumn=="Nombre"?arrow:""),Font,new Rectangle(28,5,Math.Max(1,type-35),20),ink,TextFormatFlags.EndEllipsis);TextRenderer.DrawText(e.Graphics,"Tipo"+(sortColumn=="Tipo"?arrow:""),Font,new Rectangle(type,5,Math.Max(1,duration-type-8),20),ink,TextFormatFlags.EndEllipsis);TextRenderer.DrawText(e.Graphics,"Duración"+(sortColumn=="Duración"?arrow:""),Font,new Rectangle(duration,5,Math.Max(1,listHeader.Width-duration-4),20),ink,TextFormatFlags.EndEllipsis);}
  void SortListAt(int x){string column=x< TypeColumn(listHeader.Width)?"Nombre":x<DurationColumn(listHeader.Width)?"Tipo":"Duración";if(sortColumn==column)ascending=!ascending;else{sortColumn=column;ascending=true;}Rebuild();listHeader.Invalidate();}
  static string TypeOf(Asset a){if(a==null)return "Pendiente";if(GeneratedMedia.IsText(a))return GeneratedMedia.IsSubtitle(a)?"Subtítulo":"Texto";if(MgtMedia.IsMgt(a))return a.container_format=="mgtproj"?"Proyecto MGT":"Animación MGT";bool web=a.source!=null&&a.source.kind=="web";if(a.streams.All(st=>st.kind=="audio"))return web?"Audio web":"Audio";if(a.streams.Any(st=>st.kind=="image"))return GeneratedMedia.Kind(a)=="solid"?"Color sólido":"Imagen";return web?"Video web":"Video";}
  static double DurationOf(Asset a){if(a==null)return 0;var durations=a.streams.Where(st=>st.duration!=null).Select(st=>st.duration.Seconds).ToList();return durations.Count>0?durations.Max():a.recommended_duration==null?0:a.recommended_duration.Seconds;}
  void AcceptFiles(Control target){target.AllowDrop=true;target.DragEnter+=(s,e)=>e.Effect=(e.Data.GetDataPresent(DataFormats.FileDrop)||WebDrop.Read(e.Data)!=null)&&(CanAcceptFiles==null||CanAcceptFiles())?DragDropEffects.Copy:DragDropEffects.None;target.DragDrop+=(s,e)=>{var web=WebDrop.Read(e.Data);if(web!=null&&WebDropped!=null)WebDropped(web);else if(e.Data.GetDataPresent(DataFormats.FileDrop)&&FilesDropped!=null)FilesDropped((string[])e.Data.GetData(DataFormats.FileDrop));};}

  public void SetProject(Project project){string selected=SelectedAssetId;assets.Clear();assets.AddRange(project.assets.Where(a=>!MgtMedia.IsInstance(a)));PruneThumbnails();folders.Clear();folders.AddRange(MediaLibrary.Folders(project));RebuildFolderTree();Rebuild(selected);}
  public void SetAssets(IList<Asset> newAssets){string selected=SelectedAssetId;assets.Clear();assets.AddRange(newAssets);Rebuild(selected);}
  public void SetPendingImports(IList<PendingImportJob> jobs,bool preparing){pending.Clear();foreach(var job in jobs)pending.Add(new Row{pendingId=job.Id,label=(job.State=="missing"?"Archivo no disponible: ":preparing?"Preparando: ":"Pendiente: ")+Path.GetFileName(job.Path)});Rebuild();}
  void RebuildFolderTree(){string selected=folderTree.SelectedNode==null?"all":folderTree.SelectedNode.Tag as string??"all";updatingTree=true;folderTree.BeginUpdate();try{folderTree.Nodes.Clear();folderTree.Nodes.Add(new TreeNode("Todos"){Tag="all"});folderTree.Nodes.Add(new TreeNode("Sin carpeta"){Tag="root"});foreach(var folder in folders.OrderBy(x=>x.name,StringComparer.CurrentCultureIgnoreCase))folderTree.Nodes.Add(new TreeNode(folder.name){Tag=folder.id});folderTree.SelectedNode=folderTree.Nodes.Cast<TreeNode>().FirstOrDefault(n=>(string)n.Tag==selected)??folderTree.Nodes[0];}finally{folderTree.EndUpdate();updatingTree=false;}}
  static bool InFilter(Asset asset,string value){if(value=="Todos")return true;bool web=asset.source!=null&&asset.source.kind=="web";if(value=="Web pendientes")return web&&Media.SelectRepresentation(asset,false)==null&&RangeMedia.Read(asset).Count==0;if(value=="Web resueltos")return web&&(Media.SelectRepresentation(asset,false)!=null||RangeMedia.Read(asset).Count>0);if(value=="Audio")return asset.streams.All(x=>x.kind=="audio");if(value=="Imagen")return asset.streams.Any(x=>x.kind=="image");return asset.streams.Any(x=>x.kind=="video");}
  static int Distance(string a,string b,int limit){if(Math.Abs(a.Length-b.Length)>limit)return limit+1;int[] previous=Enumerable.Range(0,b.Length+1).ToArray();for(int i=1;i<=a.Length;i++){int[] current=new int[b.Length+1];current[0]=i;int min=i;for(int j=1;j<=b.Length;j++){current[j]=Math.Min(Math.Min(previous[j]+1,current[j-1]+1),previous[j-1]+(a[i-1]==b[j-1]?0:1));min=Math.Min(min,current[j]);}if(min>limit)return limit+1;previous=current;}return previous[b.Length];}
  static int SearchRank(string name,string query){if(query.Length==0)return 0;name=(name??"").ToLowerInvariant();query=query.ToLowerInvariant();if(name==query)return 0;if(name.StartsWith(query))return 1;if(name.Contains(query))return 2;int limit=query.Length>=5?2:query.Length>=4?1:0;if(limit==0)return 99;return name.Split(new[]{' ','-','_','.'},StringSplitOptions.RemoveEmptyEntries).Any(word=>Distance(word,query,limit)<=limit)?3:99;}
  void Rebuild(string selected=null){if(selected==null)selected=SelectedAssetId;string folder=folderTree.SelectedNode==null?"all":folderTree.SelectedNode.Tag as string??"all";string query=search.Text=="Buscar…"?"":search.Text.Trim();var found=assets.Where(a=>!GeneratedMedia.IsPlaceholder(a)&&!GeneratedMedia.IsSubtitle(a)&&(folder=="all"||folder=="root"&&MediaLibrary.FolderOf(a)==null||MediaLibrary.FolderOf(a)==folder)&&InFilter(a,selectedFilter)).Select(a=>new{Asset=a,Rank=SearchRank(a.name,query)}).Where(x=>x.Rank<99).ToList();var ordered=viewMode==2?(sortColumn=="Tipo"?found.OrderBy(x=>x.Rank).ThenBy(x=>TypeOf(x.Asset),StringComparer.CurrentCultureIgnoreCase).ThenBy(x=>x.Asset.name,StringComparer.CurrentCultureIgnoreCase):sortColumn=="Duración"?found.OrderBy(x=>x.Rank).ThenBy(x=>DurationOf(x.Asset)).ThenBy(x=>x.Asset.name,StringComparer.CurrentCultureIgnoreCase):found.OrderBy(x=>x.Rank).ThenBy(x=>x.Asset.name,StringComparer.CurrentCultureIgnoreCase)):found.OrderBy(x=>x.Rank).ThenBy(x=>x.Asset.name,StringComparer.CurrentCultureIgnoreCase);var visible=(viewMode==2&&!ascending?ordered.Reverse():ordered).ToList();library.BeginUpdate();try{rows.Clear();library.Items.Clear();foreach(var item in visible){rows.Add(new Row{asset=item.Asset,label=item.Asset.name});library.Items.Add(item.Asset.name);}if(folder=="all"&&selectedFilter=="Todos"&&query.Length==0)foreach(var item in pending){rows.Add(item);library.Items.Add(item.label);}if(selected!=null){int i=rows.FindIndex(x=>x.asset!=null&&x.asset.id==selected);if(i>=0)library.SelectedIndex=i;}}finally{library.EndUpdate();}library.Invalidate();QueueVisibleThumbnails();}
  public void SelectLast(){if(assets.Count==0)return;string id=assets[assets.Count-1].id;int i=rows.FindIndex(x=>x.asset!=null&&x.asset.id==id);if(i>=0)library.SelectedIndex=i;}
  public void Reveal(string id){search.Text="";selectedFilter="Todos";filterButton.Invalidate();folderTree.SelectedNode=folderTree.Nodes[0];Rebuild(id);int i=rows.FindIndex(x=>x.asset!=null&&x.asset.id==id);if(i>=0){library.SelectedIndex=i;library.TopIndex=i;}}

  void DrawRow(object sender,DrawItemEventArgs e){
   if(e.Index<0||e.Index>=rows.Count)return;
   var row=rows[e.Index];var asset=row.asset;bool chosen=(e.State&DrawItemState.Selected)!=0;
   using(var background=new SolidBrush(chosen?Color.FromArgb(68,76,86):BackColor))e.Graphics.FillRectangle(background,e.Bounds);
   if(viewMode==2){DrawCompactRow(e.Graphics,e.Bounds,row,chosen);e.DrawFocusRectangle();return;}
   Rectangle thumb=viewMode==1?new Rectangle(e.Bounds.Left+6,e.Bounds.Top+5,Math.Max(1,e.Bounds.Width-12),76):new Rectangle(e.Bounds.Left+6,e.Bounds.Top+6,80,45);
   DrawThumbnail(e.Graphics,asset,thumb);
   Color fill=Color.FromArgb(135,138,143);bool resolved=false;
   if(asset!=null){bool web=asset.source!=null&&asset.source.kind=="web";resolved=web&&(Media.SelectRepresentation(asset,false)!=null||RangeMedia.Read(asset).Count>0);
    if(web)fill=WebMedia.IsAudioOnly(asset)?Color.FromArgb(115,176,231):Color.FromArgb(244,155,76);else if(GeneratedMedia.Kind(asset)=="solid")fill=ColorTranslator.FromHtml("#"+GeneratedMedia.ColorHex(asset));
    else if(GeneratedMedia.IsPlaceholder(asset))fill=Color.FromArgb(150,158,169);else if(asset.streams.All(x=>x.kind=="audio"))fill=Color.FromArgb(115,176,231);
    else if(asset.streams.Any(x=>x.kind=="image"))fill=Color.FromArgb(190,150,221);else fill=Color.FromArgb(226,148,173);
   }
   int dotX=thumb.Right-13,dotY=thumb.Bottom-13;using(var brush=new SolidBrush(fill))e.Graphics.FillEllipse(brush,dotX,dotY,10,10);
   if(resolved)using(var pen=new Pen(Color.FromArgb(116,210,146),2))e.Graphics.DrawEllipse(pen,dotX-1,dotY-1,12,12);
   var titleRect=viewMode==1?new Rectangle(e.Bounds.Left+6,e.Bounds.Top+85,Math.Max(1,e.Bounds.Width-12),32):new Rectangle(e.Bounds.Left+94,e.Bounds.Top+5,Math.Max(1,e.Bounds.Width-99),48);
   TextRenderer.DrawText(e.Graphics,row.label??"",library.Font,titleRect,Color.FromArgb(234,235,237),TextFormatFlags.EndEllipsis|TextFormatFlags.WordBreak|TextFormatFlags.NoPadding);
   if(viewMode==1&&asset!=null){string state=asset.source!=null&&asset.source.kind=="web"?(resolved?"Web · resuelto":Media.SelectRepresentation(asset,true)!=null?"Web · vista ligera":"Web · pendiente"):GeneratedMedia.Kind(asset)=="solid"?"Color sólido":"Local";
    TextRenderer.DrawText(e.Graphics,state,library.Font,new Rectangle(e.Bounds.Left+6,e.Bounds.Top+116,Math.Max(1,e.Bounds.Width-12),14),Color.FromArgb(158,162,168),TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
   }
   e.DrawFocusRectangle();
  }
  void DrawCompactRow(Graphics graphics,Rectangle bounds,Row row,bool chosen){var asset=row.asset;int type=TypeColumn(bounds.Width),duration=DurationColumn(bounds.Width);Color fill=Color.FromArgb(135,138,143);bool resolved=false;if(asset!=null){bool web=asset.source!=null&&asset.source.kind=="web";resolved=web&&(Media.SelectRepresentation(asset,false)!=null||RangeMedia.Read(asset).Count>0);if(web)fill=WebMedia.IsAudioOnly(asset)?Color.FromArgb(115,176,231):Color.FromArgb(244,155,76);else if(GeneratedMedia.Kind(asset)=="solid")fill=ColorTranslator.FromHtml("#"+GeneratedMedia.ColorHex(asset));else if(asset.streams.All(st=>st.kind=="audio"))fill=Color.FromArgb(115,176,231);else if(asset.streams.Any(st=>st.kind=="image"))fill=Color.FromArgb(190,150,221);else fill=Color.FromArgb(226,148,173);}using(var brush=new SolidBrush(fill))graphics.FillEllipse(brush,bounds.Left+9,bounds.Top+8,10,10);if(resolved)using(var pen=new Pen(Color.FromArgb(116,210,146),2))graphics.DrawEllipse(pen,bounds.Left+8,bounds.Top+7,12,12);Color ink=chosen?Color.White:Color.FromArgb(226,228,231);TextRenderer.DrawText(graphics,row.label??"",library.Font,new Rectangle(bounds.Left+28,bounds.Top+4,Math.Max(1,type-35),19),ink,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine|TextFormatFlags.NoPadding);TextRenderer.DrawText(graphics,TypeOf(asset),library.Font,new Rectangle(bounds.Left+type,bounds.Top+4,Math.Max(1,duration-type-8),19),ink,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine|TextFormatFlags.NoPadding);if(asset!=null)TextRenderer.DrawText(graphics,DurationOf(asset)>0?Timeline.TimeLabel(DurationOf(asset)):"—",library.Font,new Rectangle(bounds.Left+duration,bounds.Top+4,Math.Max(1,bounds.Width-duration-4),19),ink,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine|TextFormatFlags.NoPadding);}
  void ShowLibraryMenu(int index,Point point){
   menu.Close();menu.Items.Clear();
   if(index>=0&&index<rows.Count){
    var row=rows[index];
    if(row.pendingId!=null){
     string id=row.pendingId;
     menu.Items.Add("Reintentar",null,(s,e)=>Run(()=>{if(RetryPendingRequested!=null)RetryPendingRequested(id);}));
     menu.Items.Add("Descartar pendiente",null,(s,e)=>Run(()=>{if(DiscardPendingRequested!=null)DiscardPendingRequested(id);}));
    }else if(row.asset!=null){
     string id=row.asset.id;
     menu.Items.Add("Añadir a la línea de tiempo",null,(s,e)=>Run(InsertRequested));
     if(row.asset.source!=null&&row.asset.source.kind=="web"&&Media.SelectRepresentation(row.asset,false)==null)
      menu.Items.Add("Resolver este medio",null,(s,e)=>Run(()=>{if(ResolveAssetRequested!=null)ResolveAssetRequested(id);}));
     var move=new ToolStripMenuItem("Mover a carpeta");
     move.DropDownItems.Add("Sin carpeta",null,(s,e)=>Run(()=>{if(MoveAssetRequested!=null)MoveAssetRequested(id,null);}));
     foreach(var folder in folders){string folderId=folder.id;move.DropDownItems.Add(folder.name,null,(s,e)=>Run(()=>{if(MoveAssetRequested!=null)MoveAssetRequested(id,folderId);}));}
     menu.Items.Add(move);menu.Items.Add(new ToolStripSeparator());
     menu.Items.Add("Ver información del medio",null,(s,e)=>Run(()=>{if(ViewAssetInfoRequested!=null)ViewAssetInfoRequested(id);}));
     menu.Items.Add("Editar medio…",null,(s,e)=>Run(()=>{if(EditAssetRequested!=null)EditAssetRequested(id);}));
     menu.Items.Add("Abrir carpeta contenedora",null,(s,e)=>Run(()=>{if(OpenAssetFolderRequested!=null)OpenAssetFolderRequested(id);}));
     if(row.asset.source!=null&&row.asset.source.kind=="web"){
      menu.Items.Add("Redescargar fuente web",null,(s,e)=>Run(()=>{if(RedownloadAssetRequested!=null)RedownloadAssetRequested(id);}));
      menu.Items.Add("Abrir página original",null,(s,e)=>Run(()=>{if(OpenAssetPageRequested!=null)OpenAssetPageRequested(id);}));
      menu.Items.Add("Copiar URL de origen",null,(s,e)=>Run(()=>{if(CopyAssetUrlRequested!=null)CopyAssetUrlRequested(id);}));
     }else menu.Items.Add("Reconectar archivo…",null,(s,e)=>Run(()=>{if(RelinkAssetRequested!=null)RelinkAssetRequested(id);}));
     menu.Items.Add(new ToolStripSeparator());
     menu.Items.Add("Eliminar del proyecto",null,(s,e)=>Run(()=>{if(RemoveAssetRequested!=null)RemoveAssetRequested(id);}));
    }
   }else{
    menu.Items.Add("Importar archivos…",null,(s,e)=>Run(ImportRequested));
    menu.Items.Add("Importar enlace…",null,(s,e)=>Run(ImportLinkRequested));
    menu.Items.Add("Nueva carpeta…",null,(s,e)=>Run(FolderCreateRequested));
    menu.Items.Add(new ToolStripSeparator());
    menu.Items.Add("Resolver medios utilizados",null,(s,e)=>Run(ResolveUsedRequested));
    menu.Items.Add("Reconectar carpeta…",null,(s,e)=>Run(ReconnectFolderRequested));
   }
   menu.Show(library,point);
  }
  void ShowFolderMenu(TreeNode node,Point point){folderMenu.Close();folderMenu.Items.Clear();folderMenu.Items.Add("Nueva carpeta…",null,(s,e)=>Run(FolderCreateRequested));string id=node.Tag as string;if(id!="all"&&id!="root"){folderMenu.Items.Add("Renombrar carpeta…",null,(s,e)=>Run(()=>{if(FolderRenameRequested!=null)FolderRenameRequested(id);}));folderMenu.Items.Add("Eliminar carpeta",null,(s,e)=>Run(()=>{if(FolderDeleteRequested!=null)FolderDeleteRequested(id);}));}folderMenu.Show(folderTree,point);}
  protected override void Dispose(bool disposing){if(disposing){HideFilterMenu();DisposeThumbnails();viewHold.Dispose();viewMenu.Dispose();menu.Dispose();folderMenu.Dispose();addMenu.Dispose();resolveMenu.Dispose();hints.Dispose();}base.Dispose(disposing);}
 }
}

