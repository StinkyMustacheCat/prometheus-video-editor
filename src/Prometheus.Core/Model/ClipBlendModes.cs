namespace Prometheus.Core.Model;

public static class ClipBlendModes
{
    public sealed record Mode(string Id, string Name, int GpuId);
    public static readonly IReadOnlyList<Mode> All = Array.AsReadOnly(new[] {
        new Mode("normal", "Normal", 0), new("darken", "Oscurecer", 1),
        new("multiply", "Multiplicar", 2), new("color-burn", "Subexponer color", 3),
        new("lighten", "Aclarar", 4), new("screen", "Trama", 5),
        new("color-dodge", "Sobreexponer color", 6), new("add", "Añadir", 7),
        new("overlay", "Superponer", 8), new("soft-light", "Luz suave", 9),
        new("hard-light", "Luz fuerte", 10), new("difference", "Diferencia", 11),
        new("exclusion", "Exclusión", 12), new("subtract", "Restar", 13),
        new("hue", "Tono", 14), new("saturation", "Saturación", 15),
        new("color", "Color", 16), new("luminosity", "Luminosidad", 17)
    });
    public static bool IsSupported(string? id) => All.Any(m => m.Id == id);
    public static int GpuId(string? id) => All.FirstOrDefault(m => m.Id == id)?.GpuId ?? 0;
}
