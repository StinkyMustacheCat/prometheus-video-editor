using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  bool ShowExportPreflight(ExportOptions options){
   var target=ExportWindow(Store.Copy(editor.Project),options);
   double exportEnd=options.EndFrame.HasValue?options.EndFrame.Value/60.0:editor.Project.Active.Length;
   int pending=PendingJobs(editor.Project).Count(job=>!String.IsNullOrEmpty(job.TrackId)&&job.Start<exportEnd&&(options.Video||target.Active.tracks.Any(t=>t.id==job.TrackId&&t.kind=="audio"&&t.enabled)));
   var report=ExportPreflight.Inspect(target,options.Video,pending,options.Width,options.Height,options.UsePreview,options.SelectiveWeb);
   EffectRenderPreflight.Inspect(target,report,options.Video);
   if(report.CanExport)return true;
   string retryResourceId=null;
   using(var dialog=new Form{Text="Comprobación antes de exportar",Width=650,Height=390,MinimumSize=new Size(530,320),FormBorderStyle=FormBorderStyle.Sizable,StartPosition=FormStartPosition.CenterParent,BackColor=panel,ForeColor=text}){
    var heading=new Label{Dock=DockStyle.Top,Height=52,Padding=new Padding(18,12,12,0),Text=report.CanExport?(report.Warnings==0?"Todo listo para exportar":report.Warnings+" aviso(s) antes de exportar"):report.Errors+" problema(s) que impiden exportar",Font=new Font("Segoe UI",12,FontStyle.Bold)};
    var note=new Label{Dock=DockStyle.Top,Height=38,Padding=new Padding(18,2,12,0),Text=options.UsePreview?"Exportación rápida: comprueba que las vistas ligeras estén listas.":options.SelectiveWeb?"Se usarán tramos web cuando sea posible, con respaldo de medio completo.":report.checks.Any(c=>c.code=="web-pending")?"Los medios web pendientes se resolverán completos antes del render.":"Se comprueban los medios utilizados en la secuencia activa."};
    var list=new ListView{Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,HeaderStyle=ColumnHeaderStyle.None,BackColor=Color.FromArgb(35,38,43),ForeColor=text,BorderStyle=BorderStyle.None};list.Columns.Add("Estado",105);list.Columns.Add("Detalle",495);
    foreach(var check in report.checks){string label=check.severity=="error"?"✕ Problema":check.severity=="warning"?"! Aviso":"✓ Correcto";string at=check.timeline_seconds.HasValue?" · "+Timecode(check.timeline_seconds.Value):"";var item=new ListViewItem(label);item.SubItems.Add(check.message+at);item.Tag=check;item.ForeColor=check.severity=="error"?Color.FromArgb(255,170,157):check.severity=="warning"?Color.FromArgb(255,199,126):Color.FromArgb(166,224,174);list.Items.Add(item);}
    list.DoubleClick+=(s,e)=>{if(list.SelectedItems.Count==0)return;var check=list.SelectedItems[0].Tag as ExportCheck;if(check==null)return;
     var asset=target.assets.FirstOrDefault(a=>a.id==check.asset_id);
     if(asset!=null){var local=Media.SelectRepresentation(asset,options.UsePreview);string path=local==null?null:local.locator.path;
      if(!String.IsNullOrEmpty(path)&&File.Exists(path)){Process.Start("explorer.exe","/select,\""+Path.GetFullPath(path)+"\"");return;}
      if(asset.source!=null&&asset.source.kind=="web"&&Uri.IsWellFormedUriString(asset.source.uri,UriKind.Absolute)){Process.Start(new ProcessStartInfo(asset.source.uri){UseShellExecute=true});return;}
     }
     if(check.timeline_seconds.HasValue){timeline.Cursor=check.timeline_seconds.Value;timeline.EnsureCursor();UpdateTimecode(timeline.Cursor);RequestFrame();}
    };
    var actions=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=56,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(8),WrapContents=false};
    var proceed=new Button{Text="Continuar",Width=100,Height=32,DialogResult=DialogResult.OK,Enabled=report.CanExport};var cancel=new Button{Text="Volver",Width=92,Height=32,DialogResult=DialogResult.Cancel};actions.Controls.Add(proceed);actions.Controls.Add(cancel);
    if(report.checks.Any(c=>c.code=="media-unavailable")){var reconnect=new Button{Text="Reconectar carpeta…",Width=155,Height=32};reconnect.Click+=(s,e)=>{dialog.DialogResult=DialogResult.Retry;dialog.Close();};actions.Controls.Add(reconnect);}
    var missingLut=report.checks.FirstOrDefault(c=>c.code=="lut-missing"||c.code=="lut-invalid");
    if(missingLut!=null){var locate=new Button{Text="Localizar LUT…",Width=125,Height=32};locate.Click+=(s,e)=>{retryResourceId=missingLut.resource_id;dialog.DialogResult=DialogResult.Retry;dialog.Close();};actions.Controls.Add(locate);}
    var placeholderHint=new Label{Dock=DockStyle.Bottom,Height=report.checks.Any(c=>c.code=="placeholder")?30:0,Padding=new Padding(18,4,8,0),ForeColor=Color.FromArgb(255,199,126),Text="Aún hay espacios reservados por definir; aparecerán como referencia visual o silencio."};
    dialog.Controls.Add(list);dialog.Controls.Add(placeholderHint);dialog.Controls.Add(actions);dialog.Controls.Add(note);dialog.Controls.Add(heading);dialog.AcceptButton=proceed;dialog.CancelButton=cancel;
    var answer=dialog.ShowDialog(this);if(answer==DialogResult.Retry){if(retryResourceId!=null)RelinkLut(retryResourceId);else ReconnectFolder();return false;}return answer==DialogResult.OK&&report.CanExport;
   }
  }
  static string Timecode(double seconds){int frames=Math.Max(0,(int)Math.Round(seconds*60));return String.Format("{0:00}:{1:00}:{2:00}:{3:00}",frames/216000,frames/3600%60,frames/60%60,frames%60);}
 }
}
