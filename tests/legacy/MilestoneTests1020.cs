using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using Prometheus;

class MilestoneTests1020 {
 static int Main(string[] args){try{
  string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);string imageFile=Path.Combine(output,"copy.png");using(var bitmap=new Bitmap(400,400)){bitmap.Save(imageFile,ImageFormat.Png);}
  var project=Store.New();var asset=Media.Inspect(imageFile,CancellationToken.None);project.assets.Add(asset);var lanes=project.Active.tracks.Where(t=>t.kind=="video").OrderBy(t=>t.order).ToList();Media.Insert(project,asset.id,lanes[0].id,0);Media.Insert(project,asset.id,lanes[1].id,0);
  var original=project.Active.clips.ToList();project.Active.edit_groups.Add(new EditGroup{id=Store.Id(),number=1,clip_ids=original.Select(c=>c.id).ToList()});Store.Validate(project);
  string sequence=project.Active.id;var copied=original.Select(c=>Store.Json().Deserialize<Clip>(Store.Json().Serialize(Store.Fields(c)))).ToList();var tracks=lanes.Select(t=>Store.Json().Deserialize<Track>(Store.Json().Serialize(Store.Fields(t)))).ToList();string group=String.Join("|",original.Select(c=>c.id));
  Sequences.Create(project,"Destino");var editor=new Editor();editor.Reset(project);
  editor.Commit(p=>ClipboardCore1020.Paste(p,copied,tracks,project.id,sequence,new string[0],new[]{group},1));
  var target=editor.Project.Active;if(target.clips.Count!=2||target.edit_groups.Count!=1||target.clips.Select(c=>target.tracks.First(t=>t.id==c.track_id).order).Distinct().Count()!=2)throw new Exception("El pegado entre secuencias perdió clips, grupos o pistas");
  editor.Undo();if(editor.Project.Active.clips.Count!=0)throw new Exception("El pegado no se deshizo en un paso");
  Console.WriteLine("PASS clipboard/cross-sequence/groups/undo");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
