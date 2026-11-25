using System;
using Avalonia.Controls;
using Prometheus.Client.Panels;
using Prometheus.Common.Docking;

namespace Prometheus.Client.Docking;

public partial class FloatingPanelWindow : Window
{
    public string PanelId { get; private set; } = string.Empty;

    public event Action<string>? ReDockRequested;
    public event Action<string>? MinimizeRequested;
    public event Action<string>? PanelClosed;

    public FloatingPanelWindow()
    {
        InitializeComponent();
    }

    public void Initialize(FloatingWindowDescriptor descriptor)
    {
        PanelId = descriptor.PanelId;
        if (PanelId == "timeline") { MinWidth = 900; MinHeight = 270; }
        if (PanelId == "properties") MinWidth = PropertiesPanelView.MinimumPanelWidth;
        TitleText.Text = PanelRegistry.GetPanelTitle(PanelId);
        Title = $"{TitleText.Text} - Prometheus";

        Position = new Avalonia.PixelPoint((int)descriptor.X, (int)descriptor.Y);
        Width = Math.Max(MinWidth, descriptor.Width);
        Height = Math.Max(PanelId == "timeline" ? 270 : 100, descriptor.Height);

        var content = PanelRegistry.CreatePanelContent(PanelId);
        ContentHolder.Child = content;

        ReDockButton.Click += (_, _) =>
        {
            ReDockRequested?.Invoke(PanelId);
            Close();
        };

        MinimizeButton.Click += (_, _) =>
        {
            MinimizeRequested?.Invoke(PanelId);
            Close();
        };

        CloseButton.Click += (_, _) =>
        {
            PanelClosed?.Invoke(PanelId);
            Close();
        };
    }

    public FloatingWindowDescriptor GetDescriptor()
    {
        return new FloatingWindowDescriptor(
            PanelId,
            Position.X,
            Position.Y,
            Bounds.Width,
            Bounds.Height);
    }
}
