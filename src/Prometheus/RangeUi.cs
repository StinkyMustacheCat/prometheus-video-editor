using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public static class RangeUi {
  public static double Duration(Asset asset){return asset.streams.Where(x=>x.duration!=null).Select(x=>x.duration.Seconds).DefaultIfEmpty(0).Max();}
  static string Label(double seconds){var time=TimeSpan.FromSeconds(seconds);return String.Format(System.Globalization.CultureInfo.InvariantCulture,"{0:00}:{1:00}:{2:00.###}",(int)time.TotalHours,time.Minutes,time.TotalSeconds%60);}
  static bool Parse(string text,out double seconds){seconds=0;var parts=text.Trim().Split(':');if(parts.Length<2||parts.Length>3)return false;double value;int hours=0,minutes=0;if(parts.Length==3){if(!Int32.TryParse(parts[0],out hours))return false;if(!Int32.TryParse(parts[1],out minutes))return false;}else if(!Int32.TryParse(parts[0],out minutes))return false;if(!Double.TryParse(parts[parts.Length-1].Replace(',','.'),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out value))return false;if(hours<0||minutes<0||minutes>=60&&parts.Length==3||value<0||value>=60)return false;seconds=hours*3600+minutes*60+value;return true;}
  public static bool TryParseTime(string text,out double seconds){return Parse(text,out seconds);}
  public static bool Choose(Form owner,Asset asset,ref double? begin,ref double? end){
   double limit=Duration(asset);if(limit<=0)throw new Exception("El medio no tiene duración conocida");
   double? chosenBegin=begin,chosenEnd=end;
   using(var dialog=new Form{Text="Elegir tramo · "+asset.name,Width=405,Height=205,FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,BackColor=Color.FromArgb(36,38,42),ForeColor=Color.FromArgb(230,232,235)}){
    var note=new Label{Text="Duración: "+Label(limit)+" · escribe HH:MM:SS",Left=17,Top=12,Width=355,Height=24};
    var fromLabel=new Label{Text="Desde",Left=17,Top=46,Width=75,Height=25};var toLabel=new Label{Text="Hasta",Left=203,Top=46,Width=75,Height=25};
    var from=new TextBox{Text=Label(begin??0),Left=17,Top=71,Width=170};var to=new TextBox{Text=Label(end??limit),Left=203,Top=71,Width=170};
    var all=new Button{Text="Todo",Left=17,Top=112,Width=76,Height=29};all.Click+=(s,e)=>{from.Text=Label(0);to.Text=Label(limit);};
    var ok=new Button{Text="Aplicar",Left=201,Top=112,Width=82,Height=29};var cancel=new Button{Text="Cancelar",Left=291,Top=112,Width=82,Height=29,DialogResult=DialogResult.Cancel};
    ok.Click+=(s,e)=>{double start,finish;if(!Parse(from.Text,out start)||!Parse(to.Text,out finish)||start<0||finish>limit+0.001||finish-start<1.0/60){MessageBox.Show(dialog,"El tramo debe estar dentro del medio y durar al menos un fotograma.","Tramo inválido",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}if(start<0.001&&finish>=limit-0.001){chosenBegin=null;chosenEnd=null;}else{chosenBegin=Rational.Frame(start).Seconds;chosenEnd=Rational.Frame(Math.Min(finish,Math.Floor(limit*60)/60)).Seconds;}dialog.DialogResult=DialogResult.OK;dialog.Close();};
    dialog.Controls.AddRange(new Control[]{note,fromLabel,toLabel,from,to,all,ok,cancel});dialog.AcceptButton=ok;dialog.CancelButton=cancel;
    if(dialog.ShowDialog(owner)!=DialogResult.OK)return false;
    begin=chosenBegin;end=chosenEnd;return true;
   }
  }
 }
}
