using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  sealed class WaveformJob {
   public Asset Asset;
   public StreamInfo Stream;
   public MediaRepresentation Representation;
   public string Key,ProjectId;
  }
  readonly Queue<WaveformJob> waveformQueue=new Queue<WaveformJob>();
  readonly HashSet<string> waveformScheduled=new HashSet<string>();
  readonly HashSet<string> waveformFailures=new HashSet<string>();
  string waveformCacheDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","WaveformCache");
  readonly string waveformSettingPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","waveform-view.txt");
  CancellationTokenSource waveformToken=new CancellationTokenSource();
  string waveformProjectId;
  bool waveformWorking;

  void LoadWaveformPreference(){try{timeline.ShowWaveforms=File.ReadAllText(waveformSettingPath).Trim()!="off";}catch{timeline.ShowWaveforms=true;}}
  void ShowWaveformMenu(string panelId,ContextMenuStrip menu){
   if(panelId!="timeline")return;
   var item=new ToolStripMenuItem("Mostrar ondas de audio"){Checked=timeline.ShowWaveforms,CheckOnClick=true};
   item.CheckedChanged+=(s,e)=>{
    timeline.ShowWaveforms=item.Checked;
    try{Directory.CreateDirectory(Path.GetDirectoryName(waveformSettingPath));File.WriteAllText(waveformSettingPath,item.Checked?"on":"off");}catch{}
    if(item.Checked)RefreshWaveforms();
    timeline.Invalidate();
   };
   menu.Items.Add(item);menu.Items.Add(new ToolStripSeparator());
  }
  void ResetWaveforms(){
   waveformToken.Cancel();waveformToken.Dispose();waveformToken=new CancellationTokenSource();
   waveformQueue.Clear();waveformScheduled.Clear();waveformFailures.Clear();timeline.Waveforms.Clear();waveformWorking=false;
   waveformProjectId=editor.Project.id;timeline.Invalidate();
  }
  void RefreshWaveforms(){
   if(!IsHandleCreated||closing)return;
   if(waveformProjectId!=editor.Project.id)ResetWaveforms();
   if(!timeline.ShowWaveforms)return;
   var used=editor.Project.Active.clips.Where(c=>editor.Project.Active.tracks.Any(t=>t.id==c.track_id&&t.kind=="audio")).Select(c=>c.stream_id).Distinct().ToList();
   foreach(string streamId in timeline.Waveforms.Keys.Where(id=>!used.Contains(id)).ToList())timeline.Waveforms.Remove(streamId);
   foreach(string streamId in used){
    var asset=editor.Project.assets.FirstOrDefault(a=>a.streams.Any(st=>st.id==streamId));
    if(asset==null)continue;
    var stream=asset.streams.First(st=>st.id==streamId);
    var representation=Media.SelectRepresentation(asset,false)??Media.SelectRepresentation(asset,true);
    if(representation==null){timeline.Waveforms.Remove(streamId);continue;}
    string key;
    try{key=WaveformCache.Identity(asset,stream,representation);}catch{timeline.Waveforms.Remove(streamId);continue;}
    WaveformData ready;
    if(timeline.Waveforms.TryGetValue(streamId,out ready)&&ready.Key==key)continue;
    timeline.Waveforms.Remove(streamId);
    if(waveformScheduled.Contains(key)||waveformFailures.Contains(key))continue;
    var copy=Store.Json().Deserialize<Asset>(Store.Json().Serialize(Store.Fields(asset)));
    waveformQueue.Enqueue(new WaveformJob{Asset=copy,Stream=copy.streams.First(st=>st.id==streamId),Representation=copy.representations.First(r=>r.role==representation.role),Key=key,ProjectId=editor.Project.id});
    waveformScheduled.Add(key);
   }
   StartNextWaveform();
  }
  void StartNextWaveform(){
   if(waveformWorking||waveformQueue.Count==0||closing)return;
   var job=waveformQueue.Dequeue();var token=waveformToken;
   waveformWorking=true;
   Task.Factory.StartNew(()=>{
    WaveformData data=null;Exception failure=null;
    try{data=WaveformCache.LoadOrBuild(job.Asset,job.Stream,job.Representation,waveformCacheDir,token.Token);}catch(OperationCanceledException){}catch(Exception ex){failure=ex;}
    UI(()=>{
     if(waveformToken!=token)return;
     waveformWorking=false;waveformScheduled.Remove(job.Key);
     if(failure!=null){waveformFailures.Add(job.Key);status.Text="Onda no disponible para "+job.Asset.name+": "+failure.Message;}
     if(data!=null&&editor.Project.id==job.ProjectId){
      var current=editor.Project.assets.FirstOrDefault(a=>a.streams.Any(st=>st.id==job.Stream.id));
      if(current!=null)try{
       var stream=current.streams.First(st=>st.id==job.Stream.id);
       var rep=Media.SelectRepresentation(current,false)??Media.SelectRepresentation(current,true);
       if(WaveformCache.Identity(current,stream,rep)==data.Key){timeline.Waveforms[stream.id]=data;timeline.Invalidate();}
      }catch{}
     }
     StartNextWaveform();
    });
   });
  }
  void StopWaveforms(){waveformToken.Cancel();waveformQueue.Clear();}
 }
}
