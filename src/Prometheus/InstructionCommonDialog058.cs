using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 // Small visual editor for frequent instruction cards. The validated instruction contract remains the source of truth.
 public sealed class InstructionCommonDialog:Form {
  readonly Project project;readonly InstructionAction action;readonly Dictionary<string,Control> inputs=new Dictionary<string,Control>();
  readonly TableLayoutPanel rows=new TableLayoutPanel();
  static ComboBox Choice(IEnumerable<KeyValuePair<string,string>> choices,string current){
   var box=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList,BackColor=Color.FromArgb(28,30,34),ForeColor=Color.WhiteSmoke};
   foreach(var item in choices)box.Items.Add(new Option(item.Key,item.Value));
   for(int i=0;i<box.Items.Count;i++)if(((Option)box.Items[i]).Id==current){box.SelectedIndex=i;break;}
   if(box.SelectedIndex<0&&box.Items.Count>0)box.SelectedIndex=0;
   return box;
  }
  sealed class Option {public readonly string Id,Label;public Option(string id,string label){Id=id;Label=label;}public override string ToString(){return Label;}}
  static string Selected(Control control){var box=control as ComboBox;return box==null||box.SelectedItem==null?null:((Option)box.SelectedItem).Id;}
  void Add(string key,string label,Control control){int row=rows.RowCount++;rows.RowStyles.Add(new RowStyle(SizeType.Absolute,38));rows.Controls.Add(new Label{Text=label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Color.WhiteSmoke},0,row);control.Dock=DockStyle.Fill;rows.Controls.Add(control,1,row);inputs[key]=control;}
  TextBox TextInput(string key,string label,string value){var box=new TextBox{Text=value??"",BackColor=Color.FromArgb(28,30,34),ForeColor=Color.WhiteSmoke};Add(key,label,box);return box;}
  void ClipChoice(bool placeholder){var sequence=project.sequences.Find(s=>s.id==(action.sequence_id??project.active_sequence_id))??project.Active;
   var clips=sequence.clips.Where(c=>!placeholder||project.assets.Any(a=>a.id==c.asset_id&&GeneratedMedia.IsPlaceholder(a))).OrderBy(c=>c.timeline_start.Seconds).Select(c=>new KeyValuePair<string,string>(c.id,c.name+" ["+c.id.Substring(0,Math.Min(8,c.id.Length))+"]"));
   Add("clip",placeholder?"Espacio reservado":"Clip",Choice(clips,action.clip_id));
  }
  void TrackChoice(bool optional){var sequence=project.sequences.Find(s=>s.id==(action.sequence_id??project.active_sequence_id))??project.Active;
   var tracks=sequence.tracks.OrderBy(t=>t.kind).ThenBy(t=>t.order).Select(t=>new KeyValuePair<string,string>(t.id,t.name+" ("+t.kind+")")).ToList();
   if(optional)tracks.Insert(0,new KeyValuePair<string,string>("","Solo guardar en Medios"));
   Add("track","Destino",Choice(tracks,action.track_id??""));
  }
  void Placement(){Add("placement","Si el destino está ocupado",Choice(new[]{new KeyValuePair<string,string>("empty","Buscar espacio libre"),new KeyValuePair<string,string>("overwrite","Sobrescribir"),new KeyValuePair<string,string>("insert","Insertar")},action.placement??"empty"));}
  public InstructionCommonDialog(Project current,InstructionAction item){project=current;action=item;Text="Editar · "+item.op;Size=new Size(580,430);MinimumSize=new Size(480,260);StartPosition=FormStartPosition.CenterParent;BackColor=Color.FromArgb(36,38,42);ForeColor=Color.WhiteSmoke;Font=new Font("Segoe UI",9);
   rows.Dock=DockStyle.Fill;rows.ColumnCount=2;rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,185));rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));rows.AutoScroll=true;rows.Padding=new Padding(12,12,12,0);
   if(item.op=="clip.delete"){ClipChoice(false);Add("ripple","Cerrar espacio",new CheckBox{Checked=item.ripple==true,ForeColor=ForeColor});}
   if(item.op=="clip.cut"){ClipChoice(false);TextInput("at","Cortar en (HH:MM:SS:FF)",Time(item.at_seconds??0));Add("linked","Incluir medios vinculados",new CheckBox{Checked=item.include_linked!=false,ForeColor=ForeColor});}
   if(item.op=="sequence.create"){TextInput("name","Nombre",item.name);TextInput("width","Ancho (opcional)",item.width.HasValue?item.width.Value.ToString():"");TextInput("height","Alto (opcional)",item.height.HasValue?item.height.Value.ToString():"");}
   if(item.op=="track.create"){TextInput("name","Nombre (opcional)",item.name);Add("kind","Tipo",Choice(new[]{new KeyValuePair<string,string>("video","Vídeo"),new KeyValuePair<string,string>("audio","Audio")},item.source_kind??"video"));}
   if(item.op=="media.import_url"||item.op=="placeholder.fill_url"){TextInput("url","Enlace público",item.url);if(item.op=="media.import_url")Add("kind","Tipo",Choice(new[]{new KeyValuePair<string,string>("video","Vídeo o imagen"),new KeyValuePair<string,string>("image","Imagen"),new KeyValuePair<string,string>("audio","Audio")},item.source_kind??"video"));else Add("kind","Tipo (opcional)",Choice(new[]{new KeyValuePair<string,string>("","Detectar automáticamente"),new KeyValuePair<string,string>("image","Imagen"),new KeyValuePair<string,string>("video","Vídeo"),new KeyValuePair<string,string>("audio","Audio")},item.source_kind??""));}
   if(item.op=="media.import_shelf"){var pending=ShelfStore.Read(project).Select(x=>new KeyValuePair<string,string>(x.id,x.asset.name)).ToList();if(!pending.Any(x=>x.Key==item.shelf_id))pending.Insert(0,new KeyValuePair<string,string>(item.shelf_id,"[Ya no está en Bandeja]"));Add("shelf","Elemento de Bandeja",Choice(pending,item.shelf_id));}
   if(item.op=="placeholder.fill_url"){ClipChoice(true);Add("linked","Incluir medios vinculados",new CheckBox{Checked=item.include_linked!=false,ForeColor=ForeColor});}
   if(item.op=="media.import_url"||item.op=="media.import_shelf"){TrackChoice(true);TextInput("at","Posición (HH:MM:SS:FF)",Time(item.at_seconds??0));Placement();}
   var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=48,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(8,6,8,0)};var save=new Button{Text="Guardar tarjeta",Width=125};var cancel=new Button{Text="Cancelar",Width=95,DialogResult=DialogResult.Cancel};footer.Controls.Add(save);footer.Controls.Add(cancel);Controls.Add(rows);Controls.Add(footer);CancelButton=cancel;
   save.Click+=(s,e)=>{try{Save();DialogResult=DialogResult.OK;Close();}catch(Exception ex){MessageBox.Show(this,ex.Message,"Instrucción",MessageBoxButtons.OK,MessageBoxIcon.Information);}};
  }
  string Value(string key){return ((TextBox)inputs[key]).Text.Trim();}
  bool Checked(string key){return ((CheckBox)inputs[key]).Checked;}
  static string Time(double seconds){int frames=(int)Math.Round(seconds*60);return (frames/216000).ToString("00")+":"+((frames/3600)%60).ToString("00")+":"+((frames/60)%60).ToString("00")+":"+(frames%60).ToString("00");}
  static double Seconds(string value){double result;if(MonitorPanelView.TryParseTimecode(value,out result))return result;if(Double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out result)&&result>=0)return result;throw new Exception("Usa HH:MM:SS:FF o segundos positivos.");}
  void Save(){
   if(action.op=="clip.delete"||action.op=="clip.cut"||action.op=="placeholder.fill_url"){action.clip_id=Selected(inputs["clip"]);if(String.IsNullOrEmpty(action.clip_id))throw new Exception("Selecciona un clip disponible.");}
   if(action.op=="clip.delete")action.ripple=Checked("ripple");
   if(action.op=="clip.cut"){action.at_seconds=Seconds(Value("at"));action.include_linked=Checked("linked");}
   if(action.op=="sequence.create"){action.name=Value("name");if(action.name.Length==0)throw new Exception("La secuencia necesita un nombre.");int number;action.width=Int32.TryParse(Value("width"),out number)?(int?)number:null;action.height=Int32.TryParse(Value("height"),out number)?(int?)number:null;if(action.width.HasValue!=action.height.HasValue)throw new Exception("Indica ancho y alto juntos.");}
   if(action.op=="track.create"){action.name=Value("name");if(action.name.Length==0)action.name=null;action.source_kind=Selected(inputs["kind"]);}
   if(action.op=="media.import_url"||action.op=="placeholder.fill_url"){action.url=Value("url");if(action.url.Length==0)throw new Exception("Indica el enlace.");}
   if(action.op=="media.import_url")action.source_kind=Selected(inputs["kind"]);
   if(action.op=="placeholder.fill_url"){action.source_kind=Selected(inputs["kind"]);if(action.source_kind=="")action.source_kind=null;}
   if(action.op=="media.import_shelf")action.shelf_id=Selected(inputs["shelf"]);
   if(action.op=="placeholder.fill_url")action.include_linked=Checked("linked");
   if(action.op=="media.import_url"||action.op=="media.import_shelf"){action.track_id=Selected(inputs["track"]);if(action.track_id=="")action.track_id=null;action.at_seconds=action.track_id==null?null:(double?)Seconds(Value("at"));action.placement=action.track_id==null?null:Selected(inputs["placement"]);}
  }
 }
}
