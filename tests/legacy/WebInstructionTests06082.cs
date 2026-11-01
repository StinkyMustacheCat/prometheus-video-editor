using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Prometheus;

class WebInstructionTests06082 {
 static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);}
 static T Call<T>(string method,params object[] args){try{return (T)typeof(WebMedia).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);}catch(TargetInvocationException ex){throw ex.InnerException;}}
 static void Seed(InstructionMediaAdapter adapter,string url,Asset asset){var field=typeof(InstructionMediaAdapter).GetField("prepared",BindingFlags.NonPublic|BindingFlags.Instance);((Dictionary<string,Asset>)field.GetValue(adapter))["video|"+url]=asset;}
 static void Fail(InstructionMediaAdapter adapter,string url,string message){var field=typeof(InstructionMediaAdapter).GetField("failures",BindingFlags.NonPublic|BindingFlags.Instance);((Dictionary<string,string>)field.GetValue(adapter))["video|"+url]=message;}
 static void Main(string[] args){
  if(args.Length!=1)throw new Exception("Indica el JSON de instrucciones de prueba");
  var supplied=Instructions.Parse(File.ReadAllText(args[0]));Check(supplied.actions.Count==30,"el lote real contiene 30 instrucciones");
  string cache=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-cache-06082-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(cache);
  byte[] png;using(var bitmap=new Bitmap(50,30)){using(var g=Graphics.FromImage(bitmap))g.Clear(Color.CornflowerBlue);using(var memory=new MemoryStream()){bitmap.Save(memory,ImageFormat.Png);png=memory.ToArray();}}
  byte[] svg=Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\"><circle cx=\"50\" cy=\"50\" r=\"45\" fill=\"#ff6600\"/></svg>");
  Check(Call<string>("DirectKind","application/octet-stream",png,png.Length,null)=="image","una imagen sin extensión se identifica por sus bytes");
  Check(Call<string>("DirectKind","image/svg+xml",svg,svg.Length,null)=="image","SVG se identifica por su contenido");
  bool mismatch=false;try{Call<string>("DirectKind","image/svg+xml",svg,svg.Length,"video");}catch(Exception ex){mismatch=ex.Message.Contains("declara vídeo");}Check(mismatch,"una pista de tipo equivocada se rechaza");
  var assets=new Dictionary<string,Asset>();
  foreach(string url in supplied.actions.Select(a=>a.url).Distinct()){
   byte[] bytes=url.EndsWith(".svg",StringComparison.OrdinalIgnoreCase)?svg:png;
   assets[url]=Call<Asset>("ImageFromBytes",url,url,cache,bytes,CancellationToken.None);
  }
  Check(assets.Count==13&&assets.Values.Count(a=>a.name.EndsWith(".svg"))==2,"13 enlaces únicos, incluidos dos SVG, se preparan como imágenes");
  using(var rendered=new Bitmap(assets.Values.First(a=>a.name.EndsWith(".svg")).representations.First(r=>r.role=="original").locator.path))Check(rendered.GetPixel(0,0).A==0,"el SVG conserva transparencia");
  var project=Store.New();var placeholder=GeneratedMedia.CreatePlaceholder("Reserva","",30);project.assets.Add(placeholder);
  string track=project.Active.tracks.First(t=>t.kind=="video").id;var doc=Instructions.New(project);
  for(int i=0;i<supplied.actions.Count;i++){
   var clip=new Clip{id=Store.Id(),name="Reserva",asset_id=placeholder.id,stream_id=placeholder.streams[0].id,track_id=track,timeline_start=Rational.Frame(i),source_in=Rational.Time(i),duration=Rational.Frame(1)};
   project.Active.clips.Add(clip);doc.actions.Add(new InstructionAction{op="placeholder.fill_url",clip_id=clip.id,url=supplied.actions[i].url});
  }
  Store.Validate(project);doc=Instructions.Parse(Instructions.Encode(doc));var adapter=new InstructionMediaAdapter();foreach(var pair in assets)Seed(adapter,pair.Key,pair.Value);
  var plan=Instructions.Preview(project,doc,adapter);
  Check(plan.Cards.All(c=>c.status=="valid"),"las 30 acciones del lote son compatibles con imágenes");
  Check(plan.Result.assets.Count(a=>a.source.kind=="web")==13,"las URL repetidas comparten un medio");
  Check(plan.DocumentJson==Instructions.Encode(doc),"la revisión conserva una representación estable del documento");
  var editor=new Editor();editor.Reset(project);Instructions.Apply(editor,doc,plan,adapter);
  Check(editor.Project.Active.clips.Count==30&&editor.Project.assets.Count(a=>a.source.kind=="web")==13,"el lote se aplica en un solo paso");
  string output=Path.Combine(cache,"Batch.pthproj");ProjectResourceTransfer.Materialize(editor.Project,output);
  Check(editor.Project.assets.Where(a=>a.name.EndsWith(".svg")).All(a=>File.Exists(WebMedia.Detail(a,"svg_source_path"))),"el proyecto conserva los dos SVG originales");
  var badAdapter=new InstructionMediaAdapter();var badDoc=Instructions.New(project);string badUrl="https://example.org/ambiguous";Fail(badAdapter,badUrl,"El servidor rechazó el enlace (HTTP 403)");
  badDoc.actions.Add(new InstructionAction{op="placeholder.fill_url",clip_id=project.Active.clips[0].id,url=badUrl});badDoc.actions.Add(new InstructionAction{op="placeholder.fill_url",clip_id=project.Active.clips[1].id,url=badUrl});badDoc.actions.Add(new InstructionAction{op="placeholder.fill_url",clip_id=project.Active.clips[2].id,url=supplied.actions[2].url});Seed(badAdapter,supplied.actions[2].url,assets[supplied.actions[2].url]);
  var badPlan=Instructions.Preview(project,badDoc,badAdapter);
  Check(badPlan.Cards.Count(c=>c.status=="invalid")==2&&badPlan.Cards.Count(c=>c.status=="valid")==1,"un enlace fallido no bloquea los demás");
  Check(badPlan.Lines.Count(s=>s.Contains(badUrl))==1,"los fallos repetidos se agrupan por URL");
  var hinted=Instructions.New(project);hinted.actions.Add(new InstructionAction{op="placeholder.fill_url",clip_id=project.Active.clips[0].id,url=supplied.actions[0].url,source_kind="image"});var parsedHint=Instructions.Parse(Instructions.Encode(hinted));Check(parsedHint.actions[0].source_kind=="image","source_kind opcional se conserva en el contrato");
  Console.WriteLine("PASS 0.6.8.2");
 }
}
