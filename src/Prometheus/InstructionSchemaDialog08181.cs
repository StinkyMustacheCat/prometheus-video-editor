using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 // A field driven editor for every instruction in the shared catalog. JSON stays available for complex values.
 public sealed class InstructionSchemaDialog08181:Form {
  readonly InstructionAction action;readonly Project project;readonly Dictionary<string,Control> inputs=new Dictionary<string,Control>();
  readonly TableLayoutPanel rows=new TableLayoutPanel();
  static readonly string[] order={"scope","sequence_id","clip_id","clip_ids","track_id","asset_id","marker_id","effect_id","left_clip_id","right_clip_id","shelf_id","catalog_id","parameter_id","at_seconds","finish_seconds","duration_seconds","duration_frames","source_in_seconds","number","index","width","height","fps","name","text","color","url","source_kind","placement","edge","resource_path","enabled","value","ripple","include_linked","result"};
  static readonly Dictionary<string,string> titles=new Dictionary<string,string>{{"clip_id","Clip"},{"clip_ids","Clips (ID separados por coma)"},{"track_id","Pista"},{"asset_id","Medio"},{"sequence_id","Secuencia"},{"marker_id","Marcador"},{"at_seconds","Posición en segundos"},{"duration_seconds","Duración en segundos"},{"finish_seconds","Fin en segundos"},{"source_in_seconds","Entrada del medio (s)"},{"number","Valor numérico"},{"parameter_id","Propiedad o parámetro"},{"text","Texto o valor"},{"result","Nombre para usar con $"},{"enabled","Activado"},{"value","Sí / No"},{"include_linked","Incluir vinculados"},{"ripple","Cerrar hueco"},{"left_clip_id","Clip izquierdo"},{"right_clip_id","Clip derecho"},{"color","Color hexadecimal"}};
  public InstructionSchemaDialog08181(Project current,InstructionAction item){project=current;action=item;Text="Instrucción · "+item.op;Size=new Size(650,560);MinimumSize=new Size(500,350);StartPosition=FormStartPosition.CenterParent;BackColor=Color.FromArgb(36,38,42);ForeColor=Color.WhiteSmoke;Font=new Font("Segoe UI",9);
   var intro=new Label{Text="Los campos marcados con * son obligatorios. Los tiempos son absolutos en la secuencia.",Dock=DockStyle.Top,Height=32,Padding=new Padding(10,8,0,0),ForeColor=Color.FromArgb(190,194,200)};
   rows.Dock=DockStyle.Fill;rows.ColumnCount=2;rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,215));rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));rows.AutoScroll=true;rows.Padding=new Padding(12,8,12,4);
   var required=new HashSet<string>(Instructions.RequiredFields(item.op).Split(new[]{','},StringSplitOptions.RemoveEmptyEntries));
   var accepted=new HashSet<string>(Instructions.AcceptedFields(item.op));
   foreach(var key in order.Where(accepted.Contains)){
    if(key=="attributes")continue;
    var control=Make(key,Current(key));int row=rows.RowCount++;rows.RowStyles.Add(new RowStyle(SizeType.Absolute,key=="text"?62:37));
    string title;titles.TryGetValue(key,out title);rows.Controls.Add(new Label{Text=(title??key)+(required.Contains(key)?" *":""),Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=ForeColor},0,row);
    control.Dock=DockStyle.Fill;rows.Controls.Add(control,1,row);inputs[key]=control;
   }
   var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=45,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(8,5,8,0)};
   var save=new Button{Text="Guardar tarjeta",Width=130};var cancel=new Button{Text="Cancelar",Width=90,DialogResult=DialogResult.Cancel};footer.Controls.Add(save);footer.Controls.Add(cancel);Controls.Add(rows);Controls.Add(footer);Controls.Add(intro);CancelButton=cancel;
   save.Click+=(s,e)=>{try{Save(required);DialogResult=DialogResult.OK;Close();}catch(Exception ex){MessageBox.Show(this,ex.Message,"Instrucción",MessageBoxButtons.OK,MessageBoxIcon.Information);}};
  }
  object Current(string key){var map=Store.Fields(action) as Dictionary<string,object>;object value;return map!=null&&map.TryGetValue(key,out value)?value:null;}
  Control Make(string key,object current){
   if(key=="resource_path"){
    var row=new Panel();var field=new TextBox{Dock=DockStyle.Fill,Text=Convert.ToString(current??""),BackColor=Color.FromArgb(28,30,34),ForeColor=ForeColor};
    var browse=new Button{Text="…",Dock=DockStyle.Right,Width=34};browse.Click+=(s,e)=>{using(var picker=new OpenFileDialog{CheckFileExists=true,Filter=action.op=="media.import_local"?"Medios compatibles|*.mp4;*.mov;*.wav;*.mp3;*.m4a;*.flac;*.ogg;*.opus;*.aac;*.png;*.jpg;*.jpeg;*.mgt*.json;*.mgtproj|Todos los archivos|*.*":"LUT 3D|*.cube|Todos los archivos|*.*"})if(picker.ShowDialog(this)==DialogResult.OK)field.Text=picker.FileName;};
    row.Controls.Add(field);row.Controls.Add(browse);return row;
   }
   if(new[]{"enabled","value","ripple","include_linked"}.Contains(key))return new CheckBox{Checked=current!=null&&Convert.ToBoolean(current),ForeColor=ForeColor};
   if(key=="text")return new TextBox{Text=Convert.ToString(current??""),Multiline=true,ScrollBars=ScrollBars.Vertical,BackColor=Color.FromArgb(28,30,34),ForeColor=ForeColor};
   var choices=Choices(key);
   if(choices!=null){var box=new ComboBox{DropDownStyle=ComboBoxStyle.DropDown,BackColor=Color.FromArgb(28,30,34),ForeColor=ForeColor,FlatStyle=FlatStyle.Flat};
    foreach(var pair in choices)box.Items.Add(new Choice(pair.Key,pair.Value));
    string value=Convert.ToString(current??"");var match=box.Items.Cast<Choice>().FirstOrDefault(c=>c.Id==value);if(match!=null)box.SelectedItem=match;else box.Text=value;return box;}
   if(key=="clip_ids"&&current is System.Collections.IEnumerable)return new TextBox{Text=String.Join(",",((System.Collections.IEnumerable)current).Cast<object>()),BackColor=Color.FromArgb(28,30,34),ForeColor=ForeColor};
   return new TextBox{Text=current==null?"":Convert.ToString(current,CultureInfo.InvariantCulture),BackColor=Color.FromArgb(28,30,34),ForeColor=ForeColor};
  }
  sealed class Choice {public string Id,Label;public Choice(string id,string label){Id=id;Label=label;}public override string ToString(){return Label;}}
  IEnumerable<KeyValuePair<string,string>> Choices(string key){
   var sequence=project.sequences.Find(s=>s.id==(action.sequence_id??project.active_sequence_id))??project.Active;
   if(key=="sequence_id")return project.sequences.Select(s=>new KeyValuePair<string,string>(s.id,s.name));
   if(key=="clip_id"||key=="left_clip_id"||key=="right_clip_id")return sequence.clips.OrderBy(c=>c.timeline_start.Seconds).Select(c=>new KeyValuePair<string,string>(c.id,c.name+" · "+c.timeline_start.Seconds.ToString("0.###",CultureInfo.InvariantCulture)+" s"));
   if(key=="track_id")return sequence.tracks.OrderBy(t=>t.kind).ThenBy(t=>t.order).Select(t=>new KeyValuePair<string,string>(t.id,t.name));
   if(key=="asset_id")return project.assets.Select(a=>new KeyValuePair<string,string>(a.id,a.name));
   if(key=="marker_id")return Markers.Read(sequence).Select(m=>new KeyValuePair<string,string>(m.Id,m.Name+" · "+m.Seconds.ToString("0.###",CultureInfo.InvariantCulture)+" s"));
   if(key=="effect_id")return sequence.clips.SelectMany(c=>c.effects.Select(e=>new KeyValuePair<string,string>(e.id,c.name+" · "+e.effect_id))).Concat(sequence.tracks.SelectMany(t=>t.effects.Select(e=>new KeyValuePair<string,string>(e.id,t.name+" · "+e.effect_id))));
   if(key=="shelf_id")return ShelfStore.Read(project).Select(i=>new KeyValuePair<string,string>(i.id,i.asset.name));
   if(key=="catalog_id")return action.op.StartsWith("transition.")?TransitionContracts.All.Select(c=>new KeyValuePair<string,string>(c.id,c.name)):EffectContracts.All.Select(c=>new KeyValuePair<string,string>(c.id,c.name));
   if(key=="scope")return new[]{"selection","clip","track","sequence","junction","asset","timeline","project"}.Select(v=>new KeyValuePair<string,string>(v,v));
   if(key=="source_kind")return new[]{"video","audio","image"}.Select(v=>new KeyValuePair<string,string>(v,v));
   if(key=="placement")return new[]{"empty","overwrite","insert"}.Select(v=>new KeyValuePair<string,string>(v,v));
   if(key=="edge")return new[]{"left","right"}.Select(v=>new KeyValuePair<string,string>(v,v));
   if(key=="parameter_id"&&action.op=="clip.set_transform")return new[]{"scale","scale_y","scale_linked","x","y","opacity"}.Select(v=>new KeyValuePair<string,string>(v,v));
   if(key=="parameter_id"&&action.op.StartsWith("curve.")){var clip=sequence.clips.Find(c=>c.id==action.clip_id);var keys=new List<string>{"transform.scale","transform.scale_y","transform.x","transform.y","transform.opacity"};if(clip!=null)keys.AddRange(clip.effects.SelectMany(e=>{var contract=EffectContracts.Find(e.effect_id,e.contract_version);return contract==null?Enumerable.Empty<string>():contract.parameters.Where(p=>p.animatable).Select(p=>"effect:"+e.id+":"+p.id);}));return keys.Select(v=>new KeyValuePair<string,string>(v,v));}
   if(key=="parameter_id"&&action.op=="mgt.set_parameter"){
    var clip=sequence.clips.Find(c=>c.id==action.clip_id);var asset=clip==null?null:project.assets.Find(a=>a.id==clip.asset_id);
    if(MgtMedia.IsMgt(asset))return MgtMedia.Parameters(asset).OfType<Dictionary<string,object>>().SelectMany(g=>{object v;return g.TryGetValue("parameters",out v)&&v is System.Collections.IEnumerable?((System.Collections.IEnumerable)v).Cast<object>().OfType<Dictionary<string,object>>():Enumerable.Empty<Dictionary<string,object>>();}).Where(p=>p.ContainsKey("id")).Select(p=>new KeyValuePair<string,string>(Convert.ToString(p["id"]),p.ContainsKey("label")?Convert.ToString(p["label"]):Convert.ToString(p["id"])));
   }
   return null;
  }
  void Save(HashSet<string> required){
   var map=Store.Fields(action) as Dictionary<string,object>;if(map==null)throw new Exception("Tarjeta inválida");
   foreach(var pair in inputs){string key=pair.Key;var check=pair.Value as CheckBox;object value=null;
    if(check!=null)value=check.Checked;
    else {var box=pair.Value as ComboBox;var panel=pair.Value as Panel;string raw=box!=null?box.SelectedItem is Choice?((Choice)box.SelectedItem).Id:box.Text.Trim():panel!=null?panel.Controls.OfType<TextBox>().First().Text.Trim():((TextBox)pair.Value).Text.Trim();
     if(raw.Length>0){if(key=="clip_ids")value=raw.Split(new[]{','},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim()).Where(x=>x.Length>0).ToArray();
      else if(new[]{"at_seconds","finish_seconds","duration_seconds","source_in_seconds","number"}.Contains(key)){double n;if(!Double.TryParse(raw,NumberStyles.Float,CultureInfo.InvariantCulture,out n)&&!Double.TryParse(raw,NumberStyles.Float,CultureInfo.CurrentCulture,out n))throw new Exception("Valor numérico inválido en "+key);value=n;}
      else if(new[]{"duration_frames","index","width","height","fps"}.Contains(key)){int n;if(!Int32.TryParse(raw,out n))throw new Exception("Número entero inválido en "+key);value=n;}
      else value=raw;}}
    if(value==null){if(required.Contains(key))throw new Exception("Falta "+key);map.Remove(key);}else map[key]=value;
   }
   var encoded=Store.Json().Serialize(map);var validated=Instructions.ParseAction(encoded);
   foreach(var field in typeof(InstructionAction).GetFields())field.SetValue(action,field.GetValue(validated));
  }
 }
}
