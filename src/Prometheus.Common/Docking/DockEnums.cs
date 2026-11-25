namespace Prometheus.Common.Docking;

public enum LayoutNodeType
{
    Split,
    Leaf
}

public enum SplitOrientation
{
    Horizontal, // Divides Left / Right
    Vertical    // Divides Top / Bottom
}

public enum PanelDockState
{
    Docked,
    Floating,
    Minimized,
    Closed
}

public enum EdgeGutterPosition
{
    Left,
    Top,
    Right,
    Bottom
}
