using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Prometheus {
 public static class SilenceAnalysis01010 {
  public static List<double[]> Detect(Project project,string clipId,double threshold,double minimum,double margin,CancellationToken token){
   var clip=project.Active.clips.Find(c=>c.id==clipId);
   if(clip==null)throw new Exception("El audio ya no está en la secuencia");
   var asset=project.assets.Find(a=>a.id==clip.asset_id);
   var stream=asset==null?null:asset.streams.Find(s=>s.id==clip.stream_id);
   if(stream==null||stream.kind!="audio")throw new Exception("Selecciona un clip con audio");
   if(clip.speed.Seconds<=0)throw new Exception("La velocidad del clip no es válida");
   string path=Media.RequirePath(asset,true);
   double sourceLength=clip.duration.Seconds*clip.speed.Seconds;
   string args="-hide_banner -nostdin -ss "+clip.source_in.Seconds.ToString("0.######",CultureInfo.InvariantCulture)+
    " -i "+Tools.Q(path)+" -t "+sourceLength.ToString("0.######",CultureInfo.InvariantCulture)+
    " -map 0:a:0 -vn -af silencedetect=noise="+threshold.ToString("0.##",CultureInfo.InvariantCulture)+
    "dB:d="+(minimum*clip.speed.Seconds).ToString("0.###",CultureInfo.InvariantCulture)+" -f null NUL";
   var lines=new StringBuilder();
   using(var process=Tools.Start(Tools.FFmpeg,args,true)){
    process.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)lock(lines)lines.AppendLine(e.Data);};
    process.BeginErrorReadLine();
    using(token.Register(()=>{try{process.Kill();}catch{}})){
     process.StandardOutput.ReadToEnd();process.WaitForExit();token.ThrowIfCancellationRequested();
     if(process.ExitCode!=0)throw new Exception("No se pudo analizar el audio: "+lines.ToString().Trim());
    }
   }
   var found=new List<double[]>();double? start=null;
   foreach(string line in lines.ToString().Split('\n')){
    var open=Regex.Match(line,@"silence_start:\s*([0-9.]+)");
    if(open.Success){double value;if(Double.TryParse(open.Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out value))start=value;}
    var close=Regex.Match(line,@"silence_end:\s*([0-9.]+)");
    if(close.Success&&start.HasValue){double value;if(Double.TryParse(close.Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out value)){
      found.Add(new[]{start.Value,value});start=null;
    }}
   }
   if(start.HasValue)found.Add(new[]{start.Value,sourceLength});
   var result=new List<double[]>();
   foreach(var range in found){
    double begin=clip.timeline_start.Seconds+Math.Max(0,range[0]/clip.speed.Seconds)+margin;
    double end=clip.timeline_start.Seconds+Math.Min(clip.duration.Seconds,range[1]/clip.speed.Seconds)-margin;
    begin=Math.Max(clip.timeline_start.Seconds,Rational.Frame(begin).Seconds);
    end=Math.Min(clip.End,Rational.Frame(end).Seconds);
    if(end-begin>=1.0/60-0.000001)result.Add(new[]{begin,end});
   }
   return result;
  }
 }

 public partial class MainForm {
  static string SilenceTime01010(double value){var span=TimeSpan.FromSeconds(Math.Max(0,value));return String.Format("{0:00}:{1:00}:{2:00}.{3:00}",(int)span.TotalHours,span.Minutes,span.Seconds,span.Milliseconds/10);}
  void ShowSilenceCut01010(string selectedId){
   try{
    var selected=editor.Project.Active.clips.Find(c=>c.id==selectedId);
    var related=selected==null?new List<string>():editor.Related(selectedId);
    var audio=editor.Project.Active.clips.FirstOrDefault(c=>related.Contains(c.id)&&editor.Project.assets.Find(a=>a.id==c.asset_id).streams.Any(st=>st.id==c.stream_id&&st.kind=="audio"));
    if(audio==null)throw new Exception("Selecciona un medio con audio para detectar silencios");
    var snapshot=Store.Copy(editor.Project);int revision=snapshot.revision;
    using(var dialog=new Form{Text="Detectar y recortar silencios",ClientSize=new Size(620,475),MinimumSize=new Size(570,430),StartPosition=FormStartPosition.CenterParent,BackColor=panel,ForeColor=text}){
     var threshold=new NumericUpDown{Minimum=-80,Maximum=-5,Value=-35,DecimalPlaces=0,Left=115,Top=18,Width=75};
     var minimum=new NumericUpDown{Minimum=0.1M,Maximum=10,Value=0.35M,DecimalPlaces=2,Increment=0.05M,Left=320,Top=18,Width=75};
     var margin=new NumericUpDown{Minimum=0,Maximum=2,Value=0.08M,DecimalPlaces=2,Increment=0.02M,Left=500,Top=18,Width=75};
     dialog.Controls.Add(new Label{Text="Umbral (dB)",Left=16,Top=21,Width=100});dialog.Controls.Add(threshold);
     dialog.Controls.Add(new Label{Text="Mínimo (s)",Left=215,Top=21,Width=105});dialog.Controls.Add(minimum);
     dialog.Controls.Add(new Label{Text="Margen (s)",Left=415,Top=21,Width=90});dialog.Controls.Add(margin);
     var analyze=new Button{Text="Analizar",Left=16,Top=54,Width=100,Height=30};dialog.Controls.Add(analyze);
     var message=new Label{Text="Se analizará el audio seleccionado. Revisa los cortes antes de aplicarlos.",Left=130,Top=59,Width=470,Height=27};dialog.Controls.Add(message);
     var list=new ListView{Left=16,Top=94,Width=588,Height=278,Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right,View=View.Details,FullRowSelect=true,CheckBoxes=true};
     list.Columns.Add("Inicio",135);list.Columns.Add("Fin",135);list.Columns.Add("Duración",110);dialog.Controls.Add(list);
     var gap=new RadioButton{Text="Eliminar y dejar huecos (solo el medio vinculado)",Left=16,Top=386,Width=308,Checked=true,Anchor=AnchorStyles.Bottom|AnchorStyles.Left};
     var ripple=new RadioButton{Text="Eliminar con rizo (toda la secuencia)",Left=325,Top=386,Width=275,Anchor=AnchorStyles.Bottom|AnchorStyles.Left};
     dialog.Controls.Add(gap);dialog.Controls.Add(ripple);
     var apply=new Button{Text="Aplicar cortes",Left=462,Top=429,Width=142,Height=31,Enabled=false,Anchor=AnchorStyles.Bottom|AnchorStyles.Right};
     var cancel=new Button{Text="Cancelar",Left=355,Top=429,Width=100,Height=31,Anchor=AnchorStyles.Bottom|AnchorStyles.Right};dialog.Controls.Add(apply);dialog.Controls.Add(cancel);
     var ranges=new List<double[]>();CancellationTokenSource analysis=null;bool accepted=false;bool closeRipple=false;var chosen=new List<double[]>();
     Action run=()=>{
      if(analysis!=null)try{analysis.Cancel();}catch(ObjectDisposedException){}analysis=new CancellationTokenSource();var current=analysis;
      double selectedThreshold=(double)threshold.Value,selectedMinimum=(double)minimum.Value,selectedMargin=(double)margin.Value;
      analyze.Enabled=false;apply.Enabled=false;list.Items.Clear();message.Text="Analizando el audio…";
      Task.Factory.StartNew(()=>{try{var detected=SilenceAnalysis01010.Detect(snapshot,audio.id,selectedThreshold,selectedMinimum,selectedMargin,current.Token);
       if(dialog.IsDisposed)return;dialog.BeginInvoke((Action)(()=>{if(dialog.IsDisposed||analysis!=current)return;ranges=detected;foreach(var interval in ranges){var item=new ListViewItem(SilenceTime01010(interval[0])){Checked=true};item.SubItems.Add(SilenceTime01010(interval[1]));item.SubItems.Add((interval[1]-interval[0]).ToString("0.00")+" s");list.Items.Add(item);}message.Text=ranges.Count==0?"No se encontraron silencios con estos parámetros.":ranges.Count+" silencio(s) detectado(s) · desmarca los que quieras conservar";analyze.Enabled=true;apply.Enabled=ranges.Count>0;}));
      }catch(OperationCanceledException){}catch(Exception ex){if(dialog.IsDisposed)return;dialog.BeginInvoke((Action)(()=>{if(dialog.IsDisposed||analysis!=current)return;message.Text="No se pudo analizar: "+ex.Message;analyze.Enabled=true;}));}finally{current.Dispose();}});
     };
     analyze.Click+=(s,e)=>run();
     apply.Click+=(s,e)=>{if(editor.Project.id!=snapshot.id||editor.Project.revision!=revision){message.Text="La secuencia cambió. Vuelve a abrir el análisis.";return;}chosen=list.Items.Cast<ListViewItem>().Where(item=>item.Checked).Select(item=>ranges[item.Index]).ToList();if(chosen.Count==0){message.Text="Marca al menos un silencio.";return;}closeRipple=ripple.Checked;accepted=true;dialog.Close();};
     cancel.Click+=(s,e)=>dialog.Close();dialog.Shown+=(s,e)=>run();dialog.FormClosed+=(s,e)=>{if(analysis!=null)try{analysis.Cancel();}catch(ObjectDisposedException){}analysis=null;};
     dialog.ShowDialog(this);
     if(accepted){Edit(()=>editor.RemoveSilences(audio.id,chosen,closeRipple));status.Text="Silencios recortados · Ctrl+Z deshace todo el lote";}
    }
   }catch(Exception ex){Error(ex);}
  }
 }
}
