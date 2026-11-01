using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace Prometheus {
 public sealed class SubtitleWord0820 { public double start,end; public string text; }
 public sealed class SubtitleCue0820 { public double start,end; public string text; public List<SubtitleWord0820> words=new List<SubtitleWord0820>(); }
 public sealed class SubtitleDocument0820 { public string timing="cue"; public List<SubtitleCue0820> cues=new List<SubtitleCue0820>(); }
 public static class SubtitleContract0820 {
  static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
  static double Number(string value){return Double.Parse(value,NumberStyles.AllowDecimalPoint,Inv);}
  static double Clock(string value){var parts=value.Replace(',', '.').Split(':');if(parts.Length<2||parts.Length>3)throw new Exception("Marca de tiempo de subtítulos inválida");double seconds=Number(parts[parts.Length-1]);int minutes=Int32.Parse(parts[parts.Length-2],Inv);int hours=parts.Length==3?Int32.Parse(parts[0],Inv):0;return hours*3600+minutes*60+seconds;}
  static void Validate(SubtitleDocument0820 doc){if(doc.cues.Count==0)throw new Exception("No se encontraron subtítulos");if(doc.cues.Count>20000)throw new Exception("El archivo supera 20 000 frases");double previous=-1;foreach(var cue in doc.cues){if(cue.start<previous||cue.end<=cue.start||cue.end>3600*24||String.IsNullOrWhiteSpace(cue.text))throw new Exception("Los tiempos de los subtítulos no son válidos");previous=cue.start;foreach(var word in cue.words)if(word.start<cue.start-0.001||word.end>cue.end+0.001||word.end<=word.start)throw new Exception("Los tiempos por palabra no son válidos");}}
  public static SubtitleDocument0820 ParseWordline(string text){var lines=(text??"").TrimStart('\uFEFF').Replace("\r\n","\n").Replace('\r','\n').Split('\n');if(lines.Length<4||lines[0]!="UNIDAD: segundos (precisión de milisegundos)"||lines[1]!="FORMATO: inicio fin contenido"||lines[2]!="")throw new Exception("TXT Wordline no compatible");var doc=new SubtitleDocument0820{timing="word"};var words=new List<SubtitleWord0820>();Action flush=()=>{if(words.Count==0)return;doc.cues.Add(new SubtitleCue0820{start=words[0].start,end=words.Max(w=>w.end),text=String.Join(" ",words.Select(w=>w.text)),words=new List<SubtitleWord0820>(words)});words.Clear();};double previous=-1;for(int i=3;i<lines.Length;i++){if(String.IsNullOrWhiteSpace(lines[i]))continue;var m=Regex.Match(lines[i],@"^(\d+\.\d{3}) (\d+\.\d{3}) (.+)$");if(!m.Success)throw new Exception("Línea Wordline inválida: "+(i+1));double start=Number(m.Groups[1].Value),end=Number(m.Groups[2].Value);string value=m.Groups[3].Value;if(start<previous||end<=start)throw new Exception("Tiempos Wordline desordenados");previous=start;if(Regex.IsMatch(value,@"^\[(?:SIN TEXTO|PAUSA|VOZ ININTELIGIBLE|POSIBLE VOZ|SONIDO SIN IDENTIFICAR) \d+\.\d{3}\]$")){if(!value.StartsWith("[SIN TEXTO ",StringComparison.Ordinal)||end-start>.7)flush();continue;}if(words.Count>0&&(start-words.Last().end>.7||end-words[0].start>6||words.Sum(w=>w.text.Length+1)+value.Length>78))flush();words.Add(new SubtitleWord0820{start=start,end=end,text=value});if(Regex.IsMatch(value,@"[.!?]$"))flush();}flush();Validate(doc);return doc;}
  public static SubtitleDocument0820 ParseText(string text,string format){
   var doc=new SubtitleDocument0820();var normalized=(text??"").TrimStart('\uFEFF').Replace("\r\n","\n").Replace('\r','\n');
   if(format=="vtt")normalized=Regex.Replace(normalized,@"^WEBVTT[^\n]*\n","").TrimStart('\n');
   var blocks=Regex.Split(normalized.Trim(),@"\n\s*\n");
   foreach(var block in blocks){var lines=block.Split('\n');int timeLine=Array.FindIndex(lines,line=>line.Contains("-->"));if(timeLine<0)continue;
    var match=Regex.Match(lines[timeLine],@"^\s*(\d{1,2}:\d{2}(?::\d{2})?[,.]\d{3})\s*-->\s*(\d{1,2}:\d{2}(?::\d{2})?[,.]\d{3})");if(!match.Success)continue;
    double start=Clock(match.Groups[1].Value),end=Clock(match.Groups[2].Value);string raw=String.Join(" ",lines.Skip(timeLine+1)).Trim();string caption=Regex.Replace(raw,@"<[^>]+>","");if(caption.Length==0)continue;
    var cue=new SubtitleCue0820{start=start,end=end,text=caption};
    if(format=="vtt"){
     var markers=Regex.Matches(raw,@"<((?:\d{2}:)?\d{2}:\d{2}\.\d{3})>").Cast<Match>().ToList();
     if(markers.Count>0){int offset=0;double at=start;bool exact=true;foreach(var marker in markers){double stop=Clock(marker.Groups[1].Value);string part=Regex.Replace(raw.Substring(offset,marker.Index-offset),@"<[^>]+>","").Trim();if(part.Length>0){if(part.Split(new[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries).Length!=1||stop<=at){exact=false;break;}cue.words.Add(new SubtitleWord0820{start=at,end=stop,text=part});}at=stop;offset=marker.Index+marker.Length;}if(exact){string tail=Regex.Replace(raw.Substring(offset),@"<[^>]+>","").Trim();if(tail.Length>0){if(tail.Split(new[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries).Length!=1||end<=at)exact=false;else cue.words.Add(new SubtitleWord0820{start=at,end=end,text=tail});}}if(!exact||String.Join(" ",cue.words.Select(w=>w.text))!=caption)cue.words.Clear();}
    }
    doc.cues.Add(cue);
   }
   doc.timing=doc.cues.Count>0&&doc.cues.All(c=>c.words.Count>0)?"word":"cue";Validate(doc);return doc;
  }
  public static SubtitleDocument0820 Parse(string text,string format){return format=="wordline"?ParseWordline(text):ParseText(text,format);}
  static string WavText(string path){using(var file=File.OpenRead(path))using(var reader=new BinaryReader(file)){if(file.Length<12||reader.ReadUInt32()!=0x46464952){return null;}file.Position=8;if(reader.ReadUInt32()!=0x45564157)return null;while(file.Position+8<=file.Length){uint kind=reader.ReadUInt32(),length=reader.ReadUInt32();long end=file.Position+length+(length%2);if(end>file.Length)break;if(kind==0x5453494c&&length>=12&&reader.ReadUInt32()==0x4f464e49){while(file.Position+8<=end){uint tag=reader.ReadUInt32(),size=reader.ReadUInt32();long next=file.Position+size+(size%2);if(next>end)break;if(tag==0x544d4349&&size<16*1024*1024)return new UTF8Encoding(false,true).GetString(reader.ReadBytes((int)size)).TrimEnd('\0');file.Position=next;}}file.Position=end;}}return null;}
  static string Mp4Text(string path,CancellationToken token){string raw=Tools.Run(Tools.FFprobe,"-v error -show_entries format_tags=wordline_timed_text_v1 -of json "+Tools.Q(path),token);var root=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(raw);object format,tags,value;if(!root.TryGetValue("format",out format)||!(format is Dictionary<string,object>))return null;var dict=(Dictionary<string,object>)format;if(!dict.TryGetValue("tags",out tags)||!(tags is Dictionary<string,object>))return null;var values=(Dictionary<string,object>)tags;if(!values.TryGetValue("wordline_timed_text_v1",out value))return null;return new UTF8Encoding(false,true).GetString(Convert.FromBase64String(Convert.ToString(value)));}
  static SubtitleDocument0820 EmbeddedTrack(string path,CancellationToken token){
   string probe=Tools.Run(Tools.FFprobe,"-v error -select_streams s -show_entries stream=index,codec_name -of json "+Tools.Q(path),token);
   var result=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(probe);
   object streams;if(!result.TryGetValue("streams",out streams))return null;
   bool candidateFound=false;
   foreach(var entry in (System.Collections.IEnumerable)streams){
    var row=entry as Dictionary<string,object>;object codec,index;
    if(row==null||!row.TryGetValue("codec_name",out codec)||!row.TryGetValue("index",out index)||!new[]{"mov_text","subrip","webvtt","ass","ssa"}.Contains(Convert.ToString(codec)))continue;
    int streamIndex;if(!int.TryParse(Convert.ToString(index),out streamIndex)||streamIndex<0)continue;
    candidateFound=true;
    string temporary=Path.Combine(Path.GetTempPath(),"prometheus-subtitle-"+Guid.NewGuid().ToString("N")+".srt");
    try{
     Tools.Run(Tools.FFmpeg,"-hide_banner -loglevel error -nostdin -y -i "+Tools.Q(path)+" -map 0:"+streamIndex.ToString(CultureInfo.InvariantCulture)+" -c:s srt "+Tools.Q(temporary),token);
     var parsed=ParseText(File.ReadAllText(temporary,Encoding.UTF8),"srt");
     if(parsed!=null&&parsed.cues.Count>0)return parsed;
    }catch(OperationCanceledException){throw;}
    catch(Exception){/* Try the next embedded subtitle stream. */}
    finally{try{File.Delete(temporary);}catch{}}
   }
   if(candidateFound)throw new Exception("La pista de subtítulos incrustada no contiene texto utilizable");
   return null;
  }
  public static SubtitleDocument0820 Read(string path,CancellationToken token){string ext=Path.GetExtension(path).ToLowerInvariant();if(ext==".srt"||ext==".vtt"||ext==".txt"){string value=File.ReadAllText(path,Encoding.UTF8);return value.StartsWith("UNIDAD:",StringComparison.Ordinal)?ParseWordline(value):ParseText(value,ext==".vtt"?"vtt":"srt");}if(ext==".wav"){string value=WavText(path);if(value==null)return null;const string canonical="WORDLINE_TIMED_TEXT_VERSION=1\n",grouped="WORDLINE_SUBTITLES_FORMAT=";if(value.StartsWith(canonical,StringComparison.Ordinal))return ParseWordline(value.Substring(canonical.Length));if(value.StartsWith(grouped,StringComparison.Ordinal)){int line=value.IndexOf('\n');if(line<0)throw new Exception("Metadatos de subtítulos WAV incompletos");string format=value.Substring(grouped.Length,line-grouped.Length).Trim();string body=value.Substring(line+1);return body.StartsWith("UNIDAD:",StringComparison.Ordinal)?ParseWordline(body):ParseText(body,format=="vtt"?"vtt":"srt");}return value.StartsWith("UNIDAD:",StringComparison.Ordinal)?ParseWordline(value):null;}if(ext==".mp4"||ext==".m4a"||ext==".mov"){string value=Mp4Text(path,token);if(value!=null)return ParseWordline(value);return EmbeddedTrack(path,token);}if(ext==".mka"){string probe=Tools.Run(Tools.FFprobe,"-v error -select_streams t -show_entries stream=index:stream_tags=filename -of json "+Tools.Q(path),token);var result=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(probe);object streams;if(result.TryGetValue("streams",out streams))foreach(var entry in (System.Collections.IEnumerable)streams){var row=entry as Dictionary<string,object>;object tags,index;if(row==null||!row.TryGetValue("tags",out tags)||!row.TryGetValue("index",out index))continue;var values=tags as Dictionary<string,object>;object name;if(values==null||!values.TryGetValue("filename",out name)||Convert.ToString(name)!="wordline-timed-text-v1.txt")continue;string temporary=Path.Combine(Path.GetTempPath(),"prometheus-wordline-"+Guid.NewGuid().ToString("N")+".txt");try{Tools.Run(Tools.FFmpeg,"-hide_banner -loglevel error -dump_attachment:"+Convert.ToInt32(index)+" "+Tools.Q(temporary)+" -i "+Tools.Q(path)+" -f null NUL",token);return ParseWordline(File.ReadAllText(temporary,Encoding.UTF8));}finally{try{File.Delete(temporary);}catch{}}}return EmbeddedTrack(path,token);}return null;}
 }
}
