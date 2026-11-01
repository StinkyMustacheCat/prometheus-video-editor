using System;
using System.Drawing;
using System.Windows.Forms;

namespace Prometheus {
 public sealed class LinkImportDialog:Form {
  readonly TextBox address=new TextBox();
  readonly MaskedTextBox from=new MaskedTextBox(),to=new MaskedTextBox();
  readonly ComboBox rangeMode=new ComboBox();
  readonly Label fromLabel=new Label(),toLabel=new Label();
  readonly Panel range=new Panel();
  readonly Button toggle=new Button(),accept=new Button();
  bool expanded;
  public string Link{get;private set;}
  public double? RangeIn{get;private set;}
  public double? RangeOut{get;private set;}
  public bool RangeExpanded{get{return expanded;}}
  public string PrefillUrl{set{address.Text=value??"";address.SelectionStart=address.TextLength;}}
  public LinkImportDialog(bool allowRange,bool audioOnly,Color background,Color foreground){
   Text="Importar enlace";ClientSize=new Size(540,152);FormBorderStyle=FormBorderStyle.FixedDialog;
   StartPosition=FormStartPosition.CenterParent;MaximizeBox=false;MinimizeBox=false;BackColor=background;ForeColor=foreground;
   Controls.Add(new Label{Text=audioOnly?"Pega un enlace público para importar su audio":"Pega un enlace público de vídeo compatible",Left=18,Top=12,Width=505,Height=23});
   address.SetBounds(18,40,504,25);address.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;
   Controls.Add(address);
   if(allowRange){
    toggle.Text="▸ Recortar el medio (opcional)";toggle.TextAlign=ContentAlignment.MiddleLeft;
    toggle.SetBounds(18,76,280,27);toggle.FlatStyle=FlatStyle.Flat;toggle.FlatAppearance.BorderSize=0;
    toggle.Click+=(s,e)=>SetRangeExpanded(!expanded);Controls.Add(toggle);
    range.SetBounds(18,106,504,122);range.Visible=false;
    rangeMode.DropDownStyle=ComboBoxStyle.DropDownList;rangeMode.SetBounds(0,0,495,25);rangeMode.Items.AddRange(new object[]{"Empezar aquí y usar el resto","Empezar aquí y usar esta duración","Entre estos dos puntos","Desde el comienzo hasta aquí"});rangeMode.SelectedIndex=0;rangeMode.SelectedIndexChanged+=(s,e)=>RefreshRangeMode();range.Controls.Add(rangeMode);
    fromLabel.SetBounds(0,31,235,20);toLabel.SetBounds(255,31,235,20);range.Controls.Add(fromLabel);range.Controls.Add(toLabel);
    from.Mask=to.Mask="00:00:00";from.PromptChar=to.PromptChar='0';from.TextMaskFormat=to.TextMaskFormat=MaskFormat.IncludeLiterals;
    from.SetBounds(0,54,240,25);from.Text="00:00:00";to.SetBounds(255,54,240,25);to.Text="00:00:00";to.Enter+=(s,e)=>to.SelectAll();
    range.Controls.Add(from);range.Controls.Add(to);
    range.Controls.Add(new Label{Text="El tramo se comprobará al analizar el enlace.",Left=0,Top=87,Width=500,Height=23,ForeColor=Color.FromArgb(171,175,182)});RefreshRangeMode();
    Controls.Add(range);
   }
   accept.Text="Importar";accept.SetBounds(340,112,86,29);accept.Click+=(s,e)=>Accept();Controls.Add(accept);
   var cancel=new Button{Text="Cancelar",Left=434,Top=112,Width=88,Height=29,DialogResult=DialogResult.Cancel};Controls.Add(cancel);
   AcceptButton=accept;CancelButton=cancel;
  }
  void SetRangeExpanded(bool value){expanded=value;range.Visible=value;toggle.Text=value?"▾ Recortar el medio":"▸ Recortar el medio (opcional)";ClientSize=new Size(540,value?274:152);accept.Top=value?234:112;CancelButtonControl.Top=accept.Top;if(value)(rangeMode.SelectedIndex==3?to:from).Focus();}
  void RefreshRangeMode(){int mode=rangeMode.SelectedIndex;bool hasFrom=mode!=3,hasTo=mode!=0;from.Visible=fromLabel.Visible=hasFrom;to.Visible=toLabel.Visible=hasTo;fromLabel.Text="Empezar en (HH:MM:SS)";toLabel.Text=mode==1?"Duración (HH:MM:SS)":"Terminar en (HH:MM:SS)";toLabel.Left=to.Left=hasFrom?255:0;}
  Control CancelButtonControl{get{return (Control)CancelButton;}}
  bool ReadSelection(out double? begin,out double? end){
   begin=end=null;
   if(!expanded)return true;
   double start,finish;
   int mode=rangeMode.SelectedIndex;
   if(mode==3)start=0;else if(!RangeUi.TryParseTime(from.Text,out start))return false;
   if(start<0||start>48*3600)return false;
   begin=Rational.Frame(start).Seconds;
   if(mode==0){end=null;return true;}
   if(!RangeUi.TryParseTime(to.Text,out finish))return false;
   if(mode==1)finish+=begin.Value;
   if(finish-begin.Value<1.0/60||finish>48*3600)return false;
   end=Rational.Frame(finish).Seconds;return end.Value-begin.Value>=1.0/60-0.000001;
  }
  void Accept(){
   string url=address.Text.Trim();Uri uri;
   if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!="http"&&uri.Scheme!="https"){
    MessageBox.Show(this,"Pega un enlace HTTP o HTTPS válido.","Enlace inválido",MessageBoxButtons.OK,MessageBoxIcon.Information);return;
   }
   double? begin,end;
   if(!ReadSelection(out begin,out end)){
    MessageBox.Show(this,"Revisa los tiempos del tramo. La duración debe ser positiva y no superar 48 horas.","Tramo inválido",MessageBoxButtons.OK,MessageBoxIcon.Information);return;
   }
   Link=url;RangeIn=begin;RangeOut=end;DialogResult=DialogResult.OK;Close();
  }
 }
}
