using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Prometheus {
 public sealed partial class MonitorPanelView {
  TextBox timeEditor;
  Panel timeHost;
  public Action<double> SeekRequested;

  Panel BuildTimecodeControl(){
   timeHost=new Panel{Width=108,Height=30,Margin=Padding.Empty,BackColor=barColor};
   TimeLabel=new Label{Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Cursor=Cursors.IBeam,ForeColor=ink};
   timeEditor=new TextBox{Dock=DockStyle.Fill,Visible=false,BorderStyle=BorderStyle.FixedSingle,BackColor=surface,ForeColor=ink,TextAlign=HorizontalAlignment.Center};
   TimeLabel.Click+=(s,e)=>BeginTimecodeEdit();
   timeEditor.KeyDown+=(s,e)=>{
    if(e.KeyCode==Keys.Escape){EndTimecodeEdit();e.Handled=true;e.SuppressKeyPress=true;}
    else if(e.KeyCode==Keys.Enter){
     double seconds;
     if(TryParseTimecode(timeEditor.Text,out seconds)){
      EndTimecodeEdit();if(SeekRequested!=null)SeekRequested(seconds);
     }else{timeEditor.BackColor=Color.FromArgb(95,47,49);help.SetToolTip(timeEditor,"Usa HH:MM:SS:FF; los fotogramas van de 00 a 59");}
     e.Handled=true;e.SuppressKeyPress=true;
    }
   };
   timeEditor.TextChanged+=(s,e)=>timeEditor.BackColor=surface;
   timeEditor.LostFocus+=(s,e)=>{if(timeEditor.Visible)EndTimecodeEdit();};
   timeHost.Controls.Add(TimeLabel);timeHost.Controls.Add(timeEditor);
   help.SetToolTip(TimeLabel,"Haz clic para ir a un fotograma exacto (HH:MM:SS:FF)");
   return timeHost;
  }

  void BeginTimecodeEdit(){
   timeEditor.Text=TimeLabel.Text;
   timeEditor.Visible=true;
   timeEditor.BringToFront();
   timeEditor.Focus();timeEditor.SelectAll();
  }
  void EndTimecodeEdit(){timeEditor.Visible=false;TimeLabel.Focus();}

  public static bool TryParseTimecode(string value,out double seconds){
   seconds=0;
   if(String.IsNullOrWhiteSpace(value))return false;
   var parts=value.Trim().Split(':');
   if(parts.Length!=4)return false;
   long hours,minutes,wholeSeconds,frames;
   if(!Int64.TryParse(parts[0],NumberStyles.None,CultureInfo.InvariantCulture,out hours)||
      !Int64.TryParse(parts[1],NumberStyles.None,CultureInfo.InvariantCulture,out minutes)||
      !Int64.TryParse(parts[2],NumberStyles.None,CultureInfo.InvariantCulture,out wholeSeconds)||
      !Int64.TryParse(parts[3],NumberStyles.None,CultureInfo.InvariantCulture,out frames))return false;
   if(hours>100000||minutes>59||wholeSeconds>59||frames>59)return false;
   seconds=hours*3600+minutes*60+wholeSeconds+frames/60.0;
   return true;
  }
 }

 public sealed class TimelineTimecode:Panel {
  readonly Label value;
  readonly TextBox editor;
  public Action<double> SeekRequested;
  public TimelineTimecode(){
   Width=120;Height=30;Margin=new Padding(0,0,8,0);BackColor=Color.FromArgb(27,28,31);
   value=new Label{Dock=DockStyle.Fill,Text="00:00:00:00",ForeColor=Color.FromArgb(255,183,95),TextAlign=ContentAlignment.MiddleCenter,Cursor=Cursors.IBeam};
   editor=new TextBox{Dock=DockStyle.Fill,Visible=false,BackColor=Color.FromArgb(27,28,31),ForeColor=Color.White,BorderStyle=BorderStyle.FixedSingle,TextAlign=HorizontalAlignment.Center};
   value.Click+=(s,e)=>{editor.Text=value.Text;editor.Visible=true;editor.BringToFront();editor.Focus();editor.SelectAll();};
   editor.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape){editor.Visible=false;e.Handled=true;e.SuppressKeyPress=true;}else if(e.KeyCode==Keys.Enter){double seconds;if(MonitorPanelView.TryParseTimecode(editor.Text,out seconds)){editor.Visible=false;if(SeekRequested!=null)SeekRequested(seconds);}else editor.BackColor=Color.FromArgb(95,47,49);e.Handled=true;e.SuppressKeyPress=true;}};
   editor.TextChanged+=(s,e)=>editor.BackColor=Color.FromArgb(27,28,31);
   editor.LostFocus+=(s,e)=>editor.Visible=false;
   Controls.Add(value);Controls.Add(editor);
  }
  public void SetTime(double seconds){value.Text=Timeline.TimeLabel(seconds);}
 }
}
