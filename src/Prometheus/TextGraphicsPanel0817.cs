using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public sealed class TextGraphicsPanel0817:Panel {
  sealed class Row {public string Id,Label;public override string ToString(){return Label;}}
  readonly ToolTip hints=new ToolTip();readonly bool subtitlesOnly;readonly ListBox list=new ListBox();readonly Label note=new Label();readonly TextBox search=new TextBox();readonly ComboBox mode=new ComboBox();Button animationButton;bool updating;Project project;
  public bool WordMode{get{return subtitlesOnly&&mode.SelectedIndex==1;}}
  public Action AddTitleRequested,AddSubtitleRequested,ImportSubtitleRequested,LoadEmbeddedRequested,GenerateTranscriptRequested,AnimationRequested,AnimationRemoveRequested;public Action<string> SelectedRequested,EditRequested,RemoveSubtitleRequested;
  public TextGraphicsPanel0817(bool subtitlesOnly){this.subtitlesOnly=subtitlesOnly;Dock=DockStyle.Fill;BackColor=Color.FromArgb(36,38,42);ForeColor=Color.Gainsboro;
   var actions=new FlowLayoutPanel{Dock=DockStyle.Top,Height=subtitlesOnly?75:42,Padding=new Padding(8,6,0,0),WrapContents=subtitlesOnly};
   if(!subtitlesOnly)actions.Controls.Add(Button("+ Texto",()=>{if(AddTitleRequested!=null)AddTitleRequested();}));
   actions.Controls.Add(Button("+ Subtítulo",()=>{if(AddSubtitleRequested!=null)AddSubtitleRequested();}));
   if(subtitlesOnly){mode.Width=90;mode.Height=29;mode.DropDownStyle=ComboBoxStyle.DropDownList;mode.BackColor=Color.FromArgb(43,47,53);mode.ForeColor=Color.WhiteSmoke;mode.Items.AddRange(new object[]{"Frases","Palabras"});mode.SelectedIndex=0;actions.Controls.Add(mode);actions.Controls.Add(Button("Importar",()=>{if(ImportSubtitleRequested!=null)ImportSubtitleRequested();}));actions.Controls.Add(Button("Del medio",()=>{if(LoadEmbeddedRequested!=null)LoadEmbeddedRequested();}));actions.Controls.Add(Button("Transcribir",()=>{if(GenerateTranscriptRequested!=null)GenerateTranscriptRequested();}));animationButton=Button("Animación MGT",()=>{if(AnimationRequested!=null)AnimationRequested();});var animationMenu=new ContextMenuStrip();animationMenu.Items.Add("Quitar animación MGT",null,(s,e)=>{if(AnimationRemoveRequested!=null)AnimationRemoveRequested();});animationButton.ContextMenuStrip=animationMenu;actions.Controls.Add(animationButton);}
   actions.Controls.Add(Button("Editar",()=>{string id=SelectedId;if(id!=null&&EditRequested!=null)EditRequested(id);}));
   if(subtitlesOnly)actions.Controls.Add(Button("Quitar",()=>{string id=SelectedId;if(id!=null&&RemoveSubtitleRequested!=null)RemoveSubtitleRequested(id);}));
   note.Dock=DockStyle.Bottom;note.Height=50;note.Padding=new Padding(11,5,11,0);note.ForeColor=Color.FromArgb(165,172,180);note.Text=subtitlesOnly?"Seleccionar: Supr quita el subtítulo. Tijera: selecciona el intervalo y Supr lo elimina del montaje.":"Selecciona un texto para editar sus propiedades en Controles de efectos.";
   list.Dock=DockStyle.Fill;list.BackColor=Color.FromArgb(29,31,35);list.ForeColor=ForeColor;list.BorderStyle=BorderStyle.None;list.IntegralHeight=false;list.Font=new Font("Segoe UI",9);list.SelectedIndexChanged+=(s,e)=>{if(!updating&&SelectedRequested!=null)SelectedRequested(SelectedId);};list.DoubleClick+=(s,e)=>{if(SelectedId!=null&&EditRequested!=null)EditRequested(SelectedId);};
   search.Dock=DockStyle.Top;search.Height=28;search.BackColor=Color.FromArgb(29,31,35);search.ForeColor=Color.WhiteSmoke;search.BorderStyle=BorderStyle.FixedSingle;hints.SetToolTip(search,"Buscar texto en la pista");search.TextChanged+=(s,e)=>SetProject(project);
   Controls.Add(list);Controls.Add(note);Controls.Add(search);Controls.Add(actions);
  }
  static Button Button(string text,Action click){var button=new Button{Text=text,AutoSize=true,Height=29,Margin=new Padding(2,0,2,0),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(51,54,59),ForeColor=Color.Gainsboro};button.FlatAppearance.BorderColor=Color.FromArgb(78,82,89);button.Click+=(s,e)=>click();return button;}
  public string SelectedId{get{return list.SelectedItem==null?null:((Row)list.SelectedItem).Id;}}
  public void SetAnimationActive(bool active){if(animationButton==null)return;animationButton.Text=active?"Reemplazar MGT":"Animación MGT";animationButton.FlatAppearance.BorderColor=active?Color.FromArgb(255,156,44):Color.FromArgb(78,82,89);animationButton.ForeColor=active?Color.FromArgb(255,156,44):Color.Gainsboro;}
  public void Select(string clipId){updating=true;try{for(int i=0;i<list.Items.Count;i++)if(((Row)list.Items[i]).Id==clipId){list.SelectedIndex=i;return;}list.ClearSelected();}finally{updating=false;}}
  public void SetProject(Project value){project=value;string previous=SelectedId;updating=true;list.BeginUpdate();try{list.Items.Clear();if(project!=null)foreach(var clip in project.Active.clips.OrderBy(c=>c.timeline_start.Seconds)){
    var asset=project.assets.FirstOrDefault(a=>a.id==clip.asset_id);if(!GeneratedMedia.IsText(asset)||subtitlesOnly&&!SubtitleTrack0817.IsCue(project,clip))continue;
    string label=Timeline.TimeLabel(clip.timeline_start.Seconds)+"  "+(GeneratedMedia.IsSubtitle(asset)?"[Sub] ":"")+GeneratedMedia.Text(asset).Replace('\n',' ');if(search.Text.Length>0&&label.IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)<0)continue;if(label.Length>125)label=label.Substring(0,125)+"…";
    list.Items.Add(new Row{Id=clip.id,Label=label});
   }Select(previous);}finally{list.EndUpdate();updating=false;}}
  protected override void Dispose(bool disposing){if(disposing)hints.Dispose();base.Dispose(disposing);}
 }
}
