using System;
using System.Drawing;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  readonly TextGraphicsPanel0817 subtitlePanel0817=new TextGraphicsPanel0817(true),textPanel0817=new TextGraphicsPanel0817(false);
  bool selectingText0817;
  void SetupSubtitles0817(){
   var strip=timelineView.SubtitleStrip;
   strip.AddRequested=CreateSubtitle0817;strip.AnimationRequested=ShowSubtitleAnimation0820;strip.AnimationRemoveRequested=RemoveSubtitleAnimation0820;strip.PreviewVisibilityChanged=()=>{bool resume=playing;Stop();if(animatedSubtitles0820!=null)animatedSubtitles0820.SetPreviewEnabled(strip.PreviewVisible);if(resume)PlayPause();else RequestFrame();};strip.Selected=id=>SelectTextClip0817(id);strip.EditRequested=EditTextClip0817;strip.RemoveRequested=RemoveSubtitle0817;strip.DeleteIntervalRequested=DeleteSubtitleInterval0817;strip.SplitRequested=SplitSubtitle0820;strip.DragRequested=DragSubtitle0820;
   foreach(var panel in new[]{subtitlePanel0817,textPanel0817}){
    panel.AddTitleRequested=CreateTitle;panel.AddSubtitleRequested=CreateSubtitle0817;panel.SelectedRequested=SelectTextClip0817;panel.EditRequested=EditTextClip0817;panel.RemoveSubtitleRequested=RemoveSubtitle0817;
   if(panel==subtitlePanel0817){panel.ImportSubtitleRequested=ImportSubtitleFile0820;panel.LoadEmbeddedRequested=LoadEmbeddedSubtitle0820;panel.GenerateTranscriptRequested=GenerateTranscript0820;panel.AnimationRequested=ShowSubtitleAnimation0820;panel.AnimationRemoveRequested=RemoveSubtitleAnimation0820;}
   }
  }
  void SelectTextClip0817(string id){if(selectingText0817)return;selectingText0817=true;try{
   var clip=editor.Project.Active.clips.FirstOrDefault(c=>c.id==id);if(clip==null){
    bool selectedCaption=editor.Project.Active.clips.Any(c=>c.id==timeline.Selected&&SubtitleTrack0817.IsCue(editor.Project,c));
    if(selectedCaption){timeline.Selected=null;timeline.Selection.Clear();ShowClip();}
    if(timelineView.SubtitleStrip.SelectedId!=null)timelineView.SubtitleStrip.ClearSelection();
    return;
   }
   timeline.Selected=id;timeline.Selection=new List<string>{id};selectedEffectTrackId=null;selectedTransitionId=null;
   if(SubtitleTrack0817.IsCue(editor.Project,clip)){timelineView.SubtitleStrip.SetExpanded(true);if(timelineView.SubtitleStrip.SelectedId!=id)timelineView.SubtitleStrip.SelectCue(id);}
   else timeline.RevealTrack(clip.track_id);
   subtitlePanel0817.Select(id);textPanel0817.Select(id);timeline.Invalidate();ShowClip();
  }finally{selectingText0817=false;}}
  void CreateSubtitle0817(){
   using(var dialog=new Form{Text="Añadir subtítulo",ClientSize=new Size(485,250),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,BackColor=panel,ForeColor=text}){
    var caption=new Label{Text="Frase",Left=18,Top=15,Width=440,Height=22};var input=new TextBox{Left=18,Top=42,Width=445,Height=112,Multiline=true,MaxLength=1000};
    var durationLabel=new Label{Text="Duración (HH:MM:SS:FF)",Left=18,Top=169,Width=210,Height=22};var duration=new TextBox{Left=245,Top=167,Width=130,Text="00:00:02:00"};
    var accept=new Button{Text="Añadir",Left=282,Top=207,Width=85,DialogResult=DialogResult.OK};var cancel=new Button{Text="Cancelar",Left=376,Top=207,Width=85,DialogResult=DialogResult.Cancel};
    dialog.Controls.AddRange(new Control[]{caption,input,durationLabel,duration,accept,cancel});dialog.AcceptButton=accept;dialog.CancelButton=cancel;
    if(dialog.ShowDialog(this)!=DialogResult.OK)return;double seconds;if(!MonitorPanelView.TryParseTimecode(duration.Text,out seconds)||seconds<=0)throw new Exception("Escribe una duración válida");
    string cueId=null;Edit(()=>editor.Commit(p=>cueId=SubtitleTrack0817.Add(p,input.Text,timeline.Cursor,seconds).id));
    timelineView.SubtitleStrip.SetExpanded(true);SelectTextClip0817(cueId);status.Text="Subtítulo añadido · edita su aspecto en Controles de efectos";
   }
  }
  void EditTextClip0817(string clipId){
   var original=editor.Project.Active.clips.FirstOrDefault(c=>c.id==clipId);if(original==null)return;var originalAsset=editor.Project.assets.FirstOrDefault(a=>a.id==original.asset_id);if(!GeneratedMedia.IsText(originalAsset))return;
   using(var dialog=new Form{Text=GeneratedMedia.IsSubtitle(originalAsset)?"Editar subtítulo":"Editar texto",ClientSize=new Size(485,250),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,BackColor=panel,ForeColor=text}){
    var caption=new Label{Text="Contenido",Left=18,Top=15,Width=440,Height=22};var input=new TextBox{Left=18,Top=42,Width=445,Height=112,Multiline=true,MaxLength=1000,Text=GeneratedMedia.Text(originalAsset)};
    var durationLabel=new Label{Text="Duración (HH:MM:SS:FF)",Left=18,Top=169,Width=210,Height=22};var duration=new TextBox{Left=245,Top=167,Width=130,Text=Timeline.TimeLabel(original.duration.Seconds)};
    var accept=new Button{Text="Guardar",Left=282,Top=207,Width=85,DialogResult=DialogResult.OK};var cancel=new Button{Text="Cancelar",Left=376,Top=207,Width=85,DialogResult=DialogResult.Cancel};
    dialog.Controls.AddRange(new Control[]{caption,input,durationLabel,duration,accept,cancel});dialog.AcceptButton=accept;dialog.CancelButton=cancel;
    if(dialog.ShowDialog(this)!=DialogResult.OK)return;double seconds;if(!MonitorPanelView.TryParseTimecode(duration.Text,out seconds)||seconds<=0)throw new Exception("Escribe una duración válida");
    Edit(()=>editor.Commit(p=>{var clip=p.Active.clips.Find(c=>c.id==clipId);var asset=p.assets.Find(a=>a.id==clip.asset_id);GeneratedMedia.SetText(asset,input.Text,GeneratedMedia.TextSize(asset),GeneratedMedia.TextColor(asset));clip.duration=Rational.Frame(seconds);asset.recommended_duration=clip.duration;
     string name=(GeneratedMedia.IsSubtitle(asset)?"Subtítulo · ":"Texto · ")+(input.Text.Length>34?input.Text.Substring(0,34)+"…":input.Text);asset.name=name;clip.name=name;
    }));SelectTextClip0817(clipId);
   }
  }
  void RemoveSubtitle0817(string clipId){Edit(()=>editor.Commit(p=>SubtitleTrack0817.Remove(p,clipId)));timelineView.SubtitleStrip.ClearSelection();status.Text="Subtítulo eliminado · Ctrl+Z para deshacer";}
  void DeleteSubtitleInterval0817(string clipId){var cue=editor.Project.Active.clips.FirstOrDefault(c=>c.id==clipId&&SubtitleTrack0817.IsCue(editor.Project,c));if(cue==null)return;
   if(editor.RangeTrimSelection(new List<string>(),cue.timeline_start.Seconds,cue.End)){timelineView.SubtitleStrip.ClearSelection();Changed();status.Text="Intervalo eliminado en todas las pistas · Ctrl+Z para deshacer";}
  }
 }
}
