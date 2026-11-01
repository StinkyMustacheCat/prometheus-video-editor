using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  CancellationTokenSource placeholderLinkToken;
  void CancelPlaceholderLink(){if(placeholderLinkToken!=null){placeholderLinkToken.Cancel();placeholderLinkToken=null;}}
  bool AudioPlaceholder(string clipId){var clip=editor.Project.Active.clips.Find(c=>c.id==clipId);return clip!=null&&editor.Project.Active.tracks.Any(t=>t.id==clip.track_id&&t.kind=="audio");}
  void AddPlaceholderContext(ContextMenuStrip menu,string clipId){
   var clip=editor.Project.Active.clips.Find(c=>c.id==clipId);
   if(clip==null||!GeneratedMedia.IsPlaceholder(editor.Project.assets.Find(a=>a.id==clip.asset_id)))return;
   menu.Items.Add(new ToolStripSeparator());
   bool audio=AudioPlaceholder(clipId);var content=new ToolStripMenuItem("Definir contenido");
   content.DropDownItems.Add("Medio del proyecto…",null,(s,e)=>{try{ConnectPlaceholderFromLibrary(clipId);}catch(Exception ex){Error(ex);}});
   content.DropDownItems.Add(audio?"Audio local…":"Archivo local…",null,(s,e)=>{try{ConnectPlaceholderFile(clipId,false);}catch(Exception ex){Error(ex);}});
   if(!audio){
    content.DropDownItems.Add("Imagen local…",null,(s,e)=>{try{ConnectPlaceholderFile(clipId,true);}catch(Exception ex){Error(ex);}});
    content.DropDownItems.Add("Pegar MGT (JSON)…",null,(s,e)=>{try{PasteMgtDialog(clipId);}catch(Exception ex){Error(ex);}});
   }
   content.DropDownItems.Add(audio?"Enlace web de audio…":"Enlace web…",null,(s,e)=>{try{ConnectPlaceholderWeb(clipId);}catch(Exception ex){Error(ex);}});
   menu.Items.Add(content);
  }
  double ParseSourceStart(string text){double seconds;if(!MonitorPanelView.TryParseTimecode(text,out seconds))throw new Exception("Usa HH:MM:SS:FF para el inicio del medio");return seconds;}
  string AskStartTime(string title){
   using(var dialog=new Form{Text=title,Width=390,Height=165,FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,BackColor=panel,ForeColor=this.text}){
    dialog.Controls.Add(new Label{Text="Inicio dentro del medio (HH:MM:SS:FF)",Left=16,Top=17,Width=340,Height=22});
    var input=new TextBox{Text="00:00:00:00",Left=16,Top=44,Width=160};dialog.Controls.Add(input);
    var ok=new Button{Text="Conectar",DialogResult=DialogResult.OK,Left=185,Top=81,Width=82};var cancel=new Button{Text="Cancelar",DialogResult=DialogResult.Cancel,Left=276,Top=81,Width=82};dialog.Controls.Add(ok);dialog.Controls.Add(cancel);dialog.AcceptButton=ok;dialog.CancelButton=cancel;
    return dialog.ShowDialog(this)==DialogResult.OK?input.Text:null;
   }
  }
  void ReplacePlaceholderNow(string clipId,string assetId,double sourceStart){
   Edit(()=>editor.Commit(p=>PlaceholderReplacement.Replace(p,clipId,assetId,sourceStart)));
   status.Text="Medio conectado · se conservaron posición y duración del clip";
  }
  bool CanDropAssetOnPlaceholder(string clipId,string assetId){
   var clip=editor.Project.Active.clips.Find(c=>c.id==clipId);
   var asset=editor.Project.assets.Find(a=>a.id==assetId);
   return PlaceholderReplacement.Accepts(editor.Project,clip,asset);
  }
  bool CanDropWebOnPlaceholder(string clipId,WebDropInfo web){
   var clip=editor.Project.Active.clips.Find(c=>c.id==clipId);
   if(clip==null||web==null||!WebMedia.IsImageUrl(web.Url))return false;
   var track=editor.Project.Active.tracks.Find(t=>t.id==clip.track_id);
   if(track==null||track.locked||!GeneratedMedia.IsPlaceholder(editor.Project.assets.Find(a=>a.id==clip.asset_id)))return false;
   if(track.kind=="audio")return WebMedia.Platform(web.Url)!=null;
   string expected=PlaceholderReplacement.ExpectedType(clip);
   if(expected=="image"&&WebMedia.Platform(web.Url)!=null)return false;
   if(expected=="video"){
    string extension=Path.GetExtension(new Uri(web.Url).AbsolutePath).ToLowerInvariant();
    if(new[]{".png",".jpg",".jpeg",".gif",".webp",".bmp",".avif"}.Contains(extension))return false;
   }
   return true;
  }
  void ConnectPlaceholderFromLibrary(string clipId){
   var clip=editor.Project.Active.clips.Find(c=>c.id==clipId);if(clip==null)return;
   bool audio=AudioPlaceholder(clipId);string expected=PlaceholderReplacement.ExpectedType(clip);
   var candidates=editor.Project.assets.Where(a=>!GeneratedMedia.IsPlaceholder(a)&&(audio?a.streams.Any(st=>st.kind=="audio")&&(a.source.kind!="web"||WebMedia.IsAudioOnly(a)):a.streams.Any(st=>st.kind=="video"||st.kind=="image")&&(expected!="video"||a.streams.Any(st=>st.kind=="video"))&&(expected!="image"||a.streams.Any(st=>st.kind=="image"))&&(expected!="web"||a.source.kind=="web"))).ToList();
   if(candidates.Count==0)throw new Exception("No hay medios compatibles en la biblioteca");
   using(var dialog=new Form{Text="Conectar medio del proyecto",Width=490,Height=420,FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,BackColor=panel,ForeColor=text}){
    var search=new TextBox{Left=16,Top=15,Width=445};var list=new ListBox{Left=16,Top=47,Width=445,Height=254,BackColor=Color.FromArgb(36,38,42),ForeColor=text,IntegralHeight=false};
    var startLabel=new Label{Text="Inicio en la fuente (HH:MM:SS:FF)",Left=16,Top=309,Width=270,Height=22};var start=new TextBox{Text="00:00:00:00",Left=290,Top=307,Width=170};
    var ok=new Button{Text="Conectar",DialogResult=DialogResult.OK,Left=285,Top=342,Width=82};var cancel=new Button{Text="Cancelar",DialogResult=DialogResult.Cancel,Left=376,Top=342,Width=84};
    var visible=new List<Asset>();Action refresh=()=>{string selected=list.SelectedItem as string;visible.Clear();visible.AddRange(candidates.Where(a=>a.name.IndexOf(search.Text.Trim(),StringComparison.OrdinalIgnoreCase)>=0).OrderBy(a=>a.name,StringComparer.CurrentCultureIgnoreCase));list.Items.Clear();foreach(var asset in visible)list.Items.Add(asset.name+(asset.source.kind=="web"?" · web":""));if(list.Items.Count>0)list.SelectedIndex=0;};search.TextChanged+=(s,e)=>refresh();refresh();
    dialog.Controls.AddRange(new Control[]{search,list,startLabel,start,ok,cancel});dialog.AcceptButton=ok;dialog.CancelButton=cancel;
    if(dialog.ShowDialog(this)!=DialogResult.OK)return;if(list.SelectedIndex<0||list.SelectedIndex>=visible.Count)throw new Exception("Selecciona un medio");
    var chosen=visible[list.SelectedIndex];ReplacePlaceholderNow(clipId,chosen.id,!audio&&chosen.streams.Any(st=>st.kind=="image")&&!chosen.streams.Any(st=>st.kind=="video")?0:ParseSourceStart(start.Text));
   }
  }
  void ConnectPlaceholderFile(string clipId,bool imageOnly){
   bool audio=AudioPlaceholder(clipId);
   using(var picker=new OpenFileDialog{Filter=audio?"Audio WAV o MP4|*.wav;*.mp4":imageOnly?"Imagen|*.png;*.jpg;*.jpeg":"Video, imagen o MGT|*.mp4;*.mov;*.png;*.jpg;*.jpeg;*.mgt*.json;*.mgtproj",Multiselect=false}){
    if(picker.ShowDialog(this)!=DialogResult.OK)return;
    bool mgt=MgtMedia.IsFile(picker.FileName);
    string startText=imageOnly||mgt?"00:00:00:00":AskStartTime("Elegir inicio del medio");if(startText==null)return;
    StartPlaceholderFileLink(clipId,picker.FileName,imageOnly||mgt?0:ParseSourceStart(startText),imageOnly);
   }
  }
  void ConnectPlaceholderWeb(string clipId){
   bool audio=AudioPlaceholder(clipId);
   using(var dialog=new Form{Text="Conectar enlace web",Width=560,Height=215,FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,BackColor=panel,ForeColor=text}){
    dialog.Controls.Add(new Label{Text=audio?"Enlace público para extraer solo audio":"Enlace público del video o imagen",Left=16,Top=16,Width=510,Height=22});
    var url=new TextBox{Left=16,Top=41,Width=510};dialog.Controls.Add(url);
    dialog.Controls.Add(new Label{Text="Inicio en la fuente (HH:MM:SS:FF; para imágenes, 00:00:00:00)",Left=16,Top=76,Width=510,Height=22});
    var start=new TextBox{Text="00:00:00:00",Left=16,Top=101,Width=160};dialog.Controls.Add(start);
    var ok=new Button{Text="Conectar",DialogResult=DialogResult.OK,Left=350,Top=132,Width=82};var cancel=new Button{Text="Cancelar",DialogResult=DialogResult.Cancel,Left=442,Top=132,Width=84};dialog.Controls.Add(ok);dialog.Controls.Add(cancel);dialog.AcceptButton=ok;dialog.CancelButton=cancel;
    if(dialog.ShowDialog(this)!=DialogResult.OK)return;string address=url.Text.Trim();if(audio?WebMedia.Platform(address)==null:!WebMedia.IsImageUrl(address))throw new Exception(audio?"Pega un enlace de vídeo público compatible":"Pega un enlace HTTP/HTTPS válido");
    double sourceStart=ParseSourceStart(start.Text);
    var existing=editor.Project.assets.FirstOrDefault(a=>a.source!=null&&a.source.kind=="web"&&String.Equals(a.source.uri,address,StringComparison.OrdinalIgnoreCase)&&(!audio||WebMedia.IsAudioOnly(a)));
    if(existing!=null){ReplacePlaceholderNow(clipId,existing.id,sourceStart);return;}
    StartPlaceholderWebLink(clipId,address,sourceStart);
   }
  }
  void StartPlaceholderFileLink(string clipId,string file,double sourceStart,bool imageOnly){
   if(placeholderLinkToken!=null){status.Text="Ya se está conectando otro medio";return;}
   var token=new CancellationTokenSource();placeholderLinkToken=token;string projectId=editor.Project.id;status.Text="Leyendo archivo para el espacio reservado…";
   Task.Factory.StartNew(()=>{
    try{var asset=Media.Inspect(file,token.Token);if(imageOnly&&!asset.streams.Any(s=>s.kind=="image"))throw new Exception("Selecciona una imagen PNG o JPEG");
     UI(()=>FinishPlaceholderLink(token,projectId,clipId,asset,sourceStart,false));}
    catch(OperationCanceledException){UI(()=>{if(placeholderLinkToken==token){placeholderLinkToken=null;status.Text="Conexión cancelada";}});}
    catch(Exception ex){UI(()=>{if(placeholderLinkToken==token){placeholderLinkToken=null;Error(ex);}});}
   });
  }
  void StartPlaceholderWebLink(string clipId,string url,double sourceStart){StartPlaceholderWebLink(clipId,url,url,sourceStart);}
  void StartPlaceholderWebLink(string clipId,string url,string pageUrl,double sourceStart,bool authRetry=false){
   if(placeholderLinkToken!=null){status.Text="Ya se está conectando otro medio";return;}
   var token=new CancellationTokenSource();placeholderLinkToken=token;string projectId=editor.Project.id;bool audio=AudioPlaceholder(clipId);status.Text=audio?"Consultando audio web…":"Analizando enlace para el espacio reservado…";
   Task.Factory.StartNew(()=>{
    try{var asset=audio?WebMedia.InspectAudio(url,token.Token):InspectWebLink(url,pageUrl,token.Token);UI(()=>FinishPlaceholderLink(token,projectId,clipId,asset,sourceStart,asset.streams.Any(s=>s.kind=="video"||s.kind=="audio")));}
    catch(OperationCanceledException){UI(()=>{if(placeholderLinkToken==token){placeholderLinkToken=null;status.Text="Conexión cancelada";}});}
    catch(Exception ex){UI(()=>{if(placeholderLinkToken!=token)return;placeholderLinkToken=null;if(WebAuthChallenge(ex)){
      if(ShowWebAccessPrompt(new[]{url})){StartPlaceholderWebLink(clipId,url,pageUrl,sourceStart,true);return;}
      status.Text="Enlace pendiente de acceso: "+url;return;
     }Error(ex);});}
   });
  }
  void FinishPlaceholderLink(CancellationTokenSource token,string projectId,string clipId,Asset asset,double sourceStart,bool preview){
   if(placeholderLinkToken!=token||token.IsCancellationRequested||editor.Project.id!=projectId)return;
   placeholderLinkToken=null;
   try{editor.Commit(p=>{p.assets.Add(asset);PlaceholderReplacement.Replace(p,clipId,asset.id,sourceStart);});dirty=true;RefreshProject();if(!playing)RequestFrame();status.Text="Medio conectado · posición y duración conservadas";if(MgtMedia.IsMgt(asset))QueueMgtPreview(asset.id);if(preview&&(prepareWebPreviewAutomatically||WebMedia.IsAudioOnly(asset)))BeginWebPreview(asset.id);}
   catch(Exception ex){Error(ex);}
  }
 }
}
