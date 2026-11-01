using System;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class Sam2TargetTest090 {
 static int Main(string[] args){try{var project=Store.Load(Path.GetFullPath(args[0]));var clip=project.Active.clips.First(c=>Sam2MaskStore090.Active(c));var effect=EffectContracts.New(EffectContracts.MonochromeId);clip.effects.Add(effect);TemporalMasks0823.SetTarget(clip,"effect:"+effect.id,true);Store.Validate(project);using(var image=Rendering.FrameExact(project,.5,CancellationToken.None,256,144,false)){image.Save(Path.GetFullPath(args[1]));Console.WriteLine("SAM2_TARGET_FRAME_OK");}clip.effects.Remove(effect);using(var image=Rendering.FrameExact(project,.5,CancellationToken.None,256,144,false)){Console.WriteLine("SAM2_MISSING_EFFECT_FALLBACK_OK");}return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
