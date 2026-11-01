using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;

class MilestoneBlend01034 {
 static int Main(string[] args){try{
  string folder=Path.GetFullPath(args[0]);Directory.CreateDirectory(folder);
  var project=Store.New();SequenceCanvas.Set(project.Active,320,320);
  var tracks=project.Active.tracks.Where(t=>t.kind=="video").OrderBy(t=>t.order).ToList();
  while(tracks.Count<2){Store.AddTrack(project.Active,"video");tracks=project.Active.tracks.Where(t=>t.kind=="video").OrderBy(t=>t.order).ToList();}
  string basePath=Path.Combine(folder,"base.png"),topPath=Path.Combine(folder,"top.png");
  using(var image=new Bitmap(320,320)){using(var g=Graphics.FromImage(image))g.Clear(Color.FromArgb(255,200,160,100));image.Save(basePath,ImageFormat.Png);}
  using(var image=new Bitmap(320,320)){using(var g=Graphics.FromImage(image)){g.Clear(Color.Transparent);g.FillRectangle(new SolidBrush(Color.FromArgb(255,100,180,220)),0,0,160,320);}image.Save(topPath,ImageFormat.Png);}
  foreach(var pair in new[]{Tuple.Create(basePath,tracks[0].id),Tuple.Create(topPath,tracks[1].id)}){var asset=Media.Inspect(pair.Item1,CancellationToken.None);project.assets.Add(asset);Media.Insert(project,asset.id,pair.Item2,0);project.Active.clips.Last(c=>c.asset_id==asset.id).duration=Rational.Frame(1);}
  var clip=project.Active.clips.Last();int[] outputs=new int[BlendModes01034.Modes.Length];int index=0;
  foreach(string mode in BlendModes01034.Modes){BlendModes01034.Set(clip,mode);Store.Validate(project);using(var frame=Rendering.FrameExact(project,.1,CancellationToken.None,320,320,false)){frame.Save(Path.Combine(folder,mode+".png"),ImageFormat.Png);var pixel=frame.GetPixel(8,8);outputs[index++]=pixel.R+pixel.G+pixel.B;var untouched=frame.GetPixel(250,8);Console.WriteLine(mode+": active="+pixel+" untouched="+untouched);if(Math.Abs(untouched.R-200)>12||Math.Abs(untouched.G-160)>12)throw new Exception("La zona transparente alteró el fondo en "+mode);}}
  if(outputs[1]>=outputs[0]||outputs[2]<=outputs[0])throw new Exception("Los modos de fusión no cambian la combinación correctamente: "+String.Join(",",outputs));
  var attributes=ClipAttributes.Capture(project,clip.id);ClipAttributes.Reset(project,new[]{clip.id});if(BlendModes01034.Read(clip)!="normal")throw new Exception("Restablecer dejó la fusión activa");ClipAttributes.Apply(project,attributes,new[]{clip.id});if(BlendModes01034.Read(clip)!="subtract")throw new Exception("Pegar atributos perdió la fusión");
  Console.WriteLine("PASS blend modes + transparent bounds");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
