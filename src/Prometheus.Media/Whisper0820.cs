using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Prometheus {
 public static class Whisper0820 {
  sealed class Result {public string language=null;public List<SubtitleWord0820> words=null;}
  static string Tempo(double speed){if(Math.Abs(speed-1)<.0001)return "";var parts=new List<string>();while(speed>2){parts.Add("atempo=2");speed/=2;}while(speed<.5){parts.Add("atempo=0.5");speed*=2;}parts.Add("atempo="+Tools.N(speed));return " -af "+String.Join(",",parts);}
  public static SubtitleDocument0820 Transcribe(string media,double sourceStart,double timelineDuration,double speed,string language,CancellationToken token){
   string root=Path.Combine(Tools.Root,"runtime","whisper");string python=Path.Combine(root,"python","python.exe"),script=Path.Combine(root,"prometheus_asr.py"),model=Path.Combine(root,"model");
   if(!File.Exists(python)||!File.Exists(script)||!File.Exists(Path.Combine(model,"model.bin")))throw new Exception("Falta Whisper local en el paquete de Prometheus");
   string folder=Path.Combine(Path.GetTempPath(),"prometheus-asr-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
   string wav=Path.Combine(folder,"audio.wav"),json=Path.Combine(folder,"result.json");
   try{
    string speedArg=Tempo(speed);
    Tools.Run(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -y -ss "+Tools.N(sourceStart)+" -t "+Tools.N(timelineDuration*speed)+" -i "+Tools.Q(media)+" -vn -ac 1 -ar 16000"+speedArg+" -c:a pcm_s16le "+Tools.Q(wav),token);
    string args=Tools.Q(script)+" --input "+Tools.Q(wav)+" --output "+Tools.Q(json)+" --model "+Tools.Q(model)+" --language "+Tools.Q(String.IsNullOrWhiteSpace(language)?"auto":language);
    Tools.Run(python,args,token,true);
    var result=new JavaScriptSerializer().Deserialize<Result>(File.ReadAllText(json,Encoding.UTF8));
    if(result==null||result.words==null)throw new Exception("Whisper no devolvió una transcripción válida");
    var document=new SubtitleDocument0820{timing="word"};var words=new List<SubtitleWord0820>();
    Action flush=()=>{if(words.Count==0)return;document.cues.Add(new SubtitleCue0820{start=words[0].start,end=words.Max(w=>w.end),text=String.Join(" ",words.Select(w=>w.text)),words=new List<SubtitleWord0820>(words)});words.Clear();};
    foreach(var word in result.words){if(word==null||String.IsNullOrWhiteSpace(word.text)||word.end<=word.start)continue;if(words.Count>0&&(word.start-words.Last().end>.7||word.end-words[0].start>6||words.Sum(w=>w.text.Length+1)+word.text.Length>78))flush();words.Add(word);if(System.Text.RegularExpressions.Regex.IsMatch(word.text,@"[.!?]$"))flush();}flush();
    if(document.cues.Count==0)throw new Exception("No se reconocieron palabras en el audio seleccionado");return document;
   }finally{try{File.Delete(wav);}catch{}try{File.Delete(json);}catch{}try{Directory.Delete(folder);}catch{}}
  }
 }
}
