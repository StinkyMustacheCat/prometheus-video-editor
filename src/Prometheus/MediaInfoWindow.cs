using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public sealed class MediaInfoWindow:Form {
  readonly FlowLayoutPanel content=new FlowLayoutPanel();
  readonly Color surface=Color.FromArgb(36,38,42),ink=Color.FromArgb(223,225,228);
  public string AssetId {get;private set;}
  public Action<string> OpenFolderRequested,OpenPageRequested;
  public MediaInfoWindow(){
   Text="Información del medio";Width=470;Height=540;MinimumSize=new Size(370,330);
   StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.SizableToolWindow;
   BackColor=surface;ForeColor=ink;KeyPreview=true;
   content.Dock=DockStyle.Fill;content.FlowDirection=FlowDirection.TopDown;content.WrapContents=false;
   content.AutoScroll=true;content.Padding=new Padding(14);content.Resize+=(s,e)=>ResizeRows();
   Controls.Add(content);
   KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape){Close();e.Handled=true;}};
  }
  void ResizeRows(){foreach(Control c in content.Controls)if(c is Label)c.Width=Math.Max(290,content.ClientSize.Width-42);}
  void Line(string label,string value){
   content.Controls.Add(new Label{Text=label,Font=new Font(Font,FontStyle.Bold),ForeColor=ink,AutoSize=true,Margin=new Padding(0,8,0,2)});
   content.Controls.Add(new Label{Text=String.IsNullOrWhiteSpace(value)?"—":value,ForeColor=ink,
    AutoSize=true,MaximumSize=new Size(Math.Max(290,content.ClientSize.Width-42),0),Margin=new Padding(0,0,0,2)});
  }
  void Button(string title,Action action){
   var button=new Button{Text=title,AutoSize=true,Height=29,FlatStyle=FlatStyle.Flat,
    BackColor=Color.FromArgb(49,51,56),ForeColor=ink,Margin=new Padding(0,5,0,0)};
   button.FlatAppearance.BorderColor=Color.FromArgb(66,68,74);
   button.Click+=(s,e)=>action();content.Controls.Add(button);
  }
  static string Duration(Rational value){return value==null?"Imagen fija":Timeline.TimeLabel(value.Seconds);}
  public void ShowMedia(Project project,Asset asset,Clip clip){
   foreach(Control c in content.Controls.Cast<Control>().ToArray())c.Dispose();content.Controls.Clear();
   if(asset==null)return;
   AssetId=asset.id;
   Store.NormalizeAsset(asset);
   Text="Información · "+asset.name;
   Line("Medio",asset.name);
   if(clip!=null){
    var track=project.Active.tracks.FirstOrDefault(t=>t.id==clip.track_id);
    Line("Uso en la secuencia",(track==null?"Pista desconocida":track.name)+" · "+Timeline.TimeLabel(clip.timeline_start.Seconds)+" · "+Duration(clip.duration));
    Line("Punto de entrada",Duration(clip.source_in));
   }
   Line("Tipo de origen",asset.source==null?"Desconocido":asset.source.kind);
   string origin=asset.source==null?null:asset.source.uri;
   if(!String.IsNullOrWhiteSpace(origin))Line("Origen",origin);
   if(asset.source!=null&&!String.IsNullOrWhiteSpace(asset.source.page_url))Line("Página original",asset.source.page_url);
   if(asset.source!=null&&!String.IsNullOrWhiteSpace(asset.source.imported_at))Line("Importado",asset.source.imported_at);
   if(!String.IsNullOrWhiteSpace(asset.container_format))Line("Contenedor",asset.container_format);
   foreach(var stream in asset.streams){
    string detail=stream.kind+" · "+stream.codec+" · "+Duration(stream.duration);
    if(stream.width.HasValue&&stream.height.HasValue)detail+=" · "+stream.width+" × "+stream.height;
    if(stream.nominal_fps!=null)detail+=" · "+Tools.N(stream.nominal_fps.Seconds)+" FPS";
    if(stream.sample_rate.HasValue)detail+=" · "+stream.sample_rate+" Hz";
    Line("Pista interna "+stream.ordinal,detail);
   }
   foreach(var representation in asset.representations){
    string path=representation.locator==null?null:representation.locator.path;
    Line("Archivo "+representation.role,(String.IsNullOrWhiteSpace(path)?"Sin ruta":path)+(File.Exists(path)?" · disponible":" · no disponible"));
   }
   string local=asset.representations.Select(r=>r.locator==null?null:r.locator.path).FirstOrDefault(p=>!String.IsNullOrWhiteSpace(p)&&File.Exists(p));
   if(local==null&&asset.source!=null&&asset.source.kind=="local")local=asset.source.uri;
   if(!String.IsNullOrWhiteSpace(local)){
    string path=local;Button("Copiar ruta",()=>Clipboard.SetText(path));
    if(File.Exists(path))Button("Abrir carpeta",()=>{if(OpenFolderRequested!=null)OpenFolderRequested(asset.id);});
   }
   string page=asset.source==null?null:asset.source.page_url;
   if(String.IsNullOrWhiteSpace(page)&&asset.source!=null&&asset.source.kind=="web")page=asset.source.uri;
   if(!String.IsNullOrWhiteSpace(page)){
    string url=page;Button("Copiar URL",()=>Clipboard.SetText(url));
    Button("Abrir página original",()=>{if(OpenPageRequested!=null)OpenPageRequested(asset.id);});
   }
   Button("Cerrar",Close);ResizeRows();
  }
 }
}
