using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Prometheus.Core.Model;

/// <summary>A clip-wide vector mask in normalized source coordinates.</summary>
public sealed class ShapeMask
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "rectangle";

    [JsonPropertyName("points")]
    public List<ShapeMaskPoint> Points { get; set; } = new();

    public void Validate()
    {
        if (Kind is not ("rectangle" or "ellipse" or "polygon") ||
            Points.Count != (Kind == "polygon" ? Math.Clamp(Points.Count, 3, 64) : 2) ||
            Points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) ||
                p.X < 0 || p.X > 1 || p.Y < 0 || p.Y > 1))
            throw new ArgumentException("La máscara de forma no es válida.");
        if (Kind != "polygon" &&
            (Math.Abs(Points[1].X - Points[0].X) < 0.001 ||
             Math.Abs(Points[1].Y - Points[0].Y) < 0.001))
            throw new ArgumentException("La máscara de forma no tiene superficie.");
        if (Kind == "polygon")
        {
            double area = 0;
            for (var i = 0; i < Points.Count; i++)
            {
                var a = Points[i]; var b = Points[(i + 1) % Points.Count];
                area += a.X * b.Y - b.X * a.Y;
            }
            if (Math.Abs(area) < 0.00001)
                throw new ArgumentException("La máscara de forma no tiene superficie.");
        }
    }
}

public sealed class ShapeMaskPoint
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    public ShapeMaskPoint() { }
    public ShapeMaskPoint(double x, double y) { X = x; Y = y; }
}
