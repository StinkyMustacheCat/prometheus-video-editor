using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public sealed class InspectorPanelView:FlowLayoutPanel {
  readonly Color surface=Color.FromArgb(36,38,42),background=Color.FromArgb(27,28,31),text=Color.FromArgb(223,225,228);
  public Action<string> RelinkRequested;
  public Action<string> GenerateProxyRequested;
  public Action<string,bool> ProxyPreferenceChanged;
  public Action<string,bool> ClipEnabledChanged;
  public Action<string,double> VolumeChanged;
  public Action<string> PrepareWebPreviewRequested,OpenWebSourceRequested,CopyWebSourceRequested;
  public Action<string,string> AssetNameChanged;
  public Action<string,string,string,string,double> GeneratedChanged;
  public Action<string,string,double,double,double> ClipFramingChanged;
  public Action<string> AddLutRequested;
  public Action<string> RelinkLutRequested;
  public Action<string,string,bool> EffectEnabledChanged;
  public Action<string,string> RemoveEffectRequested;
  public InspectorPanelView(){
   Dock=DockStyle.Fill;FlowDirection=FlowDirection.TopDown;WrapContents=false;AutoScroll=true;
   BackColor=surface;Padding=new Padding(12);
  }
  void Info(string value){
   Controls.Add(new Label{Text=value,ForeColor=text,Width=225,AutoSize=false,
    Height=Math.Max(34,Math.Min(100,(value.Length/28+1)*18))});
  }
  Button Button(string label,Action action){
   var button=new Button{Text=label,AutoSize=true,Height=30,FlatStyle=FlatStyle.Flat,
    BackColor=Color.FromArgb(49,51,56),ForeColor=text,Margin=new Padding(3,0,3,0)};
   button.FlatAppearance.BorderColor=Color.FromArgb(66,68,74);
   button.Click+=(s,e)=>{if(action!=null)action();};return button;
  }
  void ClearContents(){foreach(Control control in Controls.Cast<Control>().ToArray()){var picture=control as PictureBox;if(picture!=null&&picture.Image!=null)picture.Image.Dispose();control.Dispose();}Controls.Clear();}
  TextBox Field(string value,bool multiline=false){var field=new TextBox{Text=value??"",Width=225,Height=multiline?64:25,Multiline=multiline,ScrollBars=multiline?ScrollBars.Vertical:ScrollBars.None,BackColor=background,ForeColor=text};Controls.Add(field);return field;}
  void AddNameEditor(Asset asset){Info("Nombre del medio");var name=Field(asset.name);name.MaxLength=120;Controls.Add(Button("Guardar nombre",()=>{if(AssetNameChanged!=null)AssetNameChanged(asset.id,name.Text);}));}
  void AddGeneratedEditor(Asset asset,Clip clip){
   string kind=GeneratedMedia.Kind(asset);if(kind!="solid"&&kind!="placeholder")return;
   Info(kind=="solid"?"Editar color sólido":"Editar espacio reservado");
   Info("Nombre");var name=Field(asset.name);name.MaxLength=120;
   TextBox detail=null;Color chosen=Color.FromArgb(64,70,79);ColorPickerWidget color=null;
   if(kind=="placeholder"){
    Info("Nota");detail=Field(GeneratedMedia.Note(asset),true);detail.MaxLength=500;
   }else{
    chosen=ColorTranslator.FromHtml("#"+GeneratedMedia.ColorHex(asset));
    color=new ColorPickerWidget("Color sólido",chosen){Width=225};color.ColorChanged=value=>chosen=value;Controls.Add(color);
   }
   Info(clip==null?"Duración inicial (HH:MM:SS:FF)":"Duración del clip (HH:MM:SS:FF)");
   var duration=Field(Timeline.TimeLabel(clip==null?asset.recommended_duration.Seconds:clip.duration.Seconds));duration.MaxLength=11;
   Controls.Add(Button("Guardar cambios",()=>{
    double seconds;if(!MonitorPanelView.TryParseTimecode(duration.Text,out seconds)||seconds<=0){MessageBox.Show(FindForm(),"Escribe una duración válida en HH:MM:SS:FF.","Propiedades",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
    string value=kind=="solid"?chosen.R.ToString("X2")+chosen.G.ToString("X2")+chosen.B.ToString("X2"):detail.Text;
    if(GeneratedChanged!=null)GeneratedChanged(asset.id,clip==null?null:clip.id,name.Text,value,seconds);
   }));
  }
  void AddFramingEditor(Clip clip){
   var frame=ClipFraming.Read(clip);
   Info("Encuadre en el lienzo");
   var mode=new ComboBox{Width=225,DropDownStyle=ComboBoxStyle.DropDownList,BackColor=background,ForeColor=text};mode.Items.AddRange(new object[]{"Encajar · imagen completa","Rellenar · recortar bordes"});mode.SelectedIndex=frame.mode=="fill"?1:0;Controls.Add(mode);
   Info("Escala (%)");var scale=new NumericUpDown{Minimum=25,Maximum=400,DecimalPlaces=1,Increment=5,Value=(decimal)(frame.scale*100),Width=120,BackColor=background,ForeColor=text};Controls.Add(scale);
   Info("Posición horizontal (%)");var x=new NumericUpDown{Minimum=-100,Maximum=100,DecimalPlaces=1,Value=(decimal)(frame.x*100),Width=120,BackColor=background,ForeColor=text};Controls.Add(x);
   Info("Posición vertical (%)");var y=new NumericUpDown{Minimum=-100,Maximum=100,DecimalPlaces=1,Value=(decimal)(frame.y*100),Width=120,BackColor=background,ForeColor=text};Controls.Add(y);
   Controls.Add(Button("Aplicar encuadre",()=>{if(ClipFramingChanged!=null)ClipFramingChanged(clip.id,mode.SelectedIndex==1?"fill":"fit",(double)scale.Value/100,(double)x.Value/100,(double)y.Value/100);}));
  }
  public void ShowAsset(Asset asset){
   ClearContents();
   if(asset==null){ShowEmpty();return;}
   if(GeneratedMedia.Kind(asset)=="solid"||GeneratedMedia.IsPlaceholder(asset)){AddGeneratedEditor(asset,null);return;}
   Store.NormalizeAsset(asset);Info("Ajustes del medio · "+asset.name);
   AddNameEditor(asset);
   if(asset.source.kind=="local")Controls.Add(Button("Reconectar archivo…",()=>{if(RelinkRequested!=null)RelinkRequested(asset.id);}));
   if(asset.source.kind=="web"){
    if(asset.source.resolver_type=="youtube/yt-dlp"||asset.source.resolver_type!=null&&asset.source.resolver_type.StartsWith("yt-dlp/",StringComparison.Ordinal)){
     if(Media.SelectRepresentation(asset,true)==null)Controls.Add(Button("Preparar vista ligera",()=>{if(PrepareWebPreviewRequested!=null)PrepareWebPreviewRequested(asset.id);}));
    }
   }
   AddProxyControls(asset);
  }
  public void ShowGeneratedClip(Asset asset,Clip clip){
   ClearContents();
   if(asset==null||clip==null){ShowEmpty();return;}
   AddGeneratedEditor(asset,clip);
  }
  void AddProxyControls(Asset asset){
   if(asset!=null&&asset.source.kind=="local"&&asset.streams.Exists(x=>x.kind=="video")){
    bool hasProxy=asset.representations.Exists(x=>x.role=="proxy"&&x.locator!=null&&System.IO.File.Exists(x.locator.path));
    Controls.Add(Button(hasProxy?"Regenerar proxy…":"Generar proxy…",()=>{if(GenerateProxyRequested!=null)GenerateProxyRequested(asset.id);}));
    if(hasProxy){var choice=new CheckBox{Text="Usar proxy en vista previa",Checked=Media.PrefersProxy(asset),Width=225,ForeColor=text};choice.CheckedChanged+=(s,e)=>{if(ProxyPreferenceChanged!=null)ProxyPreferenceChanged(asset.id,choice.Checked);};Controls.Add(choice);}
   }
  }
  public void ShowClip(Project project,Clip clip){
   ClearContents();
   if(clip==null){ShowEmpty();return;}
   Info("Ajustes del clip · "+clip.name);
   var track=project.Active.tracks.Find(x=>x.id==clip.track_id);
   var mute=new CheckBox{Text=track.kind=="audio"?"Silenciar clip":"Ocultar clip",Checked=!clip.enabled,Width=225,ForeColor=text};
   mute.CheckedChanged+=(s,e)=>{if(ClipEnabledChanged!=null)ClipEnabledChanged(clip.id,!mute.Checked);};
   Controls.Add(mute);
   if(track.kind=="audio"){
    Info("Volumen del clip (dB)");
    var volume=new NumericUpDown{Minimum=-60,Maximum=12,DecimalPlaces=1,Value=(decimal)clip.gain_db,
     Width=150,BackColor=background,ForeColor=text};
    Controls.Add(volume);
    Controls.Add(Button("Aplicar volumen",()=>{if(VolumeChanged!=null)VolumeChanged(clip.id,(double)volume.Value);}));
   }
   else{
    AddFramingEditor(clip);
    Info("Efectos del clip");
    foreach(var effect in clip.effects){
     string effectId=effect.id;
     if(effect.effect_id==EffectContracts.Lut3DId){
      string resourceId=effect.parameters.ContainsKey("lut")?effect.parameters["lut"].resource_id:null;
      var resource=project.effect_resources.Find(r=>r.id==resourceId);
      string label="LUT · "+(resource==null?"archivo desconocido":Path.GetFileName(resource.uri));
      var enabled=new CheckBox{Text=label,Checked=effect.enabled,Width=225,ForeColor=text,AutoEllipsis=true};
      enabled.CheckedChanged+=(s,e)=>{if(EffectEnabledChanged!=null)EffectEnabledChanged(clip.id,effectId,enabled.Checked);};
      Controls.Add(enabled);
      if(resource==null||!File.Exists(resource.uri)){
       Info("Archivo LUT no disponible");
       if(resource!=null)Controls.Add(Button("Localizar LUT…",()=>{if(RelinkLutRequested!=null)RelinkLutRequested(resource.id);}));
      }
     }else Info("Efecto no disponible · "+effect.effect_id);
     Controls.Add(Button("Quitar efecto",()=>{if(RemoveEffectRequested!=null)RemoveEffectRequested(clip.id,effectId);}));
    }
    Controls.Add(Button(clip.effects.Any(e=>e.effect_id==EffectContracts.Lut3DId)?"Cambiar LUT (.cube)…":"Añadir LUT (.cube)…",()=>{if(AddLutRequested!=null)AddLutRequested(clip.id);}));
   }
   var asset=project.assets.Find(x=>x.id==clip.asset_id);
   AddGeneratedEditor(asset,clip);
   AddProxyControls(asset);
  }
  void ShowEmpty(){
   Info("Selecciona un clip o un medio para editarlo.");
   Info("Módulos activos\nProyecto · Medios · Montaje · Render");
   Info("Alt+rueda: zoom bajo el puntero\nRueda: desplazar tiempo\nMayús+rueda: desplazar pistas");
  }
 }
}
