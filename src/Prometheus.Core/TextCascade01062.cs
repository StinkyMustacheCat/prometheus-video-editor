using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 // A two-line title built from independent letters. The nested clip keeps the
 // ordinary clip controls and can be moved, trimmed, or edited like other media.
 public static class TextCascade01062 {
  static readonly string[] NumberKeys={"stroke_px","shadow_x","shadow_y","shadow_blur","shadow_opacity"};
  static readonly string[] ColorKeys={"stroke_color","shadow_color"};
  static void CopyStyle(Asset original,Asset letter,string glyph){
   GeneratedMedia.SetText(letter,glyph,GeneratedMedia.TextSize(original),GeneratedMedia.TextColor(original));
   GeneratedMedia.SetTextFont(letter,GeneratedMedia.TextFont(original));
   foreach(var key in NumberKeys)GeneratedMedia.SetTextStyleNumber(letter,key,GeneratedMedia.TextStyleNumber(original,key,key=="stroke_px"?4:0));
   foreach(var key in ColorKeys)GeneratedMedia.SetTextStyleColor(letter,key,GeneratedMedia.TextStyleColor(original,key,key=="stroke_color"?"090B10":"000000"));
  }
  public static Clip Apply(Project project,string clipId){
   var sequence=project.Active;var original=sequence.clips.Find(c=>c.id==clipId);if(original==null)throw new Exception("Selecciona un clip de texto");
   var source=project.assets.Find(a=>a.id==original.asset_id);var track=sequence.tracks.Find(t=>t.id==original.track_id);
   if(source==null||!GeneratedMedia.IsText(source)||GeneratedMedia.IsSubtitle(source)||track==null||track.kind!="video"||track.locked)throw new Exception("La animación requiere un título de texto editable");
   string[] lines=GeneratedMedia.Text(source).Replace("\r","").Split(new[]{'\n'},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim()).Where(x=>x.Length>0).ToArray();
   if(lines.Length==1){int split=lines[0].IndexOf(' ');if(split>0)lines=new[]{lines[0].Substring(0,split).Trim(),lines[0].Substring(split+1).Trim()};}
   if(lines.Length!=2||lines.Any(x=>x.Length==0)||lines.Sum(x=>x.Count(c=>!Char.IsWhiteSpace(c)))>36)throw new Exception("Escribe dos líneas cortas para animar las letras");
   double duration=original.duration.Seconds;if(duration<1.5)throw new Exception("El título necesita al menos 1,5 segundos");
   var content=new NestedContent0910();int order=0;
   for(int row=0;row<2;row++){
    string word=lines[row];double step=Math.Min(.038,.42/Math.Max(1,word.Length)),startX=-(word.Length-1)*step/2,y=row==0?-.15:.15;
    for(int i=0;i<word.Length;i++){
     if(Char.IsWhiteSpace(word[i]))continue;
     var asset=GeneratedMedia.CreateText(word[i].ToString(),duration);CopyStyle(source,asset,word[i].ToString());project.assets.Add(asset);
     string innerTrack=Store.Id();content.video_orders[innerTrack]=order++;
     var child=new Clip{id=Store.Id(),name=word[i].ToString(),asset_id=asset.id,stream_id=asset.streams[0].id,track_id=innerTrack,timeline_start=Rational.Time(0),duration=Rational.Frame(duration),source_in=Rational.Time(0)};
     double targetX=startX+i*step,settle=Math.Min(duration*.60,(row==0?.48:.75)+i*.05),exitStart=Math.Max(settle+.15,duration-.55);
     ClipFraming.Set(child,"fit",1,targetX,y);
     if(row==0){ClipKeyframes.Set(child,"transform.y",0,-.96);ClipKeyframes.Set(child,"transform.y",settle,y);}
     else{ClipKeyframes.Set(child,"transform.x",0,-1);ClipKeyframes.Set(child,"transform.x",Math.Min(duration*.35,.28+i*.02),-1);ClipKeyframes.Set(child,"transform.x",settle,targetX);}
     ClipKeyframes.Set(child,"transform.x",exitStart,targetX);
     ClipKeyframes.Set(child,"transform.y",exitStart,y);
     ClipKeyframes.Set(child,"transform.x",duration,targetX+(i%2==0?-.33:.33));
     ClipKeyframes.Set(child,"transform.y",duration,1);
     content.clips.Add(child);
    }
   }
   string id=Store.Id();var nested=new Asset{id=id,name="Letras en cascada · "+source.name,source=new MediaSource{kind="generated",uri="generated:"+id,resolver_type="nested",imported_at=DateTime.UtcNow.ToString("o")},presentation_origin=Rational.Time(0),recommended_duration=Rational.Frame(duration),container_format="generated"};
   nested.streams.Add(new StreamInfo{id=Store.Id(),ordinal=0,kind="image",codec="nested",pts_origin=Rational.Time(0),time_base=new Rational(1,60),width=sequence.width,height=sequence.height});
   nested.extensions.Add(new Extension{@namespace=GeneratedMedia.Namespace,version="1",payload=new Dictionary<string,object>{{"kind","nested"},{"color_hex","303C49"},{"note","Animación de letras"}}});
   var outer=Store.Json().Deserialize<Clip>(Store.Json().Serialize(Store.Fields(original)));outer.id=Store.Id();outer.name=nested.name;outer.asset_id=nested.id;outer.stream_id=nested.streams[0].id;outer.source_in=Rational.Time(0);outer.speed=new Rational(1,1);outer.animation=new List<KeyframeCurve>();outer.extensions.RemoveAll(e=>e.@namespace==NestedMedia0910.Namespace);NestedMedia0910.Write(outer,content);
   sequence.clips.Remove(original);sequence.link_groups.RemoveAll(g=>g.clip_ids.Contains(original.id));sequence.edit_groups.RemoveAll(g=>g.clip_ids.Contains(original.id));sequence.transitions.RemoveAll(t=>t.left_clip_id==original.id||t.right_clip_id==original.id);project.assets.Add(nested);sequence.clips.Add(outer);return outer;
  }
 }
}
