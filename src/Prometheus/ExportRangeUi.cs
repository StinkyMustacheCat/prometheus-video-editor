using System;

namespace Prometheus {
 public partial class MainForm {
  void SetupExportRange(){timeline.MarkInRequested=MarkExportIn;timeline.MarkOutRequested=MarkExportOut;timeline.ClearExportMarksRequested=ClearExportMarks;}
  void MarkExportIn(double time){Edit(()=>editor.MarkExportIn(time));status.Text="Entrada: "+Timeline.TimeLabel(time);}
  void MarkExportOut(double time){Edit(()=>editor.MarkExportOut(time));status.Text="Salida: "+Timeline.TimeLabel(time);}
  void ClearExportMarks(){var marks=SequenceExportRange.Read(editor.Project.Active);if(!marks.InFrame.HasValue&&!marks.OutFrame.HasValue)return;Edit(()=>editor.ClearExportMarks());status.Text="Entrada y salida borradas";}
 }
}
