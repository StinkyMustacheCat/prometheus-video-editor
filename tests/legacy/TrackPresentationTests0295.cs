using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Prometheus;

class TrackPresentationTests0295 {
 static int count;
 static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);count++;}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();
  var project=Store.New();var sequence=project.Active;
  var audio=sequence.tracks.First(t=>t.kind=="audio");var video=sequence.tracks.First(t=>t.kind=="video");
  Check(!TrackPresentation.ShowNames(sequence,audio)&&TrackPresentation.ShowNames(sequence,video),"audio oculta nombres por defecto y vídeo los muestra");
  TrackPresentation.SetHeight(sequence,audio.id,86);
  TrackPresentation.SetCompact(sequence,audio.id,true);
  Check(TrackPresentation.Height(sequence,audio.id)==32,"minimizar pista aplica la altura compacta");
  TrackPresentation.SetCompact(sequence,audio.id,false);
  Check(TrackPresentation.Height(sequence,audio.id)==86,"restaurar recupera la altura personalizada");
  TrackPresentation.SetShowNames(sequence,audio.id,true);
  string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"prometheus-track-presentation-"+Guid.NewGuid().ToString("N")+".json");
  try{
   Store.Save(project,file);var reopened=Store.Load(file);
   Check(TrackPresentation.ShowNames(reopened.Active,reopened.Active.tracks.First(t=>t.id==audio.id))&&TrackPresentation.Height(reopened.Active,audio.id)==86,"las preferencias sobreviven al guardado");
   using(var timeline=new Timeline{Project=reopened}){
    Check(timeline.TrackHeight(audio.id)==86,"la interfaz lee la altura guardada");
    timeline.SetTrackHeight(audio.id,110);
    Check(timeline.TrackHeight(audio.id)==110,"el ajuste manual se ve durante el arrastre");
    timeline.ClearTrackHeightOverride(audio.id);
    Check(timeline.TrackHeight(audio.id)==86,"restaurar el estado persistente limpia la vista temporal");
   }
  }finally{if(File.Exists(file))File.Delete(file);if(File.Exists(file+".bak"))File.Delete(file+".bak");}
  Console.WriteLine("PASS TOTAL "+count);
 }
}
