using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Core.Preflight;

public static class PreflightAuditor
{

    public static PreflightReport Audit(Project project, string? projectDirectory = null, bool autoPurgeOrphans = true)
    {
        var report = new PreflightReport();

        if (project == null)
        {
            report.Errors.Add("El proyecto es nulo.");
            return report;
        }

        if (project.Format != "prometheus-project")
        {
            report.Errors.Add($"Formato de proyecto desconocido: '{project.Format}'. Se esperaba 'prometheus-project'.");
            return report;
        }

        // 1. Identificadores Únicos
        var uniqueIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void CheckId(string id, string context)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                report.Errors.Add($"Identificador vacío o nulo en {context}.");
            }
            else if (!uniqueIds.Add(id))
            {
                report.Errors.Add($"Identificador duplicado detectado: '{id}' en {context}.");
            }
        }

        CheckId(project.Id, "Project.Id");

        foreach (var asset in project.Assets)
        {
            CheckId(asset.Id, $"Asset '{asset.Name}'");
            foreach (var stream in asset.Streams)
            {
                CheckId(stream.Id, $"Stream #{stream.Ordinal} en Asset '{asset.Name}'");
            }
        }

        foreach (var effect in project.EffectResources)
        {
            CheckId(effect.Id, $"EffectResource '{effect.Uri}'");
        }

        if (project.Sequences.Count == 0)
        {
            report.Errors.Add("El proyecto no contiene ninguna secuencia maestra.");
            return report;
        }

        var sequence = project.ActiveSequence;
        if (sequence == null)
        {
            report.Errors.Add("El proyecto no tiene una secuencia activa definida.");
            return report;
        }

        foreach (var seq in project.Sequences)
        {
            CheckId(seq.Id, $"Sequence '{seq.Name}'");

            foreach (var track in seq.Tracks)
            {
                CheckId(track.Id, $"Track '{track.Name}'");
            }

            foreach (var clip in seq.Clips)
            {
                CheckId(clip.Id, $"Clip '{clip.Name}'");

                foreach (var effect in clip.Effects)
                {
                    CheckId(effect.Id, $"Effect '{effect.EffectId}' en Clip '{clip.Name}'");
                }
            }

            foreach (var lg in seq.LinkGroups)
            {
                CheckId(lg.Id, "LinkGroup");
            }

            foreach (var eg in seq.EditGroups)
            {
                CheckId(eg.Id, $"EditGroup #{eg.Number}");
            }
        }

        // 2. Monotonía Temporal y límites
        foreach (var seq in project.Sequences)
        {
            var validClips = new HashSet<string>(seq.Clips.Select(c => c.Id));
            // Older projects allowed 1/60-second clips even in lower-FPS sequences.
            var minClipDurationSeconds = Prometheus.Core.Timeline.EditTiming.MinimumDuration(seq);

            foreach (var clip in seq.Clips)
            {
                if (clip.NestedContent != null)
                {
                    try { NestedMediaOperations.Validate(project, clip); }
                    catch (Exception ex) { report.Errors.Add($"Medio anidado '{clip.Name}': {ex.Message}"); }
                }
                if (clip.TimelineStart.Seconds < 0)
                {
                    report.Errors.Add($"Violación de monotonía: Clip '{clip.Name}' tiene marca de inicio negativa ({clip.TimelineStart.Seconds:F4}s).");
                }

                if (clip.Duration.Seconds < minClipDurationSeconds - 0.0001)
                {
                    report.Errors.Add($"Violación de monotonía: Clip '{clip.Name}' dura menos de 1 fotograma ({clip.Duration.Seconds:F4}s < {minClipDurationSeconds:F4}s).");
                }

                if (clip.SourceIn.Seconds < 0)
                {
                    report.Errors.Add($"Clip '{clip.Name}' tiene source_in negativo ({clip.SourceIn.Seconds:F4}s).");
                }

                if (clip.Speed.Seconds <= 0)
                {
                    report.Errors.Add($"Clip '{clip.Name}' tiene velocidad inválida ({clip.Speed.Seconds:F4}).");
                }
            }

            if (autoPurgeOrphans)
            {
                VideoTransitionOperations.Reconcile(project, seq);
                AudioTransitionOperations.Reconcile(project, seq.Clips, seq.Tracks, seq.AudioTransitions);
            }
            foreach (var transition in seq.AudioTransitions)
                CheckId(transition.Id, "Transición de audio");
            foreach (var transition in seq.VideoTransitions)
                CheckId(transition.Id, "Transición de video");

            // 3. Consistencia de Vínculos y Purga Silenciosa de Huérfanos
            if (autoPurgeOrphans)
            {
                // LinkGroups
                for (int i = seq.LinkGroups.Count - 1; i >= 0; i--)
                {
                    var lg = seq.LinkGroups[i];
                    var liveMembers = lg.ClipIds.Where(id => validClips.Contains(id)).Distinct().ToList();

                    if (liveMembers.Count < lg.ClipIds.Count)
                    {
                        report.ActionsTaken.Add($"Purgadas {lg.ClipIds.Count - liveMembers.Count} referencias huérfanas en LinkGroup '{lg.Id}'.");
                    }

                    if (liveMembers.Count < 2)
                    {
                        seq.LinkGroups.RemoveAt(i);
                        report.ActionsTaken.Add($"Eliminado LinkGroup incompleto '{lg.Id}' (< 2 miembros activos).");
                    }
                    else
                    {
                        lg.ClipIds = liveMembers;
                    }
                }

                // EditGroups
                for (int i = seq.EditGroups.Count - 1; i >= 0; i--)
                {
                    var eg = seq.EditGroups[i];
                    var liveMembers = eg.ClipIds.Where(id => validClips.Contains(id)).Distinct().ToList();

                    if (liveMembers.Count < eg.ClipIds.Count)
                    {
                        report.ActionsTaken.Add($"Purgadas {eg.ClipIds.Count - liveMembers.Count} referencias huérfanas en EditGroup #{eg.Number}.");
                    }

                    if (liveMembers.Count < 2)
                    {
                        seq.EditGroups.RemoveAt(i);
                        report.ActionsTaken.Add($"Eliminado EditGroup #{eg.Number} (< 2 miembros activos).");
                    }
                    else
                    {
                        eg.ClipIds = liveMembers;
                    }
                }
            }
        }

        // 4. Detección de Medios Desconectados (Offline)
        if (!string.IsNullOrEmpty(projectDirectory) && Directory.Exists(projectDirectory))
        {
            foreach (var asset in project.Assets)
            {
                if (asset.Source.Kind is "local" or "mgt" || asset.Source.Kind == "web" &&
                    asset.Representations.Count > 0)
                {
                    var locator = asset.EffectiveLocator;
                    if (locator != null && !string.IsNullOrWhiteSpace(locator.Path) &&
                        !(Uri.TryCreate(locator.Path, UriKind.Absolute, out var remote) &&
                          remote.Scheme is "http" or "https"))
                    {
                        string fullPath = locator.Relative || !Path.IsPathRooted(locator.Path)
                            ? Path.GetFullPath(Path.Combine(projectDirectory, locator.Path))
                            : locator.Path;

                        if (!File.Exists(fullPath))
                        {
                            report.MissingAssets.Add(new MissingAssetInfo
                            {
                                AssetId = asset.Id,
                                AssetName = asset.Name,
                                ExpectedPath = fullPath,
                                Kind = asset.Source.Kind
                            });
                            report.Warnings.Add($"Medio desconectado: Asset '{asset.Name}' no existe en '{fullPath}'.");
                        }
                    }
                }
            }
        }

        return report;
    }
}
