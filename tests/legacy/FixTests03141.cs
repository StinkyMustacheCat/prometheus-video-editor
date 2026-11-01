using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class FixTests03141 {
 static int checks;
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);checks++;}
 static object Field(object owner,string name){return owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);}
 static void Call(object owner,string name,params object[] args){owner.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(owner,args);}
 static void Mouse(Control control,string method,MouseButtons button,int x,int y){typeof(Control).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(control,new object[]{new MouseEventArgs(button,1,x,y,0)});}
 static void QuickCut(Timeline timeline,string track,double time){int x=(int)timeline.X(time),y=timeline.TrackTop(track)+timeline.TrackHeight(track)/2;Mouse(timeline,"OnMouseDown",MouseButtons.Left,x,y);Mouse(timeline,"OnMouseDown",MouseButtons.Right,x,y);Mouse(timeline,"OnMouseUp",MouseButtons.Right,x,y);Mouse(timeline,"OnMouseUp",MouseButtons.Left,x,y);}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();
  typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  string bundle=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","..","dev-0314","full-smoke-verified","Prometheus-0.3.14-win-x64"));
  Tools.Root=bundle;
  var editor=new Editor();var p=editor.Project;
  string videoTrack=p.Active.tracks.First(t=>t.kind=="video").id,audioTrack=p.Active.tracks.First(t=>t.kind=="audio").id;
  var solid=GeneratedMedia.CreateSolid("Sólido", "FF8844", 5);
  var visual=GeneratedMedia.CreatePlaceholder("Visual reservado", "", 5);
  var audio=GeneratedMedia.CreateAudioPlaceholder("Audio reservado", "", 5);
  var image=Media.Inspect(System.IO.Path.Combine(bundle,"demo","grafico.png"),System.Threading.CancellationToken.None);
  p.assets.AddRange(new[]{solid,visual,audio,image});
  Media.Insert(p,solid.id,videoTrack,0);Media.Insert(p,visual.id,videoTrack,12);Media.Insert(p,audio.id,audioTrack,2);
  Media.Insert(p,image.id,videoTrack,40);
  editor.Reset(p);
  string solidId=p.Active.clips.First(c=>c.asset_id==solid.id).id;
  string visualId=p.Active.clips.First(c=>c.asset_id==visual.id).id;
  string audioId=p.Active.clips.First(c=>c.asset_id==audio.id).id;
  string imageId=p.Active.clips.First(c=>c.asset_id==image.id).id;
  editor.Trim(audioId,false,10);
  Check(Math.Abs(editor.Project.Active.clips.Find(c=>c.id==audioId).End-10)<0.0001,"audio reservado se extiende después de su duración inicial");
  editor.Trim(audioId,true,0);
  Check(Math.Abs(editor.Project.Active.clips.Find(c=>c.id==audioId).timeline_start.Seconds)<0.0001,"audio reservado se extiende hacia atrás sin fuente negativa");
  editor.Trim(solidId,false,8);editor.Move(solidId,18,videoTrack);
  Check(Math.Abs(editor.Project.Active.clips.Find(c=>c.id==solidId).timeline_start.Seconds-18)<0.0001,"sólido extendido se mueve");
  editor.Trim(visualId,false,18);editor.Move(visualId,30,videoTrack);
  Check(Math.Abs(editor.Project.Active.clips.Find(c=>c.id==visualId).timeline_start.Seconds-30)<0.0001,"espacio reservado visual extendido se mueve");
  editor.Trim(imageId,true,38);editor.Trim(imageId,false,47);editor.Move(imageId,50,videoTrack);
  Check(Math.Abs(editor.Project.Active.clips.Find(c=>c.id==imageId).timeline_start.Seconds-50)<0.0001,"imagen extendida se mueve");
  Check(editor.CutSelection(new System.Collections.Generic.List<string>{solidId},20,false),"sólido admite división");
  Check(editor.CutSelection(new System.Collections.Generic.List<string>{audioId},5,false),"audio reservado admite división");
  Check(editor.CutSelection(new System.Collections.Generic.List<string>{imageId},52,false),"imagen extendida admite división");
  Store.Validate(Store.Decode(Store.Encode(editor.Project)));
  Check(true,"proyecto con espacio reservado extendido se serializa y abre");
  using(var view=new MediaPanelView()){
   view.SetProject(editor.Project);
   Check(view.Library.Items.Count==2,"espacios reservados ocultos de Medios sin borrar sus clips");
   Call(view,"ToggleView");Call(view,"ToggleView");
   Check((int)Field(view,"viewMode")==2&&view.Library.ItemHeight==26,"vista de lista compacta sin miniaturas");
   Call(view,"SortListAt",200);
   Check(view.Library.Items.Count==2,"ordenar la lista conserva sus medios");
   using(var host=new Form{Width=720,Height=440,StartPosition=FormStartPosition.Manual,Location=new Point(40,40)}){
    host.Controls.Add(view);host.Show();Application.DoEvents();
    using(var screenshot=new Bitmap(host.Width,host.Height)){
     host.DrawToBitmap(screenshot,new Rectangle(0,0,screenshot.Width,screenshot.Height));
     screenshot.Save(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"media-list-03141.png"));
    }
    host.Close();
   }
  }
  using(var form=new MainForm()){
   form.Show();Application.DoEvents();
   var formEditor=(Editor)Field(form,"editor");var timeline=(Timeline)Field(form,"timeline");
   var q=Store.New();string track=q.Active.tracks.First(t=>t.kind=="video").id;
   var testSolid=GeneratedMedia.CreateSolid("Prueba", "FF8844", 5);q.assets.Add(testSolid);Media.Insert(q,testSolid.id,track,0);
   formEditor.Reset(q);Call(form,"RefreshProject");timeline.Zoom=55;
   int x=(int)timeline.X(2.5),y=timeline.TrackTop(track)+timeline.TrackHeight(track)/2;
   Mouse(timeline,"OnMouseDown",MouseButtons.Left,x,y);
   Mouse(timeline,"OnMouseMove",MouseButtons.Left,x+110,y);
   Mouse(timeline,"OnMouseDown",MouseButtons.Right,x+110,y);
   Mouse(timeline,"OnMouseUp",MouseButtons.Right,x+110,y);
   Mouse(timeline,"OnMouseUp",MouseButtons.Left,x+110,y);
   Check(formEditor.Project.Active.clips.Count==1&&formEditor.Project.Active.clips.Single().timeline_start.Seconds>1,"sólido se arrastra; el clic derecho durante el movimiento no corta");
   x=(int)timeline.X(formEditor.Project.Active.clips.Single().timeline_start.Seconds+2.5);
   Mouse(timeline,"OnMouseDown",MouseButtons.Left,x,y);
   Mouse(timeline,"OnMouseDown",MouseButtons.Right,x,y);
   Mouse(timeline,"OnMouseUp",MouseButtons.Right,x,y);
   Mouse(timeline,"OnMouseUp",MouseButtons.Left,x,y);
   Check(formEditor.Project.Active.clips.Count==2,"clic izquierdo sostenido + derecho divide un sólido");
   q=Store.New();track=q.Active.tracks.First(t=>t.kind=="video").id;
   var testImage=Media.Inspect(System.IO.Path.Combine(bundle,"demo","grafico.png"),System.Threading.CancellationToken.None);
   q.assets.Add(testImage);Media.Insert(q,testImage.id,track,5);formEditor.Reset(q);Call(form,"RefreshProject");
   string clipId=q.Active.clips.Single().id;formEditor.Trim(clipId,true,2);formEditor.Trim(clipId,false,13);Call(form,"RefreshProject");
   x=(int)timeline.X(7);y=timeline.TrackTop(track)+timeline.TrackHeight(track)/2;
   Mouse(timeline,"OnMouseDown",MouseButtons.Left,x,y);Mouse(timeline,"OnMouseMove",MouseButtons.Left,x+110,y);Mouse(timeline,"OnMouseUp",MouseButtons.Left,x+110,y);
   Check(formEditor.Project.Active.clips.Single().timeline_start.Seconds>1,"imagen expandida se arrastra en la interfaz");
   QuickCut(timeline,track,formEditor.Project.Active.clips.Single().timeline_start.Seconds+3);
   Check(formEditor.Project.Active.clips.Count==2,"atajo de dos botones divide una imagen expandida");
   q=Store.New();track=q.Active.tracks.First(t=>t.kind=="video").id;
   var testPlaceholder=GeneratedMedia.CreatePlaceholder("Pendiente", "",5);q.assets.Add(testPlaceholder);Media.Insert(q,testPlaceholder.id,track,0);
   formEditor.Reset(q);Call(form,"RefreshProject");QuickCut(timeline,track,2.5);
   Check(formEditor.Project.Active.clips.Count==2,"atajo de dos botones divide un espacio reservado visual");
   q=Store.New();track=q.Active.tracks.First(t=>t.kind=="audio").id;
   var testAudio=GeneratedMedia.CreateAudioPlaceholder("Pendiente audio", "",5);q.assets.Add(testAudio);Media.Insert(q,testAudio.id,track,0);
   formEditor.Reset(q);Call(form,"RefreshProject");clipId=q.Active.clips.Single().id;
   int edge=(int)Math.Floor(timeline.ClipBounds(q.Active.clips.Single()).Right)-2;
   y=timeline.TrackTop(track)+timeline.TrackHeight(track)/2;
   Mouse(timeline,"OnMouseDown",MouseButtons.Left,edge,y);Mouse(timeline,"OnMouseMove",MouseButtons.Left,edge+110,y);Mouse(timeline,"OnMouseUp",MouseButtons.Left,edge+110,y);
   Check(formEditor.Project.Active.clips.Single().duration.Seconds>5,"borde de audio reservado se extiende desde la interfaz");
   QuickCut(timeline,track,2.5);
   Check(formEditor.Project.Active.clips.Count==2,"atajo de dos botones divide audio reservado");
   typeof(MainForm).GetField("dirty",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,false);
   form.Close();
  }
  Console.WriteLine("PASS TOTAL "+checks);
 }
}
