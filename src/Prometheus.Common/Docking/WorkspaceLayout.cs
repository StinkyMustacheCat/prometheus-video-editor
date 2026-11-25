using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus.Common.Docking;

public class WorkspaceLayout
{
    public string Name { get; set; } = "Personalizado";

    public bool IsFactoryPreset { get; set; }

    public LayoutNode? RootNode { get; set; }

    public List<FloatingWindowDescriptor> FloatingPanels { get; set; } = new();

    public List<string> MinimizedPanels { get; set; } = new();

    public List<string> ClosedPanels { get; set; } = new();

    public PanelDockState GetPanelState(string panelId)
    {
        if (MinimizedPanels.Contains(panelId, StringComparer.OrdinalIgnoreCase))
            return PanelDockState.Minimized;

        if (FloatingPanels.Any(f => string.Equals(f.PanelId, panelId, StringComparison.OrdinalIgnoreCase)))
            return PanelDockState.Floating;

        if (RootNode?.FindNode(panelId) != null)
            return PanelDockState.Docked;

        return PanelDockState.Closed;
    }

    public WorkspaceLayout Clone()
    {
        return new WorkspaceLayout
        {
            Name = Name,
            IsFactoryPreset = IsFactoryPreset,
            RootNode = RootNode?.Clone(),
            FloatingPanels = FloatingPanels.Select(f => new FloatingWindowDescriptor(f.PanelId, f.X, f.Y, f.Width, f.Height)).ToList(),
            MinimizedPanels = new List<string>(MinimizedPanels),
            ClosedPanels = new List<string>(ClosedPanels)
        };
    }
}
