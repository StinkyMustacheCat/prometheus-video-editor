using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Prometheus {
 public static class SubtitleAnimation0820 {
  public const string Namespace="prometheus.subtitle-animation";
  public static volatile bool PreviewOverlayReady;
  public static string Template(Sequence sequence){var ext=sequence.extensions.FirstOrDefault(x=>x!=null&&x.@namespace==Namespace&&x.version=="1");object value;return ext!=null&&ext.payload!=null&&ext.payload.TryGetValue("template_json",out value)?Convert.ToString(value):null;}
  public static void Set(Sequence sequence,string json){if(json!=null){if(Encoding.UTF8.GetByteCount(json)>4*1024*1024)throw new Exception("La plantilla MGT supera 4 MB");var raw=Store.Json().Deserialize<Dictionary<string,object>>(json);object subtitles,kind;if(raw==null||!raw.TryGetValue("subtitles",out subtitles)||!(subtitles is Dictionary<string,object>)||!((Dictionary<string,object>)subtitles).TryGetValue("kind",out kind)||Convert.ToString(kind)!="template")throw new Exception("Selecciona una plantilla MGT de subtítulos sin transcripción");var subtitleData=(Dictionary<string,object>)subtitles;object cues;if(!subtitleData.TryGetValue("cues",out cues)||!(cues is System.Collections.IEnumerable)||((System.Collections.IEnumerable)cues).Cast<object>().Any())throw new Exception("La plantilla MGT no debe traer palabras ya cargadas");if(!raw.ContainsKey("render")||!raw.ContainsKey("html"))throw new Exception("La plantilla MGT está incompleta");foreach(string key in new[]{"videos","gifs","animatedSVGs","lotties","models","scene3d"}){object value;if(raw.TryGetValue(key,out value)&&value!=null){var map=value as Dictionary<string,object>;if(map==null||map.Count>0)throw new Exception("La vista MGT de subtítulos solo admite gráficos e imágenes incrustadas");}}}sequence.extensions.RemoveAll(x=>x!=null&&x.@namespace==Namespace);if(json!=null)sequence.extensions.Add(new Extension{@namespace=Namespace,version="1",payload=new Dictionary<string,object>{{"template_json",json}}});}
  public static object Transcript(Project project){
   var cues=new List<object>();bool allWords=true;
   var subtitleTrack=SubtitleTrack0817.Track(project.Active);
   if(subtitleTrack==null||!subtitleTrack.enabled)return new{version=1,kind="composition",timing="cue",tail=.25,cues=cues};
   foreach(var clip in SubtitleTrack0817.Cues(project)){
    var asset=project.assets.FirstOrDefault(a=>a.id==clip.asset_id);if(asset==null||!clip.enabled)continue;
    var item=new Dictionary<string,object>{{"start",clip.timeline_start.Seconds},{"end",clip.End},{"text",GeneratedMedia.Text(asset)}};
    var ext=asset.extensions.FirstOrDefault(x=>x!=null&&x.@namespace==GeneratedMedia.Namespace);object raw;bool timed=false;
    if(ext!=null&&ext.payload.TryGetValue("timed_words",out raw))try{
     var words=Store.Json().Deserialize<List<SubtitleWord0820>>(Convert.ToString(raw));
     string original=String.Join(" ",words.Select(w=>w.text));
     if(original==GeneratedMedia.Text(asset)){
      var selected=words.Where(w=>w.end>clip.source_in.Seconds&&w.start<clip.source_in.Seconds+clip.duration.Seconds)
       .Select(w=>new{start=Math.Max(clip.timeline_start.Seconds,clip.timeline_start.Seconds+w.start-clip.source_in.Seconds),end=Math.Min(clip.End,clip.timeline_start.Seconds+w.end-clip.source_in.Seconds),text=w.text})
       .Where(w=>w.end>w.start).ToList();
      if(selected.Count>0){item["words"]=selected;item["text"]=String.Join(" ",selected.Select(w=>w.text));timed=true;}
     }
    }catch{}
    if(!timed)allWords=false;cues.Add(item);
   }
   return new{version=1,kind="composition",timing=allWords?"word":"cue",tail=.25,cues=cues};
  }
  public static void SuppressStatic(Project snapshot){if(Template(snapshot.Active)!=null)snapshot.Active.clips.RemoveAll(c=>SubtitleTrack0817.IsCue(snapshot,c));}
  public static string PrepareExport(Project snapshot,string folder,CancellationToken token,Action<int> progress){string raw=Template(snapshot.Active);var subtitleTrack=SubtitleTrack0817.Track(snapshot.Active);if(raw==null||subtitleTrack==null||!subtitleTrack.enabled||!SubtitleTrack0817.Cues(snapshot).Any(c=>c.enabled))return null;double sequenceLength=snapshot.Active.Length;if(sequenceLength>3600)throw new Exception("La animación MGT de subtítulos admite hasta 60 minutos por secuencia");Directory.CreateDirectory(folder);var template=Store.Json().Deserialize<Dictionary<string,object>>(raw);template["subtitles"]=Transcript(snapshot);template["recommended"]=new Dictionary<string,object>{{"duration",Math.Max(.1,sequenceLength)},{"resolution",snapshot.Active.height},{"aspect",snapshot.Active.width>=snapshot.Active.height?"horizontal":"vertical"}};string input=Path.Combine(folder,"subtitles.mgt.json"),output=Path.Combine(folder,"subtitles.mov");File.WriteAllText(input,Store.Json().Serialize(template),new UTF8Encoding(false));using(var engine=new MgtEngine(token)){var imported=engine.Import(input,token);string id=MgtEngine.String(imported,"assetId");try{engine.Preflight(id,"final",60,new Dictionary<string,object>(),token);engine.Render(id,"final",60,new Dictionary<string,object>(),output,token,progress);}finally{engine.Release(id);}}var asset=Media.Inspect(output,token);var stream=asset.streams.FirstOrDefault(x=>x.kind=="video");if(stream==null)throw new Exception("MGT no generó la capa visual de subtítulos");snapshot.Active.clips.RemoveAll(c=>SubtitleTrack0817.IsCue(snapshot,c));var track=Store.AddTrack(snapshot.Active,"video");track.name="Animación de subtítulos MGT";snapshot.assets.Add(asset);snapshot.Active.clips.Add(new Clip{id=Store.Id(),name=asset.name,asset_id=asset.id,stream_id=stream.id,track_id=track.id,timeline_start=Rational.Frame(0),source_in=Rational.Time(0),duration=Rational.Frame(Math.Min(sequenceLength,stream.duration.Seconds))});return output;}
 }
}

