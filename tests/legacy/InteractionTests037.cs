using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class InteractionTests037 {
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Press(Timeline timeline,MouseButtons button,int x,int y){typeof(Timeline).GetMethod("OnMouseDown",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(timeline,new object[]{new MouseEventArgs(button,1,x,y,0)});}
 [STAThread]static void Main(){
  var project=Store.New();var tracks=project.Active.tracks.Where(t=>t.kind=="video").OrderBy(t=>t.order).ToList();
  var asset=GeneratedMedia.CreateSolid("Prueba","FF8833",1);project.assets.Add(asset);
  Media.Insert(project,asset.id,tracks[0].id,0);Media.Insert(project,asset.id,tracks[0].id,3);Media.Insert(project,asset.id,tracks[1].id,6);
  var ordered=project.Active.clips.OrderBy(c=>c.timeline_start.Seconds).ToList();
  var editor=new Editor();editor.Reset(project);editor.Commit(p=>p.Active.link_groups.Add(new LinkGroup{id=Store.Id(),clip_ids=new System.Collections.Generic.List<string>{ordered[0].id,ordered[2].id}}));
  int revision=editor.Project.revision;editor.DeleteSelection(new[]{ordered[0].id,ordered[1].id});
  Check(editor.Project.Active.clips.Count==0&&editor.Project.revision==revision+1,"Suprimir selección incluye vínculos en una sola operación");
  editor.Undo();Check(editor.Project.Active.clips.Count==3&&editor.Project.Active.link_groups.Count==1,"un Deshacer restaura toda la selección y el vínculo");
  editor.Project.Active.tracks.Find(t=>t.id==tracks[1].id).locked=true;revision=editor.Project.revision;bool blocked=false;
  try{editor.DeleteSelection(new[]{ordered[0].id,ordered[1].id});}catch{blocked=true;}
  Check(blocked&&editor.Project.Active.clips.Count==3&&editor.Project.revision==revision,"pista bloqueada impide eliminación parcial");
  using(var timeline=new Timeline{Project=project,Width=900,Height=400}){
   int gapCalls=0;timeline.Seek=position=>{};timeline.GapContext=(t,at,point)=>gapCalls++;
   int x=(int)timeline.X(2),y=timeline.TrackTop(tracks[0].id)+20;
   Press(timeline,MouseButtons.Right,x,y);Check(timeline.SelectedGapTrack==null&&gapCalls==0,"clic derecho no selecciona un espacio");
   Press(timeline,MouseButtons.Left,x,y);Check(timeline.SelectedGapTrack==tracks[0].id,"clic izquierdo selecciona el espacio");
   Press(timeline,MouseButtons.Right,x,y);Check(timeline.SelectedGapTrack==tracks[0].id&&gapCalls==1,"clic derecho abre opciones del espacio ya seleccionado");
   Press(timeline,MouseButtons.Left,x,y);Check(timeline.SelectedGapTrack==null,"segundo clic izquierdo deselecciona el espacio");
  }
 }
}
