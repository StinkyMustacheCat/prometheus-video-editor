using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public sealed class InstructionPanelView:Panel {
  readonly ListBox cards=new ListBox();readonly TextBox preview=new TextBox();readonly Button apply=new Button();
  readonly Label reviewStatus=new Label();readonly Label contextNotice=new Label();readonly Button reviewDetails=new Button();readonly Panel reviewArea=new Panel();readonly Splitter reviewSplitter=new Splitter();readonly ProgressBar reviewProgress=new ProgressBar();
  readonly Timer reviewTimer=new Timer();
  readonly ContextMenuStrip cardMenu=new ContextMenuStrip();int dragIndex=-1;Point dragOrigin;Form editForm;InstructionActionDialog actionDialog;
  readonly ToolStripMenuItem shelfMenu=new ToolStripMenuItem("Añadir desde Bandeja");
  Project project;List<string> selection=new List<string>();TransitionInstance selectedTransition;string selectedTrackId;
  InstructionDocument document;InstructionPlan plan;
  readonly Color surface=Color.FromArgb(36,38,42),field=Color.FromArgb(28,30,34),ink=Color.FromArgb(225,228,232),accent=Color.FromArgb(255,156,44);
  public Action<InstructionDocument> ReviewRequested,ApplyRequested;
  public Action<string,IEnumerable<string>> CopyStateRequested;
  public Action<Exception> ErrorOccurred;
  public const string AttributesClipboardFormat="Prometheus.ClipAttributes.v1";
  public InstructionDocument Document {get{return document;}}
  static Button Button(string label){return new Button{Text=label,AutoSize=true,Height=27,Margin=new Padding(3),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(48,51,56),ForeColor=Color.WhiteSmoke};}
  public InstructionPanelView(){
   Dock=DockStyle.Fill;BackColor=surface;ForeColor=ink;AllowDrop=true;
   var header=new Label{Text="Prepara cambios y revísalos antes de aplicarlos",Dock=DockStyle.Top,Height=29,Padding=new Padding(9,7,0,0),ForeColor=Color.FromArgb(187,192,198),AutoEllipsis=true};
   var toolbar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=36,AutoScroll=false,WrapContents=true,Padding=new Padding(4,2,2,0),BackColor=surface};
   var copy=Button("Copiar estado ▾");var add=Button("Añadir acción ▾");var paste=Button("Pegar instrucciones");var review=Button("Revisar");apply=Button("Aplicar");apply.BackColor=Color.FromArgb(88,55,33);apply.Enabled=false;
   toolbar.Controls.AddRange(new Control[]{copy,add,paste,review,apply});
   toolbar.SizeChanged+=(s,e)=>{int available=Math.Max(1,toolbar.ClientSize.Width-toolbar.Padding.Horizontal),used=0,rows=1;foreach(Control item in toolbar.Controls){int needed=item.Width+item.Margin.Horizontal;if(used>0&&used+needed>available){rows++;used=0;}used+=needed;}int height=rows*33+3;if(toolbar.Height!=height)toolbar.Height=height;};
   var cardTools=new Panel{Dock=DockStyle.Top,Height=34,BackColor=surface};
   var up=Button("↑");var down=Button("↓");var clear=Button("Vaciar");
   clear.Dock=DockStyle.Left;down.Dock=DockStyle.Right;up.Dock=DockStyle.Right;up.Width=33;down.Width=33;cardTools.Controls.AddRange(new Control[]{clear,down,up});
   cards.Dock=DockStyle.Fill;cards.BorderStyle=BorderStyle.None;cards.BackColor=field;cards.ForeColor=ink;cards.Font=new Font("Segoe UI",9);cards.ItemHeight=30;cards.DrawMode=DrawMode.OwnerDrawFixed;cards.HorizontalScrollbar=true;
   preview.Dock=DockStyle.Fill;preview.Multiline=true;preview.ReadOnly=true;preview.ScrollBars=ScrollBars.Vertical;preview.BorderStyle=BorderStyle.None;preview.BackColor=field;preview.ForeColor=ink;
   reviewArea.Dock=DockStyle.Bottom;reviewArea.Height=42;reviewArea.BackColor=surface;
   reviewSplitter.Dock=DockStyle.Bottom;reviewSplitter.Height=5;reviewSplitter.BackColor=Color.FromArgb(72,76,82);reviewSplitter.MinSize=80;reviewSplitter.MinExtra=100;reviewSplitter.Cursor=Cursors.HSplit;
   var reviewBar=new Panel{Dock=DockStyle.Top,Height=39};reviewDetails.Text="Detalles ▾";reviewDetails.Dock=DockStyle.Right;reviewDetails.Width=98;reviewDetails.FlatStyle=FlatStyle.Flat;reviewDetails.BackColor=Color.FromArgb(48,51,56);reviewDetails.ForeColor=ink;
   reviewStatus.Dock=DockStyle.Fill;reviewStatus.Text="Añade tarjetas para preparar cambios";reviewStatus.ForeColor=ink;reviewStatus.TextAlign=ContentAlignment.MiddleLeft;reviewStatus.Padding=new Padding(8,0,0,0);reviewStatus.AutoEllipsis=true;
   reviewProgress.Dock=DockStyle.Right;reviewProgress.Width=92;reviewProgress.Style=ProgressBarStyle.Marquee;reviewProgress.MarqueeAnimationSpeed=24;reviewProgress.Visible=false;reviewBar.Controls.Add(reviewStatus);reviewBar.Controls.Add(reviewProgress);reviewBar.Controls.Add(reviewDetails);reviewArea.Controls.Add(preview);reviewArea.Controls.Add(reviewBar);
   reviewDetails.Click+=(s,e)=>{bool open=reviewArea.Height<100;reviewArea.Height=open?178:42;reviewDetails.Text=open?"Detalles ▴":"Detalles ▾";};
   contextNotice.Dock=DockStyle.Top;contextNotice.Height=26;contextNotice.Visible=false;contextNotice.Padding=new Padding(8,4,6,0);contextNotice.ForeColor=Color.FromArgb(250,198,98);contextNotice.BackColor=Color.FromArgb(65,52,34);contextNotice.AutoEllipsis=true;
   Controls.Add(cards);Controls.Add(reviewSplitter);Controls.Add(reviewArea);Controls.Add(cardTools);Controls.Add(toolbar);Controls.Add(contextNotice);Controls.Add(header);
   reviewTimer.Interval=300;reviewTimer.Tick+=(s,e)=>{reviewTimer.Stop();if(document!=null&&project!=null&&document.project_id==project.id&&ReviewRequested!=null)ReviewRequested(document);};
   var copyMenu=new ContextMenuStrip();
   copyMenu.Items.Add("Clips seleccionados",null,(s,e)=>{if(CopyStateRequested!=null)CopyStateRequested("selection",selection.ToList());});
   copyMenu.Items.Add("Tramo In/Out",null,(s,e)=>{if(CopyStateRequested!=null)CopyStateRequested("io",selection.ToList());});
   copyMenu.Items.Add("Pistas elegidas…",null,(s,e)=>CopySelectedTracks(false));
   copyMenu.Items.Add("Secuencia activa",null,(s,e)=>{if(CopyStateRequested!=null)CopyStateRequested("sequence",selection.ToList());});
   copyMenu.Items.Add("Proyecto completo",null,(s,e)=>{if(CopyStateRequested!=null)CopyStateRequested("project",selection.ToList());});
   var complete=new ToolStripMenuItem("Estado completo para diagnóstico");
   foreach(var entry in new[]{new[]{"selection","Clips seleccionados"},new[]{"io","Tramo In/Out"},new[]{"sequence","Secuencia activa"},new[]{"project","Proyecto completo"}}){string captured=entry[0];complete.DropDownItems.Add(entry[1],null,(s,e)=>{if(CopyStateRequested!=null)CopyStateRequested("full:"+captured,selection.ToList());});}
   complete.DropDownItems.Add("Pistas elegidas…",null,(s,e)=>CopySelectedTracks(true));
   copyMenu.Items.Add(complete);
   copyMenu.Items.Add(new ToolStripSeparator());
   copyMenu.Items.Add("Referencias web usadas en la secuencia",null,(s,e)=>CopyReferences(null));
   copyMenu.Items.Add("Referencias web de la selección",null,(s,e)=>CopyReferences(selection.ToList()));
   copy.Click+=(s,e)=>copyMenu.Show(copy,0,copy.Height);
   var addMenu=new ContextMenuStrip();foreach(var group in Instructions.AvailableActions().GroupBy(op=>op.Split('.')[0])){var category=new ToolStripMenuItem(CategoryLabel(group.Key));foreach(string name in group){string captured=name;var item=category.DropDownItems.Add(ActionLabel(name),null,(s,e)=>AddAction(captured));item.ToolTipText=name;}addMenu.Items.Add(category);}add.Click+=(s,e)=>addMenu.Show(add,0,add.Height);
   paste.Click+=(s,e)=>PasteDocument();review.Click+=(s,e)=>{if(document!=null&&ReviewRequested!=null)ReviewRequested(document);};apply.Click+=(s,e)=>{if(document!=null&&plan!=null&&ApplyRequested!=null)ApplyRequested(document);};
   cards.DoubleClick+=(s,e)=>EditSelected();up.Click+=(s,e)=>MoveSelected(-1);down.Click+=(s,e)=>MoveSelected(1);clear.Click+=(s,e)=>{reviewTimer.Stop();document=null;plan=null;contextNotice.Visible=false;reviewStatus.Text="Lista vacía";preview.Text="";RefreshCards();};
   cardMenu.Items.Add("Editar instrucción",null,(s,e)=>EditSelected());
   cardMenu.Items.Add("Editar JSON avanzado",null,(s,e)=>EditSelected(true));
   cardMenu.Items.Add("Quitar instrucción",null,(s,e)=>RemoveSelected());
   cardMenu.Items.Add("Cargar atributos copiados",null,(s,e)=>AddAttributesFromClipboard());
   cardMenu.Items.Add(shelfMenu);
   cardMenu.Opening+=(s,e)=>{shelfMenu.DropDownItems.Clear();var pending=project==null?new List<ShelfItem>():ShelfStore.Read(project);foreach(var item in pending){var id=item.id;shelfMenu.DropDownItems.Add(item.asset.name??item.url,null,(x,y)=>AddShelfCard(id));}if(pending.Count==0)shelfMenu.DropDownItems.Add(new ToolStripMenuItem("Bandeja vacía"){Enabled=false});};
   cardMenu.Items.Add("Ver detalle de revisión",null,(s,e)=>ShowSelectedDetails());
   cards.MouseDown+=(s,e)=>{int index=cards.IndexFromPoint(e.Location);if(e.Button==MouseButtons.Right){if(index>=0)cards.SelectedIndex=index;cardMenu.Show(cards,e.Location);}else if(e.Button==MouseButtons.Left){dragIndex=index;dragOrigin=e.Location;}};
   cards.MouseMove+=(s,e)=>{if((e.Button&MouseButtons.Left)==0||dragIndex<0||Math.Abs(e.X-dragOrigin.X)+Math.Abs(e.Y-dragOrigin.Y)<6)return;int source=dragIndex;dragIndex=-1;cards.DoDragDrop(new DataObject("Prometheus.InstructionCardIndex",source),DragDropEffects.Move);};
   cards.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Delete){RemoveSelected();e.Handled=true;e.SuppressKeyPress=true;}};
   cards.DrawItem+=(s,e)=>DrawCard(e);
   foreach(Control target in new Control[]{this,cards,preview,header,toolbar,cardTools}){
    target.AllowDrop=true;
    target.DragEnter+=(s,e)=>{e.Effect=e.Data.GetDataPresent("Prometheus.InstructionCardIndex")?DragDropEffects.Move:e.Data.GetDataPresent("Prometheus.EffectId")||e.Data.GetDataPresent("Prometheus.TransitionId")||e.Data.GetDataPresent("Prometheus.ShelfId")?DragDropEffects.Copy:DragDropEffects.None;};
    target.DragOver+=(s,e)=>{if(e.Data.GetDataPresent("Prometheus.InstructionCardIndex"))e.Effect=DragDropEffects.Move;};
    target.DragDrop+=(s,e)=>{try{if(project==null)return;if(e.Data.GetDataPresent("Prometheus.InstructionCardIndex")){var raw=e.Data.GetData("Prometheus.InstructionCardIndex");int source=Convert.ToInt32(raw);Point at=cards.PointToClient(new Point(e.X,e.Y));int targetIndex=cards.IndexFromPoint(at);ReorderSelected(source,targetIndex<0?cards.Items.Count-1:targetIndex);return;}string effect=e.Data.GetData("Prometheus.EffectId") as string;string transition=e.Data.GetData("Prometheus.TransitionId") as string;string shelf=e.Data.GetData("Prometheus.ShelfId") as string;if(effect!=null)AddDropped("effect.apply",effect);else if(transition!=null)AddDropped("transition.apply",transition);else if(shelf!=null)AddShelfCard(shelf);}catch(Exception ex){Fail(ex);}};
   }
  }
  void CopyReferences(IEnumerable<string> ids){try{string result=MediaReferences08202.CopyText(project,ids);if(String.IsNullOrWhiteSpace(result))throw new Exception("No hay enlaces web usados en este alcance");Clipboard.SetText(result);reviewStatus.Text="Referencias copiadas al portapapeles";}catch(Exception ex){Fail(ex);}}
  void CopySelectedTracks(bool full){if(project==null)return;using(var form=new Form{Text="Contexto de pistas",Width=390,Height=440,MinimumSize=new Size(320,320),StartPosition=FormStartPosition.CenterParent,BackColor=surface,ForeColor=ink}){var list=new CheckedListBox{Dock=DockStyle.Fill,CheckOnClick=true,BackColor=field,ForeColor=ink,BorderStyle=BorderStyle.None};var tracks=project.Active.tracks.OrderBy(t=>t.kind=="video"?0:1).ThenBy(t=>t.order).ToList();foreach(var track in tracks)list.Items.Add((track.kind=="video"?"V · ":"A · ")+track.name,track.id==selectedTrackId);var bottom=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=48,FlowDirection=FlowDirection.RightToLeft};var accept=Button("Copiar contexto");var cancel=Button("Cancelar");bottom.Controls.Add(accept);bottom.Controls.Add(cancel);form.Controls.Add(list);form.Controls.Add(bottom);cancel.Click+=(s,e)=>form.Close();accept.Click+=(s,e)=>{var ids=list.CheckedIndices.Cast<int>().Select(i=>tracks[i].id).ToList();if(ids.Count==0){MessageBox.Show(form,"Selecciona al menos una pista.","Contexto de pistas",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}form.DialogResult=DialogResult.OK;form.Tag=ids;form.Close();};if(form.ShowDialog(FindForm())==DialogResult.OK&&CopyStateRequested!=null)CopyStateRequested((full?"full:":"")+"tracks",(List<string>)form.Tag);}}
  void Fail(Exception ex){if(ErrorOccurred!=null)ErrorOccurred(ex);else MessageBox.Show(ex.Message,"Instrucciones",MessageBoxButtons.OK,MessageBoxIcon.Information);}
  static string CategoryLabel(string prefix){var names=new Dictionary<string,string>{{"clip","Clips"},{"effect","Efectos"},{"transition","Transiciones"},{"sequence","Secuencias"},{"track","Pistas"},{"media","Medios"},{"placeholder","Espacios reservados"},{"text","Texto"},{"subtitle","Subtítulos"},{"marker","Marcadores"},{"mgt","MGT"},{"gap","Espacios"},{"range","Intervalos"},{"curve","Curvas"},{"mask","Máscaras"},{"audio","Audio"}};string value;return names.TryGetValue(prefix,out value)?value:prefix;}
  static string ActionLabel(string op){var names=new Dictionary<string,string>{
   {"clip.move","Mover"},{"clip.trim","Recortar borde"},{"clip.duplicate","Duplicar"},{"clip.delete","Eliminar"},{"clip.cut","Dividir"},{"clip.range_trim","Eliminar segmento"},{"clip.slip","Deslizar contenido"},{"clip.roll","Desplazar corte"},{"clip.link","Vincular"},{"clip.unlink","Separar vínculo"},{"clip.group","Agrupar"},{"clip.ungroup","Desagrupar"},{"clip.set_speed","Cambiar velocidad"},{"clip.set_transform","Cambiar transformación"},{"clip.inherit_track_effects","Heredar efectos de pista"},{"clip.paste_attributes","Pegar atributos"},
   {"effect.apply","Aplicar efecto"},{"effect.remove","Quitar efecto"},{"effect.set_parameter","Cambiar parámetro"},{"effect.set_enabled","Activar o desactivar"},{"effect.move","Reordenar"},{"effect.remove_instance","Quitar instancia"},
   {"transition.apply","Aplicar transición"},{"transition.remove","Quitar transición"},{"transition.set_duration","Cambiar duración"},
   {"sequence.create","Crear secuencia"},{"sequence.rename","Renombrar secuencia"},{"sequence.activate","Activar secuencia"},{"sequence.set_export_in","Definir entrada In"},{"sequence.set_export_out","Definir salida Out"},{"sequence.clear_export_range","Borrar marcas In/Out"},
   {"track.create","Crear pista"},{"track.rename","Renombrar pista"},{"track.set_locked","Bloquear o desbloquear"},{"track.set_enabled","Mostrar u ocultar"},{"track.set_effects_enabled","Activar efectos de pista"},
   {"media.import_url","Importar enlace"},{"media.import_local","Importar archivo"},{"media.import_shelf","Importar desde Bandeja"},{"media.place_asset","Colocar medio"},{"media.redownload_web","Redescargar medios web"},
   {"placeholder.fill_url","Conectar enlace"},{"text.create","Crear texto"},{"text.set_content","Editar texto"},{"subtitle.create","Crear subtítulo"},{"subtitle.set_content","Editar subtítulo"},{"subtitle.delete","Eliminar subtítulo"},
   {"marker.create","Crear marcador"},{"marker.move","Mover marcador"},{"marker.rename","Renombrar marcador"},{"marker.delete","Eliminar marcador"},{"mgt.set_parameter","Cambiar parámetro MGT"},{"gap.close","Cerrar espacio"},{"range.trim","Eliminar intervalo"},{"curve.set_keyframe","Crear fotograma clave"},{"curve.set_interpolation","Cambiar curva"},{"curve.set_tangent","Ajustar tangente"},{"mask.set_keyframe","Crear fotograma de máscara"},{"mask.set_interpolation","Cambiar curva de máscara"},{"mask.set_enabled","Activar máscara temporal"},{"audio.ducking.set","Activar Auto-Ducking"},{"audio.ducking.remove","Quitar Auto-Ducking"}};string value;return names.TryGetValue(op,out value)?value:op;}
  void AddShelfCard(string id){if(project==null||!ShelfStore.Read(project).Any(x=>x.id==id))throw new Exception("El elemento ya no está en la Bandeja");EnsureDocument();document.actions.Add(new InstructionAction{op="media.import_shelf",shelf_id=id,sequence_id=project.active_sequence_id});plan=null;reviewStatus.Text="Elemento de Bandeja añadido · revisando…";RefreshCards();cards.SelectedIndex=cards.Items.Count-1;if(ReviewRequested!=null)ReviewRequested(document);}
  void AddAction(string op){try{if(project==null)throw new Exception("Abre un proyecto primero");if(op=="clip.paste_attributes"){AddAttributesFromClipboard();return;}var action=new InstructionAction{op=op,sequence_id=project.active_sequence_id,clip_id=selection.FirstOrDefault(),clip_ids=selection.ToList(),track_id=selectedTrackId};
   if(op=="clip.group"||op=="clip.ungroup")action.clip_ids=selection.ToList();
   if(op=="clip.set_transform")action.parameter_id="scale";
   if(op=="media.redownload_web")action.scope="project";
   if(op=="effect.apply"||op=="effect.remove")action.scope=selection.Count>0?"selection":"sequence";
   if(op=="transition.apply"||op=="transition.remove")action.scope=selectedTransition==null?"sequence":"junction";
   if(selectedTransition!=null){action.left_clip_id=selectedTransition.left_clip_id;action.right_clip_id=selectedTransition.right_clip_id;}
   using(var dialog=new InstructionSchemaDialog08181(project,action)){if(dialog.ShowDialog(FindForm())!=DialogResult.OK)return;}
   EnsureDocument();document.actions.Add(action);plan=null;reviewStatus.Text="Acción añadida · revisa el lote";RefreshCards();cards.SelectedIndex=cards.Items.Count-1;
  }catch(Exception ex){Fail(ex);}}
  void DrawCard(DrawItemEventArgs e){
   if(e.Index<0||e.Index>=cards.Items.Count)return;
   bool selected=(e.State&DrawItemState.Selected)!=0;
   using(var brush=new SolidBrush(selected?Color.FromArgb(73,55,41):field))e.Graphics.FillRectangle(brush,e.Bounds);
   string state=plan!=null&&e.Index<plan.Cards.Count?plan.Cards[e.Index].status:"pending";
   Color mark=state=="valid"?Color.FromArgb(83,183,127):state=="partial"?Color.FromArgb(239,184,63):state=="invalid"?Color.FromArgb(232,93,87):Color.FromArgb(118,124,131);
   using(var brush=new SolidBrush(mark))e.Graphics.FillEllipse(brush,e.Bounds.Left+9,e.Bounds.Top+10,10,10);
   string label=Convert.ToString(cards.Items[e.Index]);
   TextRenderer.DrawText(e.Graphics,label,cards.Font,new Rectangle(e.Bounds.Left+27,e.Bounds.Top+1,e.Bounds.Width-34,e.Bounds.Height-2),ink,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
   e.DrawFocusRectangle();
  }
  void ShowSelectedDetails(){if(plan==null||cards.SelectedIndex<0||cards.SelectedIndex>=plan.Cards.Count){reviewStatus.Text="Esta tarjeta aún no se ha revisado";return;}var item=plan.Cards[cards.SelectedIndex];reviewStatus.Text=(cards.SelectedIndex+1)+". "+item.summary;preview.Text=String.Join(Environment.NewLine,item.details);reviewArea.Height=178;reviewDetails.Text="Detalles ▴";}
  public void SetContext(Project current,IEnumerable<string> selected,string transitionId,string trackId=null){bool changed=project==null||current==null||project.revision!=current.revision||project.id!=current.id;project=current;selection.Clear();selection.AddRange((selected??new string[0]).Distinct());selectedTransition=current==null?null:current.Active.transitions.Find(t=>t.id==transitionId);selectedTrackId=trackId;if(actionDialog!=null&&!actionDialog.IsDisposed)actionDialog.UpdateSelection();if(changed){reviewTimer.Stop();if(document!=null){plan=null;if(current==null||document.project_id!=current.id){contextNotice.Text="El proyecto no coincide con el de las instrucciones.";contextNotice.Visible=true;reviewStatus.Text="Abre el proyecto de origen para revisar estas tarjetas";}else{contextNotice.Text="El proyecto cambió; se revisarán las coincidencias en su estado actual.";contextNotice.Visible=true;reviewStatus.Text="Proyecto actualizado · revisando tarjetas…";reviewTimer.Start();}}RefreshCards();}}
  void EnsureDocument(){if(document==null)document=Instructions.New(project);if(document.project_id!=project.id)throw new Exception("El proyecto no coincide con el de las instrucciones.");}
  void AddDropped(string op,string catalogId){
   EnsureDocument();var a=new InstructionAction{op=op,catalog_id=catalogId,sequence_id=project.active_sequence_id};
   if(op.StartsWith("effect.")){a.scope=selection.Count>0?"selection":selectedTrackId!=null?"track":"sequence";a.track_id=selectedTrackId;a.clip_ids=selection.ToList();var contract=EffectContracts.All.FirstOrDefault(c=>c.id==catalogId);if(contract!=null&&contract.input_kind=="audio"&&a.clip_ids.Count>0){var audio=project.Active.link_groups.Where(g=>g.clip_ids.Any(selection.Contains)).SelectMany(g=>g.clip_ids).Where(id=>project.Active.clips.Any(c=>c.id==id&&project.Active.tracks.Any(t=>t.id==c.track_id&&t.kind=="audio"))).Distinct().ToList();if(audio.Count>0)a.clip_ids=audio;}if(catalogId==EffectContracts.Lut3DId){using(var dialog=new OpenFileDialog{Title="Elegir LUT 3D para las instrucciones",Filter="LUT 3D (*.cube)|*.cube",CheckFileExists=true}){if(dialog.ShowDialog(FindForm())!=DialogResult.OK)return;FFmpegEffectAdapter.VerifyLutFile(dialog.FileName);a.resource_path=Path.GetFullPath(dialog.FileName);}}}
   else{a.scope=selectedTransition==null?"sequence":"junction";if(selectedTransition!=null){a.left_clip_id=selectedTransition.left_clip_id;a.right_clip_id=selectedTransition.right_clip_id;}a.duration_frames=30;}
   document.actions.Add(a);plan=null;RefreshCards();cards.SelectedIndex=cards.Items.Count-1;
  }
  public void LoadDocument(string text){var parsed=Instructions.Parse(text);reviewTimer.Stop();document=parsed;plan=null;RefreshCards();if(project==null||parsed.project_id!=project.id){contextNotice.Text="El proyecto no coincide con el de las instrucciones.";contextNotice.Visible=true;reviewStatus.Text="Abre el proyecto de origen para revisar estas tarjetas";preview.Text="Se conserva el documento, pero no se aplicará a otro proyecto.";return;}contextNotice.Visible=false;reviewStatus.Text=parsed.actions.Count+" tarjeta(s) cargada(s) · revisando…";preview.Text="La revisión comprueba cada tarjeta antes de modificar el proyecto.";if(ReviewRequested!=null)ReviewRequested(document);}
  void PasteDocument(){
   using(var form=new Form{Text="Pegar instrucciones",Size=new Size(760,580),MinimumSize=new Size(520,370),StartPosition=FormStartPosition.CenterParent,BackColor=surface,ForeColor=ink}){
    var input=new TextBox{Multiline=true,AcceptsReturn=true,AcceptsTab=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill,Font=new Font("Consolas",10),BackColor=field,ForeColor=ink,Text=document==null?"":Instructions.Encode(document)};
    var help=new Label{Dock=DockStyle.Top,Height=43,Padding=new Padding(10,8,10,0),Text="Pega un documento prometheus-instructions versión 1. Cargar tarjetas inicia la revisión y no modifica el proyecto."};
    var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=43,FlowDirection=FlowDirection.RightToLeft};var load=Button("Cargar tarjetas");var cancel=Button("Cancelar");load.DialogResult=DialogResult.OK;cancel.DialogResult=DialogResult.Cancel;footer.Controls.Add(load);footer.Controls.Add(cancel);form.Controls.Add(input);form.Controls.Add(help);form.Controls.Add(footer);form.AcceptButton=load;form.CancelButton=cancel;
    if(form.ShowDialog(FindForm())==DialogResult.OK)try{LoadDocument(input.Text);}catch(Exception ex){Fail(ex);}
   }
  }
  string Description(InstructionAction a){
   if(a==null)return "(acción vacía)";
   if(a.op=="effect.apply"||a.op=="effect.remove"){var c=EffectContracts.All.FirstOrDefault(x=>x.id==a.catalog_id);string destination=a.scope=="sequence"&&c!=null?"clips de "+(c.input_kind=="audio"?"audio":"vídeo")+" de la secuencia":Scope(a.scope);return (a.op.EndsWith("apply")?"Aplicar ":"Quitar ")+(c==null?a.catalog_id:c.name)+" · "+destination;}
   if(a.op=="transition.apply"||a.op=="transition.remove"){var c=TransitionContracts.All.FirstOrDefault(x=>x.id==a.catalog_id);return (a.op.EndsWith("apply")?"Aplicar ":"Quitar ")+(c==null?(a.catalog_id??"transiciones"):c.name)+" · "+Scope(a.scope);}
   if(a.op=="clip.set_speed")return "Velocidad · "+(a.number.HasValue?a.number.Value.ToString("0.##",CultureInfo.InvariantCulture):"?")+" %";
   if(a.op=="clip.set_transform")return "Transformación · "+(a.parameter_id??"propiedad");
   if(a.op=="clip.group"||a.op=="clip.ungroup")return (a.op=="clip.group"?"Agrupar":"Desagrupar")+" · "+(a.clip_ids==null?0:a.clip_ids.Count)+" clips";
   if(a.op.StartsWith("text.")||a.op.StartsWith("subtitle."))return (a.op.EndsWith("create")?"Crear ":a.op.EndsWith("delete")?"Eliminar ":"Editar ")+(a.op.StartsWith("text.")?"texto":"subtítulo")+(a.text==null?"":" · "+a.text.Replace('\n',' ').Substring(0,Math.Min(26,a.text.Length)));
   if(a.op.StartsWith("marker."))return "Marcador · "+a.op.Substring(7)+(a.name==null?"":" · "+a.name);
   if(a.op=="media.import_local")return "Importar archivo · "+Path.GetFileName(a.resource_path);
   if(a.op=="media.place_asset")return "Colocar medio · "+(a.asset_id??"");
   if(a.op=="mgt.set_parameter")return "MGT · "+(a.parameter_id??"parámetro");
   var clip=project==null?null:project.sequences.SelectMany(s=>s.clips).FirstOrDefault(c=>c.id==a.clip_id);string name=clip==null?(a.clip_id??"clip"):clip.name;
   var labels=new Dictionary<string,string>{{"clip.move","Mover "},{"clip.trim","Recortar "},{"clip.duplicate","Duplicar "},{"clip.delete",a.ripple==true?"Eliminar con rizo ":"Eliminar "},{"clip.cut","Dividir "},{"clip.range_trim","Eliminar segmento de "},{"range.trim","Eliminar rango"},{"clip.slip","Mover contenido de "},{"clip.roll","Ajustar corte "},{"clip.link","Vincular clips"},{"clip.unlink","Desvincular "},{"gap.close","Cerrar espacio"},{"effect.set_parameter","Cambiar parámetro de efecto"},{"effect.set_enabled","Activar/desactivar efecto"},{"effect.move","Reordenar efecto"},{"effect.remove_instance","Quitar efecto"},{"transition.set_duration","Cambiar duración de transición"},{"sequence.create","Crear secuencia "},{"sequence.rename","Renombrar secuencia "},{"sequence.activate","Activar secuencia"},{"track.create","Crear pista "},{"track.rename","Renombrar pista "},{"track.set_locked","Cambiar bloqueo de pista"},{"media.import_url","Importar enlace "},{"placeholder.fill_url","Conectar enlace a espacio reservado "},{"clip.inherit_track_effects","Efectos de pista en "},{"clip.paste_attributes","Pegar atributos · "}};
   if(a.op=="media.import_shelf"){var item=project==null?null:ShelfStore.Read(project).FirstOrDefault(x=>x.id==a.shelf_id);return "Bandeja → "+(item==null?a.shelf_id:item.asset.name);}
   string label;return labels.TryGetValue(a.op,out label)?label+(a.op=="sequence.create"||a.op=="sequence.rename"||a.op=="track.create"||a.op=="track.rename"?a.name:a.op=="media.import_url"?a.url:a.op=="clip.paste_attributes"?Scope(a.scope):a.op=="gap.close"||a.op=="range.trim"||a.op=="clip.link"||a.op.StartsWith("effect.")||a.op.StartsWith("transition.")||a.op=="sequence.activate"||a.op=="track.set_locked"?"":name):a.op;
  }
  static string Scope(string scope){return scope=="selection"?"medios seleccionados":scope=="track"?"pista":scope=="sequence"?"secuencia":scope=="junction"?"corte":"clip";}
  void RefreshCards(){int old=cards.SelectedIndex;cards.BeginUpdate();cards.Items.Clear();if(document!=null)for(int i=0;i<document.actions.Count;i++)cards.Items.Add((i+1)+". "+Description(document.actions[i]));cards.EndUpdate();if(cards.Items.Count>0)cards.SelectedIndex=Math.Min(Math.Max(0,old),cards.Items.Count-1);apply.Enabled=plan!=null&&plan.ChangedCount>0&&document!=null&&project!=null&&document.project_id==project.id&&plan.ReviewedProjectId==project.id&&plan.ReviewedRevision==project.revision&&plan.DocumentJson==Instructions.Encode(document);cards.Invalidate();}
  public void ShowReviewProgress(){plan=null;reviewProgress.Style=ProgressBarStyle.Marquee;reviewProgress.MarqueeAnimationSpeed=24;reviewProgress.Visible=true;reviewStatus.Text="Revisando instrucciones…";preview.Text="Comprobando las acciones y los medios del lote.";RefreshCards();}
  public void ShowApplyProgress(int done,int total){reviewProgress.Visible=true;reviewProgress.Style=ProgressBarStyle.Continuous;reviewProgress.MarqueeAnimationSpeed=0;reviewProgress.Maximum=Math.Max(1,total);reviewProgress.Value=Math.Min(Math.Max(0,done),reviewProgress.Maximum);reviewStatus.Text="Aplicando instrucciones · "+done+" / "+total;preview.Text="Prometheus está preparando el lote. Al terminar podrás deshacerlo en un solo paso.";apply.Enabled=false;}
  public void ShowPlan(InstructionPlan result){reviewProgress.Visible=false;plan=result;int valid=result.Cards.Count(c=>c.status=="valid"),partial=result.Cards.Count(c=>c.status=="partial"),invalid=result.Cards.Count(c=>c.status=="invalid");bool updated=document!=null&&project!=null&&document.project_id==project.id&&document.revision!=result.ReviewedRevision;bool range=document!=null&&document.context_in_frame.HasValue;contextNotice.Visible=updated||range;contextNotice.Text=updated?"El proyecto cambió; se revisaron las coincidencias en su estado actual.":range?"Contexto In/Out: "+(document.context_in_frame.Value/60.0).ToString("0.###",CultureInfo.InvariantCulture)+"–"+(document.context_out_frame.Value/60.0).ToString("0.###",CultureInfo.InvariantCulture)+" s · "+(document.allow_outside_context==true?"incluye cambios fuera del tramo":"cambios fuera del tramo bloqueados"):"";reviewStatus.Text="Revisión: "+valid+" completas · "+partial+" requieren atención · "+invalid+" sin aplicación";preview.Text=String.Join(Environment.NewLine,result.Lines)+Environment.NewLine+Environment.NewLine+String.Join(Environment.NewLine,result.Changes)+Environment.NewLine+"Aplicar será un solo paso de deshacer.";RefreshCards();}
  public void ShowError(string message){reviewProgress.Visible=false;plan=null;reviewStatus.Text=message;preview.Text=message;RefreshCards();}
  public void Applied(){reviewProgress.Visible=false;reviewTimer.Stop();document=null;plan=null;contextNotice.Visible=false;reviewStatus.Text="Lote aplicado · Ctrl+Z para deshacer";preview.Text="";RefreshCards();}
  void RemoveSelected(){if(document==null||cards.SelectedIndex<0)return;document.actions.RemoveAt(cards.SelectedIndex);if(document.actions.Count==0)document=null;plan=null;reviewStatus.Text="Tarjeta eliminada · vuelve a revisar";RefreshCards();}
  public bool DeleteSelectedCard(){if(!ContainsFocus||document==null||cards.SelectedIndex<0)return false;RemoveSelected();return true;}
  void MoveSelected(int delta){if(document==null||cards.SelectedIndex<0)return;int before=cards.SelectedIndex,after=before+delta;if(after<0||after>=document.actions.Count)return;var action=document.actions[before];document.actions.RemoveAt(before);document.actions.Insert(after,action);plan=null;reviewStatus.Text="Orden cambiado · vuelve a revisar";RefreshCards();cards.SelectedIndex=after;}
  void ReorderSelected(int before,int after){if(document==null||before<0||before>=document.actions.Count||after<0||after>=document.actions.Count||before==after)return;var action=document.actions[before];document.actions.RemoveAt(before);document.actions.Insert(after,action);plan=null;reviewStatus.Text="Orden cambiado · vuelve a revisar";RefreshCards();cards.SelectedIndex=after;}
  public void AddAttributesFromClipboard(){try{
   if(project==null)throw new Exception("Abre un proyecto primero");
   var data=Clipboard.GetDataObject();if(data==null||!data.GetDataPresent(AttributesClipboardFormat))throw new Exception("Copia primero los atributos de un clip");
   var snapshot=ClipAttributes.Parse(data.GetData(AttributesClipboardFormat) as string);
   if(selection.Count==0)throw new Exception("Selecciona al menos un clip en la línea de tiempo");
   EnsureDocument();document.actions.Add(new InstructionAction{op="clip.paste_attributes",scope="selection",sequence_id=project.active_sequence_id,clip_ids=selection.ToList(),attributes=snapshot});
   plan=null;reviewStatus.Text="Atributos añadidos · pulsa Revisar";RefreshCards();cards.SelectedIndex=cards.Items.Count-1;
  }catch(Exception ex){Fail(ex);}}
  void EditSelected(bool raw=false){if(document==null||cards.SelectedIndex<0||project==null)return;if(editForm!=null&&!editForm.IsDisposed){editForm.Activate();return;}int index=cards.SelectedIndex;var clone=Store.Json().Deserialize<InstructionAction>(Store.Json().Serialize(Store.Fields(document.actions[index])));try{
   if(!raw&&new[]{"clip.delete","clip.cut","sequence.create","track.create","media.import_url","media.import_shelf","placeholder.fill_url"}.Contains(clone.op)){editForm=new InstructionCommonDialog(project,clone);}
   else if(raw){
    var form=new Form{Text="Editar instrucción JSON",Size=new Size(640,500),MinimumSize=new Size(480,320),StartPosition=FormStartPosition.CenterParent,BackColor=surface,ForeColor=ink};
     var compact=((Dictionary<string,object>)Store.Fields(clone)).Where(pair=>pair.Value!=null&&!(pair.Value is System.Collections.ICollection&&((System.Collections.ICollection)pair.Value).Count==0)).ToDictionary(pair=>pair.Key,pair=>pair.Value);
     var input=new TextBox{Multiline=true,AcceptsReturn=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill,Font=new Font("Consolas",10),BackColor=field,ForeColor=ink,Text=Store.Json().Serialize(compact)};
     var save=Button("Guardar tarjeta");save.Dock=DockStyle.Bottom;save.Height=34;form.Controls.Add(input);form.Controls.Add(save);save.Click+=(s,e)=>{try{clone=Instructions.ParseAction(input.Text);form.DialogResult=DialogResult.OK;form.Close();}catch(Exception ex){MessageBox.Show(ex.Message,"Instrucción",MessageBoxButtons.OK,MessageBoxIcon.Information);}};
     editForm=form;
   }else if(new[]{"effect.apply","effect.remove","transition.apply","transition.remove","clip.move","clip.trim","clip.duplicate","clip.inherit_track_effects"}.Contains(clone.op)){actionDialog=new InstructionActionDialog(project,selection,clone);editForm=actionDialog;}
   else editForm=new InstructionSchemaDialog08181(project,clone);
   var active=editForm;
   active.FormClosed+=(s,e)=>{if(active.DialogResult==DialogResult.OK&&document!=null&&index<document.actions.Count){document.actions[index]=clone;plan=null;reviewStatus.Text="Tarjeta editada · vuelve a revisar";RefreshCards();cards.SelectedIndex=index;}if(editForm==active){editForm=null;actionDialog=null;}active.Dispose();};
   active.Show(FindForm());
  }catch(Exception ex){Fail(ex);}}
 }
 public sealed class InstructionActionDialog:Form {
  readonly Project project;readonly InstructionAction action;readonly List<string> selection;readonly TableLayoutPanel rows=new TableLayoutPanel();readonly Label selectionInfo=new Label();
  ComboBox op,scope,clip,track,junction,edge,placement;TextBox time,duration,resourcePath;NumericUpDown frames;CheckBox ripple,value;
  readonly Color surface=Color.FromArgb(36,38,42),field=Color.FromArgb(28,30,34),ink=Color.WhiteSmoke;
  static ComboBox Combo(){return new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill,BackColor=Color.FromArgb(28,30,34),ForeColor=Color.WhiteSmoke,FlatStyle=FlatStyle.Flat};}
  void Row(string title,Control content){int index=rows.RowCount++;rows.RowStyles.Add(new RowStyle(SizeType.Absolute,33));var label=new Label{Text=title,TextAlign=ContentAlignment.MiddleLeft,Dock=DockStyle.Fill,ForeColor=ink};rows.Controls.Add(label,0,index);rows.Controls.Add(content,1,index);}
  static string ShowClip(Clip c){return c.name+"  ["+c.id.Substring(0,Math.Min(8,c.id.Length))+"]";}
  static void Fill(ComboBox box,IEnumerable<KeyValuePair<string,string>> values,string selected){foreach(var pair in values)box.Items.Add(new Choice(pair.Key,pair.Value));for(int i=0;i<box.Items.Count;i++)if(((Choice)box.Items[i]).Id==selected){box.SelectedIndex=i;break;}if(box.SelectedIndex<0&&!String.IsNullOrEmpty(selected)){box.Items.Insert(0,new Choice(selected,"[No encontrado] "+selected));box.SelectedIndex=0;}if(box.SelectedIndex<0&&box.Items.Count>0)box.SelectedIndex=0;}
  sealed class Choice {public readonly string Id,Label;public Choice(string id,string label){Id=id;Label=label;}public override string ToString(){return Label;}}
  static string Id(ComboBox box){return box.SelectedItem==null?null:((Choice)box.SelectedItem).Id;}
  public InstructionActionDialog(Project current,List<string> selected,InstructionAction item){project=current;action=item;selection=selected;Text="Editar instrucción";Size=new Size(610,420);MinimumSize=new Size(520,220);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;BackColor=surface;ForeColor=ink;Font=new Font("Segoe UI",9);
   rows.Dock=DockStyle.Fill;rows.ColumnCount=2;rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,185));rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));rows.AutoScroll=true;rows.Padding=new Padding(12,12,14,0);
   op=Combo();if(item.op.StartsWith("effect.")){Fill(op,new[]{new KeyValuePair<string,string>("effect.apply","Aplicar efecto"),new KeyValuePair<string,string>("effect.remove","Quitar efecto")},item.op);}else if(item.op.StartsWith("transition.")){Fill(op,new[]{new KeyValuePair<string,string>("transition.apply","Aplicar transición"),new KeyValuePair<string,string>("transition.remove","Quitar transición")},item.op);}else Fill(op,new[]{new KeyValuePair<string,string>(item.op,item.op)},item.op);Row("Acción",op);
   var effectContract=item.op.StartsWith("effect.")?EffectContracts.All.FirstOrDefault(c=>c.id==item.catalog_id):null;string effectKind=effectContract==null?null:effectContract.input_kind;
   scope=Combo();Fill(scope,item.op.StartsWith("transition.")?new[]{new KeyValuePair<string,string>("junction","Corte indicado"),new KeyValuePair<string,string>("sequence","Secuencia completa")}:new[]{new KeyValuePair<string,string>("selection","Medios seleccionados"),new KeyValuePair<string,string>("clip","Un clip"),new KeyValuePair<string,string>("track","Una pista"),new KeyValuePair<string,string>("sequence",effectKind==null?"Secuencia completa":"Todos los clips de "+(effectKind=="audio"?"audio":"vídeo"))},item.scope??"clip");Row("Alcance",scope);
   var seq=project.sequences.Find(s=>s.id==(item.sequence_id??project.active_sequence_id))??project.Active;
   var compatibleTracks=seq.tracks.Where(t=>effectKind==null||t.kind==effectKind).ToList();
   var compatibleClips=seq.clips.Where(c=>effectKind==null||compatibleTracks.Any(t=>t.id==c.track_id)).ToList();
   clip=Combo();string clipId=compatibleClips.Any(c=>c.id==item.clip_id)?item.clip_id:null;Fill(clip,compatibleClips.OrderBy(c=>c.timeline_start.Seconds).Select(c=>new KeyValuePair<string,string>(c.id,ShowClip(c))),clipId);if(effectKind!=null&&clipId==null)clip.SelectedIndex=-1;Row("Clip",clip);
   track=Combo();string trackId=compatibleTracks.Any(t=>t.id==item.track_id)?item.track_id:null;Fill(track,compatibleTracks.OrderBy(t=>t.kind).ThenBy(t=>t.order).Select(t=>new KeyValuePair<string,string>(t.id,t.name+" ("+t.kind+")")),trackId);if(effectKind!=null&&trackId==null)track.SelectedIndex=-1;Row("Pista",track);
   junction=Combo();var cuts=new List<KeyValuePair<string,string>>();foreach(var t in seq.tracks){var clips=seq.clips.Where(c=>c.track_id==t.id).OrderBy(c=>c.timeline_start.Seconds).ToList();for(int i=0;i+1<clips.Count;i++)if(Math.Abs(clips[i].End-clips[i+1].timeline_start.Seconds)<0.00001)cuts.Add(new KeyValuePair<string,string>(clips[i].id+"|"+clips[i+1].id,t.name+": "+clips[i].name+" → "+clips[i+1].name));}Fill(junction,cuts,item.left_clip_id==null||item.right_clip_id==null?null:item.left_clip_id+"|"+item.right_clip_id);Row("Corte",junction);
   time=new TextBox{Dock=DockStyle.Fill,BackColor=field,ForeColor=ink,Text=item.at_seconds.HasValue?TimeText(item.at_seconds.Value):"00:00:00:00"};Row("Posición HH:MM:SS:FF",time);
   duration=new TextBox{Dock=DockStyle.Fill,BackColor=field,ForeColor=ink,Text=item.duration_seconds.HasValue?TimeText(item.duration_seconds.Value):"00:00:05:00"};Row("Duración HH:MM:SS:FF",duration);
   frames=new NumericUpDown{Minimum=2,Maximum=600,Value=Math.Max(2,Math.Min(600,item.duration_frames??30)),Dock=DockStyle.Fill,BackColor=field,ForeColor=ink};Row("Transición (fotogramas)",frames);
   edge=Combo();Fill(edge,new[]{new KeyValuePair<string,string>("left","Izquierdo"),new KeyValuePair<string,string>("right","Derecho")},item.edge??"right");Row("Borde",edge);
   placement=Combo();Fill(placement,new[]{new KeyValuePair<string,string>("empty","Solo espacio libre"),new KeyValuePair<string,string>("overwrite","Sobrescribir"),new KeyValuePair<string,string>("insert","Insertar")},item.placement??"empty");Row("Destino ocupado",placement);
   ripple=new CheckBox{Text="Cerrar o abrir tiempo posterior",Checked=item.ripple==true,Dock=DockStyle.Fill,ForeColor=ink};Row("Rizo",ripple);
   value=new CheckBox{Text="Heredar efectos de pista",Checked=item.value!=false,Dock=DockStyle.Fill,ForeColor=ink};Row("Efectos de pista",value);
   var resourceRow=new Panel{Dock=DockStyle.Fill};resourcePath=new TextBox{Dock=DockStyle.Fill,BackColor=field,ForeColor=ink,Text=item.resource_path??""};var browse=new Button{Text="…",Dock=DockStyle.Right,Width=32};resourceRow.Controls.Add(resourcePath);resourceRow.Controls.Add(browse);Row("Archivo LUT (.cube)",resourceRow);browse.Click+=(s,e)=>{using(var dialog=new OpenFileDialog{Title="Elegir LUT 3D",Filter="LUT 3D (*.cube)|*.cube",CheckFileExists=true})if(dialog.ShowDialog(this)==DialogResult.OK)resourcePath.Text=dialog.FileName;};
   var bottom=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=47,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(8,6,8,0)};var save=new Button{Text="Guardar tarjeta",Width=120,DialogResult=DialogResult.None};var cancel=new Button{Text="Cancelar",Width=92,DialogResult=DialogResult.Cancel};selectionInfo.Width=170;selectionInfo.Height=25;selectionInfo.ForeColor=ink;selectionInfo.TextAlign=ContentAlignment.MiddleLeft;bottom.Controls.Add(save);bottom.Controls.Add(cancel);bottom.Controls.Add(selectionInfo);Controls.Add(rows);Controls.Add(bottom);CancelButton=cancel;UpdateSelection();
   op.SelectedIndexChanged+=(s,e)=>Visibility();scope.SelectedIndexChanged+=(s,e)=>Visibility();Visibility();save.Click+=(s,e)=>{try{Save();DialogResult=DialogResult.OK;Close();}catch(Exception ex){MessageBox.Show(ex.Message,"Instrucción",MessageBoxButtons.OK,MessageBoxIcon.Information);}};
  }
  public void UpdateSelection(){var contract=action.op.StartsWith("effect.")?EffectContracts.All.FirstOrDefault(c=>c.id==action.catalog_id):null;if(contract==null){selectionInfo.Text="Medios seleccionados: "+selection.Count;return;}var seq=project.sequences.Find(s=>s.id==(action.sequence_id??project.active_sequence_id))??project.Active;int count=selection.Count(id=>seq.clips.Any(c=>c.id==id&&seq.tracks.Any(t=>t.id==c.track_id&&t.kind==contract.input_kind)));selectionInfo.Text="Compatibles: "+count+" de "+selection.Count;}
  static string TimeText(double seconds){int frames=(int)Math.Round(seconds*60);int h=frames/(3600*60);frames%=3600*60;int m=frames/(60*60);frames%=60*60;int s=frames/60;return h.ToString("00")+":"+m.ToString("00")+":"+s.ToString("00")+":"+(frames%60).ToString("00");}
  static double ParseTime(string text){double value;if(MonitorPanelView.TryParseTimecode(text,out value))return value;if(Double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&value>=0)return value;throw new Exception("Usa HH:MM:SS:FF o segundos decimales.");}
  void Visibility(){string name=Id(op),sc=Id(scope);bool effect=name!=null&&name.StartsWith("effect."),transition=name!=null&&name.StartsWith("transition.");
   foreach(Control c in rows.Controls){int row=rows.GetRow(c);bool show=row==0||row==1&&(effect||transition)||row==2&&(!effect&&!transition||sc=="clip")||row==3&&(sc=="track"||name=="clip.move"||name=="clip.duplicate")||row==4&&transition&&sc=="junction"||row==5&&(name=="clip.move"||name=="clip.duplicate")||row==6&&name=="clip.trim"||row==7&&name=="transition.apply"||row==8&&name=="clip.trim"||row==9&&(name=="clip.move"||name=="clip.duplicate")||row==10&&name=="clip.trim"||row==11&&name=="clip.inherit_track_effects"||row==12&&effect&&action.catalog_id==EffectContracts.Lut3DId; c.Visible=show;rows.RowStyles[row].Height=show?33:0;}
   Height=Math.Max(220,105+rows.RowStyles.Cast<RowStyle>().Count(style=>style.Height>0)*33);
  }
  void Save(){action.op=Id(op);if(action.op.StartsWith("effect.")||action.op.StartsWith("transition."))action.scope=Id(scope);action.clip_id=Id(clip);action.track_id=Id(track);
   if(action.op.StartsWith("effect.")&&action.scope=="clip"&&action.clip_id==null)throw new Exception("Selecciona un clip compatible con el efecto.");
   if(action.op.StartsWith("effect.")&&action.scope=="track"&&action.track_id==null)throw new Exception("Selecciona una pista compatible con el efecto.");
   if(action.scope=="selection"){action.clip_ids=selection.ToList();if(action.clip_ids.Count==0)throw new Exception("Selecciona clips en la línea de tiempo antes de guardar esta tarjeta.");}
   if(action.scope=="junction"){var id=Id(junction);if(id==null)throw new Exception("No hay cortes contiguos en esta secuencia.");var parts=id.Split('|');action.left_clip_id=parts[0];action.right_clip_id=parts[1];}
   if(action.op=="clip.move"||action.op=="clip.duplicate"){action.at_seconds=ParseTime(time.Text);action.placement=Id(placement);if(action.op=="clip.duplicate"&&action.placement=="insert")throw new Exception("Duplicar solo admite espacio libre o sobrescribir.");}
   if(action.op=="clip.trim"){action.duration_seconds=ParseTime(duration.Text);action.edge=Id(edge);action.ripple=ripple.Checked;}
   if(action.op=="transition.apply")action.duration_frames=(int)frames.Value;
   if(action.op=="clip.inherit_track_effects")action.value=value.Checked;
   if(action.op=="effect.apply"&&action.catalog_id==EffectContracts.Lut3DId){if(!File.Exists(resourcePath.Text)||!String.Equals(Path.GetExtension(resourcePath.Text),".cube",StringComparison.OrdinalIgnoreCase))throw new Exception("Selecciona un archivo .cube válido.");FFmpegEffectAdapter.VerifyLutFile(resourcePath.Text);action.resource_path=Path.GetFullPath(resourcePath.Text);}
  }
 }
}
