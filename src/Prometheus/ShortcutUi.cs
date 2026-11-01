using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 // A Form normally treats arrows as focus navigation before KeyDown. Capture them at dialog-key level.
 public class ShortcutCaptureForm:Form {
  public Func<Keys,bool> CaptureKey;
  protected override bool ProcessDialogKey(Keys keyData){return CaptureKey!=null&&CaptureKey(keyData)||base.ProcessDialogKey(keyData);}
  protected override bool ProcessCmdKey(ref Message message,Keys keyData){return CaptureKey!=null&&CaptureKey(keyData)||base.ProcessCmdKey(ref message,keyData);}
 }
 public partial class MainForm {
  readonly ShortcutMap shortcutMap=new ShortcutMap(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","shortcuts-v1.json"));
  readonly Dictionary<Button,string> shortcutButtons=new Dictionary<Button,string>();
  readonly List<ContextMenuStrip> shortcutMenus=new List<ContextMenuStrip>();
  static string ToolCommand(Timeline.ToolMode mode){switch(mode){case Timeline.ToolMode.Select:return "tool.select";case Timeline.ToolMode.Razor:return "tool.razor";case Timeline.ToolMode.Zoom:return "tool.zoom";case Timeline.ToolMode.Hand:return "tool.hand";case Timeline.ToolMode.Slip:return "tool.slip";case Timeline.ToolMode.TrackForward:return "tool.forward";case Timeline.ToolMode.TrackBackward:return "tool.backward";case Timeline.ToolMode.TrackUp:return "tool.up";case Timeline.ToolMode.TrackDown:return "tool.down";case Timeline.ToolMode.TrackAll:return "tool.all";default:throw new ArgumentException("Herramienta desconocida");}}
  void RegisterShortcutButton(Button button,string id){
   shortcutButtons[button]=id;
   var menu=new ContextMenuStrip();shortcutMenus.Add(menu);button.ContextMenuStrip=menu;
   menu.Opening+=(s,e)=>{string current=shortcutButtons[button];menu.Items.Clear();menu.Items.Add("Asignar atajo…",null,(x,y)=>CaptureShortcut(current));menu.Items.Add("Quitar atajo",null,(x,y)=>ChangeShortcut(current,Keys.None));menu.Items.Add("Restaurar predeterminado",null,(x,y)=>ChangeShortcut(current,shortcutMap.Command(current).Default));};
   RefreshShortcutTooltips();
  }
  void RefreshShortcutTooltips(){foreach(var pair in shortcutButtons){string caption=shortcutMap.Command(pair.Value).Name+" ("+ShortcutMap.Label(shortcutMap.Get(pair.Value))+")";pair.Key.AccessibleName=caption;pair.Key.AccessibleDescription=caption;help.SetToolTip(pair.Key,caption);}}
  bool ChangeShortcut(string id,Keys key){
   if(id=="shelf.capture"&&key!=Keys.None&&(key&ShortcutMap.ModifierMask)==Keys.None){MessageBox.Show(this,"La captura global necesita Ctrl, Alt o Mayús junto a una tecla.","Atajo global",MessageBoxButtons.OK,MessageBoxIcon.Information);return false;}
   string conflict=shortcutMap.Conflict(id,key);
   if(conflict!=null&&MessageBox.Show(this,"«"+ShortcutMap.Label(key)+"» ya pertenece a «"+shortcutMap.Command(conflict).Name+"». ¿Asignarlo a «"+shortcutMap.Command(id).Name+"»?","Atajo en uso",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return false;
   shortcutMap.Assign(id,key,true);if(id=="shelf.capture"||conflict=="shelf.capture")RegisterShelfHotkey();RefreshShortcutTooltips();status.Text="Atajo de "+shortcutMap.Command(id).Name+": "+ShortcutMap.Label(key);return true;
  }
  void CaptureShortcut(string id){
   using(var dialog=new ShortcutCaptureForm{Text="Asignar atajo · "+shortcutMap.Command(id).Name,Width=390,Height=190,FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,KeyPreview=true,BackColor=panel,ForeColor=text}){
    var instruction=new Label{Text="Pulsa la combinación deseada",Left=18,Top=16,Width=345,Height=25};
    var display=new Label{Text=ShortcutMap.Label(shortcutMap.Get(id)),Left=18,Top=45,Width=345,Height=30,TextAlign=ContentAlignment.MiddleCenter,Font=new Font("Segoe UI",13,FontStyle.Bold)};
    Keys candidate=shortcutMap.Get(id);
    var save=new Button{Text="Guardar",Left=175,Top=103,Width=85,Enabled=candidate!=Keys.None,DialogResult=DialogResult.OK};
    var cancel=new Button{Text="Cancelar",Left=270,Top=103,Width=85,DialogResult=DialogResult.Cancel};
    dialog.CaptureKey=key=>{if(key==Keys.Escape){dialog.DialogResult=DialogResult.Cancel;dialog.Close();return true;}if(!ShortcutMap.Valid(key))return false;candidate=key;display.Text=ShortcutMap.Label(candidate);save.Enabled=true;return true;};
    dialog.Controls.Add(instruction);dialog.Controls.Add(display);dialog.Controls.Add(save);dialog.Controls.Add(cancel);dialog.AcceptButton=save;dialog.CancelButton=cancel;
    if(dialog.ShowDialog(this)==DialogResult.OK&&candidate!=Keys.None)ChangeShortcut(id,candidate);
   }
  }
  void ShowShortcutSettings(){
   using(var dialog=new Form{Text="Ajustes · Atajos de teclado",Width=550,Height=550,StartPosition=FormStartPosition.CenterParent,BackColor=panel,ForeColor=text,MinimumSize=new Size(440,350)}){
    var list=new ListView{Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,MultiSelect=false,BackColor=bg,ForeColor=text,BorderStyle=BorderStyle.None};list.Columns.Add("Acción",330);list.Columns.Add("Atajo",150);
    Action refresh=()=>{string selected=list.SelectedItems.Count>0?(string)list.SelectedItems[0].Tag:null;list.Items.Clear();foreach(var command in ShortcutMap.Commands){var item=new ListViewItem(command.Name){Tag=command.Id};item.SubItems.Add(ShortcutMap.Label(shortcutMap.Get(command.Id)));list.Items.Add(item);if(command.Id==selected)item.Selected=true;}};refresh();
    var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=48,Padding=new Padding(8,8,0,0)};
    var assign=Button("Asignar…",()=>{if(list.SelectedItems.Count==0)return;CaptureShortcut((string)list.SelectedItems[0].Tag);refresh();});
    var remove=Button("Quitar",()=>{if(list.SelectedItems.Count==0)return;ChangeShortcut((string)list.SelectedItems[0].Tag,Keys.None);refresh();});
    var reset=Button("Restaurar",()=>{if(list.SelectedItems.Count==0)return;string id=(string)list.SelectedItems[0].Tag;ChangeShortcut(id,shortcutMap.Command(id).Default);refresh();});
    var wheel=Button("Zoom con rueda…",ShowWheelZoomSettings);buttons.Controls.Add(wheel);buttons.Controls.Add(assign);buttons.Controls.Add(remove);buttons.Controls.Add(reset);list.DoubleClick+=(s,e)=>{if(list.SelectedItems.Count>0){CaptureShortcut((string)list.SelectedItems[0].Tag);refresh();}};
    dialog.Controls.Add(list);dialog.Controls.Add(buttons);dialog.ShowDialog(this);
   }
  }
  void ShowWheelZoomSettings(){
   using(var dialog=new Form{Text="Zoom con rueda",Width=350,Height=180,FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,BackColor=panel,ForeColor=text}){
    var caption=new Label{Text="Alt + rueda: zoom; Ctrl + rueda: pistas (Premiere).",Left=16,Top=16,Width=310,Height=25};
    var choices=new ComboBox{Left=16,Top=45,Width=305,DropDownStyle=ComboBoxStyle.DropDownList};
    Keys[] values={Keys.Control|Keys.Alt,Keys.Control,Keys.Alt,Keys.Shift,Keys.Control|Keys.Shift,Keys.Alt|Keys.Shift,Keys.Control|Keys.Alt|Keys.Shift};
    foreach(Keys value in values)choices.Items.Add(ShortcutMap.WheelLabel(value));
    choices.SelectedIndex=Math.Max(0,Array.IndexOf(values,shortcutMap.WheelZoomModifiers));
    var save=new Button{Text="Guardar",Left=142,Top=87,Width=85,DialogResult=DialogResult.OK};
    var cancel=new Button{Text="Cancelar",Left=236,Top=87,Width=85,DialogResult=DialogResult.Cancel};
    dialog.Controls.Add(caption);dialog.Controls.Add(choices);dialog.Controls.Add(save);dialog.Controls.Add(cancel);dialog.AcceptButton=save;dialog.CancelButton=cancel;
    if(dialog.ShowDialog(this)==DialogResult.OK){shortcutMap.AssignWheelZoom(values[choices.SelectedIndex]);timeline.WheelZoomModifiers=shortcutMap.WheelZoomModifiers;timeline.WheelTrackModifiers=shortcutMap.WheelTrackModifiers;status.Text="Zoom: "+ShortcutMap.WheelLabel(shortcutMap.WheelZoomModifiers)+" · pistas: "+ShortcutMap.WheelLabel(shortcutMap.WheelTrackModifiers);}
   }
  }
  void ShowSettingsMenu(Button anchor){var menu=new ContextMenuStrip();menu.Items.Add("Lienzo de la secuencia…",null,(s,e)=>ShowSequenceCanvasSettings());menu.Items.Add("Atajos de teclado…",null,(s,e)=>ShowShortcutSettings());menu.Items.Add("Medios web…",null,(s,e)=>{ShowWebSettings();});menu.Closed+=(s,e)=>DisposePopupLater(menu);menu.Show(anchor,new Point(0,anchor.Height));}
  void RippleDeleteShortcut(){
   if(timeline.SelectedGapTrack!=null){string track=timeline.SelectedGapTrack;double time=(timeline.SelectedGapStart+timeline.SelectedGapEnd)/2;Edit(()=>editor.RippleDeleteGap(track,time));timeline.SelectedGapTrack=null;timeline.Invalidate();return;}
   var selected=timeline.Selection.Distinct().ToList();
   if(selected.Count==0){status.Text="Selecciona un clip o un hueco para eliminar y cerrar espacio";return;}
   var clip=editor.Project.Active.clips.Find(c=>c.id==selected[0]);
   var related=clip==null?new List<string>():editor.Related(clip.id);
   if(clip==null||selected.Any(id=>!related.Contains(id))||!CanRippleDeleteClip(clip,related)){status.Text="Elige un único grupo sin pistas bloqueadas para cerrar el espacio";return;}
   Edit(()=>editor.RippleDelete(clip.id));
  }
  bool ExecuteShortcut(Keys key){
   string id=shortcutMap.CommandAt(key);if(id==null)return false;
   if(id=="preview.render"&&!timeline.ContainsFocus)return false;
   switch(id){
    case "tool.select":SetTool(Timeline.ToolMode.Select);break;
    case "tool.text":ActivateTextTool();break;
    case "tool.mask":timelineView.ActivateLastMaskTool();break;
    case "tool.razor":SetTool(Timeline.ToolMode.Razor);break;
    case "tool.zoom":SetTool(Timeline.ToolMode.Zoom);break;
    case "tool.hand":SetTool(Timeline.ToolMode.Hand);break;
    case "tool.slip":SetTool(Timeline.ToolMode.Slip);break;
    case "tool.forward":SetTool(Timeline.ToolMode.TrackForward);break;
    case "tool.backward":SetTool(Timeline.ToolMode.TrackBackward);break;
    case "tool.up":SetTool(Timeline.ToolMode.TrackUp);break;
    case "tool.down":SetTool(Timeline.ToolMode.TrackDown);break;
    case "tool.all":SetTool(Timeline.ToolMode.TrackAll);break;
    case "edit.divide":Cut();break;
    case "edit.undo":Edit(()=>editor.Undo());break;
    case "edit.redo":case "edit.redo.alias":Edit(()=>editor.Redo());break;
    case "edit.copy":Copy();break;
    case "edit.cut":CutToClipboard();break;
    case "edit.paste":PasteContextual();break;
    case "edit.delete":Delete();break;
    case "edit.ripple-delete":RippleDeleteShortcut();break;
    case "edit.paste-attributes":if(HasCopiedAttributes()&&EditableClips(timeline.Selection))PasteClipAttributes(timeline.Selection.ToList());else status.Text="Selecciona clips y copia primero sus atributos";break;
    case "timeline.snap":timeline.Snap=!timeline.Snap;UpdateSnapButton();status.Text="Ajuste magnético "+(timeline.Snap?"activado":"desactivado");break;
    case "project.save":SaveProject();break;
    case "project.open":OpenProject();break;
   case "shelf.capture":break;
    case "transport.play":ExecuteEditorAction(new EditorActionRequest{id="transport.toggle"});break;
    case "preview.render":RenderRedPreviews();break;
    case "nav.frame.previous":ExecuteEditorAction(new EditorActionRequest{id="playhead.step_frames",frames=-1});break;
    case "nav.frame.next":ExecuteEditorAction(new EditorActionRequest{id="playhead.step_frames",frames=1});break;
    case "nav.five.previous":ExecuteEditorAction(new EditorActionRequest{id="playhead.step_frames",frames=-5});break;
    case "nav.five.next":ExecuteEditorAction(new EditorActionRequest{id="playhead.step_frames",frames=5});break;
    case "nav.cut.next":ExecuteEditorAction(new EditorActionRequest{id="playhead.boundary.next"});break;
    case "nav.cut.previous":ExecuteEditorAction(new EditorActionRequest{id="playhead.boundary.previous"});break;
     case "marker.add":AddMarkerAt(timeline.Cursor);break;
     case "export.mark-in":MarkExportIn(timeline.Cursor);break;
     case "export.mark-out":MarkExportOut(timeline.Cursor);break;
     case "export.clear-marks":ClearExportMarks();break;
     case "nav.marker.previous":ExecuteEditorAction(new EditorActionRequest{id="playhead.marker.previous"});break;
     case "nav.marker.next":ExecuteEditorAction(new EditorActionRequest{id="playhead.marker.next"});break;
   }
   return true;
  }
 }
}
