using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Prometheus.Common.Docking;

namespace Prometheus.Common.Persistence;

public class LayoutPersistenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string LayoutFilePath { get; }

    public LayoutPersistenceService(string? customPath = null)
    {
        if (!string.IsNullOrEmpty(customPath))
        {
            LayoutFilePath = customPath;
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var prometheusDir = Path.Combine(localAppData, "Prometheus");
            LayoutFilePath = Path.Combine(prometheusDir, "layout-v1.json");
        }
    }

    public bool SaveLayout(WorkspaceLayout layout)
    {
        try
        {
            var directory = Path.GetDirectoryName(LayoutFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = LayoutFilePath + ".tmp";
            var json = JsonSerializer.Serialize(layout, JsonOptions);

            File.WriteAllText(tempPath, json);

            if (File.Exists(LayoutFilePath))
            {
                File.Replace(tempPath, LayoutFilePath, null);
            }
            else
            {
                File.Move(tempPath, LayoutFilePath);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public WorkspaceLayout LoadLayout()
    {
        try
        {
            if (!File.Exists(LayoutFilePath))
            {
                return FactoryPresets.CreateEquilibrado();
            }

            var json = File.ReadAllText(LayoutFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return FactoryPresets.CreateEquilibrado();
            }

            var layout = JsonSerializer.Deserialize<WorkspaceLayout>(json, JsonOptions);
            if (layout == null || layout.RootNode == null)
            {
                return FactoryPresets.CreateEquilibrado();
            }

            MigrateMediaControls(layout);
            return layout;
        }
        catch
        {
            return FactoryPresets.CreateEquilibrado();
        }
    }

    private static void MigrateMediaControls(WorkspaceLayout layout)
    {
        const string obsolete = "effect_controls";
        const string current = "properties";
        var hasCurrent = layout.RootNode?.FindNode(current) != null ||
            layout.FloatingPanels.Any(p => p.PanelId == current) || layout.MinimizedPanels.Contains(current);
        if (hasCurrent)
        {
            layout.RootNode = LayoutNode.Remove(layout.RootNode, obsolete);
            layout.FloatingPanels.RemoveAll(p => p.PanelId == obsolete);
            layout.MinimizedPanels.RemoveAll(p => p == obsolete);
        }
        else
        {
            if (layout.RootNode?.FindNode(obsolete) is { } oldNode) oldNode.PanelId = current;
            foreach (var panel in layout.FloatingPanels.Where(p => p.PanelId == obsolete)) panel.PanelId = current;
            for (var i = 0; i < layout.MinimizedPanels.Count; i++)
                if (layout.MinimizedPanels[i] == obsolete) layout.MinimizedPanels[i] = current;
            if (layout.RootNode?.FindNode(current) != null || layout.FloatingPanels.Any(p => p.PanelId == current) || layout.MinimizedPanels.Contains(current))
                layout.ClosedPanels.RemoveAll(p => p == current);
        }
        layout.ClosedPanels.RemoveAll(p => p == obsolete);
    }
}
