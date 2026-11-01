using System;
using System.Diagnostics;
using System.IO;
using System.Drawing;
using System.Windows.Forms;

namespace Prometheus {
 public enum WebFailureChoice {Retry,Continue,Cancel}
 public sealed class ExportProgressWindow:Form {
  readonly Label mediaText=new Label(),renderText=new Label(),detail=new Label();
  readonly ExportProgressBar mediaBar=new ExportProgressBar(),renderBar=new ExportProgressBar();
  readonly Button action=new Button(),openFile=new Button(),openFolder=new Button(),retryMedia=new Button(),continueMedia=new Button(),pendingDetails=new Button();
  readonly string outputPath;
  readonly Stopwatch renderClock=new Stopwatch();
  bool finished,cancelling,trackingMedia;int mediaDone,mediaTotal;string currentMedia="";double smoothedRemaining=-1;Action<WebFailureChoice> mediaDecision;string pendingFailures;
  public event Action CancelRequested;
  public ExportProgressWindow(string output){
   outputPath=output;Text="Exportando Â· Prometheus";ClientSize=new Size(570,275);MinimumSize=new Size(570,275);FormBorderStyle=FormBorderStyle.FixedDialog;StartPosition=FormStartPosition.CenterParent;MaximizeBox=false;MinimizeBox=false;BackColor=Color.FromArgb(36,38,42);ForeColor=Color.FromArgb(225,227,230);Font=new Font("Segoe UI",9);
   Controls.Add(new Label{Text="Preparando exportaciÃ³n",Left=20,Top=15,Width=520,Height=31,Font=new Font("Segoe UI",15,FontStyle.Bold)});
   mediaText.SetBounds(20,60,530,26);mediaText.Text="Comprobando mediosâ€¦";mediaText.AutoEllipsis=true;Controls.Add(mediaText);
   mediaBar.SetBounds(20,87,530,16);mediaBar.Indeterminate=true;Controls.Add(mediaBar);
   renderText.SetBounds(20,121,530,26);renderText.Text="Render Â· pendiente";Controls.Add(renderText);
   renderBar.SetBounds(20,148,530,16);Controls.Add(renderBar);
   detail.SetBounds(20,182,530,39);detail.ForeColor=Color.FromArgb(171,176,183);detail.Text="Salida: "+output;detail.AutoEllipsis=true;Controls.Add(detail);
   openFile.Text="Ver resultado";openFile.SetBounds(20,229,110,31);openFile.Enabled=false;openFile.Click+=(s,e)=>{if(File.Exists(outputPath))Process.Start(new ProcessStartInfo(outputPath){UseShellExecute=true});};Controls.Add(openFile);
   openFolder.Text="Ver en carpeta";openFolder.SetBounds(138,229,120,31);openFolder.Enabled=false;openFolder.Click+=(s,e)=>{if(File.Exists(outputPath))Process.Start("explorer.exe","/select,\""+outputPath+"\"");};Controls.Add(openFolder);
   StyleResultButton(openFile);StyleResultButton(openFolder);
   retryMedia.Text="Reintentar";retryMedia.SetBounds(20,229,110,31);retryMedia.Visible=false;retryMedia.Click+=(s,e)=>ChooseMedia(WebFailureChoice.Retry);Controls.Add(retryMedia);
   continueMedia.Text="Seguir con el resto";continueMedia.SetBounds(138,229,140,31);continueMedia.Visible=false;continueMedia.Click+=(s,e)=>ChooseMedia(WebFailureChoice.Continue);Controls.Add(continueMedia);
   pendingDetails.Text="Ver pendientes";pendingDetails.SetBounds(20,229,120,31);pendingDetails.Visible=false;pendingDetails.Click+=(s,e)=>MessageBox.Show(this,pendingFailures??"","Medios web pendientes",MessageBoxButtons.OK,MessageBoxIcon.Information);Controls.Add(pendingDetails);
   action.Text="Cancelar";action.SetBounds(440,229,110,31);action.FlatStyle=FlatStyle.Flat;action.BackColor=Color.FromArgb(49,51,56);action.ForeColor=ForeColor;action.Click+=(s,e)=>{if(finished)Close();else RequestCancel();};Controls.Add(action);
   FormClosing+=(s,e)=>{if(!finished){e.Cancel=true;RequestCancel();}};
  }
  static void StyleResultButton(Button button){button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderSize=1;Action update=()=>{button.BackColor=Color.FromArgb(43,45,49);button.ForeColor=button.Enabled?Color.FromArgb(220,224,228):Color.FromArgb(135,140,146);button.FlatAppearance.BorderColor=button.Enabled?Color.FromArgb(85,89,95):Color.FromArgb(62,65,70);};button.EnabledChanged+=(s,e)=>update();update();}
  void RequestCancel(){if(cancelling)return;cancelling=true;action.Enabled=false;detail.Text="Cancelando la operaciÃ³nâ€¦";if(CancelRequested!=null)CancelRequested();}
  public void MediaProgress(int completed,int total,string name){if(finished)return;trackingMedia=total>0;mediaDone=completed;mediaTotal=total;currentMedia=name??"";if(trackingMedia){mediaBar.Value=(int)Math.Round(completed*100.0/total);mediaBar.Indeterminate=completed<total;mediaText.Text="Medios "+completed+" de "+total+(currentMedia.Length>0?" Â· "+currentMedia:"");}else MediaComplete();}
  public void MediaStatus(string message){if(finished)return;if(!trackingMedia)mediaBar.Indeterminate=true;mediaText.Text=trackingMedia?"Medios "+mediaDone+" de "+mediaTotal+" Â· "+currentMedia+" Â· "+message:message;}
  void ChooseMedia(WebFailureChoice choice){var callback=mediaDecision;ClearMediaFailure();if(callback!=null)callback(choice);}
  public void PromptMediaFailure(string name,string error,Action<WebFailureChoice> decision){if(finished)return;mediaDecision=decision;mediaText.Text="No se pudo preparar: "+name;detail.Text=error;retryMedia.Visible=continueMedia.Visible=true;openFile.Visible=openFolder.Visible=false;}
  public void ClearMediaFailure(){mediaDecision=null;retryMedia.Visible=continueMedia.Visible=false;openFile.Visible=openFolder.Visible=true;}
  public void FailedMedia(System.Collections.Generic.IEnumerable<string> failures){ClearMediaFailure();pendingFailures=String.Join(Environment.NewLine+Environment.NewLine,failures);openFile.Visible=openFolder.Visible=false;pendingDetails.Visible=true;Text="Medios web pendientes Â· Prometheus";renderText.Text="Render Â· no iniciado";End("Quedaron medios web pendientes. Los demÃ¡s medios estÃ¡n preparados.");}
  public void MediaComplete(){mediaBar.Indeterminate=false;mediaBar.Value=100;mediaText.Text="Medios listos";}
  string encoderMode="";
  public void BeginRender(string mode=""){
   encoderMode=mode??"";
   MediaComplete();renderClock.Restart();
   string prefix=encoderMode.Length>0?"Render ["+encoderMode+"]":"Render";
   renderText.Text=prefix+" · iniciando…";
   if(encoderMode.Length>0)Text="Exportando ["+encoderMode+"] · Prometheus";
  }
  public void Render(double percent){
   if(finished)return;int value=Math.Max(renderBar.Value,Math.Min(100,(int)percent));renderBar.Value=value;
   string eta="";if(value>=5&&value<99&&renderClock.Elapsed.TotalSeconds>=6){double candidate=renderClock.Elapsed.TotalSeconds*(100.0/value-1);smoothedRemaining=smoothedRemaining<0?candidate:smoothedRemaining*0.8+candidate*0.2;eta=" · quedan aprox. "+TimeSpan.FromSeconds(smoothedRemaining).ToString(smoothedRemaining>=3600?@"h\:mm\:ss":@"mm\:ss");}
   string prefix=encoderMode.Length>0?"Render ["+encoderMode+"]":"Render";
   renderText.Text=prefix+" · "+value+" %"+eta;
  }
  public void Complete(string message){MediaComplete();renderBar.Value=100;renderBar.ForeColor=Color.FromArgb(104,205,130);renderText.Text="Render Â· completado";openFile.Enabled=openFolder.Enabled=File.Exists(outputPath);End(message);}
  public void Cancelled(){End("ExportaciÃ³n cancelada. Se conservÃ³ cualquier salida anterior.");}
  public void Failed(string message){End("No se pudo exportar: "+message);}
  void End(string message){finished=true;mediaBar.Indeterminate=false;detail.Text=message;action.Enabled=true;action.Text="Cerrar";}
 }
}
