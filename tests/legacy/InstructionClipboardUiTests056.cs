using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class InstructionClipboardUiTests056 {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  using(var form=new MainForm()){
   form.Show();Application.DoEvents();var editor=(Editor)typeof(MainForm).GetField("editor",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
   var view=(InstructionPanelView)typeof(MainForm).GetField("instructionView",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
   string sourceId=null,targetId=null;
   editor.Commit(p=>{
    var track=p.Active.tracks.First(t=>t.kind=="video");var asset=GeneratedMedia.CreateSolid("Prueba","446688",4);p.assets.Add(asset);
    var first=new Clip{id=Store.Id(),name="Origen",asset_id=asset.id,stream_id=asset.streams[0].id,track_id=track.id,timeline_start=Rational.Frame(0),duration=Rational.Frame(4),source_in=Rational.Time(0)};
    var second=new Clip{id=Store.Id(),name="Destino",asset_id=asset.id,stream_id=asset.streams[0].id,track_id=track.id,timeline_start=Rational.Frame(5),duration=Rational.Frame(4),source_in=Rational.Time(0)};
    sourceId=first.id;targetId=second.id;p.Active.clips.Add(first);p.Active.clips.Add(second);ClipFraming.Set(first,"fit",1.7,0.2,0);first.effects.Add(EffectContracts.New(EffectContracts.BlurId));
   });
   typeof(MainForm).GetMethod("RefreshProject",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);
   typeof(MainForm).GetMethod("CopyClipAttributes",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{sourceId});
   Check(Clipboard.GetDataObject().GetDataPresent(InstructionPanelView.AttributesClipboardFormat),"Atributos copiados al portapapeles");
   view.SetContext(editor.Project,new[]{targetId},null);view.AddAttributesFromClipboard();
   Check(view.Document.actions.Count==1&&view.Document.actions[0].op=="clip.paste_attributes"&&view.Document.actions[0].clip_ids.SequenceEqual(new[]{targetId}),"Portapapeles cargado como tarjeta");
   typeof(MainForm).GetMethod("PasteClipAttributes",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{new List<string>{targetId}});
   var target=editor.Project.Active.clips.Find(c=>c.id==targetId);Check(ClipFraming.Read(target).scale==1.7&&target.effects.Count==1,"Pegado directo usa los mismos atributos");
   editor.Undo();Check(editor.Project.Active.clips.Find(c=>c.id==targetId).effects.Count==0,"Pegado directo se deshace en un paso");
   typeof(MainForm).GetField("dirty",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,false);
   typeof(MainForm).GetField("busy",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,false);form.Close();
  }
  Console.WriteLine("InstructionClipboardUiTests056 OK");
  Environment.Exit(0);
 }
}
