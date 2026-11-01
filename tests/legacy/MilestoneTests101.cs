using System;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;
class MilestoneTests101 {
 static int Main(string[] args){try{
  string image=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
  var p=Store.New();SequenceCanvas.Set(p.Active,640,360);var asset=Media.Inspect(image,CancellationToken.None);p.assets.Add(asset);
  var tracks=p.Active.tracks.Where(t=>t.kind=="video").OrderBy(t=>t.order).ToList();Media.Insert(p,asset.id,tracks[0].id,0);Media.Insert(p,asset.id,tracks[1].id,0);
  var original=p.Active.clips.Where(c=>c.asset_id==asset.id).ToList();var nested=NestedMedia0910.Create(p,original.Select(c=>c.id),"Prueba anidada");Store.Validate(p);
  if(p.Active.clips.Count!=1||NestedMedia0910.Read(nested).clips.Count!=2)throw new Exception("No se replegaron los componentes");
  var flattened=NestedMedia0910.Flatten(p);if(flattened.Active.clips.Count!=2)throw new Exception("No se desplegaron para render");
  using(var frame=Rendering.FrameExact(p,.2,CancellationToken.None,640,360,false))frame.Save(Path.Combine(output,"nested.png"));
  nested.effects.Add(EffectContracts.New(EffectContracts.ZoomInDynamicId));using(var first=Rendering.FrameExact(p,.1,CancellationToken.None,640,360,false))first.Save(Path.Combine(output,"nested-zoom-first.png"));using(var last=Rendering.FrameExact(p,.8,CancellationToken.None,640,360,false))last.Save(Path.Combine(output,"nested-zoom-last.png"));if(File.ReadAllBytes(Path.Combine(output,"nested-zoom-first.png")).SequenceEqual(File.ReadAllBytes(Path.Combine(output,"nested-zoom-last.png"))))throw new Exception("El zoom del medio anidado no avanzó");
  Media.Insert(p,asset.id,nested.track_id,nested.End);string previousTrack=nested.track_id;var child=NestedMedia0910.Read(nested).clips[0];NestedMedia0910.UpdateChild(p,nested.id,child.id,0,6,child.track_id);Store.Validate(p);if(nested.duration.Seconds<6||nested.track_id==previousTrack)throw new Exception("El medio no creció o no cambió de pista al encontrar otro clip");
  var q=Store.New();SequenceCanvas.Set(q.Active,640,360);q.assets.Add(Media.Inspect(image,CancellationToken.None));Media.Insert(q,q.assets[0].id,q.Active.tracks.First(t=>t.kind=="video").id,0);var source=q.Active.clips[0];var blurred=NestedMedia0910.CreateBlurredBackground(q,source.id);Store.Validate(q);var inner=NestedMedia0910.Read(blurred);if(inner.clips.Count!=2||!inner.clips.Any(c=>c.effects.Any(e=>e.effect_id==EffectContracts.BlurId)))throw new Exception("Faltan capas del fondo");using(var frame=Rendering.FrameExact(q,.2,CancellationToken.None,640,360,false))frame.Save(Path.Combine(output,"blurred.png"));
  string saved=Path.Combine(output,"nested.prometheus.json");Store.Save(q,saved);var reopened=Store.Load(saved);if(!NestedMedia0910.IsNested(reopened,reopened.Active.clips[0]))throw new Exception("No se conservó el medio al guardar");
  Console.WriteLine("PASS nested/render/resize/blur/save");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
