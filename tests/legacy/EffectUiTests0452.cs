using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class EffectUiTests0452 {
 static void Check(bool yes,string label){if(!yes)throw new Exception(label);Console.WriteLine("PASS "+label);}
 static object Field(object obj,string name){return obj.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(obj);}
 static void Call(MainForm form,string name,params object[] args){form.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,args);Application.DoEvents();}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Public|BindingFlags.Static).SetValue(null,true);
  using(var form=new MainForm()){
   form.Show();Application.DoEvents();
   var editor=(Editor)Field(form,"editor");var timeline=(Timeline)Field(form,"timeline");
   var asset=GeneratedMedia.CreateSolid("Fondo","808080",1);
   editor.Commit(p=>{
    p.assets.Add(asset);
    foreach(var track in p.Active.tracks.Where(t=>t.kind=="video"))Media.Insert(p,asset.id,track.id,0);
   });
   Call(form,"RefreshProject");
   var ids=editor.Project.Active.clips.Select(c=>c.id).ToList();
   timeline.Selected=ids[0];timeline.Selection=ids;
   Call(form,"ShowClip");
   var inspector=(InspectorPanelView)Field(form,"inspectorView");
   Check(inspector.Controls.OfType<Button>().Any(b=>b.Text.StartsWith("Añadir LUT")),"el Inspector ofrece LUT al seleccionar un clip visual");
   string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"lut-ui-"+Guid.NewGuid().ToString("N"));
   Directory.CreateDirectory(directory);
   try{
    string lut=Path.Combine(directory,"test.cube");
    File.WriteAllText(lut,"LUT_3D_SIZE 2\n0 0 0\n1 0 0\n0 1 0\n1 1 0\n0 0 1\n1 0 1\n0 1 1\n1 1 1\n");
    Call(form,"ApplyLut",ids[0],lut);
    Check(editor.Project.Active.clips.All(c=>c.effects.Count==1)&&editor.Project.effect_resources.Count==1,
     "el LUT se aplica en una sola operación a la selección múltiple");
    var effectId=editor.Project.Active.clips.First().effects.Single().id;
    Call(form,"SetLutEnabled",ids[0],effectId,false);
    Check(editor.Project.Active.clips.All(c=>!c.effects.Single().enabled),"activar o desactivar afecta a los clips seleccionados");
    editor.Undo();Call(form,"RefreshProject");
    Check(editor.Project.Active.clips.All(c=>c.effects.Single().enabled),"deshacer recupera el efecto activo");
    Call(form,"RemoveEffect",ids[0],effectId);
    Check(editor.Project.Active.clips.All(c=>c.effects.Count==0)&&editor.Project.effect_resources.Count==0,
     "quitar LUT limpia la selección y el recurso sin referencias");
   }finally{Directory.Delete(directory,true);}
  }
 }
}
