using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Prometheus;

class Regression08175 {
 static void Check(bool pass,string message){if(!pass)throw new Exception(message);Console.WriteLine("PASS "+message);}
 [STAThread] static void Main(string[] args){
  Tools.Root=Path.GetFullPath(args[0]);
  var mov=Media.Inspect(args[1],CancellationToken.None);
  Check(mov.streams.Any(s=>s.codec=="prores"&&s.pixel_format.Contains("a")),"MGT MOV ProRes 4444 con alfa");
  var mislabeled=Media.Inspect(args[2],CancellationToken.None);
  Check(mislabeled.streams.Any(s=>s.codec=="prores"),"MOV ProRes con extensión .mp4 se importa por contenido");
  var editor=new Editor();
  editor.Commit(p=>{p.assets.Add(mov);string track=p.Active.tracks.First(t=>t.kind=="video").id;Media.Insert(p,mov.id,track,0);Media.Insert(p,mov.id,track,10);});
  using(var image=Rendering.FrameExact(editor.Project,1,CancellationToken.None,160,90,true))Check(image.Width==160&&image.Height==90,"fotograma MGT MOV visible en el monitor");
  var ids=editor.Project.Active.clips.Select(c=>c.id).ToList();
  editor.Group(ids);
  Check(editor.Project.Active.edit_groups.Count==1&&editor.Project.Active.edit_groups[0].clip_ids.Count==2,"grupo de edición independiente");
  Check(editor.CutSelection(new System.Collections.Generic.List<string>{ids[0]},2),"corte de clip agrupado");
  Check(editor.Project.Active.edit_groups[0].clip_ids.Count==3,"el fragmento nuevo mantiene su grupo");
  var clip=editor.Project.Active.clips.First(c=>c.id==ids[0]);
  Check(ClipFraming.Read(clip).scaleLinked,"escala enlazada por defecto");
  ClipFraming.SetScaleLinked(clip,false);ClipFraming.SetScaleY(clip,1.5);
  Check(Math.Abs(ClipFraming.Read(clip).scaleY-1.5)<0.00001&&Math.Abs(ClipFraming.Read(clip).scale-1)<0.00001,"escala vertical independiente");
  ClipFraming.SetScaleLinked(clip,true);
  Check(ClipFraming.Read(clip).scaleLinked&&Math.Abs(ClipFraming.Read(clip).scaleY-ClipFraming.Read(clip).scale)<0.00001,"al reenlazar se sincronizan las escalas");
  editor.UnlinkMany(editor.Project.Active.clips.Select(c=>c.id));
  editor.UngroupMany(editor.Project.Active.clips.Select(c=>c.id));
  Check(editor.Project.Active.edit_groups.Count==0,"desagrupar selección completa");
  using(var form=new Form{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Left=-3000,Top=-3000,Width=650,Height=260}){
   var timeline=new Timeline{Dock=DockStyle.Fill,Project=Store.Copy(editor.Project)};form.Controls.Add(timeline);
   for(int i=0;i<7;i++)timeline.Project.Active.tracks.Add(new Track{id=Store.Id(),name="Vídeo extra",kind="video",order=10+i,enabled=true});
   for(int i=0;i<7;i++)timeline.Project.Active.tracks.Add(new Track{id=Store.Id(),name="Audio extra",kind="audio",order=10+i,enabled=true});
   form.Show();timeline.SyncVerticalScrollbars();Application.DoEvents();
   Check(timeline.Controls.OfType<VScrollBar>().Count(bar=>bar.Visible)==2,"barras verticales de vídeo y audio al desbordar");
   form.Close();
  }
 }
}
