using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  void AddClipColorMenu(ContextMenuStrip menu){
   var ids=timeline.Selection.Where(id=>editor.Project.Active.clips.Any(c=>c.id==id)).Distinct().ToList();
   if(ids.Count==0&&timeline.Selected!=null&&editor.Project.Active.clips.Any(c=>c.id==timeline.Selected))ids.Add(timeline.Selected);
   var colors=new ToolStripMenuItem(ids.Count>1?"Color de los clips seleccionados":"Color del clip");
   colors.Enabled=ids.Count>0&&ids.All(id=>{var clip=editor.Project.Active.clips.Find(c=>c.id==id);var track=editor.Project.Active.tracks.Find(t=>t.id==clip.track_id);return track!=null&&!track.locked;});
   var reset=new ToolStripMenuItem("Predeterminado (según tipo)");
   reset.Checked=ids.Count>0&&ids.All(id=>ClipLabels.Get(editor.Project.Active.clips.Find(c=>c.id==id))==null);
   reset.Click+=(s,e)=>ApplyClipColorSafely(ids,null);
   colors.DropDownItems.Add(reset);colors.DropDownItems.Add(new ToolStripSeparator());
   foreach(var option in ClipLabels.Palette){string id=option.Id;var entry=new ToolStripMenuItem(option.Name){Image=ClipLabels.Swatch(option)};
    entry.Checked=ids.Count>0&&ids.All(cid=>ClipLabels.Get(editor.Project.Active.clips.Find(c=>c.id==cid))==id);
    entry.Click+=(s,e)=>ApplyClipColorSafely(ids,id);colors.DropDownItems.Add(entry);
   }
   menu.Items.Add(colors);
  }
  void ApplyClipColorSafely(List<string> ids,string colorId){try{ApplyClipColor(ids,colorId);}catch(Exception ex){Error(ex);}}
  void ApplyClipColor(List<string> ids,string colorId){
   if(busy)throw new Exception("Espera o cancela la tarea en curso");
   if(ids==null||ids.Count==0)return;
   if(ids.All(id=>{var clip=editor.Project.Active.clips.Find(c=>c.id==id);return clip!=null&&ClipLabels.Get(clip)==colorId;}))return;
   editor.Commit(p=>{foreach(string id in ids){var clip=p.Active.clips.Find(c=>c.id==id);if(clip==null)continue;var track=p.Active.tracks.Find(t=>t.id==clip.track_id);if(track==null||track.locked)throw new Exception("Hay una pista bloqueada en la selección");ClipLabels.Set(clip,colorId);}});
   dirty=true;RefreshProject();status.Text=ids.Count==1?"Color del clip actualizado":"Color aplicado a "+ids.Count+" clips";
  }
 }
}
