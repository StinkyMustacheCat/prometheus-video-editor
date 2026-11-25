using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Services;

internal static class MgtTransitionCatalog
{
#if MGT_TRANSITION_PILOT
    public static bool ImportEnabled => false;
#else
    public static bool ImportEnabled => true;
#endif

    public static IReadOnlyList<MgtGpuTransition> Factory { get; } = LoadFactory();

    public static IEnumerable<MgtGpuTransition> All(Project? project) =>
        Factory.Concat(project?.MgtTransitionLibrary ?? []).DistinctBy(t => t.Id, StringComparer.Ordinal);

    public static MgtGpuTransition? Find(Project? project, string id) =>
        All(project).FirstOrDefault(t => t.Id == id);

    public static bool Supports(Project? project, string kind) =>
        VideoTransitionOperations.Supports(kind) || Find(project, kind) != null;

    public static EffectCatalogEntry Entry(MgtGpuTransition transition) =>
        new(transition.Id, transition.Name, "transitions", "MGT", "Transición GPU entre dos clips de video");

    private static IReadOnlyList<MgtGpuTransition> LoadFactory()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Runtime", "MgtTransitions");
        if (!Directory.Exists(root)) return [];
        var templates = new List<MgtGpuTransition>();
        foreach (var file in Directory.EnumerateFiles(root, "*.mgt.json"))
        {
            try { templates.Add(MgtGpuTransition.Parse(File.ReadAllText(file))); }
            catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or System.Text.Json.JsonException)
            {
                System.Diagnostics.Trace.TraceError("Transición de fábrica {0}: {1}", Path.GetFileName(file), error.Message);
            }
        }
        return templates.OrderBy(t => t.Name, StringComparer.CurrentCulture).ToArray();
    }
}
