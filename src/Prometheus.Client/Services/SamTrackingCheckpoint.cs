using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Prometheus.Core.Model;
using Prometheus.Core.Storage;

namespace Prometheus.Client.Services;

/// <summary>Completed SAM windows are durable, so Cancel can resume from the last window.</summary>
internal sealed class SamTrackingCheckpoint
{
    internal sealed class Chunk
    {
        public int Low { get; set; }
        public int High { get; set; }
        public bool Reverse { get; set; }
        public List<SamMaskKeyframe> Frames { get; set; } = new();
        public SamMaskKeyframe Boundary { get; set; } = new();
    }

    private readonly string _folder;

    public SamTrackingCheckpoint(string projectPath, Project project, Clip clip,
        string sourcePath, SamMaskKeyframe seed, double fps, int count, int anchor,
        string mode)
    {
        var file = new FileInfo(sourcePath);
        var signature = JsonSerializer.Serialize(new
        {
            version = 1, mode, projectId = project.Id, clipId = clip.Id,
            sourcePath = Path.GetFullPath(sourcePath),
            file.Length, file.LastWriteTimeUtc.Ticks, clip.SourceIn, clip.Duration, clip.Speed,
            fps, count, anchor, seed, clip.Effects, clip.SamMasks
        });
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)));
        var clipKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clip.Id)))[..16];
        _folder = Path.Combine(ProjectStorage.GetDataDirectory(projectPath),
            "cache", "sam", clipKey, key);
    }

    private string FileFor(int low, int high, bool reverse) => Path.Combine(_folder,
        (reverse ? "r" : "f") + "-" + low + "-" + high + ".json");

    public bool TryLoad(int low, int high, bool reverse, out Chunk? chunk)
    {
        chunk = null;
        var path = FileFor(low, high, reverse);
        if (!File.Exists(path)) return false;
        try
        {
            var saved = JsonSerializer.Deserialize<Chunk>(File.ReadAllText(path));
            if (saved == null || saved.Low != low || saved.High != high ||
                saved.Reverse != reverse || saved.Frames.Count != high - low + 1 ||
                saved.Frames.Any(frame => frame.Width < 1 || frame.Height < 1) ||
                saved.Boundary.Width < 1 || saved.Boundary.Height < 1)
                throw new InvalidDataException("Ventana SAM incompleta.");
            foreach (var frame in saved.Frames) frame.DecodeAlpha();
            if (saved.Boundary.DecodeObjectAlphas().Count == 0)
                throw new InvalidDataException("Semilla SAM incompleta.");
            chunk = saved;
            return true;
        }
        catch
        {
            File.Delete(path);
            return false;
        }
    }

    public void Save(Chunk chunk)
    {
        Directory.CreateDirectory(_folder);
        var path = FileFor(chunk.Low, chunk.High, chunk.Reverse);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(chunk));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Complete()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
