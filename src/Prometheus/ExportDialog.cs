using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Prometheus {
 public sealed class ExportDialog:Form {
  readonly Project project;readonly ToolTip hints=new ToolTip();
  readonly Color ink=Color.FromArgb(225,227,230),surface=Color.FromArgb(36,38,42),field=Color.FromArgb(48,50,55);
  readonly ComboBox format=new ComboBox(),rangeChoice=new ComboBox(),resolution=new ComboBox(),fps=new ComboBox(),quality=new ComboBox(),rateMode=new ComboBox(),preset=new ComboBox(),audioRate=new ComboBox(),acceleration=new ComboBox();
  readonly TextBox destination=new TextBox(),fileName=new TextBox();
  readonly CheckBox usePreview=new CheckBox(),selectiveWeb=new CheckBox(),openFolderAfter=new CheckBox(),playAfter=new CheckBox();
  readonly NumericUpDown crf=new NumericUpDown(),videoRate=new NumericUpDown(),customWidth=new NumericUpDown(),customHeight=new NumericUpDown();
  readonly Label summary=new Label(),rangeHint=new Label(),warning=new Label(),outputSummary=new Label(),readiness=new Label(),extensionLabel=new Label();
  ExportPreflightReport readinessReport;
  string readinessFailure;
  int readinessRevision;
  readonly Panel advanced=new Panel();
  readonly Button expand=new Button(),start=new Button();
  bool expanded,syncingQuality;
  public ExportOptions Options{get;private set;}
  public string OutputPath{get;private set;}
  public bool OpenFolderAfter{get{return openFolderAfter.Checked;}}
  public bool PlayAfter{get{return playAfter.Checked;}}
  static Label Caption(string value,int x,int y,int width){return new Label{Text=value,Left=x,Top=y,Width=width,Height=22};}
  ComboBox Choice(ComboBox control,int x,int y,int width,string[] values,int selected){control.SetBounds(x,y,width,27);control.DropDownStyle=ComboBoxStyle.DropDownList;control.BackColor=field;control.ForeColor=ink;control.Items.AddRange(values);control.SelectedIndex=selected;return control;}
  NumericUpDown Number(NumericUpDown control,int x,int y,int width,int min,int max,int value){control.SetBounds(x,y,width,26);control.Minimum=min;control.Maximum=max;control.Value=value;control.BackColor=field;control.ForeColor=ink;return control;}
  public ExportDialog(Project snapshot){
   project=snapshot;Text="Exportar montaje";ClientSize=new Size(640,570);FormBorderStyle=FormBorderStyle.FixedDialog;StartPosition=FormStartPosition.CenterParent;MaximizeBox=false;MinimizeBox=false;BackColor=surface;ForeColor=ink;Font=new Font("Segoe UI",9);
   var marks=SequenceExportRange.Read(snapshot.Active);
   var title=new Label{Text="Exportar",Left=18,Top=15,Width=600,Height=32,Font=new Font("Segoe UI",17,FontStyle.Bold)};Controls.Add(title);
   summary.SetBounds(19,50,600,31);summary.Text=snapshot.Active.name+" · "+Timeline.TimeLabel(snapshot.Active.Length)+" · lienzo "+snapshot.Active.width+" × "+snapshot.Active.height+" · 60 FPS";summary.ForeColor=Color.FromArgb(169,174,181);Controls.Add(summary);
   Controls.Add(Caption("Formato",18,93,260));Choice(format,18,118,288,new[]{"Vídeo MP4 · H.264 + AAC","Audio WAV · PCM 16 bits"},0);format.SelectedIndexChanged+=(s,e)=>UpdateFormat();Controls.Add(format);
   Controls.Add(Caption("Rango",332,93,280));Choice(rangeChoice,332,118,289,new[]{"Secuencia completa","Entre entrada y salida"},marks.Complete?1:0);rangeChoice.Enabled=marks.Complete;rangeChoice.SelectedIndexChanged+=(s,e)=>UpdateOutputSummary();Controls.Add(rangeChoice);
   rangeHint.SetBounds(332,148,289,23);rangeHint.ForeColor=Color.FromArgb(169,174,181);rangeHint.Text=marks.Complete?Timeline.TimeLabel(marks.InFrame.Value/60.0)+" → "+Timeline.TimeLabel(marks.OutFrame.Value/60.0):"";rangeHint.Visible=false;Controls.Add(rangeHint);
   if(marks.Complete)hints.SetToolTip(rangeChoice,"I/O: "+rangeHint.Text);
   Controls.Add(Caption("Carpeta de salida",18,157,260));destination.SetBounds(18,182,239,26);destination.BackColor=field;destination.ForeColor=ink;destination.Text=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);Controls.Add(destination);
   var browse=Button("…",()=>Browse());browse.SetBounds(265,181,42,28);Controls.Add(browse);
   Controls.Add(Caption("Nombre del archivo",332,157,280));fileName.SetBounds(332,182,233,26);fileName.BackColor=field;fileName.ForeColor=ink;fileName.Text="Montaje";Controls.Add(fileName);extensionLabel.SetBounds(570,183,51,24);extensionLabel.Text=".mp4";extensionLabel.TextAlign=ContentAlignment.MiddleCenter;extensionLabel.BackColor=field;extensionLabel.ForeColor=ink;Controls.Add(extensionLabel);
   Controls.Add(Caption("Resolución",18,228,275));Choice(resolution,18,253,288,new[]{"Igual al lienzo","HD · lado corto 720","Full HD · lado corto 1080","4K · lado corto 2160","Personalizada…"},0);resolution.SelectedIndexChanged+=(s,e)=>{if(resolution.SelectedIndex==4)SetAdvanced(true);UpdateOutputSummary();};Controls.Add(resolution);
   Controls.Add(Caption("Fotogramas por segundo",332,228,285));Choice(fps,332,253,289,new[]{"60 FPS · como el montaje","30 FPS","25 FPS","24 FPS"},0);fps.SelectedIndexChanged+=(s,e)=>UpdateOutputSummary();Controls.Add(fps);
   Controls.Add(Caption("Calidad de vídeo",18,299,290));Choice(quality,18,324,288,new[]{"Rápida","Equilibrada","Alta","Personalizada (avanzado)"},1);quality.SelectedIndexChanged+=(s,e)=>{if(quality.SelectedIndex==3)SetAdvanced(true);else SyncQuality();UpdateOutputSummary();};Controls.Add(quality);
   bool hasGpu=Tools.CanUseNvenc();
   Controls.Add(Caption("Aceleración / Codificador",332,299,285));
   Choice(acceleration,332,324,289,hasGpu?new[]{"GPU · NVIDIA NVENC (Acelerado)","CPU · Software libx264 (Compatible)"}:new[]{"CPU · Software libx264 (Compatible)"},0);
   acceleration.SelectedIndexChanged+=(s,e)=>UpdateOutputSummary();
   Controls.Add(acceleration);
   usePreview.Text="Exportar con vistas ligeras o proxys (menor calidad)";usePreview.SetBounds(18,370,595,28);usePreview.ForeColor=ink;usePreview.CheckedChanged+=(s,e)=>{warning.Visible=usePreview.Checked;if(usePreview.Checked)selectiveWeb.Checked=false;selectiveWeb.Enabled=!usePreview.Checked&&format.SelectedIndex==0;UpdateOutputSummary();};Controls.Add(usePreview);
   warning.Text="Útil para revisión rápida. La exportación final usa medios completos.";warning.ForeColor=Color.FromArgb(255,190,122);warning.SetBounds(37,397,570,22);warning.Visible=false;Controls.Add(warning);
   outputSummary.SetBounds(18,422,600,22);outputSummary.ForeColor=Color.FromArgb(172,209,180);Controls.Add(outputSummary);
   readiness.SetBounds(18,449,490,24);readiness.ForeColor=Color.FromArgb(172,209,180);Controls.Add(readiness);
   var details=Button("Ver detalles",ShowReadinessDetails);details.SetBounds(511,446,110,28);Controls.Add(details);
   expand=Button("▸ Avanzado",()=>SetAdvanced(!expanded));expand.SetBounds(18,480,132,29);Controls.Add(expand);
   var history=Button("Historial…",ShowHistory);history.SetBounds(157,480,105,29);Controls.Add(history);
   var preferences=ExportHistory0814.Preferences();openFolderAfter.Text="Abrir carpeta al terminar";openFolderAfter.SetBounds(275,479,177,29);openFolderAfter.Checked=preferences.open_folder;Controls.Add(openFolderAfter);
   playAfter.Text="Reproducir al terminar";playAfter.SetBounds(455,479,168,29);playAfter.Checked=preferences.play_result;Controls.Add(playAfter);
   advanced.SetBounds(18,521,604,214);advanced.BackColor=Color.FromArgb(30,32,36);advanced.Visible=false;Controls.Add(advanced);
   AdvancedContents();
   start=Button("Iniciar exportación",Accept);start.SetBounds(451,531,170,32);start.BackColor=Color.FromArgb(255,156,44);start.ForeColor=Color.FromArgb(25,27,30);Controls.Add(start);
   var cancel=Button("Cancelar",()=>{DialogResult=DialogResult.Cancel;Close();});cancel.SetBounds(346,531,96,32);Controls.Add(cancel);CancelButton=cancel;
   Shown+=(s,e)=>ScheduleReadiness();
   UpdateOutputSummary();
  }
  Button Button(string value,Action click){var result=new Button{Text=value,FlatStyle=FlatStyle.Flat,BackColor=field,ForeColor=ink};result.FlatAppearance.BorderColor=Color.FromArgb(74,76,82);result.Click+=(s,e)=>click();return result;}
  void AdvancedContents(){
   advanced.Controls.Add(Caption("Control de vídeo",11,8,160));Choice(rateMode,11,32,190,new[]{"Calidad constante (CRF)","Bitrate objetivo (VBR)"},0);advanced.Controls.Add(rateMode);
   advanced.Controls.Add(Caption("CRF",217,8,80));Number(crf,217,32,72,15,32,20);advanced.Controls.Add(crf);
   advanced.Controls.Add(Caption("Mbps",304,8,80));Number(videoRate,304,32,82,1,100,8);videoRate.DecimalPlaces=1;videoRate.Increment=0.5M;advanced.Controls.Add(videoRate);
   rateMode.SelectedIndexChanged+=(s,e)=>{crf.Enabled=rateMode.SelectedIndex==0;videoRate.Enabled=rateMode.SelectedIndex==1;if(!syncingQuality)quality.SelectedIndex=3;};videoRate.Enabled=false;
   advanced.Controls.Add(Caption("Codificación",400,8,160));Choice(preset,400,32,192,new[]{"Ultrarrápida","Muy rápida","Rápida","Equilibrada","Lenta"},1);advanced.Controls.Add(preset);
   advanced.Controls.Add(Caption("Ancho personalizado",11,78,190));Number(customWidth,11,101,140,320,4096,project.Active.width);advanced.Controls.Add(customWidth);
   advanced.Controls.Add(Caption("Alto personalizado",167,78,190));Number(customHeight,167,101,140,320,4096,project.Active.height);advanced.Controls.Add(customHeight);
   advanced.Controls.Add(Caption("Audio AAC",323,78,130));Choice(audioRate,323,101,130,new[]{"128 kbps","192 kbps","256 kbps","320 kbps"},1);advanced.Controls.Add(audioRate);
   selectiveWeb.Text="Descargar solo tramos web usados (si falla, obtener medio completo)";selectiveWeb.SetBounds(11,144,580,28);selectiveWeb.ForeColor=ink;advanced.Controls.Add(selectiveWeb);
   var note=Caption("Las dimensiones personalizadas deben conservar la relación del lienzo.",11,181,560);note.ForeColor=Color.FromArgb(162,166,172);advanced.Controls.Add(note);
   Action custom=()=>{if(!syncingQuality&&quality.SelectedIndex!=3)quality.SelectedIndex=3;};crf.ValueChanged+=(s,e)=>custom();preset.SelectedIndexChanged+=(s,e)=>custom();
   customWidth.ValueChanged+=(s,e)=>UpdateOutputSummary();customHeight.ValueChanged+=(s,e)=>UpdateOutputSummary();
  }
  void SetAdvanced(bool value){expanded=value;advanced.Visible=value;expand.Text=value?"▾ Avanzado":"▸ Avanzado";ClientSize=new Size(640,value?810:570);start.Top=value?766:531;var cancel=CancelButton as Control;if(cancel!=null)cancel.Top=start.Top;}
  void SyncQuality(){syncingQuality=true;try{if(quality.SelectedIndex==0){crf.Value=25;preset.SelectedIndex=1;}else if(quality.SelectedIndex==1){crf.Value=20;preset.SelectedIndex=1;}else if(quality.SelectedIndex==2){crf.Value=18;preset.SelectedIndex=3;}if(quality.SelectedIndex<3)rateMode.SelectedIndex=0;}finally{syncingQuality=false;}}
  void UpdateFormat(){bool video=format.SelectedIndex==0;resolution.Enabled=fps.Enabled=quality.Enabled=acceleration.Enabled=usePreview.Enabled=video;advanced.Enabled=video;selectiveWeb.Enabled=video&&!usePreview.Checked;if(!video){usePreview.Checked=false;selectiveWeb.Checked=false;}fileName.Text=BaseName(fileName.Text);extensionLabel.Text=video?".mp4":".wav";UpdateOutputSummary();}
  static string BaseName(string value){string name=(value??"").Trim();if(name.EndsWith(".mp4",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".wav",StringComparison.OrdinalIgnoreCase))name=name.Substring(0,name.LastIndexOf('.'));return name.Trim();}
  int SelectedFps(){return fps.SelectedIndex==1?30:fps.SelectedIndex==2?25:fps.SelectedIndex==3?24:60;}
  int[] SelectedSize(){if(resolution.SelectedIndex==0)return new[]{project.Active.width,project.Active.height};if(resolution.SelectedIndex==4)return new[]{(int)customWidth.Value,(int)customHeight.Value};return ExportOptions.AtShortSide(project.Active,resolution.SelectedIndex==1?720:resolution.SelectedIndex==2?1080:2160);}
  void UpdateOutputSummary(){try{string range=rangeChoice.SelectedIndex==1?" · intervalo I/O":"";if(format.SelectedIndex==1)outputSummary.Text="Salida: WAV · audio estéreo PCM 16 bits / 48 kHz"+range;else{var size=SelectedSize();bool isGpu=Tools.CanUseNvenc()&&acceleration.SelectedIndex==0;string accelLabel=isGpu?" · GPU (NVENC)":" · CPU (x264)";outputSummary.Text="Salida: "+size[0]+" × "+size[1]+" · "+SelectedFps()+" FPS"+accelLabel+(usePreview.Checked?" · vista ligera":" · medios completos")+range;}ScheduleReadiness();}catch(Exception ex){outputSummary.Text=ex.Message;}}
  void ScheduleReadiness(){
   if(!IsHandleCreated||format.SelectedIndex<0||resolution.SelectedIndex<0)return;
   int revision=Interlocked.Increment(ref readinessRevision);bool video=format.SelectedIndex==0,preview=video&&usePreview.Checked,selective=video&&selectiveWeb.Checked;var size=video?SelectedSize():new[]{project.Active.width,project.Active.height};
   var marks=SequenceExportRange.Read(project.Active);bool window=rangeChoice.SelectedIndex==1&&marks.Complete;long startFrame=window?marks.InFrame.Value:0,endFrame=window?marks.OutFrame.Value:0;
   readiness.Text="Comprobando medios…";readiness.ForeColor=Color.FromArgb(169,174,181);readinessReport=null;readinessFailure=null;
   Task.Factory.StartNew(()=>{
    ExportPreflightReport report=null;string failure=null;
    try{var target=window?SequenceExportRange.Window(project,startFrame,endFrame):project;report=ExportPreflight.Inspect(target,video,0,size[0],size[1],preview,selective);}catch(Exception ex){failure=ex.Message;}
    if(IsDisposed||!IsHandleCreated)return;
    try{BeginInvoke((Action)(()=>{if(IsDisposed||revision!=readinessRevision)return;readinessReport=report;readinessFailure=failure;
     if(failure!=null){readiness.Text="No se pudo comprobar: "+failure;readiness.ForeColor=Color.FromArgb(255,170,157);start.Text="Revisar problemas";return;}
     int automatic=report.checks.Count(c=>c.code=="web-pending"||c.code=="web-range-pending"||c.code=="mgt-render-pending");
     if(report.Errors>0){readiness.Text=report.Errors+" problema(s) que requieren acción antes de exportar";readiness.ForeColor=Color.FromArgb(255,170,157);start.Text="Revisar problemas";}
     else if(report.Warnings>0){readiness.Text=report.Warnings+" aviso(s)"+(automatic>0?" · "+automatic+" medio(s) se prepararán al exportar":" · revisa los detalles");readiness.ForeColor=Color.FromArgb(255,199,126);start.Text="Iniciar exportación";}
     else{readiness.Text="Preparación: todo listo";readiness.ForeColor=Color.FromArgb(172,209,180);start.Text="Iniciar exportación";}
    }));}catch(InvalidOperationException){}
   });
  }
  void ShowReadinessDetails(){var report=readinessReport;if(report==null){MessageBox.Show(this,"La comprobación de medios aún está en curso.","Preparación",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
   using(var dialog=new Form{Text="Detalles de la exportación",ClientSize=new Size(650,340),MinimumSize=new Size(520,260),StartPosition=FormStartPosition.CenterParent,BackColor=surface,ForeColor=ink}){
    var list=new ListView{Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,HeaderStyle=ColumnHeaderStyle.None,BackColor=field,ForeColor=ink};list.Columns.Add("Estado",100);list.Columns.Add("Detalle",530);
    foreach(var check in report.checks){var item=new ListViewItem(check.severity=="error"?"Problema":check.severity=="warning"?"Aviso":"Correcto");item.SubItems.Add(check.message);item.ForeColor=check.severity=="error"?Color.FromArgb(255,170,157):check.severity=="warning"?Color.FromArgb(255,199,126):Color.FromArgb(172,209,180);list.Items.Add(item);}
    dialog.Controls.Add(list);dialog.ShowDialog(this);
   }
  }
  void Browse(){using(var picker=new FolderBrowserDialog{Description="Elegir carpeta de exportación",SelectedPath=Directory.Exists(destination.Text)?destination.Text:Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)})if(picker.ShowDialog(this)==DialogResult.OK)destination.Text=picker.SelectedPath;}
  void ShowHistory(){var records=ExportHistory0814.For(project.id);using(var dialog=new Form{Text="Exportaciones de este proyecto",ClientSize=new Size(730,355),MinimumSize=new Size(560,300),StartPosition=FormStartPosition.CenterParent,BackColor=surface,ForeColor=ink}){
   var list=new ListView{Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,BackColor=field,ForeColor=ink};list.Columns.Add("Fecha",135);list.Columns.Add("Archivo",245);list.Columns.Add("Ubicación",310);
   foreach(var record in records){DateTime date;string when=DateTime.TryParse(record.exported_utc,out date)?date.ToLocalTime().ToString("yyyy-MM-dd HH:mm"):"";var item=new ListViewItem(when);item.SubItems.Add(Path.GetFileName(record.path));item.SubItems.Add(File.Exists(record.path)?Path.GetDirectoryName(record.path):"No disponible · "+Path.GetDirectoryName(record.path));item.Tag=record;list.Items.Add(item);}
   var actions=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=48,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(8)};
   var use=Button("Usar ubicación",()=>{if(list.SelectedItems.Count==0)return;var record=(ExportRecord0814)list.SelectedItems[0].Tag;string folder=Path.GetDirectoryName(record.path);if(!Directory.Exists(folder)){MessageBox.Show(dialog,"La carpeta ya no existe en este equipo.");return;}destination.Text=folder;fileName.Text=BaseName(Path.GetFileName(record.path));dialog.Close();});use.Width=115;
   var show=Button("Ver en carpeta",()=>{if(list.SelectedItems.Count==0)return;var record=(ExportRecord0814)list.SelectedItems[0].Tag;if(File.Exists(record.path))Process.Start("explorer.exe","/select,\""+record.path+"\"");});show.Width=112;
   var open=Button("Ver resultado",()=>{if(list.SelectedItems.Count==0)return;var record=(ExportRecord0814)list.SelectedItems[0].Tag;if(File.Exists(record.path))Process.Start(new ProcessStartInfo(record.path){UseShellExecute=true});});open.Width=110;
   actions.Controls.Add(use);actions.Controls.Add(show);actions.Controls.Add(open);dialog.Controls.Add(list);dialog.Controls.Add(actions);dialog.ShowDialog(this);
  }}
  void Accept(){
   if(readinessFailure!=null){MessageBox.Show(this,readinessFailure,"Revisa la exportación",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
   if(readinessReport!=null&&!readinessReport.CanExport){ShowReadinessDetails();return;}
   try{
    string folder=destination.Text.Trim(),name=BaseName(fileName.Text);if(!Directory.Exists(folder))throw new Exception("La carpeta de salida no existe");if(name.Length==0||name!=Path.GetFileName(name)||name=="."||name==".."||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new Exception("Escribe un nombre de archivo válido");
    bool video=format.SelectedIndex==0;string path=Path.GetFullPath(Path.Combine(folder,name+(video?".mp4":".wav")));
    var selected=ExportOptions.For(project);selected.Video=video;selected.UsePreview=video&&usePreview.Checked;selected.SelectiveWeb=video&&!selected.UsePreview&&selectiveWeb.Checked;
    if(rangeChoice.SelectedIndex==1){var marks=SequenceExportRange.Read(project.Active);if(!marks.Complete)throw new Exception("Marca entrada y salida antes de exportar este rango");selected.StartFrame=marks.InFrame;selected.EndFrame=marks.OutFrame;}
    if(video){var size=SelectedSize();selected.Width=size[0];selected.Height=size[1];selected.Fps=SelectedFps();selected.Crf=(int)crf.Value;selected.RateControl=rateMode.SelectedIndex==1?"bitrate":"crf";selected.TargetBitrateKbps=(int)Math.Round(videoRate.Value*1000);selected.Preset=new[]{"ultrafast","veryfast","fast","medium","slow"}[preset.SelectedIndex];selected.AudioBitrateKbps=new[]{128,192,256,320}[audioRate.SelectedIndex];selected.Acceleration=(Tools.CanUseNvenc()&&acceleration.SelectedIndex==0)?"nvenc":"cpu";}
    selected.Validate(project);
    if(File.Exists(path)){
     var answer=MessageBox.Show(this,"Ya existe un archivo con ese nombre en esta carpeta:\n"+Path.GetFileName(path)+"\n\n¿Quieres reemplazarlo?","Archivo existente",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2);
     if(answer!=DialogResult.Yes){fileName.Focus();fileName.SelectAll();return;}
    }
    Options=selected;OutputPath=path;ExportHistory0814.SetPreferences(openFolderAfter.Checked,playAfter.Checked);DialogResult=DialogResult.OK;Close();
   }catch(Exception ex){MessageBox.Show(this,ex.Message,"Revisa la exportación",MessageBoxButtons.OK,MessageBoxIcon.Information);}
  }
  protected override void Dispose(bool disposing){if(disposing)hints.Dispose();base.Dispose(disposing);}
 }
}
