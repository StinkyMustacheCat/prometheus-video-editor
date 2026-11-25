using System;
using System.IO;
using System.Text.RegularExpressions;
using Prometheus.Core.Model;

namespace Prometheus.Client.Services;

public static class ExportPresentation
{
    public static string SuggestedName(Project project, Sequence sequence)
    {
        var name = sequence.Name.Trim();
        if (Regex.IsMatch(name, @"^(Montaje|Secuencia|Sequence)\s+\d+$", RegexOptions.IgnoreCase))
            name = $"{project.Name.Trim()} - {name}";
        foreach (var character in Path.GetInvalidFileNameChars()) name = name.Replace(character, '_');
        name = name.Trim().TrimEnd('.', ' ');
        if (name.Length == 0) name = "Exportación";
        if (Regex.IsMatch(name.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase))
            name = "_" + name;
        return name;
    }

    /// <summary>Encoding metadata only: never replace the design sequence in the project.</summary>
    public static Sequence OutputSequence(Sequence sequence, GpuExportOptions options)
    {
        if (options.Width.HasValue != options.Height.HasValue)
            throw new ArgumentException("Indica tanto ancho como alto de salida.");
        var width = options.Width ?? sequence.Width;
        var height = options.Height ?? sequence.Height;
        try { CanvasResolutions.ValidateDimensions(width, height, minimumSide: 2); }
        catch (ArgumentOutOfRangeException error) { throw new ArgumentException(error.Message); }
        if (!CanvasResolutions.SameAspect(sequence.Width, sequence.Height, width, height))
            throw new ArgumentException("La salida debe conservar la proporción del montaje. Cambia la proporción en los ajustes del lienzo.");
        return new Sequence { Width = width, Height = height, Fps = sequence.Fps,
            ColorSpace = sequence.ColorSpace, MixSampleRate = sequence.MixSampleRate, MixChannels = sequence.MixChannels };
    }
}
