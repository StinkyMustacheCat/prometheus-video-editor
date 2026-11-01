using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  readonly MarkerPanelView markerPanelView=new MarkerPanelView();
  void SetupMarkers(){
   timeline.AddMarkerRequested=AddMarkerAt;
   timeline.MoveMarkerRequested=(id,time)=>Edit(()=>editor.MoveMarker(id,time));
   timeline.RenameMarkerRequested=RenameMarkerPrompt;
   timeline.DeleteMarkerRequested=id=>Edit(()=>editor.DeleteMarker(id));
   timeline.MarkerColorRequested=(id,argb)=>Edit(()=>editor.SetMarkerColor(id,argb));
   markerPanelView.AddRequested=AddMarkerAt;
   markerPanelView.SeekRequested=Seek;
   markerPanelView.RenameRequested=RenameMarkerPrompt;
   markerPanelView.DeleteRequested=id=>Edit(()=>editor.DeleteMarker(id));
  }
  void AddMarkerAt(double time){Edit(()=>editor.AddMarker(time));status.Text="Marcador añadido en "+Timeline.TimeLabel(time);}
  void RenameMarkerPrompt(string id){
   var marker=Markers.Read(editor.Project.Active).FirstOrDefault(value=>value.Id==id);
   if(marker==null)return;
   using(var dialog=new Form{Text="Renombrar marcador",Width=390,Height=155,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,StartPosition=FormStartPosition.CenterParent,BackColor=panel,ForeColor=text}){
    var input=new TextBox{Text=marker.Name,MaxLength=100,Left=15,Top=15,Width=345};
    var okay=new Button{Text="Guardar",DialogResult=DialogResult.OK,Left=190,Top=55,Width=80};
    var cancel=new Button{Text="Cancelar",DialogResult=DialogResult.Cancel,Left=280,Top=55,Width=80};
    dialog.Controls.Add(input);dialog.Controls.Add(okay);dialog.Controls.Add(cancel);
    dialog.AcceptButton=okay;dialog.CancelButton=cancel;
    if(dialog.ShowDialog(this)==DialogResult.OK)Edit(()=>editor.RenameMarker(id,input.Text));
   }
  }
  void NavigateMarker(bool forward){
   var markers=Markers.Read(editor.Project.Active);
   var next=forward?markers.FirstOrDefault(marker=>marker.Seconds>timeline.Cursor+0.000001):markers.LastOrDefault(marker=>marker.Seconds<timeline.Cursor-0.000001);
   if(next!=null)Seek(next.Seconds);
   else status.Text=forward?"No hay otro marcador adelante":"No hay otro marcador atrás";
  }
  void RefreshMarkerPanel(){markerPanelView.CurrentTime=timeline.Cursor;markerPanelView.SetMarkers(editor.Project.Active);}
 }
}
