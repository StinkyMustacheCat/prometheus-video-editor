using System;

namespace Prometheus {
 public partial class Timeline {
  public Action<string,int> MoveTrackContentsRequested;
  public string AdjacentCompatibleTrack(string sourceId,int direction){
   var tracks=Tracks();int index=tracks.FindIndex(t=>t.id==sourceId);
   int next=index+direction;
   if(index<0||next<0||next>=tracks.Count||tracks[next].kind!=tracks[index].kind)return null;
   return tracks[next].id;
  }
 }
 public partial class MainForm {
  void MoveContentsOneTrack(string sourceId,int direction){
   string target=timeline.AdjacentCompatibleTrack(sourceId,direction);
   if(target==null){status.Text="No hay una pista compatible en esa dirección";return;}
   try{Edit(()=>editor.MoveTrackContents(sourceId,target));status.Text="Recursos movidos a la pista vecina";}
   catch(Exception ex){Error(ex);}
  }
 }
}
