namespace Prometheus.Common.Docking;

public class FloatingWindowDescriptor
{
    public string PanelId { get; set; } = string.Empty;
    public double X { get; set; } = 100;
    public double Y { get; set; } = 100;
    public double Width { get; set; } = 400;
    public double Height { get; set; } = 300;

    public FloatingWindowDescriptor()
    {
    }

    public FloatingWindowDescriptor(string panelId, double x, double y, double width, double height)
    {
        PanelId = panelId;
        X = x;
        Y = y;
        Width = Math.Max(100, width);
        Height = Math.Max(100, height);
    }
}
