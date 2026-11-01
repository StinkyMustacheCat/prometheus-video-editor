using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace Prometheus {
 // The engine is an isolated, bundled worker. Its transient asset and job IDs never enter a project.
 public sealed class MgtEngine : IDisposable {
  readonly Process process;
  readonly string root, dataRoot, instanceId;
  readonly string url, token;
  readonly StringBuilder startupErrors=new StringBuilder();
  readonly object errorLock=new object();
  public MgtEngine(CancellationToken cancel) {
   root=Path.Combine(Path.GetDirectoryName(typeof(MgtEngine).Assembly.Location),"mgt-engine");
   string node=Path.Combine(root,"runtime","node","node.exe");
   string server=Path.Combine(root,"motor","server.cjs");
   if(!File.Exists(node)||!File.Exists(server))throw new Exception("Falta el motor MGT incluido con Prometheus.");
   string dataBase=Environment.GetEnvironmentVariable("PROMETHEUS_MGT_DATA_ROOT");
   if(string.IsNullOrWhiteSpace(dataBase))dataBase=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","MGT-runtime");
   dataRoot=Path.Combine(dataBase,"session-"+Store.Id());
   Directory.CreateDirectory(dataRoot);
   using(var sha=System.Security.Cryptography.SHA256.Create())instanceId=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.Combine(root,"motor").ToLowerInvariant()))).Replace("-","").Substring(0,16).ToLowerInvariant();
   var start=new ProcessStartInfo(node,Tools.Q(server)){WorkingDirectory=Path.Combine(root,"motor"),UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardError=true};
   start.EnvironmentVariables["MGT_DATA_ROOT"]=dataRoot;
   process=Process.Start(start);
   process.ErrorDataReceived+=(sender,args)=>{if(args.Data==null)return;lock(errorLock){startupErrors.AppendLine(args.Data);if(startupErrors.Length>16384)startupErrors.Remove(0,startupErrors.Length-16384);}};
   process.BeginErrorReadLine();
   string running=Path.Combine(dataRoot,instanceId,"running.json");
   DateTime deadline=DateTime.UtcNow.AddSeconds(30);
   try{
    while(DateTime.UtcNow<deadline){
     cancel.ThrowIfCancellationRequested();
     if(process.HasExited){string errors;lock(errorLock)errors=startupErrors.ToString();throw new Exception("El motor MGT no pudo iniciarse (código "+process.ExitCode+"): "+errors);}
     if(File.Exists(running)){
      try{var state=Json(File.ReadAllText(running));string address=String(state,"url");
       if(Number(state,"pid")!=process.Id)throw new Exception("La instancia MGT no coincide.");
       var info=Request(address,"GET","/api/info",null,null,null,cancel);
       if(Number(info,"pid")==process.Id&&Bool(info,"ready")){
        var capabilities=Request(address,"GET","/api/asset/v2/capabilities",null,null,null,cancel);
        object versions;var supported=capabilities.TryGetValue("assetContracts",out versions)?versions as System.Collections.IEnumerable:null;
        if(supported!=null){foreach(var value in supported)if(Convert.ToInt32(value)==2){url=address;token=String(info,"token");return;}}
        throw new Exception("El motor MGT incluido no admite el contrato de assets v2.");
       }
      }catch(WebException){}catch(IOException){}
     }
     Thread.Sleep(100);
    }
    throw new Exception("El motor MGT no respondió a tiempo.");
   }catch{Dispose();throw;}
  }
  public static Dictionary<string,object> Json(string value){return Store.Json().Deserialize<Dictionary<string,object>>(value);}
  public static string String(Dictionary<string,object> value,string key){object result;return value!=null&&value.TryGetValue(key,out result)&&result!=null?Convert.ToString(result,CultureInfo.InvariantCulture):"";}
  public static double Number(Dictionary<string,object> value,string key){object result;return value!=null&&value.TryGetValue(key,out result)&&result!=null?Convert.ToDouble(result,CultureInfo.InvariantCulture):0;}
  public static bool Bool(Dictionary<string,object> value,string key){object result;return value!=null&&value.TryGetValue(key,out result)&&result!=null&&Convert.ToBoolean(result,CultureInfo.InvariantCulture);}
  public static Dictionary<string,object> Map(Dictionary<string,object> value,string key){object result;return value!=null&&value.TryGetValue(key,out result)?result as Dictionary<string,object>:null;}
  static WebRequest Create(string address,string method,string route,string token,string contentType){
   var request=(HttpWebRequest)WebRequest.Create(address+route);request.Method=method;request.Timeout=600000;request.ReadWriteTimeout=600000;
   if(method=="POST"){request.ContentType=contentType??"application/json";request.Headers["Origin"]=address;request.Headers["X-Motion-Token"]=token;}
   return request;
  }
  static Dictionary<string,object> Request(string address,string method,string route,string token,string contentType,byte[] body,CancellationToken cancel){
   cancel.ThrowIfCancellationRequested();var request=Create(address,method,route,token,contentType);
   using(cancel.Register(()=>request.Abort())){
    if(body!=null){request.ContentLength=body.Length;using(var stream=request.GetRequestStream())stream.Write(body,0,body.Length);}else if(method=="POST")request.ContentLength=0;
    try{using(var response=(HttpWebResponse)request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream()))return Json(reader.ReadToEnd());}
    catch(WebException ex){cancel.ThrowIfCancellationRequested();string detail="";if(ex.Response!=null)using(var reader=new StreamReader(ex.Response.GetResponseStream()))detail=reader.ReadToEnd();try{var error=Json(detail);detail=String(error,"error");}catch{}throw new Exception("MGT: "+(string.IsNullOrWhiteSpace(detail)?ex.Message:detail),ex);}
   }
  }
  Dictionary<string,object> Call(string method,string route,object input,CancellationToken cancel){return Request(url,method,route,token,"application/json",input==null?null:Encoding.UTF8.GetBytes(Store.Json().Serialize(input)),cancel);}
  byte[] CallBytes(string route,object input,CancellationToken cancel){
   cancel.ThrowIfCancellationRequested();byte[] body=Encoding.UTF8.GetBytes(Store.Json().Serialize(input));var request=Create(url,"POST",route,token,"application/json");
   using(cancel.Register(()=>request.Abort())){
    request.ContentLength=body.Length;using(var stream=request.GetRequestStream())stream.Write(body,0,body.Length);
    try{using(var response=request.GetResponse())using(var source=response.GetResponseStream())using(var target=new MemoryStream()){source.CopyTo(target);return target.ToArray();}}
    catch(WebException ex){cancel.ThrowIfCancellationRequested();string detail="";if(ex.Response!=null)using(var reader=new StreamReader(ex.Response.GetResponseStream()))detail=reader.ReadToEnd();try{detail=String(Json(detail),"error");}catch{}throw new Exception("MGT: "+(string.IsNullOrWhiteSpace(detail)?ex.Message:detail),ex);}
   }
  }
  public Dictionary<string,object> TransitionCapabilities(CancellationToken cancel){return Call("GET","/api/transition/v1/capabilities",null,cancel);}
  public Dictionary<string,object> ImportTransition(string document,CancellationToken cancel){
   var capabilities=TransitionCapabilities(cancel);if(capabilities==null)throw new Exception("El motor MGT no admite transiciones.");
   return Call("POST","/api/transition/v1/import",new{document=Store.Json().DeserializeObject(document)},cancel);
  }
  public Dictionary<string,object> PreflightTransition(object input,CancellationToken cancel){return Call("POST","/api/transition/v1/preflight",input,cancel);}
  public byte[] TransitionFrame(object input,CancellationToken cancel){return CallBytes("/api/transition/v1/frame",input,cancel);}
  public void ReleaseTransition(string id){try{Call("POST","/api/transition/v1/release",new{transitionId=id},CancellationToken.None);}catch{}}
  public Dictionary<string,object> Import(string file,CancellationToken cancel){
   if(!File.Exists(file))throw new FileNotFoundException("Falta el archivo MGT",file);
   byte[] bytes;string type;
   if(MgtMedia.IsJsonFile(file)){
    string document=File.ReadAllText(file,Encoding.UTF8);
    bytes=Encoding.UTF8.GetBytes("{\"format\":\"mgt.json\",\"document\":"+document+"}");type="application/json";
   }else if(file.EndsWith(".mgtproj",StringComparison.OrdinalIgnoreCase)){bytes=File.ReadAllBytes(file);type="application/zip";}
   else throw new Exception("Selecciona un .mgt.json o .mgtproj.");
   return Request(url,"POST","/api/asset/v2/import",token,type,bytes,cancel);
  }
  public Dictionary<string,object> Preflight(string assetId,string profile,int fps,Dictionary<string,object> overrides,CancellationToken cancel){return Call("POST","/api/asset/v2/preflight",new{assetId,profile,fps,overrides=overrides??new Dictionary<string,object>()},cancel);}
  public Dictionary<string,object> Render(string assetId,string profile,int fps,Dictionary<string,object> overrides,string output,CancellationToken cancel,Action<int> progress){
   var response=Call("POST","/api/asset/v2/render",new{assetId,profile,fps,overrides=overrides??new Dictionary<string,object>()},cancel);
   string job=String(response,"id");if(string.IsNullOrEmpty(job))throw new Exception("MGT no devolvió un trabajo de render.");
   try{
    while(true){
     if(cancel.IsCancellationRequested){try{Call("POST","/api/jobs/"+job+"/cancel",null,CancellationToken.None);}catch{}cancel.ThrowIfCancellationRequested();}
     var state=Call("GET","/api/jobs/"+job,null,cancel);string status=String(state,"status");
     if(status=="done")break;
     if(status=="error"||status=="cancelled")throw new Exception("MGT: "+String(state,"error"));
     if(progress!=null&&Number(state,"frames")>0)progress((int)Math.Min(99,Number(state,"completed")/Number(state,"frames")*100));
     Thread.Sleep(200);
    }
    var request=Create(url,"GET","/api/jobs/"+job+"/download",null,null);using(cancel.Register(()=>request.Abort()))using(var responseFile=request.GetResponse())using(var source=responseFile.GetResponseStream()){
     Directory.CreateDirectory(Path.GetDirectoryName(output));string partial=output+".part-"+Store.Id();
     try{using(var target=File.Create(partial))source.CopyTo(target);if(File.Exists(output))File.Delete(output);File.Move(partial,output);}finally{if(File.Exists(partial))File.Delete(partial);}
    }
    if(progress!=null)progress(100);return response;
   }finally{try{Call("POST","/api/jobs/"+job+"/release",null,CancellationToken.None);}catch{}}
  }
  public void Release(string assetId){try{Call("POST","/api/asset/v2/release",new{assetId},CancellationToken.None);}catch{}}
  public void Dispose(){try{if(url!=null&&token!=null)Call("POST","/api/shutdown",null,CancellationToken.None);}catch{}try{if(process!=null&&!process.HasExited){if(!process.WaitForExit(1500))process.Kill();}if(process!=null)process.Dispose();}catch{}try{if(Directory.Exists(dataRoot))Directory.Delete(dataRoot,true);}catch{}}
 }
}
