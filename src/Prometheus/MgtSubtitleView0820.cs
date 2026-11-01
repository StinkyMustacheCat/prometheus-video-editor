using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Prometheus {
 public sealed class MgtSubtitleView0820 : IDisposable {
  readonly PreviewCanvas canvas;
  readonly WebView2 view;
  readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=16*1024*1024};
  string signature;int requestId,initRequest;bool initialized,initializing,navigated,ready,disposed,previewEnabled=true;double duration,time;int width,height;
  public Action ReadyChanged;public Action<string> Failed;
  public bool Ready{get{return ready&&view.Visible;}}
  public void SetPreviewEnabled(bool enabled){previewEnabled=enabled;view.Visible=enabled&&ready;SubtitleAnimation0820.PreviewOverlayReady=enabled&&ready;}
  public MgtSubtitleView0820(PreviewCanvas canvas){this.canvas=canvas;view=new WebView2{Visible=false,Enabled=false,DefaultBackgroundColor=Color.Transparent};canvas.Controls.Add(view);view.BringToFront();canvas.Resize+=(s,e)=>Position();canvas.ViewMoved+=Position;}
  async void Initialize(){if(initializing||initialized)return;initializing=true;try{string user=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","MGT-subtitle-WebView2");var environment=await CoreWebView2Environment.CreateAsync(null,user);if(disposed)return;await view.EnsureCoreWebView2Async(environment);if(disposed)return;view.CoreWebView2.Settings.IsWebMessageEnabled=true;view.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;view.CoreWebView2.Settings.AreDevToolsEnabled=false;view.CoreWebView2.WebMessageReceived+=(s,e)=>Message(e.WebMessageAsJson);view.NavigationCompleted+=(s,e)=>{navigated=e.IsSuccess;if(navigated)SendInit();else Fail("No se pudo abrir la vista local MGT");};initialized=true;var path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"runtime","mgt-subtitle-view","subtitle-view-v1.html");if(!File.Exists(path))throw new FileNotFoundException("Falta la vista visual MGT",path);view.Source=new Uri(path);}catch(Exception ex){Fail("Vista MGT no disponible: "+ex.Message);}finally{initializing=false;}}
  void Fail(string message){ready=false;SubtitleAnimation0820.PreviewOverlayReady=false;view.Visible=false;if(Failed!=null)Failed(message);if(ReadyChanged!=null)ReadyChanged();}
  void Message(string raw){try{var result=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(raw);object protocol,type,id;if(!result.TryGetValue("protocol",out protocol)||Convert.ToString(protocol)!="mgt.subtitle-view"||!result.TryGetValue("type",out type)||!result.TryGetValue("requestId",out id))return;int received=Convert.ToInt32(id);if(received<initRequest||signature==null)return;string kind=Convert.ToString(type);if(kind=="ready"&&received==initRequest){ready=true;SubtitleAnimation0820.PreviewOverlayReady=previewEnabled;view.Visible=previewEnabled;Position();SetFrame(time);if(ReadyChanged!=null)ReadyChanged();}else if(kind=="error"){object error;result.TryGetValue("error",out error);Fail("MGT: "+Convert.ToString(error));}}catch(Exception ex){Fail("Respuesta MGT inválida: "+ex.Message);}}
  void Post(object message){if(!disposed&&initialized&&navigated)view.CoreWebView2.PostWebMessageAsJson(json.Serialize(message));}
  void SendInit(){if(signature==null||!initialized||!navigated)return;ready=false;SubtitleAnimation0820.PreviewOverlayReady=false;view.Visible=false;var template=json.DeserializeObject(templateJson);initRequest=++requestId;Post(new{protocol="mgt.subtitle-view",version=1,requestId=initRequest,op="init",template=template,subtitles=json.DeserializeObject(transcriptJson),width=width,height=height,fps=60,duration=duration});}
  string templateJson,transcriptJson;
  public void SetProject(Project project,double at){time=at;if(!previewEnabled)return;string template=SubtitleAnimation0820.Template(project.Active);var subtitleTrack=SubtitleTrack0817.Track(project.Active);if(template==null||subtitleTrack==null||!subtitleTrack.enabled||!SubtitleTrack0817.Cues(project).Any(c=>c.enabled)){bool changed=ready;signature=null;ready=false;SubtitleAnimation0820.PreviewOverlayReady=false;view.Visible=false;if(changed&&ReadyChanged!=null)ReadyChanged();return;}if(project.Active.Length>3600){Fail("La vista MGT admite secuencias de hasta 60 minutos");return;}string transcript=json.Serialize(SubtitleAnimation0820.Transcript(project));double length=Math.Max(.1,project.Active.Length);string next=template+"\n"+transcript+"\n"+project.Active.width+"x"+project.Active.height+"/"+length.ToString(System.Globalization.CultureInfo.InvariantCulture);if(next==signature){SetFrame(at);return;}signature=next;templateJson=template;transcriptJson=transcript;width=project.Active.width;height=project.Active.height;duration=length;if(!initialized)Initialize();else SendInit();}
  public void SetFrame(double at){time=at;if(!ready||!previewEnabled)return;int frame=Math.Max(0,Math.Min((int)Math.Ceiling(duration*60)-1,(int)Math.Round(at*60)));Post(new{protocol="mgt.subtitle-view",version=1,requestId=++requestId,op="frame",frame=frame});}
  public void Position(){if(disposed||width<=0||height<=0)return;var bounds=Rectangle.Round(canvas.ImageBounds());if(bounds.Width<1||bounds.Height<1)return;view.Bounds=bounds;try{double zoom=Math.Max(.01,Math.Min(5,bounds.Width/(double)width));if(Math.Abs(view.ZoomFactor-zoom)>.005)view.ZoomFactor=zoom;}catch{}}
  public void Dispose(){disposed=true;ready=false;SubtitleAnimation0820.PreviewOverlayReady=false;view.Dispose();}
 }
}
