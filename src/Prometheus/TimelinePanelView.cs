using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Prometheus {
 // Owns timeline chrome and visual tool state; editing commands are supplied by MainForm.
 public sealed class TimelinePanelView:Panel {
  readonly Color surface=Color.FromArgb(27,28,31),barColor=Color.FromArgb(36,38,42),ink=Color.FromArgb(223,225,228);
  readonly ToolTip help=new ToolTip();
  readonly ContextMenuStrip speedMenu=new ContextMenuStrip();
  readonly ContextMenuStrip sequenceMenu=new ContextMenuStrip();
  readonly FlowLayoutPanel sequenceStrip=new FlowLayoutPanel();
  string sequenceSignature;
  public Timeline Timeline{get;private set;}
  public NestedInlineView0106 NestedEditor{get;private set;}
  public NestedInlineView0106 NestedAudioEditor{get;private set;}
  public SubtitleStrip0817 SubtitleStrip{get;private set;}
  public TimelineOverviewScrollBar HorizontalNavigator{get;private set;}
  public Button AddTrackButton{get;private set;}
  public Button UndoButton{get;private set;}
  public Button RedoButton{get;private set;}
  public Button DivideButton{get;private set;}
  public Button SnapButton{get;private set;}
  public Button SpeedButton{get;private set;}
  public Button LoopButton{get;private set;}
  public Button DirectionButton{get;private set;}
  public Button TextToolButton{get;private set;}
  public Button MaskToolButton{get;private set;}
  public Button MaskToolDropButton{get;private set;}
  string lastMaskTool="sam2";
  readonly ContextMenuStrip maskToolMenu=new ContextMenuStrip();
  public Button DirectionDropButton{get;private set;}
  public TimelineTimecode Timecode{get;private set;}
  public Dictionary<Timeline.ToolMode,Button> ToolButtons{get;private set;}
  public Action AddTrackRequested,UndoRequested,RedoRequested,DivideRequested,SnapRequested,LoopRequested,DirectionRequested,DirectionMenuRequested,TextToolRequested;
  public Action<Timeline.ToolMode> ToolRequested;
  public Action<string> MaskToolRequested;
  public Action<double> SpeedRequested;
  public Action<double> SeekRequested;
  public Action<string> SequenceRequested;
  public Action SequenceCreateRequested,SequenceRenameRequested,SequenceDuplicateRequested,SequenceDeleteRequested;
  public Action<Exception> ErrorOccurred;
  public TimelinePanelView(){
   Dock=DockStyle.Fill;BackColor=surface;Timeline=new Timeline{Dock=DockStyle.Fill};NestedEditor=new NestedInlineView0106(Timeline);NestedAudioEditor=new NestedInlineView0106(Timeline);Timeline.Controls.Add(NestedEditor);Timeline.Controls.Add(NestedAudioEditor);Timeline.RegisterInlineEditors(NestedEditor,NestedAudioEditor);SubtitleStrip=new SubtitleStrip0817(Timeline);HorizontalNavigator=new TimelineOverviewScrollBar(Timeline);ToolButtons=new Dictionary<Timeline.ToolMode,Button>();
   var toolbar=new Panel{Dock=DockStyle.Top,Height=40,BackColor=barColor};
   var trackTools=new FlowLayoutPanel{Dock=DockStyle.Left,Width=Timeline.HeaderWidth,WrapContents=false,Padding=new Padding(2,4,0,0)};
   AddTrackButton=Button("Agregar pista",()=>Raise(AddTrackRequested));AddTrackButton.AutoSize=false;AddTrackButton.Width=Timeline.HeaderWidth-44;trackTools.Controls.Add(AddTrackButton);
   var collapse=Button("◀",()=>Timeline.CollapseHeader());collapse.AutoSize=false;collapse.Width=28;help.SetToolTip(collapse,"Contraer/restaurar cabeceras de pista");trackTools.Controls.Add(collapse);
   Timeline.HeaderWidthChanged=width=>{trackTools.Width=width;SubtitleStrip.SetHeaderWidth(width);AddTrackButton.Width=width-44;AddTrackButton.Text=width<=100?"+":"Agregar pista";collapse.Text=width<=76?"▶":"◀";};
   var tools=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Padding=new Padding(6,4,0,0)};
   Timecode=new TimelineTimecode();Timecode.SeekRequested=seconds=>{if(SeekRequested!=null)SeekRequested(seconds);};tools.Controls.Add(Timecode);help.SetToolTip(Timecode,"Posición del lector · clic para ir a HH:MM:SS:FF");
   AddMouseButton(tools,Timeline.ToolMode.Select,CursorToolButton("Seleccionar",()=>RaiseTool(Timeline.ToolMode.Select)));
   AddMouseButton(tools,Timeline.ToolMode.Razor,ToolButton("✂","Cortar",()=>RaiseTool(Timeline.ToolMode.Razor)));
   AddMouseButton(tools,Timeline.ToolMode.Zoom,ToolButton("🔍","Zoom",()=>RaiseTool(Timeline.ToolMode.Zoom)));
   AddMouseButton(tools,Timeline.ToolMode.Hand,ToolButton("✋","Navegar por la línea de tiempo",()=>RaiseTool(Timeline.ToolMode.Hand)));
   AddMouseButton(tools,Timeline.ToolMode.Slip,ToolButton("↔","Mover contenido interno del clip",()=>RaiseTool(Timeline.ToolMode.Slip)));
   TextToolButton=ToolButton("T","Escribir texto en el monitor",()=>Raise(TextToolRequested));tools.Controls.Add(TextToolButton);
   var maskGroup=new Panel{Width=58,Height=30,Margin=new Padding(3,0,3,0)};
   MaskToolButton=ToolButton("👤","Activar la última herramienta de máscara",()=>ActivateMaskTool(lastMaskTool));MaskToolButton.SetBounds(0,0,37,30);MaskToolButton.Margin=Padding.Empty;
   MaskToolDropButton=ToolButton("▾","Elegir herramienta de máscara",ShowMaskToolMenu);MaskToolDropButton.SetBounds(38,0,20,30);MaskToolDropButton.Margin=Padding.Empty;MaskToolDropButton.Font=new Font("Segoe UI",9,FontStyle.Bold);
   maskGroup.Controls.Add(MaskToolButton);maskGroup.Controls.Add(MaskToolDropButton);tools.Controls.Add(maskGroup);
   var group=new Panel{Width=58,Height=30,Margin=new Padding(3,0,3,0)};
   DirectionButton=ToolButton("⇥","Seleccionar hacia adelante",()=>Raise(DirectionRequested));DirectionButton.SetBounds(0,0,37,30);DirectionButton.Margin=Padding.Empty;
   DirectionDropButton=ToolButton("▾","Elegir dirección de selección",()=>Raise(DirectionMenuRequested));DirectionDropButton.SetBounds(38,0,20,30);DirectionDropButton.Margin=Padding.Empty;DirectionDropButton.Font=new Font("Segoe UI",9,FontStyle.Bold);
   group.Controls.Add(DirectionButton);group.Controls.Add(DirectionDropButton);tools.Controls.Add(group);
   UndoButton=ToolButton("↶","Deshacer",()=>Raise(UndoRequested));tools.Controls.Add(UndoButton);
   RedoButton=ToolButton("↷","Rehacer",()=>Raise(RedoRequested));tools.Controls.Add(RedoButton);
   var utility=new FlowLayoutPanel{Dock=DockStyle.Right,Width=224,WrapContents=false,Padding=new Padding(0,4,0,0)};
   DivideButton=ToolButton("◧│◨","Dividir en el cursor",()=>Raise(DivideRequested));utility.Controls.Add(DivideButton);
   SpeedButton=Button("×1",ShowSpeedMenu);SpeedButton.AutoSize=false;SpeedButton.Width=50;SpeedButton.Height=30;help.SetToolTip(SpeedButton,"Velocidad de previsualización · clic derecho para ajustar");SpeedButton.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Right)ShowSpeedSlider();};utility.Controls.Add(SpeedButton);
   SnapButton=ToolButton("🧲","Activar o desactivar ajuste magnético",()=>Raise(SnapRequested));utility.Controls.Add(SnapButton);
   utility.Controls.Add(ToolButton("⛶","Ajustar a contenido",()=>Timeline.Fit()));utility.Controls.Add(ToolButton("▣","Ajustar a clips seleccionados",()=>Timeline.FitSelection()));
   LoopButton=Button("∞",()=>Raise(LoopRequested));LoopButton.Dock=DockStyle.Right;LoopButton.AutoSize=false;LoopButton.Width=44;LoopButton.Font=new Font("Segoe UI",17,FontStyle.Bold);help.SetToolTip(LoopButton,"Loop: reiniciar audio y video desde el comienzo al terminar");
   toolbar.Controls.Add(tools);toolbar.Controls.Add(trackTools);toolbar.Controls.Add(utility);toolbar.Controls.Add(LoopButton);
   sequenceStrip.Dock=DockStyle.Top;sequenceStrip.Height=35;sequenceStrip.WrapContents=false;sequenceStrip.AutoScroll=true;sequenceStrip.BackColor=Color.FromArgb(31,33,37);sequenceStrip.Padding=new Padding(5,3,0,0);
   Controls.Add(Timeline);Controls.Add(HorizontalNavigator);Controls.Add(SubtitleStrip);Controls.Add(toolbar);Controls.Add(sequenceStrip);UpdateToolState(Timeline.ActiveTool);UpdateSnapState();
  }
  void Raise(Action action){try{if(action!=null)action();}catch(Exception ex){if(ErrorOccurred!=null)ErrorOccurred(ex);else throw;}}
  public void ActivateLastMaskTool(){ActivateMaskTool(lastMaskTool);}
  void ActivateMaskTool(string choice){lastMaskTool=choice;MaskToolButton.Text=choice=="rectangle"?"▭":choice=="ellipse"?"◯":choice=="polygon"?"✒":"👤";if(MaskToolRequested!=null)MaskToolRequested(choice);}
  void ShowMaskToolMenu(){if(maskToolMenu.Items.Count==0)foreach(var item in new[]{new[]{"▭  Máscara rectangular","rectangle"},new[]{"◯  Máscara elíptica","ellipse"},new[]{"✒  Máscara libre","polygon"},new[]{"👤  Recorte inteligente","sam2"}}){string choice=item[1];maskToolMenu.Items.Add(item[0],null,(s,e)=>ActivateMaskTool(choice));}maskToolMenu.Show(MaskToolDropButton,0,MaskToolDropButton.Height);}
  void RaiseTool(Timeline.ToolMode mode){try{if(ToolRequested!=null)ToolRequested(mode);}catch(Exception ex){if(ErrorOccurred!=null)ErrorOccurred(ex);else throw;}}
  Button Button(string label,Action action){var button=new Button{Text=label,AutoSize=true,Height=30,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(49,51,56),ForeColor=ink,Margin=new Padding(3,0,3,0)};button.FlatAppearance.BorderColor=Color.FromArgb(66,68,74);button.Click+=(s,e)=>action();return button;}
  Button ToolButton(string icon,string tooltip,Action action){var button=Button(icon,action);button.AutoSize=false;button.Width=36;button.Height=30;button.Font=new Font("Segoe UI Emoji",12,FontStyle.Regular);button.AccessibleName=tooltip;button.AccessibleDescription=tooltip;help.SetToolTip(button,tooltip);return button;}
  Button CursorToolButton(string tooltip,Action action){var button=ToolButton("",tooltip,action);button.Paint+=(s,e)=>{e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;float x=(button.Width-18)/2f,y=(button.Height-21)/2f;var shape=new[]{new PointF(x+2,y+1),new PointF(x+2,y+18),new PointF(x+6,y+14),new PointF(x+9,y+20),new PointF(x+12,y+18.5f),new PointF(x+9,y+12),new PointF(x+16,y+11)};using(var fill=new SolidBrush(Color.FromArgb(226,231,237)))e.Graphics.FillPolygon(fill,shape);using(var border=new Pen(Color.FromArgb(21,23,27),1.4f))e.Graphics.DrawPolygon(border,shape);};return button;}
  void AddMouseButton(FlowLayoutPanel bar,Timeline.ToolMode mode,Button button){ToolButtons[mode]=button;bar.Controls.Add(button);}
  void ShowSpeedMenu(){speedMenu.Close();speedMenu.Items.Clear();foreach(double speed in new[]{1.0,1.25,1.5,2.0,3.0}){double choice=speed;speedMenu.Items.Add("×"+Tools.N(speed),null,(s,e)=>Raise(()=>{if(SpeedRequested!=null)SpeedRequested(choice);}));}speedMenu.Show(SpeedButton,new Point(0,SpeedButton.Height));}
  void ShowSpeedSlider(){var popup=new ContextMenuStrip();var slider=new TrackBar{Minimum=50,Maximum=300,Value=Math.Max(50,Math.Min(300,(int)Math.Round(previewSpeed*100))),TickStyle=TickStyle.None,Width=190};popup.Items.Add(new ToolStripLabel("Velocidad: ×"+Tools.N(previewSpeed)));popup.Items.Add(new ToolStripControlHost(slider));slider.ValueChanged+=(s,e)=>((ToolStripLabel)popup.Items[0]).Text="Velocidad: ×"+Tools.N(slider.Value/100.0);slider.MouseUp+=(s,e)=>Raise(()=>{if(SpeedRequested!=null)SpeedRequested(slider.Value/100.0);});popup.Closed+=(s,e)=>{double value=slider.Value/100.0;if(Math.Abs(value-previewSpeed)>0.000001)Raise(()=>{if(SpeedRequested!=null)SpeedRequested(value);});if(IsHandleCreated)BeginInvoke(new Action(()=>popup.Dispose()));else popup.Dispose();};popup.Show(SpeedButton,new Point(0,SpeedButton.Height));}
  double previewSpeed=1;
  public void SetPlaybackSpeed(double speed){previewSpeed=speed;SpeedButton.Text="×"+Tools.N(speed);}
  public void UpdateToolState(Timeline.ToolMode active){foreach(var pair in ToolButtons){bool selected=pair.Key==active;pair.Value.BackColor=selected?Color.FromArgb(255,156,44):Color.FromArgb(49,51,56);pair.Value.ForeColor=selected?Color.FromArgb(25,27,30):ink;pair.Value.FlatAppearance.BorderSize=selected?1:0;pair.Value.FlatAppearance.BorderColor=selected?Color.FromArgb(255,208,123):Color.FromArgb(49,51,56);pair.Value.Invalidate();}}
  public void UpdateTextToolState(bool active){if(!active){UpdateToolState(Timeline.ActiveTool);}else foreach(var button in ToolButtons.Values){button.BackColor=Color.FromArgb(49,51,56);button.ForeColor=ink;button.FlatAppearance.BorderSize=0;button.Invalidate();}TextToolButton.BackColor=active?Color.FromArgb(255,156,44):Color.FromArgb(49,51,56);TextToolButton.ForeColor=active?Color.FromArgb(25,27,30):ink;TextToolButton.Invalidate();}
  public void UpdateMaskToolState(bool active){if(active)foreach(var button in ToolButtons.Values){button.BackColor=Color.FromArgb(49,51,56);button.ForeColor=ink;button.FlatAppearance.BorderSize=0;button.Invalidate();}MaskToolButton.BackColor=active?Color.FromArgb(255,156,44):Color.FromArgb(49,51,56);MaskToolButton.ForeColor=active?Color.FromArgb(25,27,30):ink;MaskToolButton.FlatAppearance.BorderSize=active?1:0;MaskToolButton.FlatAppearance.BorderColor=active?Color.FromArgb(255,208,123):Color.FromArgb(49,51,56);MaskToolButton.Invalidate();}
  public void UpdateSnapState(){SnapButton.BackColor=Timeline.Snap?Color.FromArgb(78,91,70):Color.FromArgb(49,51,56);SnapButton.ForeColor=ink;}
  public void SetLoopEnabled(bool enabled){LoopButton.BackColor=enabled?Color.FromArgb(255,156,44):Color.FromArgb(49,51,56);LoopButton.ForeColor=enabled?Color.FromArgb(25,27,30):ink;}
  public void SetTime(double seconds){Timecode.SetTime(seconds);}
  public void SetSequences(Project project){
   string signature=project.active_sequence_id+"|"+String.Join("|",project.sequences.ConvertAll(s=>s.id+":"+s.name));
   if(signature==sequenceSignature)return;
   sequenceSignature=signature;sequenceMenu.Close();
   while(sequenceStrip.Controls.Count>0)sequenceStrip.Controls[0].Dispose();
   foreach(var sequence in project.sequences){
    string id=sequence.id;bool active=id==project.active_sequence_id;
    var tab=Button(sequence.name,()=>Raise(()=>{if(SequenceRequested!=null)SequenceRequested(id);}));
    tab.AutoSize=false;tab.Width=Math.Max(104,Math.Min(230,TextRenderer.MeasureText(sequence.name,Font).Width+28));tab.Height=28;
    tab.BackColor=active?Color.FromArgb(255,156,44):Color.FromArgb(49,51,56);
    tab.ForeColor=active?Color.FromArgb(25,27,30):ink;
    tab.Margin=new Padding(1,0,3,0);tab.AccessibleName="Secuencia "+sequence.name;
    help.SetToolTip(tab,sequence.name+(active?" · activa":" · clic para abrir"));
    tab.MouseUp+=(s,e)=>{if(e.Button==MouseButtons.Right){if(active)ShowSequenceMenu(tab);else Raise(()=>{if(SequenceRequested!=null)SequenceRequested(id);});}};
    sequenceStrip.Controls.Add(tab);
   }
   var add=Button("+",()=>Raise(SequenceCreateRequested));add.AutoSize=false;add.Width=32;add.Height=28;help.SetToolTip(add,"Crear secuencia");sequenceStrip.Controls.Add(add);
   var options=Button("⋯",()=>ShowSequenceMenu(null));options.AutoSize=false;options.Width=32;options.Height=28;help.SetToolTip(options,"Opciones de la secuencia activa");sequenceStrip.Controls.Add(options);
  }
  void ShowSequenceMenu(Control anchor){
   sequenceMenu.Close();sequenceMenu.Items.Clear();
   sequenceMenu.Items.Add("Renombrar",null,(s,e)=>BeginInvoke((Action)(()=>Raise(SequenceRenameRequested))));
   sequenceMenu.Items.Add("Duplicar",null,(s,e)=>BeginInvoke((Action)(()=>Raise(SequenceDuplicateRequested))));
   sequenceMenu.Items.Add("Eliminar",null,(s,e)=>BeginInvoke((Action)(()=>Raise(SequenceDeleteRequested))));
   var target=anchor??sequenceStrip.Controls[sequenceStrip.Controls.Count-1];
   sequenceMenu.Show(target,new Point(0,target.Height));
  }
  protected override void Dispose(bool disposing){if(disposing){help.Dispose();speedMenu.Dispose();sequenceMenu.Dispose();}base.Dispose(disposing);}
 }
}
