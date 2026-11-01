using System;
using System.Drawing;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  void CreateSolid(){CreateGenerated(false);}
  void CreatePlaceholder(){CreateGenerated(true);}
  void CreateAudioPlaceholder(){CreateGenerated(true,null,0,true);}
  void CreateGenerated(bool placeholder){CreateGenerated(placeholder,null,0,false);}
  void CreateGenerated(bool placeholder,string trackId,double at){CreateGenerated(placeholder,trackId,at,false);}
  void CreateGenerated(bool placeholder,string trackId,double at,bool audio,double? exactDuration=null){
   using(var dialog=new Form{Text=placeholder?"Crear espacio reservado":"Crear color sólido",Width=485,Height=placeholder?290:250,FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,BackColor=panel,ForeColor=text}){
    var nameLabel=new Label{Text="Nombre",Left=18,Top=16,Width=420,Height=20};
    var name=new TextBox{Left=18,Top=39,Width=430,MaxLength=120,Text=audio?"Audio reservado":placeholder?"Espacio reservado":"Color sólido"};
    var durationLabel=new Label{Text="Duración (HH:MM:SS:FF, a 60 FPS)",Left=18,Top=74,Width=420,Height=20};
    long frames=exactDuration.HasValue?(long)Math.Round(exactDuration.Value*60):300;
    string fixedTime=(frames/216000).ToString("D2")+":"+((frames/3600)%60).ToString("D2")+":"+((frames/60)%60).ToString("D2")+":"+(frames%60).ToString("D2");
    var duration=new TextBox{Left=18,Top=98,Width=168,Text=exactDuration.HasValue?fixedTime:"00:00:05:00",ReadOnly=exactDuration.HasValue};
    var noteLabel=new Label{Text="Nota para encontrar el medio después",Left=18,Top=130,Width=420,Height=20};
    var note=new TextBox{Left=18,Top=153,Width=430,Height=50,Multiline=true,MaxLength=500};
    Color chosen=Color.FromArgb(24,28,34);
    var color=new ColorPickerWidget("Color sólido",chosen){Left=205,Top=96,Width=222};color.ColorChanged=value=>chosen=value;
    var ok=new Button{Text="Crear",DialogResult=DialogResult.OK,Left=276,Top=placeholder?211:169,Width=80};
    var cancel=new Button{Text="Cancelar",DialogResult=DialogResult.Cancel,Left=365,Top=placeholder?211:169,Width=83};
    dialog.Controls.AddRange(new Control[]{nameLabel,name,durationLabel,duration,ok,cancel});
    if(placeholder)dialog.Controls.AddRange(new Control[]{noteLabel,note});else dialog.Controls.Add(color);
    dialog.AcceptButton=ok;dialog.CancelButton=cancel;
    if(dialog.ShowDialog(this)!=DialogResult.OK)return;
    double seconds;if(!MonitorPanelView.TryParseTimecode(duration.Text,out seconds)||seconds<=0)throw new Exception("Escribe una duración válida, por ejemplo 00:00:05:00");if(exactDuration.HasValue)seconds=exactDuration.Value;
    string hex=chosen.R.ToString("X2")+chosen.G.ToString("X2")+chosen.B.ToString("X2");
    var asset=audio?GeneratedMedia.CreateAudioPlaceholder(name.Text,note.Text,seconds):placeholder?GeneratedMedia.CreatePlaceholder(name.Text,note.Text,seconds):GeneratedMedia.CreateSolid(name.Text,hex,seconds);
    // A placeholder is an editorial clip, so creating it from Medios places it
    // directly on a compatible timeline track instead of leaving an invisible asset.
    if(placeholder&&trackId==null){
     string kind=audio?"audio":"video";
     var active=editor.Project.Active.tracks.Find(t=>t.id==timeline.ActiveTrack&&t.kind==kind&&!t.locked);
     var fallback=editor.Project.Active.tracks.Find(t=>t.kind==kind&&!t.locked);
     trackId=(active??fallback)==null?null:(active??fallback).id;
     at=timeline.Cursor;
     if(trackId==null)throw new Exception("No hay una pista compatible disponible para el espacio reservado");
    }
    PlacementResult placed=null;
    Edit(()=>editor.Commit(p=>{p.assets.Add(asset);if(trackId!=null)placed=Media.PlaceInFreeTrack(p,asset.id,trackId,at);}));
    if(placeholder){if(placed!=null)RevealPlacedClip(placed);}else{mediaView.Reveal(asset.id);if(placed!=null)RevealPlacedClip(placed);else ShowAsset();}
    status.Text=placed!=null?"Medio creado y añadido a la línea de tiempo":"Color sólido creado · arrástralo a la línea de tiempo";
   }
  }
 }
}
