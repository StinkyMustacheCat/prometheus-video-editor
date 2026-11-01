using System;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus;
class MilestoneTests095 {
 static int Main(string[] args){try{var asset=Media.Inspect(Path.GetFullPath(args[0]),CancellationToken.None);asset.source.kind="web";asset.source.uri="https://example.test/video";asset.source.resolver_type="youtube/yt-dlp";asset.representations[0].role="final";WebMedia.PreviewHeight=720;var rep=WebMedia.PreviewFromFull(asset,Path.GetFullPath(args[1]),CancellationToken.None,null);var proxy=Media.Inspect(rep.locator.path,CancellationToken.None);var visual=proxy.streams.First(s=>s.kind=="video");if(visual.height>720||visual.height<700)throw new Exception("La vista ligera no respetó 720p: "+visual.height);if(!File.Exists(asset.representations[0].locator.path))throw new Exception("La fuente completa se perdió");Console.WriteLine("PASS preview-from-full 720p");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}

