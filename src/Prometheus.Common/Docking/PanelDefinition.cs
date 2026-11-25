namespace Prometheus.Common.Docking;

public class PanelDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string IconKey { get; set; } = string.Empty;
    public double DefaultWidth { get; set; } = 300;
    public double DefaultHeight { get; set; } = 250;

    public PanelDefinition()
    {
    }

    public PanelDefinition(string id, string title, string category, string iconKey, double defaultWidth = 300, double defaultHeight = 250)
    {
        Id = id;
        Title = title;
        Category = category;
        IconKey = iconKey;
        DefaultWidth = defaultWidth;
        DefaultHeight = defaultHeight;
    }
}
