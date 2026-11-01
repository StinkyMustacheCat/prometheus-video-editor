using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  bool HasCopiedAttributes(){try{var data=Clipboard.GetDataObject();return data!=null&&data.GetDataPresent(InstructionPanelView.AttributesClipboardFormat);}catch{return false;}}
  void CopyClipAttributes(string clipId){
   var snapshot=ClipAttributes.Capture(editor.Project,clipId);
   var data=new DataObject();data.SetData(InstructionPanelView.AttributesClipboardFormat,ClipAttributes.Encode(snapshot));
   Clipboard.SetDataObject(data,true);status.Text="Atributos copiados · "+(snapshot.source_kind=="video"?"imagen y efectos":"audio y efectos");
  }
  void PasteClipAttributes(IEnumerable<string> clipIds){
   var clipboardData=Clipboard.GetDataObject();
   if(clipboardData==null||!clipboardData.GetDataPresent(InstructionPanelView.AttributesClipboardFormat))throw new Exception("Copia primero los atributos de un clip");
   var snapshot=ClipAttributes.Parse(clipboardData.GetData(InstructionPanelView.AttributesClipboardFormat) as string);
   var ids=clipIds.Distinct().ToList();if(ids.Count==0)throw new Exception("Selecciona clips de destino");
   var result=new AttributeApplyResult();
   Edit(()=>editor.Commit(p=>{result=ClipAttributes.Apply(p,snapshot,ids);if(result.applied==0)throw new Exception("Ningún clip seleccionado admite estos atributos");}));
   status.Text="Atributos pegados en "+result.applied+" clip(s)"+(result.skipped>0?" · "+result.skipped+" omitido(s) por incompatibilidad o bloqueo":"");
  }
 }
}
