using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  string displayedSequenceId;
  void SetupSequences(){
   timelineView.SequenceRequested=SwitchSequence;
   timelineView.SequenceCreateRequested=CreateSequence;
   timelineView.SequenceRenameRequested=RenameSequence;
   timelineView.SequenceDuplicateRequested=DuplicateSequence;
   timelineView.SequenceDeleteRequested=DeleteSequence;
  }
  void CheckSequenceChange(){
   if(busy||importToken!=null||webImportToken!=null||placeholderLinkToken!=null)throw new Exception("Termina o cancela la importación antes de cambiar de secuencia");
  }
  void RememberSequenceView(){
   if(displayedSequenceId!=editor.Project.active_sequence_id)return;
   Sequences.WriteView(editor.Project.Active,timeline.Cursor,timeline.Scroll,timeline.Zoom);
  }
  void RestoreSequenceViewOnChange(){
   string id=editor.Project.active_sequence_id;
   if(displayedSequenceId==id)return;
   displayedSequenceId=id;
   timeline.Selection.Clear();timeline.Selected=null;timeline.SelectedGapTrack=null;
   var view=Sequences.ReadView(editor.Project.Active);
   timeline.Cursor=view.Cursor;timeline.Zoom=view.Zoom;timeline.Scroll=view.Scroll;
   var track=editor.Project.Active.tracks.FirstOrDefault(t=>t.kind=="video")??editor.Project.Active.tracks.FirstOrDefault();
   timeline.ActiveTrack=track==null?null:track.id;
  }
  void SwitchSequence(string id){
   if(editor.Project.active_sequence_id==id)return;
   CheckSequenceChange();Stop();RememberSequenceView();editor.SwitchSequence(id);dirty=true;
   RefreshProject();RequestFrame();status.Text="Secuencia: "+editor.Project.Active.name;
  }
  string AskSequenceName(string caption,string initial){
   using(var dialog=new Form{Text=caption,ClientSize=new Size(380,120),FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,StartPosition=FormStartPosition.CenterParent,BackColor=panel,ForeColor=text,Font=Font}){
    var input=new TextBox{Text=initial,MaxLength=80,Left=15,Top=16,Width=350,BackColor=Color.FromArgb(49,51,56),ForeColor=text};
    var okay=Button("Guardar",()=>{});okay.DialogResult=DialogResult.OK;okay.SetBounds(184,70,85,30);
    var cancel=Button("Cancelar",()=>{});cancel.DialogResult=DialogResult.Cancel;cancel.SetBounds(275,70,90,30);
    dialog.Controls.Add(input);dialog.Controls.Add(okay);dialog.Controls.Add(cancel);dialog.AcceptButton=okay;dialog.CancelButton=cancel;
    if(dialog.ShowDialog(this)!=DialogResult.OK)return null;
    return input.Text.Trim();
   }
  }
  void CreateSequence(){
   CheckSequenceChange();string name=AskSequenceName("Nueva secuencia",Sequences.NextName(editor.Project));if(name==null)return;
   RememberSequenceView();Edit(()=>editor.CreateSequence(name));status.Text="Secuencia creada: "+name;
  }
  void RenameSequence(){
   CheckSequenceChange();string id=editor.Project.active_sequence_id;
   string name=AskSequenceName("Renombrar secuencia",editor.Project.Active.name);if(name==null)return;
   Edit(()=>editor.RenameSequence(id,name));status.Text="Secuencia renombrada: "+name;
  }
  void DuplicateSequence(){
   CheckSequenceChange();string id=editor.Project.active_sequence_id;
   string name=AskSequenceName("Duplicar secuencia",Sequences.NextName(editor.Project,editor.Project.Active.name+" copia"));if(name==null)return;
   RememberSequenceView();Edit(()=>editor.DuplicateSequence(id,name));status.Text="Secuencia duplicada: "+name;
  }
  void DeleteSequence(){
   CheckSequenceChange();if(editor.Project.sequences.Count<=1)throw new Exception("El proyecto debe conservar al menos una secuencia");
   string id=editor.Project.active_sequence_id,name=editor.Project.Active.name;
   var answer=MessageBox.Show(this,"¿Eliminar la secuencia «"+name+"» y sus clips? Los medios seguirán en la biblioteca.","Eliminar secuencia",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2);
   if(answer!=DialogResult.Yes)return;
   Edit(()=>editor.DeleteSequence(id));status.Text="Secuencia eliminada: "+name;
  }
 }
}
