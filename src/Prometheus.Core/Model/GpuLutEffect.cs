using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using Prometheus.Core.Storage;

namespace Prometheus.Core.Model;

public sealed record CubeLut(int Size, Vector3 DomainMin, Vector3 DomainMax, float[] Rgba);

public static class GpuLutEffect
{
    public const string Id = "prometheus.color.lut3d";

    public static void Add(Clip clip)
    {
        var existing = clip.Effects.FirstOrDefault(e => e.EffectId == Id);
        if (existing != null) { existing.Enabled = true; return; }
        var effect = new EffectInstance { EffectId = Id };
        effect.Parameters["lut"] = new EffectValue { Kind = "resource" };
        effect.Parameters["interpolation"] = new EffectValue { Kind = "text", Text = "trilinear" };
        clip.Effects.Add(effect);
    }

    public static string? ResolvePath(Project project, Clip clip, string? projectPath)
    {
        var effect = clip.Effects.FirstOrDefault(e => e.EffectId == Id && e.Enabled);
        if (effect == null || !effect.Parameters.TryGetValue("lut", out var value)) return null;
        var resource = project.EffectResources.FirstOrDefault(r => r.Id == value.ResourceId);
        if (resource == null || string.IsNullOrWhiteSpace(resource.Uri)) return null;
        var path = resource.Relative && projectPath != null
            ? Path.Combine(ProjectStorage.GetDataDirectory(projectPath), resource.Uri)
            : resource.Uri;
        return File.Exists(path) ? Path.GetFullPath(path) : null;
    }

    public static bool IsTetrahedral(Clip clip) => clip.Effects
        .FirstOrDefault(e => e.EffectId == Id)?.Parameters.TryGetValue("interpolation", out var value) == true &&
        value.Text == "tetrahedral";

    public static CubeLut ParseCube(string path)
    {
        var size = 0;
        var min = Vector3.Zero;
        var max = Vector3.One;
        var colors = new System.Collections.Generic.List<float>();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Split('#', 2)[0].Trim();
            if (line.Length == 0 || line.StartsWith("TITLE", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts[0].Equals("LUT_3D_SIZE", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out size) || size < 2 || size > 128)
                    throw new InvalidDataException("Tamaño de LUT 3D no válido (2 a 128).");
                continue;
            }
            if (parts[0].Equals("DOMAIN_MIN", StringComparison.OrdinalIgnoreCase)) { min = ReadVector(parts); continue; }
            if (parts[0].Equals("DOMAIN_MAX", StringComparison.OrdinalIgnoreCase)) { max = ReadVector(parts); continue; }
            if (parts[0].StartsWith("LUT_", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Solo se admiten LUT 3D .cube.");
            if (size == 0 || parts.Length != 3 || colors.Count >= size * size * size * 4)
                throw new InvalidDataException("Datos de LUT 3D no válidos.");
            var color = ReadVector(parts);
            colors.Add(color.X); colors.Add(color.Y); colors.Add(color.Z); colors.Add(1);
        }
        if (size == 0 || colors.Count != size * size * size * 4 ||
            max.X <= min.X || max.Y <= min.Y || max.Z <= min.Z)
            throw new InvalidDataException("LUT 3D incompleta o dominio no válido.");
        return new CubeLut(size, min, max, colors.ToArray());
    }

    private static Vector3 ReadVector(string[] parts)
    {
        if (parts.Length != 3 && parts.Length != 4) throw new InvalidDataException("Vector de LUT no válido.");
        var offset = parts.Length - 3;
        var v = new Vector3(Parse(parts[offset]), Parse(parts[offset + 1]), Parse(parts[offset + 2]));
        if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z))
            throw new InvalidDataException("LUT contiene valores no finitos.");
        return v;
    }

    private static float Parse(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
}
