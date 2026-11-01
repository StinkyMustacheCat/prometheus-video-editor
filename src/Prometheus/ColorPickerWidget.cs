using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Prometheus {
 // Shared color input for generated media and effect parameters.
 public sealed class ColorPickerWidget:Panel {
  readonly Label caption=new Label();
  readonly TextBox hex=new TextBox();
  readonly Button swatch=new Button(),palette=new Button(),eyedropper=new Button(),reset=new Button();
  readonly ToolTip hints=new ToolTip();
  Color selected;
  bool changing;
  public Action<Color> ColorChanged;
  public Action ResetRequested;
  Action<Action<Color>> sampleRequested;
  public Action<Action<Color>> SampleRequested{get{return sampleRequested;}set{sampleRequested=value;eyedropper.Visible=value!=null;LayoutParts();}}
  public Color SelectedColor{get{return selected;}}
  public ColorPickerWidget(string title,Color initial){
   Width=214;Height=61;BackColor=Color.FromArgb(36,38,42);selected=initial;
   caption.Text=title;caption.ForeColor=Color.FromArgb(225,227,230);caption.TextAlign=ContentAlignment.MiddleLeft;caption.AutoEllipsis=true;
   hex.BorderStyle=BorderStyle.FixedSingle;hex.BackColor=Color.FromArgb(27,28,31);hex.ForeColor=Color.FromArgb(235,236,238);hex.TextAlign=HorizontalAlignment.Center;
   SetupButton(swatch);SetupButton(palette);SetupButton(eyedropper);SetupButton(reset);
   palette.Text="Paleta…";eyedropper.Text="⌖";eyedropper.Visible=false;reset.Text="↶";reset.Visible=false;
   hints.SetToolTip(swatch,"Color actual · abrir paleta");hints.SetToolTip(palette,"Elegir color en una paleta");hints.SetToolTip(eyedropper,"Tomar un color visible en la pantalla");hints.SetToolTip(reset,"Restablecer este color");
   Controls.Add(caption);Controls.Add(hex);Controls.Add(swatch);Controls.Add(palette);Controls.Add(eyedropper);Controls.Add(reset);
   Resize+=(s,e)=>LayoutParts();
   swatch.Click+=(s,e)=>ChoosePalette();palette.Click+=(s,e)=>ChoosePalette();eyedropper.Click+=(s,e)=>ChooseScreenColor();
   reset.Click+=(s,e)=>{if(ResetRequested!=null)ResetRequested();};
   hex.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){ApplyHex();e.SuppressKeyPress=true;}else if(e.KeyCode==Keys.Escape){hex.Text=Format(selected);e.SuppressKeyPress=true;}};
   hex.Leave+=(s,e)=>ApplyHex();
   RefreshColor(false);LayoutParts();
  }
  static void SetupButton(Button button){button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderColor=Color.FromArgb(76,79,85);button.BackColor=Color.FromArgb(50,52,57);button.ForeColor=Color.WhiteSmoke;button.Font=new Font("Segoe UI",8);}
  static string Format(Color color){return "#"+color.R.ToString("X2")+color.G.ToString("X2")+color.B.ToString("X2");}
  public void EnableReset(Action action){ResetRequested=action;reset.Visible=action!=null;LayoutParts();}
  void LayoutParts(){
   int resetWidth=reset.Visible?25:0;
   caption.SetBounds(0,0,Math.Max(62,Width-103-resetWidth),25);
   hex.SetBounds(Math.Max(64,Width-99-resetWidth),2,73,23);
   reset.SetBounds(Width-24,2,23,23);
   int available=Math.Max(125,Width-2),pickerWidth=eyedropper.Visible?29:0;
   swatch.SetBounds(0,30,42,26);
   palette.SetBounds(47,30,Math.Max(55,available-47-pickerWidth),26);
   eyedropper.SetBounds(available-pickerWidth,30,pickerWidth,26);
  }
  void RefreshColor(bool notify){
   changing=true;hex.Text=Format(selected);swatch.BackColor=selected;swatch.ForeColor=selected.GetBrightness()>0.65f?Color.Black:Color.White;changing=false;
   if(notify&&ColorChanged!=null)ColorChanged(selected);
  }
  public void SetColor(Color color,bool notify){if(color.ToArgb()==selected.ToArgb()){hex.Text=Format(selected);return;}selected=Color.FromArgb(color.R,color.G,color.B);RefreshColor(notify);}
  void ApplyHex(){
   if(changing)return;string value=hex.Text.Trim();if(!Regex.IsMatch(value,@"^#?[0-9a-fA-F]{6}$")){hex.Text=Format(selected);return;}
   if(value[0]!='#')value="#"+value;
   SetColor(ColorTranslator.FromHtml(value),true);
  }
  void ChoosePalette(){using(var picker=new ColorDialog{Color=selected,FullOpen=false})if(picker.ShowDialog(FindForm())==DialogResult.OK)SetColor(picker.Color,true);}
  void ChooseScreenColor(){if(sampleRequested!=null)sampleRequested(color=>{if(!IsDisposed)SetColor(color,true);});}
  protected override void Dispose(bool disposing){if(disposing)hints.Dispose();base.Dispose(disposing);}
 }

}
