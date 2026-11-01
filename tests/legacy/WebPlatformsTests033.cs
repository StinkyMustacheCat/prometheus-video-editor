using System;
using System.Linq;
using System.Windows.Forms;
using Prometheus;

class WebPlatformsTests033 {
 static void Check(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Classify(string url,string expected){Check(WebMedia.Platform(url)==expected,expected+" reconoce enlace");}
 [STAThread]static void Main(){
  Classify("https://www.youtube.com/watch?v=jNQXAC9IVRw","youtube");
  Classify("https://www.instagram.com/reel/ABC123/","instagram");
  Classify("https://www.tiktok.com/@creator/video/7059698374567611694","tiktok");
  Classify("https://www.facebook.com/reel/123456789","facebook");
  Classify("https://x.com/creator/status/1578401165338976258","twitter");
  Classify("https://clips.twitch.tv/FriendlyClipSlug","twitch");
  Classify("https://www.twitch.tv/videos/1536751224","twitch");
  Check(WebMedia.Platform("https://www.twitch.tv/creator")==null,"un directo de Twitch no se acepta como VOD");
  Check(WebMedia.Platform("https://www.tiktok.com/@creator")==null,"una página de perfil no se acepta como video");
  Check(WebMedia.Platform("https://fake-x.com/a/status/123")==null,"un dominio parecido no se acepta");
  var text=new DataObject();text.SetData(DataFormats.UnicodeText,"https://x.com/creator/status/1578401165338976258");
  Check(WebDrop.Read(text).Url.Contains("x.com/creator/status"),"arrastre de enlace X se reconoce");
  string twitter="{\"id\":\"post123\",\"title\":\"Clip de prueba\",\"duration\":8.4,\"thumbnail\":\"https://pbs.twimg.com/media/pic.jpg\",\"webpage_url\":\"https://x.com/creator/status/1578401165338976258\",\"formats\":[{\"width\":1280,\"height\":720,\"fps\":30,\"acodec\":\"aac\"}]}";
  var asset=WebMedia.FromMetadata("https://x.com/creator/status/1578401165338976258",twitter);
  Check(asset.source.resolver_type=="yt-dlp/twitter"&&asset.streams.Count==2&&asset.streams[0].width==1280&&asset.streams[0].duration.Seconds>8,"X crea medio virtual con duración y audio");
  var project=Store.New();project.assets.Add(asset);Media.Insert(project,asset.id,project.Active.tracks.First(t=>t.kind=="video").id,0);Store.Validate(Store.Copy(project));
  Check(project.Active.clips.Any(c=>c.asset_id==asset.id),"medio X virtual entra en la línea de tiempo");
  string twitch="{\"id\":\"v1536751224\",\"title\":\"VOD de prueba\",\"duration\":3600,\"live_status\":\"was_live\",\"acodec\":\"aac\",\"vcodec\":\"h264\"}";
  asset=WebMedia.FromMetadata("https://www.twitch.tv/videos/1536751224",twitch);
  Check(asset.source.resolver_type=="yt-dlp/twitch"&&asset.streams[0].duration.Seconds==3600,"VOD terminado de Twitch conserva duración");
  bool rejected=false;try{WebMedia.FromMetadata("https://www.twitch.tv/videos/1536751224",twitch.Replace("was_live","is_live"));}catch(Exception){rejected=true;}Check(rejected,"directo activo se rechaza");
  rejected=false;try{WebMedia.FromMetadata("https://x.com/creator/status/1578401165338976258","{\"_type\":\"playlist\",\"duration\":5}");}catch(Exception){rejected=true;}Check(rejected,"lista se rechaza antes de crear medio");
 }
}
