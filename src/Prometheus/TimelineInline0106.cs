using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public partial class Timeline {
  NestedInlineView0106 inlineVideoEditor,inlineAudioEditor;
  string inlineVideoId,inlineAudioId,inlineVideoTrackId,inlineAudioTrackId;
  int inlineVideoHeight,inlineAudioHeight;
  public void RegisterInlineEditors(NestedInlineView0106 video,NestedInlineView0106 audio){inlineVideoEditor=video;inlineAudioEditor=audio;videoScrollBar.BringToFront();audioScrollBar.BringToFront();}
  int TrackRowHeight(Track track){return TrackHeight(track.id)+(track.id==inlineVideoTrackId?inlineVideoHeight:0)+(track.id==inlineAudioTrackId?inlineAudioHeight:0);}
  public void SetInlineNested(NestedInlineView0106 view,string clipId,int height){
   bool video=Object.ReferenceEquals(view,inlineVideoEditor);string previous=video?inlineVideoId:inlineAudioId;int oldHeight=video?inlineVideoHeight:inlineAudioHeight;
   var clip=Project==null?null:Project.Active.clips.Find(c=>c.id==clipId);string trackId=clip==null?null:clip.track_id;
   if(video){inlineVideoId=clipId;inlineVideoTrackId=trackId;inlineVideoHeight=clipId==null?0:height;}else{inlineAudioId=clipId;inlineAudioTrackId=trackId;inlineAudioHeight=clipId==null?0:height;}
   if(previous!=clipId||oldHeight!=height){SyncVerticalScrollbars();if(clipId!=null)RevealTrack(clipId);Invalidate();}
   PositionInlineEditors();
  }
  void PositionInlineEditors(){PositionInlineEditor(inlineVideoEditor,inlineVideoId,inlineVideoHeight);PositionInlineEditor(inlineAudioEditor,inlineAudioId,inlineAudioHeight);}
  void PositionInlineEditor(NestedInlineView0106 view,string clipId,int height){
   if(view==null)return;var clip=Project==null?null:Project.Active.clips.Find(c=>c.id==clipId);if(clip==null||height<1){view.Visible=false;return;}
   var track=Project.Active.tracks.Find(t=>t.id==clip.track_id);if(track==null){view.Visible=false;return;}
   int top=TrackTop(track.id)+TrackHeight(track.id);var viewport=TrackViewport(track.kind);int visibleTop=Math.Max(0,viewport.Top-top),visibleBottom=Math.Min(height,viewport.Bottom-top);
   if(visibleBottom<=visibleTop){view.Visible=false;return;}
   var bounds=new Rectangle(0,top,Width,height);if(view.Bounds!=bounds)view.Bounds=bounds;
   string signature=visibleTop+":"+visibleBottom+":"+Width;if(!Object.Equals(view.Tag,signature)){var region=new Region(new Rectangle(0,visibleTop,Width,visibleBottom-visibleTop));var previous=view.Region;view.Region=region;if(previous!=null)previous.Dispose();view.Tag=signature;}view.Visible=true;
  }
 }
}
