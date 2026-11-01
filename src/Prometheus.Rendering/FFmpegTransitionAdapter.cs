using System;
using System.Linq;

namespace Prometheus {
 // The adapter is deliberately the only place that knows FFmpeg transition syntax.
 public static class FFmpegTransitionAdapter {
  public static bool Visible(Sequence sequence,TransitionInstance transition){
   if(transition==null||!transition.enabled)return false;
   var left=sequence.clips.FirstOrDefault(c=>c.id==transition.left_clip_id);
   var right=sequence.clips.FirstOrDefault(c=>c.id==transition.right_clip_id);
   return left!=null&&right!=null&&left.enabled&&right.enabled&&sequence.tracks.Any(t=>t.id==left.track_id&&t.enabled)&&sequence.tracks.Any(t=>t.id==right.track_id&&t.enabled);
  }
  public static TransitionInstance Incoming(Sequence sequence,Clip clip){return sequence.transitions.FirstOrDefault(t=>t.right_clip_id==clip.id&&Visible(sequence,t));}
  public static TransitionInstance Outgoing(Sequence sequence,Clip clip){return sequence.transitions.FirstOrDefault(t=>t.left_clip_id==clip.id&&Visible(sequence,t));}
  public static double Preroll(TransitionInstance transition){return transition!=null&&transition.transition_id==TransitionContracts.Dissolve?transition.duration_frames/60.0:0;}
  static string N(double value){return Tools.N(value);}
  public static string VideoFilters(TransitionInstance incoming,TransitionInstance outgoing,double renderedStart){
   string filters="";
   if(outgoing!=null){double cut=outgoing.cut_frame/60.0,duration=outgoing.duration_frames/60.0;
    if(outgoing.transition_id==TransitionContracts.DipBlack||outgoing.transition_id==TransitionContracts.DipWhite)
     filters+=",fade=t=out:st="+N(cut-duration/2)+":d="+N(duration/2)+":color="+(outgoing.transition_id==TransitionContracts.DipWhite?"white":"black");
   }
   if(incoming!=null){double cut=incoming.cut_frame/60.0,duration=incoming.duration_frames/60.0;
    if(incoming.transition_id==TransitionContracts.Dissolve)filters+=",fade=t=in:st="+N(cut-duration)+":d="+N(duration)+":alpha=1";
    else filters+=",fade=t=in:st="+N(cut)+":d="+N(duration/2)+":color="+(incoming.transition_id==TransitionContracts.DipWhite?"white":"black");
   }
   return filters;
  }
  public static string AudioFilters(TransitionInstance incoming,TransitionInstance outgoing,double renderedStart){
   string filters="";
   if(outgoing!=null){double cut=outgoing.cut_frame/60.0,duration=outgoing.duration_frames/60.0;
    double start=outgoing.transition_id==TransitionContracts.Dissolve?cut-duration:cut-duration/2;
    double fade=outgoing.transition_id==TransitionContracts.Dissolve?duration:duration/2;
    filters+=",afade=t=out:st="+N(start-renderedStart)+":d="+N(fade);
   }
   if(incoming!=null){double cut=incoming.cut_frame/60.0,duration=incoming.duration_frames/60.0;
    double fade=incoming.transition_id==TransitionContracts.Dissolve?duration:duration/2;
    filters+=",afade=t=in:st="+N(incoming.transition_id==TransitionContracts.Dissolve?cut-duration-renderedStart:cut-renderedStart)+":d="+N(fade);
   }
   return filters;
  }
 }
}
