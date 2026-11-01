using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 public sealed class NestedAudioMember01034 { public Clip clip; public string label; public List<NestedRenderGroup01034> chain; }
 public sealed class NestedAudioOutput01034 { public string label,trackId; }
 public static class NestedAudioRender01034 {
  public static List<NestedAudioOutput01034> Build(List<string> graph,Project project,List<NestedAudioMember01034> members){int next=0;return Level(graph,project,members,0,ref next);}
  static List<NestedAudioOutput01034> Level(List<string> graph,Project project,List<NestedAudioMember01034> members,int depth,ref int next){
   var result=new List<NestedAudioOutput01034>();
   foreach(var grouping in members.GroupBy(m=>depth<m.chain.Count?"group:"+m.chain[depth].outer.id:"leaf:"+m.clip.id)){
    var sample=grouping.First();
    if(depth>=sample.chain.Count){foreach(var item in grouping)result.Add(new NestedAudioOutput01034{label=item.label,trackId=item.clip.track_id});continue;}
    var outer=sample.chain[depth].outer;
    var children=Level(graph,project,grouping.ToList(),depth+1,ref next);
    if(children.Count==0)continue;
    string mixed="nested_audio_mix_"+next,processed="nested_audio_out_"+next;next++;
    graph.Add(String.Join("",children.Select(c=>"["+c.label+"]"))+(children.Count==1?"anull":"amix=inputs="+children.Count+":duration=longest:normalize=0")+"["+mixed+"]");
    var track=project.Active.tracks.Find(t=>t.id==outer.track_id);
    double gain=outer.gain_db+(track==null?0:track.gain_db);
    string filters=FFmpegAudioEffectAdapter.AudioFilters(project,outer)+",volume="+Tools.N(gain)+"dB";
    if(Math.Abs(outer.balance)>0.000001)filters+=",stereotools=balance_out="+Tools.N(outer.balance);
    if(track!=null&&Math.Abs(track.balance)>0.000001)filters+=",stereotools=balance_out="+Tools.N(track.balance);
    graph.Add("["+mixed+"]anull"+filters+"["+processed+"]");
    result.Add(new NestedAudioOutput01034{label=processed,trackId=outer.track_id});
   }
   return result;
  }
 }
}
