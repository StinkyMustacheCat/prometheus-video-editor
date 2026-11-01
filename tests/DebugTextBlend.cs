using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using Prometheus;

class DebugTextBlend {
    static void Main() {
        Rendering.GeneratedCacheRoot = Path.Combine(Path.GetTempPath(), "prom-cache-" + Guid.NewGuid().ToString("N"));

        var p = Store.New();
        var videoTracks = p.Active.tracks.Where(t => t.kind == "video").OrderBy(t => t.order).ToList();
        var track1 = videoTracks[0];
        var track2 = videoTracks[1];

        var white = GeneratedMedia.CreateSolid("White", "FFFFFF", 5);
        p.assets.Add(white);
        Media.Insert(p, white.id, track1.id, 0);

        var textAsset = GeneratedMedia.CreateText("TEST", 5);
        p.assets.Add(textAsset);
        Media.Insert(p, textAsset.id, track2.id, 0);

        var textClip = p.Active.clips.First(c => c.asset_id == textAsset.id);
        BlendModes01034.Set(textClip, "multiply");

        // Let us inspect the Build command!
        string args = Rendering.Build(p, true, false, 320, 180, 60, "raw", 0, true);
        Console.WriteLine("ARGS:\n" + args);
    }
}