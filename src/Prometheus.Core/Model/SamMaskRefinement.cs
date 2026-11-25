using System;

namespace Prometheus.Core.Model;

public readonly record struct SamMaskRefinement(double FeatherPixels,
    double ExpandPixels, bool Inverted)
{
    public static SamMaskRefinement Neutral => new(0, 0, false);

    public SamMaskRefinement Clamped() => new(
        Math.Clamp(double.IsFinite(FeatherPixels) ? FeatherPixels : 0, 0, 20),
        Math.Clamp(double.IsFinite(ExpandPixels) ? ExpandPixels : 0, -20, 20),
        Inverted);
}
