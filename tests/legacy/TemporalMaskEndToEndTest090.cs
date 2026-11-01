using System;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class TemporalMaskEndToEndTest090 {
 static int Main(string[] args){try{var project=Store.New();var asset=Media.Inspect(Path.GetFullPath(args[0]),CancellationToken.None);project.assets.Add(asset);var track=project.Active.tracks.First(t=>t.kind=="video");Media.Insert(project,asset.id,track.id,0);var clip=project.Active.clips.First(c=>c.asset_id==asset.id&&c.track_id==track.id);
  TemporalMasks0823.Set(clip,0,new ClipMask{shape="rectangle",points=new System.Collections.Generic.List<MaskVertex>{new MaskVertex{x=0,y=0},new MaskVertex{x=.5,y=1}}});TemporalMasks0823.Set(clip,.8,new ClipMask{shape="rectangle",points=new System.Collections.Generic.List<MaskVertex>{new MaskVertex{x=.5,y=0},new MaskVertex{x=1,y=1}}});var effect=EffectContracts.New(EffectContracts.MonochromeId);clip.effects.Add(effect);TemporalMasks0823.SetTarget(clip,"effect:"+effect.id,false);Store.Validate(project);Console.WriteLine("MID="+TemporalMasks0823.At(clip,.4).points[0].x);
  using(var frame=Rendering.FrameExact(project,.4,CancellationToken.None,256,144,false)){frame.Save(Path.GetFullPath(args[1]));Console.WriteLine("FRAME_OK "+frame.GetPixel(110,70));}return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
