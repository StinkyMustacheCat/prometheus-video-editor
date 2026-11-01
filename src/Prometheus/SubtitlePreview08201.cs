using System;

namespace Prometheus {
 public partial class MainForm {
  void PreparePreviewSubtitles08201(Project snapshot){
   if(snapshot==null||timelineView.SubtitleStrip.PreviewVisible)return;
   snapshot.Active.clips.RemoveAll(clip=>SubtitleTrack0817.IsCue(snapshot,clip));
  }
 }
}
