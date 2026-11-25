using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace Prometheus.Client.Panels;

public partial class TimelinePanelView
{
    private string _selectedMaskTool = "sam";

    private void SelectMaskTool(string kind)
    {
        _selectedMaskTool = kind;
        ToolSamBtn.Content = kind switch
        {
            "rectangle" => LineIcon("M 2 3 L 14 3 L 14 13 L 2 13 Z"),
            "ellipse" => LineIcon("M 8 2 A 6 6 0 1 0 8 14 A 6 6 0 1 0 8 2"),
            "polygon" => LineIcon("M 2 12 L 5 3 L 13 5 L 14 12 Z M 5 3 L 5 4"),
            _ => LineIcon("M 8 2 C 3 2 2 5 2 8 C 2 12 5 14 8 14 C 12 14 14 11 14 8 C 14 4 11 2 8 2 Z M 5 8 L 7 10 L 11 6")
        };
        UpdateShortcutTooltips();
        SetTool(Prometheus.Core.Timeline.TimelineTool.Sam);
    }
    private static Path LineIcon(string data) => new()
    {
        Data = Geometry.Parse(data),
        Stroke = Brushes.White,
        StrokeThickness = 1.7,
        StrokeLineCap = PenLineCap.Round,
        StrokeJoin = PenLineJoin.Round,
        Width = 16,
        Height = 16,
        Stretch = Stretch.Uniform
    };

    private void SetupTimelineIcons()
    {
        ToolVBtn.Content = LineIcon("M 3 2 L 3 14 L 6 11 L 9 16 L 11 15 L 8 10 L 13 10 Z");
        ToolCBtn.Content = LineIcon("M 3 3 L 13 13 M 13 3 L 3 13 M 3 4 L 4 4 M 12 12 L 13 12");
        ToolABtn.Content = LineIcon("M 2 8 L 14 8 M 10 4 L 14 8 L 10 12 M 4 3 L 4 13");
        ToolTextBtn.Content = LineIcon("M 3 4 L 13 4 M 8 4 L 8 13 M 6 13 L 10 13");
        ToolHBtn.Content = LineIcon("M 4 9 L 4 5 Q 4 4 5 4 Q 6 4 6 5 L 6 8 L 6 3 Q 6 2 7 2 Q 8 2 8 3 L 8 8 L 8 4 Q 8 3 9 3 Q 10 3 10 4 L 10 8 L 10 6 Q 10 5 11 5 Q 12 5 12 6 L 12 11 Q 12 15 8 15 L 6 15 Q 4 15 3 12 L 2 10 Q 2 9 3 9 Z");
        ToolZBtn.Content = LineIcon("M 7 2 A 5 5 0 1 0 7 12 A 5 5 0 1 0 7 2 M 11 11 L 15 15 M 7 5 L 7 9 M 5 7 L 9 7");
        ToolSamBtn.Content = LineIcon("M 8 2 C 3 2 2 5 2 8 C 2 12 5 14 8 14 C 12 14 14 11 14 8 C 14 4 11 2 8 2 Z M 5 8 L 7 10 L 11 6 M 8 0 L 8 2 M 8 14 L 8 16");
        SnapToggle.Content = LineIcon("M 3 2 L 3 10 A 5 5 0 0 0 13 10 L 13 2 M 3 2 L 6 2 L 6 5 L 3 5 M 10 2 L 13 2 L 13 5 L 10 5");
        TimelineSettingsButton.Content = LineIcon("M 8 2 L 8 4 M 8 12 L 8 14 M 2 8 L 4 8 M 12 8 L 14 8 M 4 4 L 5.4 5.4 M 10.6 10.6 L 12 12 M 12 4 L 10.6 5.4 M 5.4 10.6 L 4 12 M 8 5 A 3 3 0 1 0 8 11 A 3 3 0 1 0 8 5");
        UpdatePlayIcon(false);
    }

    private void UpdatePlayIcon(bool playing) => PlayPauseBtn.Content = playing
        ? LineIcon("M 5 3 L 5 13 M 11 3 L 11 13")
        : LineIcon("M 5 3 L 13 8 L 5 13 Z");

    private static Control TrackStateIcon(string kind, bool enabled) => kind == "video"
        ? LineIcon(enabled ? "M 2 8 Q 8 1 14 8 Q 8 15 2 8 M 8 6 A 2 2 0 1 0 8 10 A 2 2 0 1 0 8 6" : "M 2 8 Q 8 1 14 8 Q 8 15 2 8 M 3 13 L 13 3")
        : LineIcon(enabled ? "M 2 6 L 5 6 L 9 3 L 9 13 L 5 10 L 2 10 Z M 11 5 Q 14 8 11 11" : "M 2 6 L 5 6 L 9 3 L 9 13 L 5 10 L 2 10 Z M 11 5 L 15 11");

    private static Control LockIcon(bool locked) => LineIcon(locked
        ? "M 4 7 L 12 7 L 12 14 L 4 14 Z M 5 7 L 5 5 A 3 3 0 0 1 11 5 L 11 7"
        : "M 4 7 L 12 7 L 12 14 L 4 14 Z M 5 7 L 5 5 A 3 3 0 0 1 11 5 L 11 7 M 11 5 L 11 3");
}
