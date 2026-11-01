using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class MilestoneNested01034 {
 static int Main(string[] args){try{
  string folder=Path.GetFullPath(args[0]);Directory.CreateDirectory(folder);
  var project=Store.New();SequenceCanvas.Set(project.Active,640,360);
  var tracks=project.Active.tracks.Where(t=>t.kind=="video").OrderBy(t=>t.order).ToList();
  while(tracks.Count<3){Store.AddTrack(project.Active,"video");tracks=project.Active.tracks.Where(t=>t.kind=="video").OrderBy(t=>t.order).ToList();}
  var ids=new System.Collections.Generic.List<string>();
  for(int i=0;i<3;i++){
   string path=Path.Combine(folder,"tile"+i+".png");using(var image=new Bitmap(640,360))using(var drawing=Graphics.FromImage(image)){drawing.Clear(Color.Transparent);drawing.FillRectangle(i==0?Brushes.Red:i==1?Brushes.Blue:Brushes.Lime,20+i*80,35,45,45);image.Save(path,ImageFormat.Png);}
   var asset=Media.Inspect(path,CancellationToken.None);project.assets.Add(asset);Media.Insert(project,asset.id,tracks[i].id,0);
   var clip=project.Active.clips.Last(c=>c.asset_id==asset.id);clip.duration=Rational.Frame(2);ids.Add(clip.id);
  }
  var inner=NestedMedia0910.Create(project,ids.Take(2),"Interior");inner.effects.Add(EffectContracts.New(EffectContracts.ZoomInDynamicId));
  var flat=NestedMedia0910.Flatten(project);if(flat.Active.clips.Any(c=>c.effects.Any(e=>e.effect_id==EffectContracts.ZoomInDynamicId)))throw new Exception("El zoom del contenedor se copió a sus hijos");
  using(var image=Rendering.FrameExact(project,.5,CancellationToken.None,640,360,false))image.Save(Path.Combine(folder,"first.png"),ImageFormat.Png);
  var outer=NestedMedia0910.Create(project,new[]{inner.id,ids[2]},"Exterior");outer.effects.Add(EffectContracts.New(EffectContracts.ZoomInDynamicId));BlendModes01034.Set(outer,"screen");
  var deep=NestedMedia0910.FindDeepChild(project,outer.id,ids[0]);if(deep==null)throw new Exception("No se encontró el clip del segundo nivel");
  NestedMedia0910.UpdateDeepChild(project,outer.id,ids[0],0,1.5,deep.child.track_id);
  var changed=NestedMedia0910.FindDeepChild(project,outer.id,ids[0]);if(changed==null||Math.Abs(changed.child.duration.Seconds-1.5)>1.0/60)throw new Exception("No se actualizó la duración interna");
  Store.Validate(project);var recursive=NestedMedia0910.Flatten(project);if(recursive.Active.clips.Any(c=>NestedMedia0910.IsNested(recursive,c)))throw new Exception("Quedó un anidado sin resolver");
  using(var image=Rendering.FrameExact(project,1,CancellationToken.None,640,360,false))image.Save(Path.Combine(folder,"second.png"),ImageFormat.Png);
  Console.WriteLine("PASS nested-composite/outer-effect/recursive-render");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
