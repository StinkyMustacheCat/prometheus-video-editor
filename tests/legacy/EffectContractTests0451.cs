using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Prometheus;

class EffectContractTests0451 {
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS "+text);}
 static bool Rejects(Action action){try{action();return false;}catch{return true;}}
 static void Main(){
  var project=Store.New();var asset=GeneratedMedia.CreateSolid("Prueba","808080",3);
  project.assets.Add(asset);Media.Insert(project,asset.id,project.Active.tracks.First(t=>t.kind=="video").id,0);
  var clip=project.Active.clips.Single();
  string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"prometheus-effects-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(directory);
  try{
   string lut=Path.Combine(directory,"neutral.cube");File.WriteAllText(lut,"LUT_3D_SIZE 2\n");
   var resource=new EffectResource{id=Store.Id(),kind="lut3d",uri=lut};project.effect_resources.Add(resource);
   clip.effects.Add(EffectContracts.NewLut3D(resource.id));
   var copy=Store.Copy(project);
   Check(copy.Active.clips.Single().effects.Single().effect_id==EffectContracts.Lut3DId&&
    copy.Active.clips.Single().effects.Single().parameters["lut"].resource_id==resource.id,
    "contrato y recurso sobreviven al guardado en memoria");
   string file=Path.Combine(directory,"test.prometheus.json");Store.Save(project,file);
   var restored=Store.Load(file);
   Check(restored.effect_resources.Single().uri==lut&&
    File.ReadAllText(file).Contains("neutral.cube"),"recurso se guarda relativo y se reconecta al abrir");
   var invalid=Store.Copy(project);invalid.Active.clips.Single().effects.Single().parameters["interpolation"].text="inexistente";
   Check(Rejects(()=>Store.Validate(invalid)),"el contrato rechaza parámetros fuera del catálogo");
   var future=Store.Copy(project);future.Active.clips.Single().effects.Single().effect_id="otro.motor.futuro";
   Check(Store.Copy(future).Active.clips.Single().effects.Single().effect_id=="otro.motor.futuro",
    "un contrato desconocido se conserva sin borrarlo");
   Check(Rejects(()=>Rendering.Build(future,true,false,640,360,60,"raw",0,true)),
    "el render no omite silenciosamente contratos sin adaptador");
   var old=Store.New();var raw=Store.Json().DeserializeObject(Store.Encode(old)) as Dictionary<string,object>;
   raw["schema_version"]="0.2.0";raw.Remove("effect_resources");
   var migrated=Store.Decode(Store.Json().Serialize(raw));
   Check(migrated.schema_version=="0.4.0"&&migrated.effect_resources.Count==0,
    "los proyectos 0.4.4 sin efectos migran al formato actual");
  }finally{Directory.Delete(directory,true);}
 }
}
