using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  const string ClipClipboardFormat="Prometheus.ClipCopyToken.v1";
  string clipClipboardToken;
  readonly string webSettingsPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","web-settings-v1.txt");
  bool pasteWebAutomatically,prepareWebPreviewAutomatically=true;

  void MarkInternalClipboard(){
   clipClipboardToken=Guid.NewGuid().ToString("N");
   try{var data=new DataObject();data.SetData(ClipClipboardFormat,clipClipboardToken);Clipboard.SetDataObject(data,true);}catch{clipClipboardToken=null;status.Text="Clips copiados; Windows no permitió escribir en el portapapeles";}
  }
  void LoadWebSettings(){
   try{foreach(string line in File.ReadAllLines(webSettingsPath)){
    if(line=="paste=automatic")pasteWebAutomatically=true;
    if(line=="preview=manual")prepareWebPreviewAutomatically=false;
    if(line=="preview-height=720")WebMedia.PreviewHeight=720;
    if(line=="preview-height=1080")WebMedia.PreviewHeight=1080;
    if(line.StartsWith("browser=",StringComparison.Ordinal))WebMedia.CookieBrowser=line.Substring(8);
    if(line.StartsWith("cookiefile=",StringComparison.Ordinal))WebMedia.CookieFile=line.Substring(11);
   }}catch(IOException){}catch(UnauthorizedAccessException){}
  }
  void SaveWebSettings(){Directory.CreateDirectory(Path.GetDirectoryName(webSettingsPath));File.WriteAllLines(webSettingsPath,new[]{"paste="+(pasteWebAutomatically?"automatic":"confirm"),"preview="+(prepareWebPreviewAutomatically?"automatic":"manual"),"preview-height="+WebMedia.PreviewHeight,"browser="+(WebMedia.CookieBrowser??""),"cookiefile="+(WebMedia.CookieFile??"")});}
  bool ShowWebSettings(){
   using(var dialog=new Form{Text="Ajustes · Medios web",ClientSize=new Size(520,350),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,BackColor=panel,ForeColor=text}){
    var intro=new Label{Text="Comportamiento al pegar un enlace web",Left=18,Top=15,Width=430,Height=24};
    var confirm=new RadioButton{Text="Abrir Importar enlace para revisar (recomendado)",Left=18,Top=44,Width=440,Height=25,Checked=!pasteWebAutomatically};
    var automatic=new RadioButton{Text="Importar directamente al pegar",Left=18,Top=73,Width=440,Height=25,Checked=pasteWebAutomatically};
    var preview=new CheckBox{Text="Preparar vista ligera al importar vídeos",Left=18,Top=108,Width=440,Height=25,Checked=prepareWebPreviewAutomatically};
    var qualityLabel=new Label{Text="Calidad predeterminada de la vista ligera",Left=18,Top=145,Width=470,Height=23};
    var quality=new ComboBox{Left=18,Top=169,Width=315,DropDownStyle=ComboBoxStyle.DropDownList,BackColor=bg,ForeColor=text};quality.Items.AddRange(new object[]{"Ligera · 360p","Equilibrada · 720p","Alta · 1080p"});quality.SelectedIndex=WebMedia.PreviewHeight>=1080?2:WebMedia.PreviewHeight>=720?1:0;
    var authLabel=new Label{Text="Acceso a sitios que piden iniciar sesión",Left=18,Top=208,Width=470,Height=23};
    var auth=new ComboBox{Left=18,Top=233,Width=197,DropDownStyle=ComboBoxStyle.DropDownList,BackColor=bg,ForeColor=text};auth.Items.AddRange(new object[]{"Sin cookies","Chrome","Edge","Firefox","Archivo de cookies"});
    auth.SelectedIndex=!String.IsNullOrWhiteSpace(WebMedia.CookieFile)?4:WebMedia.CookieBrowser=="chrome"?1:WebMedia.CookieBrowser=="edge"?2:WebMedia.CookieBrowser=="firefox"?3:0;
    var cookiePath=new TextBox{Left=223,Top=233,Width=214,Text=WebMedia.CookieFile??"",ReadOnly=true,BackColor=bg,ForeColor=text};
    var browse=new Button{Text="…",Left=444,Top=231,Width=43,Height=27};
    browse.Click+=(s,e)=>{using(var picker=new OpenFileDialog{Title="Seleccionar cookies en formato Netscape",Filter="Archivo de cookies (*.txt)|*.txt|Todos los archivos|*.*"})if(picker.ShowDialog(dialog)==DialogResult.OK){cookiePath.Text=picker.FileName;auth.SelectedIndex=4;}};
    var authNote=new Label{Text="La fuente completa se conserva para exportar; cambiar la calidad afecta las próximas vistas ligeras.",Left=18,Top=270,Width=480,Height=35,ForeColor=Color.FromArgb(170,174,180)};
    var save=new Button{Text="Guardar",Left=324,Top=311,Width=82,DialogResult=DialogResult.OK};
    var cancel=new Button{Text="Cancelar",Left=416,Top=311,Width=82,DialogResult=DialogResult.Cancel};
    dialog.Controls.AddRange(new Control[]{intro,confirm,automatic,preview,qualityLabel,quality,authLabel,auth,cookiePath,browse,authNote,save,cancel});dialog.AcceptButton=save;dialog.CancelButton=cancel;
    if(dialog.ShowDialog(this)==DialogResult.OK){pasteWebAutomatically=automatic.Checked;prepareWebPreviewAutomatically=preview.Checked;WebMedia.PreviewHeight=quality.SelectedIndex==2?1080:quality.SelectedIndex==1?720:360;WebMedia.CookieBrowser=auth.SelectedIndex==1?"chrome":auth.SelectedIndex==2?"edge":auth.SelectedIndex==3?"firefox":null;WebMedia.CookieFile=auth.SelectedIndex==4?cookiePath.Text:null;try{SaveWebSettings();status.Text="Preferencias de medios web guardadas";return true;}catch(Exception ex){Error(ex);}}
   }
   return false;
  }
  string PasteTrack(bool audio){
   var active=editor.Project.Active.tracks.Find(t=>t.id==timeline.ActiveTrack&&!t.locked&&t.kind==(audio?"audio":"video"));
   if(active!=null)return active.id;
   var other=editor.Project.Active.tracks.Where(t=>!t.locked&&t.kind==(audio?"audio":"video")).OrderBy(t=>t.order).FirstOrDefault();
   return other==null?null:other.id;
  }
  static bool IsAudioFile(string file){return Media.IsAudioFileExtension(file);}
  void PasteFiles(string[] files,bool toTimeline){
   files=files.Where(File.Exists).ToArray();if(files.Length==0){status.Text="No hay archivos disponibles para pegar";return;}
   if(!toTimeline){StartImport(files,null,0);return;}
   bool audio=IsAudioFile(files[0]);string track=PasteTrack(audio);
   if(track==null){status.Text="No hay una pista "+(audio?"de audio":"visual")+" desbloqueada";return;}
   if(files.Any(f=>IsAudioFile(f)!=audio)){status.Text="La selección mezcla audio y vídeo; pégalos por separado en la línea de tiempo";return;}
   StartImport(files,track,timeline.Cursor,true);
  }
  void PasteBitmap(bool toTimeline){
   string folder=projectFile==null?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","ClipboardMedia"):ProjectStorage.ImportDirectory(projectFile);
   Directory.CreateDirectory(folder);string file=Path.Combine(folder,"imagen-"+Guid.NewGuid().ToString("N")+".png");
   using(var image=Clipboard.GetImage()){
    if(image==null){status.Text="La imagen copiada no está disponible";return;}
    image.Save(file,ImageFormat.Png);
   }
   PasteFiles(new[]{file},toTimeline);
  }
  void PasteUrl(string value,bool toTimeline){
   string track=null;
   if(toTimeline){bool audio=timeline.ActiveTrack!=null&&editor.Project.Active.tracks.Any(t=>t.id==timeline.ActiveTrack&&t.kind=="audio");track=PasteTrack(audio);}
   if(pasteWebAutomatically){StartWebImport(new WebDropInfo{Url=value,PageUrl=value,IsYouTube=WebMedia.IsYouTubeUrl(value)},track,toTimeline?timeline.Cursor:0,toTimeline);return;}
   ShowImportLink(track,toTimeline?timeline.Cursor:0,toTimeline,value);
  }
  void PasteContextual(){
   bool toTimeline=!mediaView.ContainsFocus;
   try{
    var data=Clipboard.GetDataObject();
    if(data==null){status.Text="El portapapeles está vacío";return;}
    if(data.GetDataPresent(ClipClipboardFormat)&&clipClipboardToken!=null&&String.Equals(data.GetData(ClipClipboardFormat) as string,clipClipboardToken,StringComparison.Ordinal)){Paste();return;}
    if(data.GetDataPresent(DataFormats.FileDrop)){PasteFiles(data.GetData(DataFormats.FileDrop) as string[],toTimeline);return;}
    if(Clipboard.ContainsImage()){PasteBitmap(toTimeline);return;}
    if(Clipboard.ContainsText()){
     string value=Clipboard.GetText().Trim();Uri uri;
     if(MgtMedia.LooksLikeDocument(value)){
      string track=toTimeline?PasteTrack(false):null;
      if(toTimeline&&track==null){status.Text="No hay una pista de vídeo desbloqueada";return;}
      StartImport(new[]{SavePastedMgt(value)},track,toTimeline?timeline.Cursor:0,toTimeline);return;
     }
     if(Uri.TryCreate(value,UriKind.Absolute,out uri)&&(uri.Scheme=="http"||uri.Scheme=="https")){PasteUrl(value,toTimeline);return;}
    }
    status.Text="El contenido copiado no es un medio o enlace compatible";
   }catch(System.Runtime.InteropServices.ExternalException){status.Text="Windows está usando el portapapeles; vuelve a intentar pegar";}
  }
 }
}
