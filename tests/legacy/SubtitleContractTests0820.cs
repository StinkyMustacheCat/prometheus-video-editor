using System;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class SubtitleContractTests0820 {
 static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
 static void Main(string[] args){Tools.Root=args[0];string fixtures=args[1];
  var txt=SubtitleContract0820.Read(Path.Combine(fixtures,"wordline-ejemplo.txt"),CancellationToken.None);
  Require(txt.timing=="word"&&txt.cues.Count>0&&txt.cues.Sum(c=>c.words.Count)==3,"Wordline TXT");
  foreach(string name in new[]{"wordline-ejemplo.wav","wordline-ejemplo.mp4","wordline-ejemplo.mka"}){var doc=SubtitleContract0820.Read(Path.Combine(fixtures,name),CancellationToken.None);Require(doc!=null&&doc.cues.Count==txt.cues.Count&&doc.cues[0].text==txt.cues[0].text,name);}
  var srt=SubtitleContract0820.Parse("1\n00:00:01,000 --> 00:00:02,500\nHola mundo\n\n2\n00:00:03,000 --> 00:00:04,000\nPrueba", "srt");
  Require(srt.timing=="cue"&&srt.cues.Count==2&&srt.cues[0].end==2.5,"SRT");
  var vtt=SubtitleContract0820.Parse("WEBVTT\n\n00:01.000 --> 00:02.500\nHola mundo", "vtt");Require(vtt.cues.Count==1&&vtt.cues[0].start==1,"VTT");
  var marked=SubtitleContract0820.Parse("WEBVTT\n\n00:01.000 --> 00:02.500\nHola <00:01.750>mundo", "vtt");Require(marked.timing=="word"&&marked.cues[0].words.Count==2&&marked.cues[0].words[0].end==1.75,"VTT por palabra");
  if(args.Length>3){foreach(string name in new[]{"embedded-grouped-export.wav","audio-with-sub-mp4-aac-srt.mp4","audio-with-sub-mka-opus-vtt.mka"}){var embedded=SubtitleContract0820.Read(Path.Combine(args[3],name),CancellationToken.None);Require(embedded!=null&&embedded.cues.Count>0,name);}}
  var project=Store.New();SubtitleTrack0817.Add(project,"Hola",0,1);string template=File.ReadAllText(args[2]);SubtitleAnimation0820.Set(project.Active,template);Require(SubtitleAnimation0820.Template(project.Active)!=null,"Template MGT");Store.Validate(project);Require(SubtitleAnimation0820.Transcript(project)!=null,"Transcript MGT");
  string saved=Path.Combine(Path.GetDirectoryName(args[0]),"tests","subtitle-project.pthproj");Store.Save(project,saved);var reopened=Store.Load(saved);Require(SubtitleAnimation0820.Template(reopened.Active)==template&&SubtitleTrack0817.Cues(reopened).Count==1,"Guardar y abrir proyecto");
  Console.WriteLine("OK: Wordline TXT/WAV/MP4/MKA, SRT, VTT y plantilla MGT");
 }
}
