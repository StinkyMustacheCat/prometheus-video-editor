using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Prometheus;

class TimelinePlacementUiTests039 {
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static object Field(object target,string name){return target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
 static void Call(object target,string name,params object[] args){target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);}
 static void Press(Timeline timeline,MouseButtons button,int x,int y){typeof(Timeline).GetMethod("OnMouseDown",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(timeline,new object[]{new MouseEventArgs(button,1,x,y,0)});}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();typeof(MainForm).Assembly.GetType("Prometheus.App").GetField("Capture",BindingFlags.Static|BindingFlags.Public).SetValue(null,true);
  using(var form=new MainForm()){
   form.Show();Application.DoEvents();var editor=(Editor)Field(form,"editor");var timeline=(Timeline)Field(form,"timeline");var menu=(ContextMenuStrip)Field(form,"gapMenu");
   var project=Store.New();var image=GeneratedMedia.CreateSolid("Color de prueba","FF8833",5);project.assets.Add(image);
   var v1=project.Active.tracks.First(t=>t.kind=="video"&&t.order==0);var v2=project.Active.tracks.First(t=>t.kind=="video"&&t.order==1);
   Media.Insert(project,image.id,v1.id,1);editor.Reset(project);Call(form,"RefreshProject");
   int x=(int)timeline.X(3),y=timeline.TrackTop(v2.id)+20;
   Press(timeline,MouseButtons.Right,x,y);
   Check(timeline.SelectedGapTrack==null,"clic derecho en vacío no selecciona espacio");
   Check(menu.Items.Cast<ToolStripItem>().Any(item=>item.Text=="Añadir medio del proyecto"),"clic derecho ofrece insertar medio");menu.Close();
   Call(form,"PlaceAssetAt",image.id,v1.id,1d);
   Check(editor.Project.Active.clips.Count==2&&timeline.Selected!=null,"inserción contextual conserva anterior y selecciona nuevo");
   Check(editor.Project.Active.clips.Find(c=>c.id==timeline.Selected).track_id==v2.id,"nuevo clip salta a pista superior disponible");
   Check(timeline.ActiveTrack==v2.id,"la pista usada queda visible como activa");
   form.GetType().GetField("dirty",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,false);form.Close();
  }
 }
}
