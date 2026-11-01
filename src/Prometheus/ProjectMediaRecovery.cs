using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Prometheus {
 public sealed class ProjectMediaRecovery:Form {
  readonly Project project;
  readonly string projectFile;
  readonly ListView list=new ListView();
  readonly Label summary=new Label(),progress=new Label();
  readonly Button locate=new Button(),folder=new Button(),download=new Button(),changeUrl=new Button(),open=new Button(),cancel=new Button();
  CancellationTokenSource taskToken;
  bool working;
  public bool Changed {get;private set;}
  static bool RepresentationAvailable(Asset asset,bool preview){try{Media.Verify(asset,CancellationToken.None,preview);return true;}catch{return false;}}
  static List<EffectResource> MissingResources(Project project){return project.effect_resources.Where(resource=>resource!=null&&!File.Exists(resource.uri)).ToList();}
  public static bool RequiresRecovery(Project project){return Missing(project).Count>0||MissingResources(project).Count>0;}
  public static List<Asset> Missing(Project project){
   return project.assets.Where(asset=>{
    if(asset.source==null||asset.source.kind=="generated")return false;
    if(asset.source.kind=="local"||asset.source.kind=="mgt")return !File.Exists(asset.source.uri)||asset.source.kind=="local"&&!RepresentationAvailable(asset,false);
    if(asset.source.kind=="web")return !RepresentationAvailable(asset,true);
    return false;
   }).ToList();
  }
  public ProjectMediaRecovery(Project project,string projectFile){
   this.project=project;this.projectFile=projectFile;
   Text="Resolver medios desconectados";Size=new Size(850,475);MinimumSize=new Size(730,420);StartPosition=FormStartPosition.CenterParent;
   BackColor=Color.FromArgb(36,38,42);ForeColor=Color.FromArgb(224,225,228);Font=new Font("Segoe UI",9);
   summary.Dock=DockStyle.Top;summary.Height=47;summary.Padding=new Padding(14,11,8,0);
   list.Dock=DockStyle.Fill;list.View=View.Details;list.FullRowSelect=true;list.MultiSelect=false;list.HideSelection=false;
   list.BackColor=Color.FromArgb(28,30,34);list.ForeColor=ForeColor;
   list.Columns.Add("Medio",270);list.Columns.Add("Origen",100);list.Columns.Add("Ubicación o enlace",430);
   list.SelectedIndexChanged+=(s,e)=>UpdateButtons();
   var bottom=new Panel{Dock=DockStyle.Bottom,Height=102,BackColor=BackColor};
   progress.SetBounds(14,5,805,24);progress.Text="Selecciona un medio para recuperarlo.";bottom.Controls.Add(progress);
   SetButton(locate,"Buscar archivo…",14,39,116,async (s,e)=>await LocateSelected());
   SetButton(folder,"Buscar carpeta…",138,39,120,async (s,e)=>await SearchFolder());
   SetButton(download,"Recuperar web ▾",266,39,148,(s,e)=>ShowWebRecoveryOptions());
   SetButton(changeUrl,"Cambiar enlace…",422,39,132,async (s,e)=>await ChangeSelectedUrl());
   SetButton(open,"Abrir proyecto",610,39,108,(s,e)=>{DialogResult=DialogResult.OK;Close();});
   SetButton(cancel,"Cancelar",726,39,94,(s,e)=>{if(working){taskToken.Cancel();progress.Text="Cancelando…";}else{DialogResult=DialogResult.Cancel;Close();}});
   foreach(var button in new[]{locate,folder,download,changeUrl,open,cancel})bottom.Controls.Add(button);
   Controls.Add(list);Controls.Add(bottom);Controls.Add(summary);CancelButton=cancel;
   FormClosing+=(s,e)=>{if(working){taskToken.Cancel();progress.Text="Cancelando…";e.Cancel=true;}};
   RefreshMissing();
  }
  void SetButton(Button button,string label,int x,int y,int width,EventHandler click){button.Text=label;button.SetBounds(x,y,width,29);button.Click+=click;button.FlatStyle=FlatStyle.Flat;button.BackColor=Color.FromArgb(52,55,60);button.ForeColor=ForeColor;}
  Asset Selected {get{return list.SelectedItems.Count==0?null:project.assets.Find(a=>a.id==(string)list.SelectedItems[0].Tag);}}
  EffectResource SelectedResource {get{return list.SelectedItems.Count==0?null:project.effect_resources.Find(r=>r.id==(string)list.SelectedItems[0].Tag);}}
  void RefreshMissing(){
   string selected=Selected==null?null:Selected.id;list.BeginUpdate();list.Items.Clear();
   foreach(var asset in Missing(project)){
    string kind=asset.source.kind=="web"?"Web":asset.source.kind=="mgt"?"MGT":"Local";
    var item=new ListViewItem(new[]{asset.name,kind,asset.source.uri??""}){Tag=asset.id};list.Items.Add(item);
    if(asset.id==selected)item.Selected=true;
   }
   foreach(var resource in MissingResources(project)){
    var item=new ListViewItem(new[]{"Tabla de color (LUT)","Recurso",resource.uri??""}){Tag=resource.id};list.Items.Add(item);
    if(resource.id==selected)item.Selected=true;
   }
   list.EndUpdate();summary.Text=list.Items.Count==0?"Todos los recursos están disponibles. Ya puedes abrir el proyecto.":
    "Faltan "+list.Items.Count+" recurso(s). Recupéralos antes de abrir el editor.";
   UpdateButtons();
  }
  void UpdateButtons(){
   var asset=Selected;bool has=(asset!=null||SelectedResource!=null)&&!working;
   locate.Enabled=has;folder.Enabled=!working&&Missing(project).Any(a=>a.source.kind=="local");
   download.Enabled=asset!=null&&!working&&asset.source.kind=="web";changeUrl.Enabled=download.Enabled;
   open.Enabled=!working&&!RequiresRecovery(project);cancel.Enabled=true;cancel.Text=working?"Detener":"Cancelar";
  }
  async Task Run(string message,Func<CancellationToken,bool> work){
   working=true;taskToken=new CancellationTokenSource();progress.Text=message;UpdateButtons();
   try{bool changed=await Task.Run(()=>work(taskToken.Token));if(changed)Changed=true;progress.Text=changed?"Medio recuperado.":"No se encontró un medio compatible.";RefreshMissing();}
   catch(OperationCanceledException){progress.Text="Recuperación cancelada.";}
   catch(Exception ex){progress.Text="No se pudo recuperar el medio.";MessageBox.Show(this,ex.Message,"Resolver medio",MessageBoxButtons.OK,MessageBoxIcon.Information);}
   finally{working=false;taskToken.Dispose();taskToken=null;UpdateButtons();}
  }
  static bool Compatible(Asset original,Asset replacement){
   foreach(var stream in original.streams){
    var match=replacement.streams.FirstOrDefault(s=>s.kind==stream.kind);
    if(match==null)return false;
    if(stream.duration!=null&&match.duration!=null&&match.duration.Seconds+0.001<stream.duration.Seconds)return false;
   }
   return true;
  }
  static MediaRepresentation MapRepresentation(Asset original,Asset candidate,string file){
   if(!Compatible(original,candidate))throw new Exception("El archivo no contiene las pistas o la duración que necesita este medio.");
   var info=new FileInfo(file);
   var rep=new MediaRepresentation{role="final",locator=new Locator{path=info.FullName},size_bytes=info.Length,modified_utc=info.LastWriteTimeUtc.ToString("o",System.Globalization.CultureInfo.InvariantCulture)};
   foreach(var kind in original.streams.Select(s=>s.kind).Distinct()){
    var originals=original.streams.Where(s=>s.kind==kind).ToList();var candidates=candidate.streams.Where(s=>s.kind==kind).ToList();
    if(candidates.Count<originals.Count)throw new Exception("Faltan pistas "+kind+" en el archivo elegido.");
    for(int i=0;i<originals.Count;i++)rep.stream_ordinals[originals[i].id]=candidates[i].ordinal;
   }
   return rep;
  }
  static string CopyManaged(string file,string directory,string id){
   Directory.CreateDirectory(directory);
   string extension=MgtMedia.IsJsonFile(file)?".mgt.json":Path.GetExtension(file);
   string target=Path.Combine(directory,id.Substring(0,Math.Min(12,id.Length))+"-"+Store.Id().Substring(0,8)+extension);
   File.Copy(file,target);return target;
  }
  async void ShowWebRecoveryOptions(){
   var asset=Selected;if(asset==null||asset.source.kind!="web")return;
   if(WebMedia.IsAudioOnly(asset)||asset.source.resolver_type=="image-http"){await DownloadSelected(null,false);return;}
   var menu=new ContextMenuStrip();
   menu.Items.Add("Preparar vista ligera",null,async (s,e)=>await DownloadSelected(null,true));
   menu.Items.Add("Volver a descargar completo",null,async (s,e)=>await DownloadSelected(null,false));
   menu.Closed+=(s,e)=>menu.Dispose();menu.Show(download,0,download.Height);
  }
  async Task LocateSelected(){
   var asset=Selected;var resource=SelectedResource;if(asset==null&&resource==null)return;
   using(var chooser=new OpenFileDialog{Title="Localizar "+(asset==null?"recurso LUT":asset.name),Filter="Todos los archivos|*.*"}){
    if(chooser.ShowDialog(this)!=DialogResult.OK)return;
    string file=chooser.FileName;
    await Run("Comprobando "+(asset==null?"recurso":asset.name)+"…",token=>{
     token.ThrowIfCancellationRequested();
     if(resource!=null){
      if(!String.Equals(Path.GetExtension(file),".cube",StringComparison.OrdinalIgnoreCase))throw new Exception("Selecciona un archivo LUT .cube.");
      resource.uri=CopyManaged(file,ProjectStorage.LutDirectory(projectFile),resource.id);resource.relative=false;return true;
     }
     if(asset.source.kind=="mgt"){
      if(!MgtMedia.IsFile(file))throw new Exception("Selecciona el proyecto o la animación MGT original.");
      var mgtCandidate=MgtMedia.Inspect(file,token);MgtMedia.Relink(asset,mgtCandidate);MgtMedia.ReconcileClipDurations(project,asset);MgtMedia.InvalidateSam2(project,asset.id);
      asset.source.uri=CopyManaged(file,ProjectStorage.MgtDirectory(projectFile),asset.id);asset.source.relative=false;return true;
     }
     var candidate=Media.Inspect(file,token);
     token.ThrowIfCancellationRequested();
     if(asset.source.kind=="local"){
      if(!Media.CompatibleLocal(asset,candidate))throw new Exception("El archivo no coincide con el medio original.");
      if(asset.streams.Any(s=>s.kind=="image"))candidate=Media.Inspect(CopyManaged(file,ProjectStorage.ImportDirectory(projectFile),asset.id),token);
      Media.RelinkLocal(asset,candidate);return true;
     }
     string managed=CopyManaged(file,ProjectStorage.WebDirectory(projectFile),asset.id);
     var rep=MapRepresentation(asset,candidate,managed);
     asset.representations.RemoveAll(r=>r.role=="final");asset.representations.Add(rep);Store.NormalizeAsset(asset);return true;
    });
   }
  }
  async Task SearchFolder(){
   using(var chooser=new FolderBrowserDialog{Description="Buscar medios locales faltantes por nombre"}){
    if(chooser.ShowDialog(this)!=DialogResult.OK)return;
    await Run("Buscando medios locales…",token=>{
     var plan=Media.FindRelinks(project,chooser.SelectedPath,token);
     token.ThrowIfCancellationRequested();
     foreach(var match in plan.matches){
      var asset=project.assets.Find(a=>a.id==match.Key);if(asset==null)continue;
      var replacement=match.Value;
      if(asset.streams.Any(s=>s.kind=="image"))replacement=Media.Inspect(CopyManaged(replacement.source.uri,ProjectStorage.ImportDirectory(projectFile),asset.id),token);
      Media.RelinkLocal(asset,replacement);
     }
     return plan.matches.Count>0;
    });
   }
  }
  async Task DownloadSelected(string updatedUrl,bool lightweight){
   var asset=Selected;if(asset==null||asset.source.kind!="web")return;
   await Run((lightweight?"Preparando vista ligera de ":"Descargando ")+asset.name+"…",token=>{
    Asset source=asset;
    string directory=ProjectStorage.WebDirectory(projectFile);MediaRepresentation representation=null;
    if(updatedUrl!=null){
     Asset inspected;
     if(asset.source.resolver_type=="image-http")inspected=WebMedia.DownloadImage(updatedUrl,updatedUrl,directory,token);
     else{
      if(WebMedia.Platform(updatedUrl)==null)throw new Exception("El enlace no pertenece a una plataforma web compatible.");
      inspected=WebMedia.IsAudioOnly(asset)?WebMedia.InspectAudio(updatedUrl,token):WebMedia.InspectVideo(updatedUrl,token);
     }
     if(!Compatible(asset,inspected))throw new Exception("El nuevo enlace no coincide con la duración o las pistas del medio original.");
     source=Store.Json().Deserialize<Asset>(Store.Json().Serialize(Store.Fields(asset)));
     source.source.uri=inspected.source.uri;source.source.page_url=inspected.source.page_url;source.source.resolver_type=inspected.source.resolver_type;
     if(asset.source.resolver_type=="image-http")representation=MapRepresentation(asset,inspected,inspected.representations.First().locator.path);
    }
    if(representation==null){
     if(source.source.resolver_type=="image-http"){
      var image=WebMedia.DownloadImage(source.source.uri,source.source.page_url,directory,token);
      representation=MapRepresentation(asset,image,image.representations.First().locator.path);
     }else if(WebMedia.IsAudioOnly(source))representation=WebMedia.ResolveAudio(source,directory,token,null);
     else representation=WebMedia.ResolveVideo(source,directory,token,null,lightweight);
    }
    token.ThrowIfCancellationRequested();
    if(updatedUrl!=null){asset.source.uri=source.source.uri;asset.source.page_url=source.source.page_url;asset.source.resolver_type=source.source.resolver_type;}
    if(WebMedia.DurationProvisional(asset)&&representation.locator!=null)WebMedia.ReconcileDuration(project,asset.id,WebMedia.MeasureDuration(representation.locator.path,token));
    asset.representations.RemoveAll(r=>r.role==representation.role);asset.representations.Add(representation);Store.NormalizeAsset(asset);
    return true;
   });
  }
  async Task ChangeSelectedUrl(){
   var asset=Selected;if(asset==null||asset.source.kind!="web")return;
   using(var dialog=new Form{Text="Actualizar enlace · "+asset.name,Size=new Size(560,165),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,BackColor=BackColor,ForeColor=ForeColor}){
    var input=new TextBox{Text=asset.source.page_url??asset.source.uri,Left=14,Top=18,Width=515};
    var ok=new Button{Text="Comprobar y descargar",Left=292,Top=58,Width=148,DialogResult=DialogResult.OK};
    var no=new Button{Text="Cancelar",Left=448,Top=58,Width=82,DialogResult=DialogResult.Cancel};
    dialog.Controls.AddRange(new Control[]{input,ok,no});dialog.AcceptButton=ok;dialog.CancelButton=no;
    if(dialog.ShowDialog(this)!=DialogResult.OK)return;
    Uri uri;if(!Uri.TryCreate(input.Text.Trim(),UriKind.Absolute,out uri)||(uri.Scheme!="https"&&uri.Scheme!="http")){MessageBox.Show(this,"Escribe un enlace HTTP o HTTPS válido.");return;}
    await DownloadSelected(input.Text.Trim(),true);
   }
  }
 }
}
