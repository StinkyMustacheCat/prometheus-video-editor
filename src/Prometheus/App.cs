using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Drawing;using System.Windows.Forms;using System.Threading;using System.Threading.Tasks;using System.Runtime.InteropServices;
namespace Prometheus {
 sealed class PanelWheelGuard:IMessageFilter {
  [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Point point);
  readonly MainForm owner;readonly Control timeline;
  public PanelWheelGuard(MainForm owner,Control timeline){this.owner=owner;this.timeline=timeline;}
  public bool PreFilterMessage(ref Message message){
   if(message.Msg!=0x20A||owner.IsDisposed||!owner.Visible)return false;
   Point pointer=Control.MousePosition;Control hovered=Control.FromChildHandle(WindowFromPoint(pointer));
   if(hovered==null||!owner.Contains(hovered))return false;
   Control target=Control.FromChildHandle(message.HWnd);
   if((hovered is ComboBox||hovered.Parent is ComboBox)&&target!=hovered&&target!=hovered.Parent)return true;
   return !timeline.RectangleToScreen(timeline.ClientRectangle).Contains(pointer)&&target!=null&&(target==timeline||timeline.Contains(target));
  }
 }
 public partial class MainForm:Form {
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
  PanelWheelGuard wheelGuard;
  ContextMenuStrip clipMenu=new ContextMenuStrip(),trackMenu=new ContextMenuStrip(),gapMenu=new ContextMenuStrip();Editor editor=new Editor();PanelWorkspace workspace=new PanelWorkspace();TimelinePanelView timelineView=new TimelinePanelView();Timeline timeline;MediaPanelView mediaView=new MediaPanelView();ListBox library;EffectControlsPanelView effectControlsView=new EffectControlsPanelView();KeyframeTimelineView keyframeView=new KeyframeTimelineView();SplitContainer effectSplit=new SplitContainer();Panel effectHost=new Panel();Button keyframeToggle=new Button();bool keyframeCollapsed;EffectsCatalogView effectsCatalogView=new EffectsCatalogView();string selectedEffectTrackId;MediaInfoWindow infoWindow;MonitorPanelView monitorView=new MonitorPanelView();PreviewCanvas monitor;Label timeLabel,status=new Label(),projectLabel=new Label();CancellationTokenSource mediaToken,renderToken;bool playing,busy,dirty,exportRunning,loopEnabled;Button loopButton,snapButton;Dictionary<Timeline.ToolMode,Button> mouseButtons;ExportProgressBar exportProgress=new ExportProgressBar();Label exportLabel=new Label();Panel exportPanel=new Panel();Button cancelExport;Button addTrackButton;TrackBar previewSlider;NumericUpDown previewPercent;ToolTip help=new ToolTip();string projectFile;string recoveryDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","0.2.6-preview");List<Clip> clipboard;List<Track> clipboardTracks;string clipboardSequenceId,clipboardProjectId;List<string> clipboardGroups,clipboardEditGroups;readonly List<Action> webUpdatesAfterPlayback=new List<Action>();bool closing;
  Color bg=Color.FromArgb(27,28,31),panel=Color.FromArgb(36,38,42),text=Color.FromArgb(223,225,228);System.Windows.Forms.Timer recover=new System.Windows.Forms.Timer();double playbackSpeed=1;
  readonly System.Windows.Forms.Timer interactionPreviewTimer=new System.Windows.Forms.Timer{Interval=70};Project pendingInteractionPreview;
  readonly object playbackFrameLock=new object();Bitmap pendingPlaybackFrame;double pendingPlaybackTime;CancellationTokenSource pendingPlaybackToken;bool playbackFrameDispatchQueued;
  public string StartupProjectPath;MgtSubtitleView0820 animatedSubtitles0820;
  readonly Panel openingCover=new Panel{Dock=DockStyle.Fill,BackColor=Color.FromArgb(27,28,31),Visible=false};
  public MainForm(){help.AutoPopDelay=6000;help.InitialDelay=400;help.ReshowDelay=120;Text="Prometheus 1.0.0 · Editor modular";Width=1440;Height=900;MinimumSize=new Size(1050,700);StartPosition=FormStartPosition.CenterScreen;BackColor=bg;ForeColor=text;Font=new Font("Segoe UI",9);KeyPreview=true;LoadWebSettings();workspace.DropFeedback=message=>status.Text=message;
   interactionPreviewTimer.Tick+=(s,e)=>{var next=pendingInteractionPreview;pendingInteractionPreview=null;interactionPreviewTimer.Stop();if(next!=null)RequestFrameFor(next,true);};
   timeline=timelineView.Timeline;wheelGuard=new PanelWheelGuard(this,timeline);Application.AddMessageFilter(wheelGuard);SetupNested();timeline.WheelZoomModifiers=shortcutMap.WheelZoomModifiers;timeline.WheelTrackModifiers=shortcutMap.WheelTrackModifiers;mouseButtons=timelineView.ToolButtons;addTrackButton=timelineView.AddTrackButton;snapButton=timelineView.SnapButton;loopButton=timelineView.LoopButton;LoadWaveformPreference();workspace.PopulatePanelMenu=ShowWaveformMenu;
   timelineView.ErrorOccurred=Error;timelineView.ToolRequested=SetTool;timelineView.AddTrackRequested=ShowTrackMenu;
   timelineView.UndoRequested=()=>Edit(()=>editor.Undo());timelineView.RedoRequested=()=>Edit(()=>editor.Redo());timelineView.DivideRequested=Cut;
   timelineView.SnapRequested=()=>{timeline.Snap=!timeline.Snap;UpdateSnapButton();status.Text="Ajuste magnético "+(timeline.Snap?"activado":"desactivado");};
   timelineView.SpeedRequested=SetPreviewSpeed;
   timelineView.LoopRequested=()=>{loopEnabled=!loopEnabled;timelineView.SetLoopEnabled(loopEnabled);status.Text="Loop "+(loopEnabled?"activado":"desactivado");};
   timelineView.DirectionRequested=()=>SetTool(directionMode);timelineView.DirectionMenuRequested=ShowDirectionMenu;
   monitor=monitorView.Canvas;monitor.ContextRequested=ShowMonitorContext01034;monitor.TextCreated=(value,x,y)=>{try{CreateTitleAt08171(value,x,y);}catch(Exception ex){Error(ex);}};timelineView.TextToolRequested=ActivateTextTool;timelineView.MaskToolRequested=BeginMaskTool;SetupMonitorTransform();animatedSubtitles0820=new MgtSubtitleView0820(monitor);animatedSubtitles0820.ReadyChanged=()=>{if(!playing&&!closing)RequestFrame();};animatedSubtitles0820.Failed=message=>status.Text=message;monitorView.ViewChanged+=animatedSubtitles0820.Position;timeLabel=monitorView.TimeLabel;previewSlider=monitorView.ZoomSlider;previewPercent=monitorView.ZoomPercent;
   timelineView.SeekRequested=Seek;monitorView.SeekRequested=Seek;monitorView.StepRequested=direction=>Seek(Math.Max(0,timeline.Cursor+direction/60.0));monitorView.PlayPauseRequested=PlayPause;monitorView.PreviewOptionsChanged=()=>{if(!playing)RequestFrame();};
   library=mediaView.Library;mediaView.ImportRequested=Import;mediaView.ImportLinkRequested=ShowImportLink;mediaView.ShelfRequested=ShowShelf;mediaView.ResolveUsedRequested=ResolveUsedWebNow;mediaView.ResolveProjectRequested=ResolveProjectWebNow;mediaView.RedownloadAllRequested=RedownloadAllWebNow;mediaView.WebDropped=web=>StartWebImport(web,null,0);mediaView.InsertRequested=Insert;mediaView.SelectionChanged=()=>{};mediaView.FilesDropped=ImportFiles;mediaView.CanAcceptFiles=()=>!busy;mediaView.ErrorOccurred=Error;mediaView.ReconnectFolderRequested=ReconnectFolder;mediaView.RetryPendingRequested=RetryPending;mediaView.DiscardPendingRequested=DiscardPending;mediaView.FolderCreateRequested=CreateLibraryFolder;mediaView.FolderRenameRequested=RenameLibraryFolder;mediaView.FolderDeleteRequested=DeleteLibraryFolder;mediaView.MoveAssetRequested=MoveLibraryAsset;mediaView.RemoveAssetRequested=DeleteLibraryAsset;mediaView.OpenAssetFolderRequested=OpenLibraryAssetFolder;mediaView.OpenAssetPageRequested=OpenWebSource;mediaView.CopyAssetUrlRequested=CopyWebSource;mediaView.RelinkAssetRequested=Relink;mediaView.ViewAssetInfoRequested=id=>ShowMediaInfo(id,null);mediaView.CreateSolidRequested=CreateSolid;mediaView.CreatePlaceholderRequested=CreatePlaceholder;mediaView.CreateAudioPlaceholderRequested=CreateAudioPlaceholder;mediaView.CreateTextRequested=CreateTitle;
   mediaView.ResolveAssetRequested=ResolveAssetWebNow;mediaView.RedownloadAssetRequested=RedownloadAssetWebNow;mediaView.EditAssetRequested=ShowMediaSettings;
   effectControlsView.TextAssetChanged=UpdateTextAsset;effectControlsView.TextFontChanged=UpdateTextFont;effectControlsView.TextStyleNumberChanged=UpdateTextStyleNumber;effectControlsView.TextStyleColorChanged=UpdateTextStyleColor;effectControlsView.ClipFramingChanged=UpdateClipFraming;effectControlsView.ClipFramingPreview=PreviewClipFraming;effectControlsView.ScaleYChanged=UpdateScaleY;effectControlsView.ScaleYPreview=PreviewScaleY;effectControlsView.ScaleLinkChanged=UpdateScaleLink;effectControlsView.BlendModeChanged=SetBlendMode01034;
   effectControlsView.ClipOpacityChanged=UpdateClipOpacity;effectControlsView.ClipOpacityPreview=PreviewClipOpacity;effectControlsView.ClipFitChanged=SetClipFit;
   effectControlsView.AnimationToggleRequested=ToggleAnimationKeyframe;effectControlsView.AnimationValueChanged=ChangeAnimationValue;effectControlsView.AnimationValuePreview=PreviewAnimationValue;effectControlsView.AnimationResetRequested=ResetAnimationParameter;effectControlsView.AnimationClearRequested=ClearAnimationParameter;effectControlsView.AnimationNavigateRequested=NavigateAnimationParameter;
   effectSplit.Dock=DockStyle.Fill;effectSplit.Orientation=Orientation.Horizontal;effectSplit.SplitterWidth=6;effectSplit.Panel1MinSize=80;effectSplit.Panel2MinSize=70;effectSplit.Panel1.Controls.Add(effectControlsView);effectSplit.Panel2.Controls.Add(keyframeView);keyframeView.Dock=DockStyle.Fill;keyframeView.SeekRequested=Seek;keyframeView.KeyframeMoved=MoveAnimationKeyframe;keyframeView.KeyframeToggled=AddAnimationKeyframe;keyframeView.KeyframesDeleted=DeleteAnimationKeyframes;keyframeView.KeyframesPasted=PasteAnimationKeyframes;keyframeView.InterpolationChanged=ChangeAnimationInterpolation;keyframeView.TangentChanged=ChangeAnimationTangent;keyframeView.GraphVisibilityChanged=visible=>{if(visible&&effectSplit.Height>300&&effectSplit.Panel2.Height<205)effectSplit.SplitterDistance=Math.Max(effectSplit.Panel1MinSize,effectSplit.Height-210-effectSplit.SplitterWidth);};
   effectHost.Dock=DockStyle.Fill;effectHost.Controls.Add(effectSplit);
   keyframeToggle.Dock=DockStyle.Bottom;keyframeToggle.Height=28;keyframeToggle.FlatStyle=FlatStyle.Flat;keyframeToggle.BackColor=Color.FromArgb(45,47,52);keyframeToggle.ForeColor=Color.FromArgb(255,170,65);keyframeToggle.Visible=false;
   keyframeToggle.Click+=(s,e)=>{keyframeCollapsed=!keyframeCollapsed;UpdateKeyframePanel01042(editor.Project.Active.clips.Find(c=>c.id==timeline.Selected));};
   effectHost.Controls.Add(keyframeToggle);effectSplit.Panel2Collapsed=true;
   effectControlsView.ClipEnabledChanged=(id,enabled)=>{try{var ids=timeline.Selection.Contains(id)?timeline.Selection.ToList():new List<string>{id};Edit(()=>editor.Commit(p=>{foreach(string cid in ids){var clip=p.Active.clips.Find(x=>x.id==cid);if(clip==null)continue;if(p.Active.tracks.Find(x=>x.id==clip.track_id).locked)throw new Exception("Pista bloqueada");clip.enabled=enabled;}}));}catch(Exception ex){Error(ex);}};
   effectControlsView.TrackEnabledChanged=(id,enabled)=>{try{Edit(()=>editor.Commit(p=>{var track=p.Active.tracks.Find(t=>t.id==id);if(track==null||track.locked)throw new Exception("Pista bloqueada");track.enabled=enabled;}));}catch(Exception ex){Error(ex);}};
   effectControlsView.VolumeChanged=(id,gain)=>{try{Edit(()=>editor.Commit(p=>{var clip=p.Active.clips.Find(x=>x.id==id);if(p.Active.tracks.Find(x=>x.id==clip.track_id).locked)throw new Exception("Pista bloqueada");clip.gain_db=gain;}));}catch(Exception ex){Error(ex);}};
   effectControlsView.AudioVolumeChanged=(onTrack,id,value)=>UpdateAudioNumber(onTrack,id,value,false);
   effectControlsView.AudioBalanceChanged=(onTrack,id,value)=>UpdateAudioNumber(onTrack,id,value,true);
   effectControlsView.VoiceCleanRequested=ApplyVoiceClean;
   effectControlsView.AutoDuckRequested=SetAutoDucking;
   effectControlsView.AutoDuckRemoved=RemoveAutoDucking;
   effectControlsView.AutoDuckParameterChanged=SetAutoDuckingParameter;
   effectControlsView.InheritTrackEffectsChanged=SetClipTrackEffectsInheritance;
   effectControlsView.TrackEffectsEnabledChanged=SetTrackEffectsEnabled;
   effectControlsView.EffectEnabledChanged=SetEffectEnabled;
   effectControlsView.RemoveEffectRequested=RemoveEffectFromTarget;
   effectControlsView.ResetEffectRequested=ResetEffectParameters;
   effectControlsView.ResetParameterRequested=ResetEffectParameter;
   effectControlsView.MoveEffectRequested=MoveEffect;
   effectControlsView.ReorderEffectRequested=ReorderEffect;
   effectControlsView.CatalogEffectDropped=ApplyCatalogEffectToControls;
   effectControlsView.NumberChanged=SetEffectNumber;
   effectControlsView.EffectTextChanged=SetEffectText;
   effectControlsView.ScreenSampleRequested=BeginEffectColorSample;
   effectControlsView.NumberPreviewChanged=PreviewEffectNumber;
   effectControlsView.ChangeLutRequested=ChangeLutForTarget;
   effectControlsView.RelinkLutRequested=RelinkLut;
   effectsCatalogView.EffectActivated=ApplyCatalogEffectToSelection;
   string appIcon=Path.Combine(Tools.Root,"assets","Prometheus.ico");
   bool iconLoaded=false;if(File.Exists(appIcon)){try{Icon=new Icon(appIcon);iconLoaded=true;}catch{}}
   string logo=Path.Combine(Tools.Root,"assets","Prometheus.png");if(!iconLoaded&&File.Exists(logo)){try{using(var image=new Bitmap(logo)){IntPtr handle=image.GetHicon();try{using(var owned=Icon.FromHandle(handle))Icon=(Icon)owned.Clone();}finally{DestroyIcon(handle);}}}catch{}}
   var top=new Panel{Dock=DockStyle.Top,Height=58,BackColor=panel};var topLeft=new FlowLayoutPanel{Dock=DockStyle.Left,Width=705,Padding=new Padding(8,12,0,0),WrapContents=false};if(File.Exists(logo))topLeft.Controls.Add(new PictureBox{Image=Image.FromFile(logo),SizeMode=PictureBoxSizeMode.Zoom,Width=32,Height=32});projectLabel=new Label{Width=220,Height=32,TextAlign=ContentAlignment.MiddleLeft};topLeft.Controls.Add(projectLabel);Button recentButton=null;recentButton=Button("Recientes",()=>ShowRecentProjects01042(recentButton));topLeft.Controls.Add(recentButton);topLeft.Controls.Add(Button("Nuevo",NewProject));topLeft.Controls.Add(Button("Abrir",OpenProject));var saveButton=Button("Guardar",SaveProject);saveButton.MouseUp+=(s,e)=>{if(e.Button==MouseButtons.Right)ShowSaveContext01042(saveButton,e.Location);};topLeft.Controls.Add(saveButton);Button settingsButton=null;settingsButton=Button("Ajustes",()=>ShowSettingsMenu(settingsButton));topLeft.Controls.Add(settingsButton);var exportButton=Button("EXPORTAR",Export);exportButton.AutoSize=false;exportButton.Dock=DockStyle.Right;exportButton.Width=160;exportButton.Font=new Font("Segoe UI",12,FontStyle.Bold);exportButton.BackColor=Color.FromArgb(255,156,44);exportButton.ForeColor=Color.FromArgb(25,27,30);top.Controls.Add(workspace.PanelShelf);top.Controls.Add(topLeft);top.Controls.Add(exportButton);
   var footer=new Panel{Dock=DockStyle.Bottom,Height=27,BackColor=panel};status.Dock=DockStyle.Fill;status.Padding=new Padding(10,5,0,0);status.Text="Importa medios locales, web y composiciones MGT";footer.Controls.Add(status);
   workspace.Dock=DockStyle.Fill;
   
   var viewer=monitorView;
   var timelinePanel=timelineView;
   foreach(var pair in mouseButtons)RegisterShortcutButton(pair.Value,ToolCommand(pair.Key));
   AddDirectionToolGroup();
   RegisterShortcutButton(timelineView.UndoButton,"edit.undo");RegisterShortcutButton(timelineView.RedoButton,"edit.redo");RegisterShortcutButton(timelineView.DivideButton,"edit.divide");RegisterShortcutButton(timelineView.MaskToolButton,"tool.mask");
   UpdateMouseButtons();UpdateSnapButton();
   workspace.Register("medios","MEDIOS","▣",mediaView);workspace.Register("monitor","MONITOR · MONTAJE","▤",viewer);workspace.Register("audio-monitor","MONITOR DE AUDIO","♫",new AudioMonitorPanel());workspace.Register("propiedades","CONTROLES DE EFECTOS","✦",effectHost);workspace.Register("mgt","CONTROLES MGT","◇",mgtControlsView);workspace.Register("efectos","EFECTOS","✧",effectsCatalogView);workspace.Register("timeline","LÍNEA DE TIEMPO","▥",timelinePanel);workspace.Register("marcadores","MARCADORES","⚑",markerPanelView);workspace.Register("transiciones","TRANSICIONES","⇄",transitionCatalogView);workspace.Register("instrucciones","INSTRUCCIONES","≡",instructionView);workspace.Register("color","COLOR","◐",colorPanel);workspace.Register("subtitulos","SUBTÍTULOS","▤",subtitlePanel0817);workspace.Register("graficos","TEXTO Y GRÁFICOS","T",textPanel0817);PlaceholderPanels.RegisterAll(workspace);SetupColor();SetupMasks();SetupTransitions();SetupMarkers();SetupExportRange();SetupSequences();SetupInstructions();SetupMgt();SetupSubtitles0817();workspace.UseDefaultLayout();Controls.Add(workspace);openingCover.Controls.Add(new Label{Text="Abriendo proyecto…",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Font=new Font("Segoe UI",15,FontStyle.Regular),ForeColor=text});workspace.Controls.Add(openingCover);Controls.Add(top);SetupExportProgress();Controls.Add(exportPanel);Controls.Add(footer);DarkScrollTheme.Attach(this);
   timeline.Seek=Seek;timeline.SelectionChanged=x=>{selectedEffectTrackId=null;selectedTransitionId=null;timeline.SelectedTransitionId=null;if(!editor.Project.Active.clips.Any(c=>c.id==timeline.Selected&&SubtitleTrack0817.IsCue(editor.Project,c)))timelineView.SubtitleStrip.ClearSelection();instructionView.SetContext(editor.Project,timeline.Selection,null);ShowClip();};timeline.TrackSelected=id=>{selectedTransitionId=null;timeline.SelectedTransitionId=null;instructionView.SetContext(editor.Project,timeline.Selection,null,id);SelectEffectTrack(id);};timeline.ManageTrackEffects=ManageTrackEffects;timeline.ToggleTrackEffects=ToggleTrackEffects;timeline.DropEffectOnClip=ApplyCatalogEffectToClip;timeline.DropEffectOnTrack=ApplyCatalogEffectToTrack;timeline.MoveClip=(id,t,tr)=>Edit(()=>{if(tr==Timeline.NewVideoTrackDrop)editor.MoveSelectionAboveVideos(timeline.Selection.ToList(),id,t,true);else if(tr==Timeline.NewAudioTrackDrop)editor.MoveSelectionBelowAudios(timeline.Selection.ToList(),id,t,true);else editor.MoveSelection(timeline.Selection.ToList(),id,t,tr);});timeline.MoveIndependent=(id,t,tr)=>Edit(()=>editor.MoveIndependent(id,t,tr));timeline.DuplicateClip=(id,t,tr)=>{var original=editor.Project.Active.clips.Find(c=>c.id==id);if(original==null||Math.Abs(original.timeline_start.Seconds-t)<1.0/120&&(tr==null||tr==original.track_id))return;List<string> copies=null;Edit(()=>copies=editor.DuplicateRelated(id,t,tr));if(copies!=null&&copies.Count>0){timeline.Selection=copies;timeline.Selected=copies[0];timeline.Invalidate();instructionView.SetContext(editor.Project,timeline.Selection,null);ShowClip();status.Text="Copia independiente creada · Ctrl+Z para deshacer";}};timeline.MoveGroup=(ids,id,t,tr,includeLinked)=>Edit(()=>{if(tr==Timeline.NewVideoTrackDrop)editor.MoveSelectionAboveVideos(ids,id,t,includeLinked);else if(tr==Timeline.NewAudioTrackDrop)editor.MoveSelectionBelowAudios(ids,id,t,includeLinked);else editor.MoveSelectedGroup(ids,id,t,tr,includeLinked);});timeline.InsertClip=(id,t,tr)=>Edit(()=>editor.InsertSelection(timeline.Selection.ToList(),id,t,tr));timeline.RangeTrimAction=(ids,a,b)=>{if(editor.RangeTrimSelection(ids,a,b)){Changed();status.Text="Rango eliminado en las pistas afectadas";}};timeline.RippleDeleteClip=id=>Edit(()=>editor.RippleDelete(id));SetupRollPreview();timeline.RollPreviewChanged=QueueRollPreview;timeline.LimitRoll=(l,r,cut,linked)=>{try{return editor.LimitRoll(l,r,cut,linked);}catch{return editor.Project.Active.clips.Find(c=>c.id==l).End;}};timeline.RollClipLinked=(l,r,cut,linked)=>{try{Edit(()=>editor.RollEditLinked(l,r,cut,linked));}catch{status.Text="El punto de corte no se puede mover con los vínculos actuales";}};timeline.SlipClip=(id,source)=>Edit(()=>editor.Slip(id,source));timeline.RazorCut=(id,at)=>{if(editor.CutSelection(new List<string>{id},at,true)){Changed();status.Text="Clip dividido";}};timeline.LimitTrim=(id,left,t)=>editor.TrimEdge(id,left,t);timeline.LimitSlip=(id,source)=>editor.SlipSource(id,source);timeline.TrimClip=(id,left,t)=>Edit(()=>editor.Trim(id,left,t));timeline.RippleTrimClip=(id,left,t)=>Edit(()=>editor.RippleTrim(id,left,t));timeline.ToggleTrack=(id,locked)=>Edit(()=>editor.Commit(p=>{var t=p.Active.tracks.Find(x=>x.id==id);if(locked)t.locked=!t.locked;else t.enabled=!t.enabled;}));
   timeline.SelectTrackClipsRequested=SelectTrackClips01034;timeline.DeleteTrackRequested=DeleteTrack01034;timeline.RenameTrack=RenameTrack;timeline.ShowTrackNames=SetTrackNamesVisible;timeline.ToggleTrackCompact=SetTrackCompact;timeline.TrackHeightChanged=SaveTrackHeight;timeline.MoveTrackContentsRequested=MoveContentsOneTrack;timeline.ClipContext=ShowClipContext;timeline.GapContext=ShowGapContext;timeline.EmptyContext=BuildEmptyContextMenu;timeline.CanDropAsset=CanDrop;timeline.DropAsset=(id,tr,at)=>{try{Edit(()=>editor.Commit(p=>{if(tr==Timeline.NewVideoTrackDrop)tr=Store.AddTrack(p.Active,"video").id;else if(tr==Timeline.NewAudioTrackDrop)tr=Store.AddTrack(p.Active,"audio").id;Media.Insert(p,id,tr,at);}));status.Text="Medio añadido a la pista";}catch(Exception ex){Error(ex);}};timeline.DropFeedback=message=>status.Text=message;timeline.CanDropExternal=id=>!busy;timeline.CanDropFiles=(files,id)=>{if(id==Timeline.NewVideoTrackDrop||id==Timeline.NewAudioTrackDrop)return files!=null&&files.Length==1&&MediaDropCompatibility.CanPlace(files,id==Timeline.NewVideoTrackDrop?"video":"audio");var target=editor.Project.Active.tracks.Find(t=>t.id==id);return target!=null&&!target.locked&&MediaDropCompatibility.CanPlace(files,target.kind);};timeline.DropExternalAudio=ImportMediaToTimeline;timeline.DropWebUrl=(web,tr,at)=>StartWebImport(web,tr,at,true);
   timeline.CanFillPlaceholderAsset=CanDropAssetOnPlaceholder;
   timeline.CanFillPlaceholderFiles=(id,files)=>!busy&&MediaDropCompatibility.CanFillPlaceholder(files,editor.Project,editor.Project.Active.clips.Find(c=>c.id==id));
   timeline.CanFillPlaceholderWeb=(id,web)=>!busy&&CanDropWebOnPlaceholder(id,web);
   timeline.FillPlaceholderAsset=(id,asset)=>{try{ReplacePlaceholderNow(id,asset,0);}catch(Exception ex){Error(ex);}};
   timeline.FillPlaceholderFiles=(id,files)=>{if(files!=null&&files.Length==1)StartPlaceholderFileLink(id,files[0],0,false);};
   timeline.FillPlaceholderWeb=(id,web)=>StartPlaceholderWebLink(id,web.Url,web.PageUrl,0);
   timeline.CanDropShelf=CanDropShelfItem;timeline.DropShelf=PromoteShelfDrop;
   Load+=(s,e)=>{workspace.LoadLayout();RefreshProject();if(!String.IsNullOrWhiteSpace(StartupProjectPath))ShowOpeningCover(false);};
   Shown+=(s,e)=>{if(!App.Capture)BeginInvoke((Action)(()=>{ProjectFileAssociation.RegisterOrRefresh();if(!String.IsNullOrWhiteSpace(StartupProjectPath)){try{OpenProjectPath(StartupProjectPath);}catch(Exception ex){openingCover.Visible=false;Error(ex);}}else Recover();RegisterShelfHotkey();}));};FormClosing+=OnClosingRequest;KeyDown+=(s,e)=>{if(monitor.ColorSamplingActive&&e.KeyCode==Keys.Escape){monitor.CancelColorSample();e.Handled=true;e.SuppressKeyPress=true;}};KeyDown+=KeysDown;
   recover.Interval=20000;recover.Tick+=(s,e)=>AutosaveTick();recover.Start();
  }
  void SetTool(Timeline.ToolMode mode){textToolActive=false;monitor.TextToolActive=false;if(maskToolActive){maskToolActive=false;StopMaskPick01061();monitor.CancelMaskEdit(false);monitor.ShowObjectOverlay(null);}timeline.ActiveTool=mode;timelineView.SubtitleStrip.RefreshToolState();UpdateMouseButtons();string name=shortcutMap.Command(ToolCommand(mode)).Name;status.Text="Herramienta: "+name;timeline.Invalidate();}
  void UpdateMouseButtons(){timelineView.UpdateTextToolState(textToolActive);timelineView.UpdateMaskToolState(maskToolActive);UpdateDirectionButton();}
  void UpdateSnapButton(){timelineView.UpdateSnapState();}
  void SetupExportProgress(){exportPanel.Dock=DockStyle.Bottom;exportPanel.Height=42;exportPanel.BackColor=panel;exportPanel.Padding=new Padding(8);exportPanel.Visible=false;exportLabel.Dock=DockStyle.Left;exportLabel.Width=250;exportLabel.ForeColor=text;exportLabel.TextAlign=ContentAlignment.MiddleLeft;exportProgress.Dock=DockStyle.Fill;cancelExport=Button("Cancelar",()=>{if(exportRunning&&mediaToken!=null){mediaToken.Cancel();exportLabel.Text="Cancelando…";}else exportPanel.Visible=false;});cancelExport.Dock=DockStyle.Right;cancelExport.Width=100;exportPanel.Controls.Add(exportProgress);exportPanel.Controls.Add(exportLabel);exportPanel.Controls.Add(cancelExport);}
  static void ClearMenu(ContextMenuStrip menu){menu.Close();menu.Items.Clear();}
  protected override void Dispose(bool disposing){if(disposing){if(wheelGuard!=null)Application.RemoveMessageFilter(wheelGuard);ClearPlaybackFrame();DisposeRollPreview();clipMenu.Dispose();trackMenu.Dispose();gapMenu.Dispose();transitionMenu.Dispose();foreach(var menu in shortcutMenus)menu.Dispose();help.Dispose();}base.Dispose(disposing);}
  void ShowTrackMenu(){var menu=trackMenu;ClearMenu(menu);menu.Items.Add("Video",null,(s,e)=>Edit(()=>editor.Commit(p=>Store.AddTrack(p.Active,"video"))));menu.Items.Add("Audio",null,(s,e)=>Edit(()=>editor.Commit(p=>Store.AddTrack(p.Active,"audio"))));menu.Show(addTrackButton,new Point(0,addTrackButton.Height));}
  bool CanDrop(string id,string track,double at){if(busy)return false;var asset=editor.Project.assets.Find(a=>a.id==id);string kind=track==Timeline.NewVideoTrackDrop?"video":track==Timeline.NewAudioTrackDrop?"audio":null;var target=kind==null?editor.Project.Active.tracks.Find(t=>t.id==track):null;if(target!=null){if(target.locked)return false;kind=target.kind;}return asset!=null&&kind!=null&&asset.streams.Any(s=>kind=="audio"?s.kind=="audio":s.kind=="video"||s.kind=="image");}
  void RenameTrack(string id){var track=editor.Project.Active.tracks.Find(t=>t.id==id);if(track==null)return;using(var dialog=new Form{Text="Renombrar pista",Width=360,Height=155,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,StartPosition=FormStartPosition.CenterParent,BackColor=panel,ForeColor=text}){var input=new TextBox{Text=track.name,MaxLength=64,Left=15,Top=15,Width=310};var ok=new Button{Text="Guardar",DialogResult=DialogResult.OK,Left=155,Top=55,Width=80};var cancel=new Button{Text="Cancelar",DialogResult=DialogResult.Cancel,Left=245,Top=55,Width=80};dialog.Controls.Add(input);dialog.Controls.Add(ok);dialog.Controls.Add(cancel);dialog.AcceptButton=ok;dialog.CancelButton=cancel;if(dialog.ShowDialog(this)==DialogResult.OK){try{string name=input.Text.Trim();if(name.Length==0)throw new Exception("El nombre no puede estar vacío");Edit(()=>editor.Commit(p=>p.Active.tracks.Find(t=>t.id==id).name=name));}catch(Exception ex){Error(ex);}}}}
  void ShowGapContext(string trackId,double at,Point point){BuildGapContextMenu(trackId,at,point);}
  void ShowClipContext(Point point){BuildClipContextMenu(point);}
  Control FocusedChild(Control control){foreach(Control c in control.Controls){if(c.Focused)return c;if(c.ContainsFocus){var child=FocusedChild(c);if(child!=null)return child;}}return null;}
  protected override bool ProcessCmdKey(ref Message msg,Keys keyData){var focus=FocusedChild(this);if(!(focus is TextBoxBase)&&!(focus is NumericUpDown)&&!(focus is TrackBar)&&!(focus is ComboBox)&&!(focus is ListBox)){string id=shortcutMap.CommandAt(keyData);if(id!=null&&id.StartsWith("nav.",StringComparison.Ordinal)){ExecuteShortcut(keyData);return true;}}return base.ProcessCmdKey(ref msg,keyData);}
  void NavigateCut(bool forward){var edges=editor.Project.Active.clips.SelectMany(c=>new[]{c.timeline_start.Seconds,c.End}).Distinct().OrderBy(t=>t).ToList();double next=forward?edges.Where(t=>t>timeline.Cursor+0.000001).DefaultIfEmpty(timeline.Cursor).First():edges.Where(t=>t<timeline.Cursor-0.000001).DefaultIfEmpty(timeline.Cursor).Last();if(Math.Abs(next-timeline.Cursor)>0.000001)Seek(next);}
  Label Heading(string title,DockStyle dock){return new Label{Text=title,Dock=dock,Height=30,Padding=new Padding(10,8,0,0),ForeColor=text,BackColor=panel,Font=new Font("Segoe UI",8,FontStyle.Bold)};}
  Button Button(string title,Action action){var b=new Button{Text=title,AutoSize=true,Height=30,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(49,51,56),ForeColor=text,Margin=new Padding(3,0,3,0)};b.FlatAppearance.BorderColor=Color.FromArgb(66,68,74);b.Click+=(s,e)=>{try{action();}catch(Exception ex){Error(ex);}};return b;}
  static bool AssetContainsPath(Asset asset,string file){string target=Path.GetFullPath(file);Store.NormalizeAsset(asset);return asset.representations.Any(r=>r.locator!=null&&String.Equals(Path.GetFullPath(r.locator.path),target,StringComparison.OrdinalIgnoreCase))||(asset.source.kind=="local"&&String.Equals(Path.GetFullPath(asset.source.uri),target,StringComparison.OrdinalIgnoreCase));}
  void UI(Action action){if(IsDisposed||closing)return;try{BeginInvoke((Action)(()=>{if(!IsDisposed&&!closing)action();}));}catch{}}
  void DisposePopupLater(ContextMenuStrip menu){
   if(!IsHandleCreated||IsDisposed){menu.Dispose();return;}
   try{BeginInvoke((Action)(()=>{if(!menu.IsDisposed)menu.Dispose();}));}catch(InvalidOperationException){menu.Dispose();}
  }
  void Error(Exception e){status.Text="No se pudo completar la operación: "+e.Message;if(!App.Capture)MessageBox.Show(e.Message,"Prometheus",MessageBoxButtons.OK,MessageBoxIcon.Information);}
  void UpdateTimecode(double seconds){monitorView.SetTime(seconds);timelineView.SetTime(seconds);keyframeView.SetPlayhead(seconds);RefreshMonitorTransform();if(animatedSubtitles0820!=null)animatedSubtitles0820.SetFrame(seconds);}
  void RefreshProject(){RestoreSequenceViewOnChange();monitorView.SetCanvasSize(editor.Project.Active.width,editor.Project.Active.height);timeline.Project=editor.Project;timeline.ClearInvalidEditCues();timelineView.NestedEditor.Set(editor.Project,editingVideoNestedId);timelineView.NestedAudioEditor.Set(editor.Project,editingAudioNestedId);timeline.MgtPreviewReady=t=>MgtTransitionCache.Ready(editor.Project,t,"preview",60,monitorView.PreviewWidth,monitorView.PreviewHeight);if(!editor.Project.Active.tracks.Any(t=>t.id==timeline.ActiveTrack))timeline.ActiveTrack=editor.Project.Active.tracks.First(t=>t.kind=="video").id;timeline.Selection.RemoveAll(id=>!editor.Project.Active.clips.Any(c=>c.id==id));if(!editor.Project.Active.clips.Any(c=>c.id==timeline.Selected))timeline.Selected=null;timeline.SyncVerticalScrollbars();timeline.Invalidate();projectLabel.Text="Prometheus · "+editor.Project.name+(dirty?" *":"");Text="Prometheus 1.0.0 · "+editor.Project.name+" · "+editor.Project.Active.name+(dirty?" *":"");timelineView.SetSequences(editor.Project);mediaView.SetProject(editor.Project);subtitlePanel0817.SetProject(editor.Project);subtitlePanel0817.SetAnimationActive(SubtitleAnimation0820.Template(editor.Project.Active)!=null);timelineView.SubtitleStrip.SetAnimationActive(SubtitleAnimation0820.Template(editor.Project.Active)!=null);textPanel0817.SetProject(editor.Project);timelineView.SubtitleStrip.RefreshCues();transitionCatalogView.SetProject(editor.Project);instructionView.SetContext(editor.Project,timeline.Selection,selectedTransitionId,selectedEffectTrackId);if(infoWindow!=null&&!infoWindow.IsDisposed&&!editor.Project.assets.Any(a=>a.id==infoWindow.AssetId))CloseMediaInfo();RefreshShelfUi();RefreshMarkerPanel();UpdateTimecode(timeline.Cursor);ShowClip();RefreshWaveforms();}
  void Changed(){dirty=true;RefreshProject();RequestFrame();}
  void Edit(Action action){if(busy)throw new Exception("Espera o cancela la tarea en curso");Stop();action();Changed();}
  void QueuePlaybackFrame(Bitmap bitmap,double time,CancellationTokenSource token){Bitmap discarded=null;bool dispatch=false;lock(playbackFrameLock){if(token.IsCancellationRequested)discarded=bitmap;else{discarded=pendingPlaybackFrame;pendingPlaybackFrame=bitmap;pendingPlaybackTime=time;pendingPlaybackToken=token;if(!playbackFrameDispatchQueued){playbackFrameDispatchQueued=true;dispatch=true;}}}if(discarded!=null)discarded.Dispose();if(dispatch)UI(()=>{lock(playbackFrameLock)playbackFrameDispatchQueued=false;FlushPlaybackFrame(renderToken);});}
  void FlushPlaybackFrame(CancellationTokenSource token){Bitmap frame;double time;CancellationTokenSource owner;lock(playbackFrameLock){frame=pendingPlaybackFrame;time=pendingPlaybackTime;owner=pendingPlaybackToken;pendingPlaybackFrame=null;pendingPlaybackToken=null;}if(frame==null)return;if(token==null||token!=owner||token.IsCancellationRequested){frame.Dispose();return;}SetImage(frame);timeline.Cursor=time;timeline.EnsureCursor();UpdateTimecode(time);markerPanelView.CurrentTime=time;}
  void ClearPlaybackFrame(){Bitmap frame;lock(playbackFrameLock){frame=pendingPlaybackFrame;pendingPlaybackFrame=null;pendingPlaybackToken=null;playbackFrameDispatchQueued=false;}if(frame!=null)frame.Dispose();}
  void Stop(){if(playing&&renderToken!=null)FlushPlaybackFrame(renderToken);playing=false;AudioMonitorState.Reset();if(renderToken!=null){renderToken.Cancel();renderToken=null;}if(scrubPreviewToken!=null){scrubPreviewToken.Cancel();scrubPreviewToken=null;}scrubPreviewBusy=false;if(monitor.ColorSamplingActive)monitor.CancelColorSample(false);ClearPlaybackFrame();pendingInteractionPreview=null;interactionPreviewTimer.Stop();FlushWebUpdatesAfterPlayback();}
  void FlushWebUpdatesAfterPlayback(){if(playing||webUpdatesAfterPlayback.Count==0)return;var updates=webUpdatesAfterPlayback.ToArray();webUpdatesAfterPlayback.Clear();foreach(var update in updates)update();}
  void SetImage(Bitmap b){monitorView.SetImage(b);}
  void ShowOpeningCover(bool paint){openingCover.Visible=true;openingCover.BringToFront();if(paint&&Visible)openingCover.Refresh();}
  void RequestFrame(){if(animatedSubtitles0820!=null)animatedSubtitles0820.SetProject(editor.Project,timeline.Cursor);RequestFrameFor(Store.Copy(editor.Project));}
  CancellationTokenSource scrubPreviewToken;bool scrubPreviewBusy;double scrubRequestedTime;
  void RequestScrubPreview(){
   if(scrubPreviewBusy||scrubPreviewToken==null||!timeline.IsScrubbingRuler)return;
   scrubPreviewBusy=true;var token=scrubPreviewToken;double at=scrubRequestedTime;var snapshot=Store.Copy(editor.Project);
   int width=monitorView.PreviewWidth,height=monitorView.PreviewHeight;if(Math.Max(width,height)>640){double ratio=640.0/Math.Max(width,height);width=Math.Max(2,(int)Math.Round(width*ratio/2)*2);height=Math.Max(2,(int)Math.Round(height*ratio/2)*2);}
   PreparePreviewSubtitles08201(snapshot);
   int frameWidth=width,frameHeight=height;
   Task.Factory.StartNew(()=>{Bitmap frame=null;Exception failure=null;try{frame=Rendering.Frame(snapshot,at,token.Token,frameWidth,frameHeight);}catch(OperationCanceledException){}catch(Exception ex){failure=ex;}
    UI(()=>{if(scrubPreviewToken!=token||token.IsCancellationRequested){if(frame!=null)frame.Dispose();return;}scrubPreviewBusy=false;
     if(frame!=null){if(Math.Abs(at-scrubRequestedTime)<1.0/120)SetImage(frame);else frame.Dispose();}
     else if(failure!=null&&Math.Abs(at-scrubRequestedTime)<1.0/120)status.Text="Vista al mover el lector: "+failure.Message.Replace('\n',' ');
     if(timeline.IsScrubbingRuler&&Math.Abs(at-scrubRequestedTime)>=1.0/120)RequestScrubPreview();
    });
   });
  }
  void PreviewProject(Action<Project> change){try{var snapshot=Store.Copy(editor.Project);change(snapshot);Store.Validate(snapshot);pendingInteractionPreview=snapshot;if(!interactionPreviewTimer.Enabled)interactionPreviewTimer.Start();}catch(Exception ex){status.Text="Vista previa: "+ex.Message;}}
  void RequestFrameFor(Project snapshot){RequestFrameFor(snapshot,false);}
  void RequestFrameFor(Project snapshot,bool interactive){Stop();PreparePreviewSubtitles08201(snapshot);if(snapshot.Active.Length<=0||timeline.Cursor>=snapshot.Active.Length){SetImage(null);status.Text="Sin medios en este instante · la exportación termina en el último clip";return;}var token=new CancellationTokenSource();renderToken=token;double at=timeline.Cursor;int width=monitorView.PreviewWidth,height=monitorView.PreviewHeight;if(interactive&&Math.Max(width,height)>640){double ratio=640.0/Math.Max(width,height);width=Math.Max(2,(int)Math.Round(width*ratio/2)*2);height=Math.Max(2,(int)Math.Round(height*ratio/2)*2);}Task.Factory.StartNew(()=>{try{var frame=Rendering.Frame(snapshot,at,token.Token,width,height);if(token.IsCancellationRequested){frame.Dispose();return;}UI(()=>{if(renderToken==token&&!token.IsCancellationRequested)SetImage(frame);else frame.Dispose();});}catch(OperationCanceledException){}catch(Exception ex){UI(()=>{if(renderToken==token){SetImage(null);status.Text="Vista previa: "+ex.Message.Replace('\n',' ');}});}});}
  void SetPreviewSpeed(double speed){double next=Math.Max(0.5,Math.Min(3,Math.Round(speed,2)));if(Math.Abs(next-playbackSpeed)<0.000001)return;bool restart=playing;if(restart)Stop();playbackSpeed=next;timelineView.SetPlaybackSpeed(next);if(restart)PlayPause();else status.Text="Velocidad de previsualización ×"+Tools.N(next);}
  void Seek(double at){if(timeline.IsScrubbingRuler){if(scrubPreviewToken==null){Stop();scrubPreviewToken=new CancellationTokenSource();}timeline.Cursor=Math.Max(0,Rational.Frame(at).Seconds);timeline.EnsureCursor();UpdateTimecode(timeline.Cursor);markerPanelView.CurrentTime=timeline.Cursor;effectControlsView.RefreshPlayhead(editor.Project,timeline.Cursor);scrubRequestedTime=timeline.Cursor;RequestScrubPreview();MaskCursorChanged0107();ShowSam2Overlay0104(timeline.Selected);return;}Stop();timeline.Cursor=Math.Max(0,Rational.Frame(at).Seconds);timeline.EnsureCursor();UpdateTimecode(timeline.Cursor);markerPanelView.CurrentTime=timeline.Cursor;effectControlsView.RefreshPlayhead(editor.Project,timeline.Cursor);RequestFrame();MaskCursorChanged0107();ShowSam2Overlay0104(timeline.Selected);}
  void PlayPause(){if(playing){Stop();status.Text="Pausa";ShowSam2Overlay0104(timeline.Selected);return;}if(busy||editor.Project.Active.Length<=0)return;if(timeline.Cursor>=editor.Project.Active.Length){status.Text="Sin medios desde el cursor. Muévelo dentro del montaje para reproducir.";SetImage(null);return;}Stop();monitor.ShowObjectOverlay(null);playing=true;var token=new CancellationTokenSource();renderToken=token;var snapshot=Store.Copy(editor.Project);PreparePreviewSubtitles08201(snapshot);if(snapshot.Active.Length<=0){playing=false;renderToken=null;token.Dispose();status.Text="No hay medios visibles para reproducir";return;}double at=timeline.Cursor;int fps=monitorView.PreviewFps,width=monitorView.PreviewWidth,height=monitorView.PreviewHeight;Task.Factory.StartNew(()=>{try{Rendering.Play(snapshot,at,recoveryDir,token.Token,(bitmap,time)=>QueuePlaybackFrame(bitmap,time,token),message=>UI(()=>{if(renderToken==token)status.Text=message;}),fps,width,height,playbackSpeed);UI(()=>{if(renderToken==token){FlushPlaybackFrame(token);playing=false;FlushWebUpdatesAfterPlayback();status.Text="Fin de reproducción";if(loopEnabled&&!token.IsCancellationRequested&&!busy&&!closing){timeline.Cursor=0;timeline.EnsureCursor();UpdateTimecode(0);markerPanelView.CurrentTime=0;PlayPause();}}});}catch(OperationCanceledException){}catch(Exception ex){UI(()=>{if(renderToken==token){FlushPlaybackFrame(token);playing=false;FlushWebUpdatesAfterPlayback();Error(ex);}});}});}
  void Import(){if(busy)return;using(var d=new OpenFileDialog{Filter="Medios compatibles|*.mp4;*.mov;*.wav;*.mp3;*.m4a;*.flac;*.ogg;*.opus;*.aac;*.aif;*.aiff;*.png;*.jpg;*.jpeg;*.mgt*.json;*.mgtproj|Todos los archivos|*.*",Multiselect=true}){if(d.ShowDialog()==DialogResult.OK)ImportFiles(d.FileNames);}}
  void ImportFiles(string[] files){StartImport(files,null,0);}
  void ImportAudioToTimeline(string[] files,string trackId,double at){ImportMediaToTimeline(files,trackId,at);}
  void ImportMediaToTimeline(string[] files,string trackId,double at){StartImport(files,trackId,at);}
  void Insert(){string id=mediaView.SelectedAssetId;if(id==null)throw new Exception("Selecciona un medio en la biblioteca");Edit(()=>editor.Commit(p=>Media.Insert(p,id,timeline.ActiveTrack,timeline.Cursor)));}
  void Cut(){if(busy)throw new Exception("Espera o cancela la tarea en curso");if(playing&&renderToken!=null)FlushPlaybackFrame(renderToken);if(editor.CutSelection(timeline.Selection.ToList(),timeline.Cursor)){if(playing){dirty=true;RefreshProject();}else Changed();status.Text="Clips divididos sin alterar su contenido";}else status.Text="No hay clips editables que dividir en este punto";}
  void Delete(){if(timeline.SelectedJunctionLeft!=null&&timeline.SelectedJunctionRight!=null){if(editor.CanJoinCut(timeline.SelectedJunctionLeft,timeline.SelectedJunctionRight)){Edit(()=>editor.JoinCut(timeline.SelectedJunctionLeft,timeline.SelectedJunctionRight));timeline.SelectedJunctionLeft=null;timeline.SelectedJunctionRight=null;status.Text="Clips unidos";}else status.Text="Esta unión no se puede deshacer con los ajustes actuales";timeline.Invalidate();return;}if(timeline.SelectedGapTrack!=null)Edit(()=>editor.RippleDeleteGap(timeline.SelectedGapTrack,(timeline.SelectedGapStart+timeline.SelectedGapEnd)/2));else{var ids=timeline.Selection.Count>0?timeline.Selection.ToList():timeline.Selected==null?new List<string>():new List<string>{timeline.Selected};if(ids.Count>0)Edit(()=>editor.DeleteSelection(ids));}timeline.SelectedGapTrack=null;timeline.Invalidate();}
  void Copy(){if(timeline.Selected==null)return;var ids=timeline.Selection.Count>1?timeline.Selection:editor.Related(timeline.Selected);clipboard=editor.Project.Active.clips.Where(c=>ids.Contains(c.id)).Select(c=>Store.Json().Deserialize<Clip>(Store.Json().Serialize(Store.Fields(c)))).ToList();clipboardTracks=editor.Project.Active.tracks.Where(t=>clipboard.Any(c=>c.track_id==t.id)).Select(t=>Store.Json().Deserialize<Track>(Store.Json().Serialize(Store.Fields(t)))).ToList();clipboardSequenceId=editor.Project.Active.id;clipboardProjectId=editor.Project.id;clipboardGroups=editor.Project.Active.link_groups.Where(g=>g.clip_ids.All(ids.Contains)).Select(g=>String.Join("|",g.clip_ids)).ToList();clipboardEditGroups=editor.Project.Active.edit_groups.Where(g=>g.clip_ids.All(ids.Contains)).Select(g=>String.Join("|",g.clip_ids)).ToList();MarkInternalClipboard();status.Text="Copiados "+clipboard.Count+" clips";}
  void CutToClipboard(){if(busy)throw new Exception("Espera a que termine la tarea actual");if(timeline.Selected==null)return;var ids=timeline.Selection.Count>1?timeline.Selection.ToList():editor.Related(timeline.Selected);if(ids.Any(id=>{var c=editor.Project.Active.clips.Find(x=>x.id==id);return c==null||editor.Project.Active.tracks.Find(t=>t.id==c.track_id).locked;}))throw new Exception("Una pista de la selección está bloqueada");Copy();if(clipboard==null||clipboard.Count==0)return;Edit(()=>editor.DeleteSelection(ids));status.Text="Cortados "+clipboard.Count+" clips · Ctrl+V para pegar";}
  void Paste(){if(clipboard==null||clipboard.Count==0)return;Edit(()=>editor.Commit(p=>ClipboardCore1020.Paste(p,clipboard,clipboardTracks,clipboardProjectId,clipboardSequenceId,clipboardGroups,clipboardEditGroups,timeline.Cursor)));}
  void ShowForDecision(){if(WindowState==FormWindowState.Minimized)WindowState=FormWindowState.Normal;if(!Visible)Show();BringToFront();Activate();}
  bool MayDiscard(){if(busy){ShowForDecision();MessageBox.Show(this,"Cancela o termina la tarea antes de cambiar de proyecto.","Tarea en curso",MessageBoxButtons.OK,MessageBoxIcon.Information);return false;}if(!dirty)return true;ShowForDecision();var answer=MessageBox.Show(this,"¿Guardar el proyecto antes de continuar?","Cambios sin guardar",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question,MessageBoxDefaultButton.Button1);if(answer==DialogResult.Cancel)return false;if(answer==DialogResult.Yes){SaveProject();return !dirty;}return true;}
  void NewProject(){if(!MayDiscard())return;StopMaskPick01061();CancelTransitionPreviewRender();CancelMgtPreview();CloseMediaInfo();RemoveRecovery(CurrentRecoveryFile);CancelImport();CancelWebWork();CancelShelfWork();CancelProxy();CancelMgtPreview();Stop();timeline.ClearAllTrackHeightOverrides();editor.Reset(Store.New());projectFile=null;ConfigureProjectStorage();displayedSequenceId=null;timeline.Cursor=0;dirty=false;RefreshProject();RefreshPendingUi();SetImage(null);}
  void OpenProject(){if(!MayDiscard())return;using(var d=new OpenFileDialog{Filter="Proyecto Prometheus|*.pthproj|Proyecto anterior|*.prometheus.json"}){if(d.ShowDialog(this)==DialogResult.OK)OpenProjectPath(d.FileName);}}
  void OpenProjectPath(string path){
   ShowOpeningCover(true);
   try{
   bool recovered;var candidate=OpenWithRecovery(path,out recovered);Sam2MaskStore090.Reconnect(candidate,path);
   bool reconnected=false;
   if(ProjectMediaRecovery.RequiresRecovery(candidate)){
    using(var dialog=new ProjectMediaRecovery(candidate,path)){
     if(dialog.ShowDialog(this)!=DialogResult.OK)return;
     reconnected=dialog.Changed;
    }
   }
   StopMaskPick01061();CancelTransitionPreviewRender();CloseMediaInfo();RemoveRecovery(CurrentRecoveryFile);CancelImport();CancelWebWork();CancelShelfWork();CancelProxy();CancelMgtPreview();Stop();
   timeline.ClearAllTrackHeightOverrides();editor.Reset(candidate);projectFile=path;ConfigureProjectStorage();displayedSequenceId=null;dirty=recovered||reconnected;
   RefreshProject();RequestFrame();ResumePendingImports();QueueMissingMgtPreviews();RememberRecentProject01042(path);
   }finally{openingCover.Visible=false;}
  }
  void SaveProjectAs(){SaveProjectToPath(null);}
  void SaveProject(){
   SaveProjectToPath(ProjectStorage.IsProjectFile(projectFile??"")?projectFile:null);
  }
  void SaveProjectToPath(string path){
   if(path==null)using(var dialog=new SaveFileDialog{Filter="Proyecto Prometheus|*.pthproj",DefaultExt="pthproj",AddExtension=true,FileName=editor.Project.name=="Sin título"?"Proyecto.pthproj":editor.Project.name+".pthproj"}){ShowForDecision();if(dialog.ShowDialog(this)!=DialogResult.OK)return;path=dialog.FileName;}
   if(!ProjectStorage.IsProjectFile(path))throw new Exception("Guarda el proyecto con extensión .pthproj");
   if(editor.Project.assets.Any(a=>AssetContainsPath(a,path)))throw new Exception("El proyecto no puede sustituir un medio original");
   RememberSequenceView();var snapshot=Store.Copy(editor.Project);
   snapshot.name=Path.GetFileNameWithoutExtension(path);
   ProjectResourceTransfer.Materialize(snapshot,path);
   EffectResourceStorage.Materialize(snapshot,path);Sam2MaskStore090.Materialize(snapshot,path);
   Store.Save(snapshot,path);RememberRecentProject01042(path);
   string previous=CurrentRecoveryFile;projectFile=path;ConfigureProjectStorage();editor.SyncMaterializedPaths(editor.Project,snapshot);dirty=false;
   RemoveRecovery(previous);RemoveRecovery(CurrentRecoveryFile);
   RefreshProject();status.Text="Proyecto guardado · copia anterior .bak";
  }
  void Export(){ShowExportDialog();}
  void ShowAsset(){mediaView.SetProject(editor.Project);}
  void Relink(string id){
   bool mgt=editor.Project.assets.Any(a=>a.id==id&&MgtMedia.IsMgt(a));
   using(var dialog=new OpenFileDialog{Filter=mgt?"Composiciones MGT|*.mgt*.json;*.mgtproj":"Medios|*.mp4;*.mov;*.wav;*.png;*.jpg;*.jpeg"}){
    if(dialog.ShowDialog()!=DialogResult.OK)return;
    Stop();busy=true;mediaToken=new CancellationTokenSource();var token=mediaToken;string file=dialog.FileName;
    status.Text="Comprobando archivo para reconexión…";
    Task.Factory.StartNew(()=>{
     try{var candidate=Media.Inspect(file,token.Token);UI(()=>{busy=false;try{editor.Commit(p=>{var source=p.assets.Find(x=>x.id==id);if(mgt){MgtMedia.Relink(source,candidate);MgtMedia.ReconcileClipDurations(p,source);MgtMedia.InvalidateSam2(p,source.id);}else Media.RelinkLocal(source,candidate);});Changed();if(mgt)QueueMgtPreview(id);status.Text="Medio reconectado · revisa su contenido";}catch(Exception ex){Error(ex);}});}
     catch(OperationCanceledException){UI(()=>busy=false);}
     catch(Exception ex){UI(()=>{busy=false;Error(ex);});}
    });
   }
  }
  void ReconnectFolder(){
   if(busy)return;
   using(var dialog=new FolderBrowserDialog{Description="Buscar medios faltantes por nombre en esta carpeta y sus subcarpetas"}){
    if(dialog.ShowDialog(this)==DialogResult.OK)RelinkFromFolder(dialog.SelectedPath,false);
   }
  }
  void RelinkFromFolder(string folder,bool silent){
   if(busy)return;
   int offline=editor.Project.assets.Count(a=>{Store.NormalizeAsset(a);return a.source.kind=="local"&&Media.SelectRepresentation(a,false)==null;});
   if(offline==0){status.Text="No hay medios locales faltantes";return;}
   busy=true;mediaToken=new CancellationTokenSource();var token=mediaToken;var snapshot=Store.Copy(editor.Project);int revision=snapshot.revision;
   status.Text="Buscando "+offline+" medios faltantes por nombre… Esc cancela";
   Task.Factory.StartNew(()=>{
    try{var plan=Media.FindRelinks(snapshot,folder,token.Token);UI(()=>{
     busy=false;if(token.IsCancellationRequested)return;
     if(editor.Project.revision!=revision){status.Text="El proyecto cambió durante la búsqueda. Repite la reconexión.";return;}
     try{
      if(plan.matches.Count>0){editor.Commit(p=>{foreach(var match in plan.matches)Media.RelinkLocal(p.assets.Find(a=>a.id==match.Key),match.Value);});Changed();}
      else{RefreshProject();RequestFrame();}
      string summary="Reconectados "+plan.matches.Count+" de "+offline+" medios · "+plan.missing.Count+" no encontrados · "+plan.ambiguous.Count+" ambiguos";
      status.Text=summary;ResumePendingImports();
      if(!silent)MessageBox.Show(summary+(plan.ambiguous.Count>0?"\n\nAmbiguos: "+String.Join(", ",plan.ambiguous.Take(8)):""),"Reconectar medios",MessageBoxButtons.OK,MessageBoxIcon.Information);
     }catch(Exception ex){Error(ex);}
    });}
    catch(OperationCanceledException){UI(()=>{busy=false;status.Text="Búsqueda de medios cancelada";});}
    catch(Exception ex){UI(()=>{busy=false;if(silent)status.Text="No se pudo buscar medios: "+ex.Message;else Error(ex);});}
   });
  }
  void UpdateKeyframePanel01042(Clip clip){
   bool active=clip!=null&&clip.animation!=null&&clip.animation.Any(c=>c.enabled);
   keyframeToggle.Visible=active;keyframeToggle.Text=keyframeCollapsed?"▴  Fotogramas clave":"▾  Fotogramas clave";
   effectSplit.Panel2Collapsed=!active||keyframeCollapsed;
  }
  void ShowClip(){MaskSelectionChanged0107();effectControlsView.PlayheadTime=timeline.Cursor;var colorClip=selectedTransitionId==null&&selectedEffectTrackId==null?editor.Project.Active.clips.Find(c=>c.id==timeline.Selected):null;effectsCatalogView.SetPreferredKind(colorClip==null?null:editor.Project.Active.tracks.Find(t=>t.id==colorClip.track_id).kind);ShowSam2Overlay0104(colorClip==null?null:colorClip.id);keyframeView.SetClip(colorClip,timeline.Cursor);UpdateKeyframePanel01042(colorClip);RefreshMonitorTransform();colorPanel.SetContext(editor.Project,colorClip,timeline.Cursor);ShowMgtClip();var transition=editor.Project.Active.transitions.Find(t=>t.id==selectedTransitionId);if(transition!=null){effectControlsView.ShowTransition(editor.Project,transition);return;}selectedTransitionId=null;timeline.SelectedTransitionId=null;var track=selectedEffectTrackId==null?null:editor.Project.Active.tracks.Find(t=>t.id==selectedEffectTrackId);if(track!=null)effectControlsView.ShowTrack(editor.Project,track);else effectControlsView.ShowClip(editor.Project,editor.Project.Active.clips.Find(x=>x.id==timeline.Selected));}
  public void LoadFixture(string file){timeline.ClearAllTrackHeightOverrides();editor.Reset(Store.Load(file));projectFile=file;ConfigureProjectStorage();displayedSequenceId=null;dirty=false;RefreshProject();RequestFrame();QueueMissingMgtPreviews();}
  void Recover(){string file=UnsavedRecoveryFile;if(!File.Exists(file))return;if(MessageBox.Show("Hay una recuperación de un proyecto sin guardar. ¿Abrirla?","Recuperar proyecto",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;try{timeline.ClearAllTrackHeightOverrides();editor.Reset(Store.Load(file));projectFile=null;ConfigureProjectStorage();displayedSequenceId=null;dirty=true;RefreshProject();RequestFrame();ResumePendingImports();QueueMissingMgtPreviews();}catch(Exception ex){Error(ex);}}
  void OnClosingRequest(object sender,FormClosingEventArgs e){if(busy){ShowForDecision();if(MessageBox.Show(this,"Hay una tarea en curso. ¿Cancelarla y cerrar?","Cerrar",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes){e.Cancel=true;return;}if(mediaToken!=null)mediaToken.Cancel();if(sam2Token090!=null)sam2Token090.Cancel();busy=false;}if(!MayDiscard()){e.Cancel=true;return;}if(importToken!=null)CancelImport();CancelWebWork();CancelShelfWork();UnregisterShelfHotkey();shelfCaptureTimer.Dispose();CancelProxy();CancelMgtPreview();CancelTransitionPreviewRender();StopWaveforms();StopMaskPick01061();Sam2Worker01023.Shutdown();CloseMediaInfo();closing=true;workspace.Shutdown();Stop();recover.Stop();recover.Dispose();if(animatedSubtitles0820!=null)animatedSubtitles0820.Dispose();if(maskOverlayCache01058!=null){maskOverlayCache01058.Dispose();maskOverlayCache01058=null;}if(shelfWindow!=null&&!shelfWindow.IsDisposed)shelfWindow.Dispose();interactionPreviewTimer.Dispose();if(!App.Capture)RemoveRecovery(CurrentRecoveryFile);}
  void KeysDown(object s,KeyEventArgs e){if(monitor.MaskEditing){if(e.KeyCode==Keys.Escape)monitor.CancelMaskEdit();else if(e.KeyCode==Keys.Enter)monitor.AcceptMaskEdit();else return;e.Handled=true;e.SuppressKeyPress=true;return;}if(e.KeyCode==Keys.Escape&&FocusedChild(this) is TextBoxBase&&transcriptToken0820==null)return;if(e.KeyCode==Keys.Escape&&textToolActive){SetTool(Timeline.ToolMode.Select);e.Handled=true;e.SuppressKeyPress=true;return;}if(e.KeyCode==Keys.Escape){if(timeline.CancelCurrentGesture()){status.Text="Gesto cancelado";e.Handled=true;e.SuppressKeyPress=true;return;}CancelTransitionPreviewRender();Stop();if(transcriptToken0820!=null){transcriptToken0820.Cancel();status.Text="Transcripción cancelada";}if(mediaToken!=null)mediaToken.Cancel();if(importToken!=null){CancelImport();status.Text="Importación cancelada";}if(webImportToken!=null||webPreviewToken!=null||webResolveToken!=null||placeholderLinkToken!=null){CancelWebWork();status.Text="Tarea web cancelada";}if(proxyToken!=null){CancelProxy();status.Text="Proxy cancelado";}e.Handled=true;return;}var focused=FocusedChild(this);if(focused is NumericUpDown||focused is TextBoxBase||focused is TrackBar||focused is ComboBox)return;if(keyframeView.ContainsFocus&&keyframeView.HandleShortcut(e.KeyData)){e.Handled=true;e.SuppressKeyPress=true;return;}if(e.KeyCode==Keys.Delete&&timelineView.SubtitleStrip.HandleDelete()){e.Handled=true;e.SuppressKeyPress=true;return;}if(e.KeyCode==Keys.Delete&&instructionView.ContainsFocus&&instructionView.DeleteSelectedCard()){e.Handled=true;e.SuppressKeyPress=true;return;}if(e.KeyCode==Keys.Delete&&effectControlsView.ContainsFocus&&effectControlsView.DeleteSelectedEffect()){e.Handled=true;e.SuppressKeyPress=true;return;}try{if(e.KeyCode==Keys.Delete&&e.Modifiers==Keys.None&&DeleteSelectedTransition()){e.Handled=true;e.SuppressKeyPress=true;return;}if(ExecuteShortcut(e.KeyData)){e.Handled=true;e.SuppressKeyPress=true;}}catch(Exception ex){Error(ex);e.Handled=true;e.SuppressKeyPress=true;}}
 }
 static class App {public static bool Capture;[STAThread]static void Main(string[] args){TlsDefaults.Apply();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.ThreadException+=(s,e)=>MessageBox.Show(e.Exception.Message,"Prometheus");Capture=args.Length>=2&&args[0]=="--capture-ui";var form=new MainForm();if(Capture){if(args.Length>2)form.Shown+=(s,e)=>form.LoadFixture(args[2]);var timer=new System.Windows.Forms.Timer{Interval=3500};timer.Tick+=(s,e)=>{timer.Stop();using(var b=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(args[1],System.Drawing.Imaging.ImageFormat.Png);}form.Close();};timer.Start();}else if(args.Length>0&&File.Exists(args[0]))form.StartupProjectPath=args[0];Application.Run(form);}}
}













































