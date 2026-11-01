using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class EffectUiTests0453 {
 static void Check(bool yes,string label){if(!yes)throw new Exception(label);Console.WriteLine("PASS "+label);}
 static object Field(object owner,string name){return owner.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(owner);}
 static void Set(object owner,string name,object value){owner.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(owner,value);}
 static void Call(MainForm form,string name,params object[] args){form.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,args);Application.DoEvents();}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Public|BindingFlags.Static).SetValue(null,true);
  using(var form=new MainForm()){
   form.Show();Application.DoEvents();
   string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"lut-ui-final-"+Guid.NewGuid().ToString("N"));
   Directory.CreateDirectory(directory);
   try{
    var editor=(Editor)Field(form,"editor");var timeline=(Timeline)Field(form,"timeline");
    var asset=GeneratedMedia.CreateSolid("Base","808080",1);editor.Commit(p=>{p.assets.Add(asset);Media.Insert(p,asset.id,p.Active.tracks.First(t=>t.kind=="video").id,0);});
    string clipId=editor.Project.Active.clips.Single().id;
    timeline.Selected=clipId;timeline.Selection.Add(clipId);Call(form,"RefreshProject");
    string original=Path.Combine(directory,"origen.cube");
    using(var writer=new StreamWriter(original)){writer.WriteLine("LUT_3D_SIZE 2");for(int i=0;i<8;i++)writer.WriteLine("1 0 0");}
    Call(form,"ApplyLut",clipId,original);
    string resourceId=editor.Project.effect_resources.Single().id;
    string projectFile=Path.Combine(directory,"Proyecto.prometheus.json");Set(form,"projectFile",projectFile);
    Call(form,"SaveProject");
    string local=editor.Project.effect_resources.Single().uri;
    Check(File.Exists(local)&&local.StartsWith(ProjectStorage.LutDirectory(projectFile),StringComparison.OrdinalIgnoreCase),
     "Guardar proyecto traslada el LUT a su carpeta de datos");
    File.Delete(original);File.Delete(local);Call(form,"ShowClip");
    var inspector=(InspectorPanelView)Field(form,"inspectorView");
    Check(inspector.Controls.OfType<Button>().Any(b=>b.Text.StartsWith("Localizar LUT")),
     "el Inspector ofrece reconectar cuando falta el archivo");
    string replacement=Path.Combine(directory,"repuesto.cube");
    using(var writer=new StreamWriter(replacement)){writer.WriteLine("LUT_3D_SIZE 2");for(int i=0;i<8;i++)writer.WriteLine("0 1 0");}
    Call(form,"ApplyLutRelink",resourceId,replacement);
    Check(editor.Project.effect_resources.Single().uri==replacement&&editor.Project.Active.clips.Single().effects.Count==1,
     "reconectar conserva el efecto y actualiza su recurso");
   }finally{Directory.Delete(directory,true);}
  }
 }
}
