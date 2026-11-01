using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class InstructionTypeFilterUiTests0562 {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static ComboBox Box(InstructionActionDialog form,string name){return (ComboBox)typeof(InstructionActionDialog).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();var p=Store.New();var video=p.Active.tracks.First(t=>t.kind=="video");var audio=p.Active.tracks.First(t=>t.kind=="audio");
  var asset=GeneratedMedia.CreateSolid("Visual","336699",4);p.assets.Add(asset);var clip=new Clip{id=Store.Id(),name="Visual",asset_id=asset.id,stream_id=asset.streams[0].id,track_id=video.id,timeline_start=Rational.Frame(0),duration=Rational.Frame(4),source_in=Rational.Time(0)};p.Active.clips.Add(clip);
  using(var dialog=new InstructionActionDialog(p,new List<string>{clip.id},new InstructionAction{op="effect.apply",scope="sequence",catalog_id=EffectContracts.SharpenId,sequence_id=p.active_sequence_id})){
   Check(Box(dialog,"track").Items.Count>0&&Box(dialog,"track").Items.Cast<object>().All(x=>x.ToString().Contains("video")),"Nitidez solo ofrece pistas de vídeo");
   Check(Box(dialog,"clip").Items.Count==1,"Nitidez solo ofrece clips de vídeo");
   Check(Box(dialog,"scope").Items.Cast<object>().Any(x=>x.ToString().Contains("Todos los clips de vídeo")),"El alcance explica que filtra por tipo");
  }
  using(var dialog=new InstructionActionDialog(p,new List<string>{clip.id},new InstructionAction{op="effect.apply",scope="sequence",catalog_id=EffectContracts.HighPassId,sequence_id=p.active_sequence_id})){
   Check(Box(dialog,"track").Items.Count>0&&Box(dialog,"track").Items.Cast<object>().All(x=>x.ToString().Contains("audio")),"El efecto de audio solo ofrece pistas de audio");
   Check(Box(dialog,"clip").Items.Count==0,"El vídeo no aparece como destino de un efecto de audio");
  }
  Console.WriteLine("InstructionTypeFilterUiTests0562 OK");
 }
}
