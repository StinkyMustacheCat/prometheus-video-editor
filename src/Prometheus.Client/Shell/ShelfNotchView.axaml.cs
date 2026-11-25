using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;
using Prometheus.Client.Panels;
using Prometheus.Common.Docking;

namespace Prometheus.Client.Shell;

public partial class ShelfNotchView : UserControl
{
    public event Action<string>? RestoreMinimizedAsFloatingRequested;
    public event Action<string, PanelDockState>? PanelDirectActionRequested;

    private readonly Dictionary<string, MenuItem> _menuItems = new(StringComparer.OrdinalIgnoreCase);

    public ShelfNotchView()
    {
        InitializeComponent();

        _menuItems["media_pool"] = MenuMediaPool;
        _menuItems["monitor"] = MenuMonitor;
        _menuItems["timeline"] = MenuTimeline;
        _menuItems["properties"] = MenuProperties;
        _menuItems["effects"] = MenuEffects;
        _menuItems["transcript"] = MenuTranscript;
        _menuItems["instructions"] = MenuInstructions;
        _menuItems["audio_mixer"] = MenuAudioMixer;
        _menuItems["color"] = MenuColor;

        foreach (var menuItem in _menuItems.Values)
        {
            menuItem.Click += MenuItem_Click;
        }
    }

    public void UpdateMinimizedShelf(IReadOnlyList<string> minimizedPanelIds)
    {
        var buttons = new List<Button>();

        foreach (var panelId in minimizedPanelIds)
        {
            var title = PanelRegistry.GetPanelTitle(panelId);
            var initial = !string.IsNullOrEmpty(title) ? title.Substring(0, 1).ToUpperInvariant() : "P";

            var btn = new Button
            {
                Classes = { "shelf-btn" },
                Content = new TextBlock
                {
                    Text = initial,
                    FontSize = 10,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            ToolTip.SetTip(btn, $"Abrir {title} debajo del estante");

            var capturedId = panelId;
            btn.Click += (_, _) => RestoreMinimizedAsFloatingRequested?.Invoke(capturedId);

            buttons.Add(btn);
        }

        MinimizedShelfItems.ItemsSource = buttons;
        MinimizedShelfSection.IsVisible = buttons.Count > 0;
    }

    public void UpdatePanelsMenuState(WorkspaceLayout layout)
    {
        foreach (var (panelId, menuItem) in _menuItems)
        {
            var title = PanelRegistry.GetPanelTitle(panelId);
            var state = layout.GetPanelState(panelId);

            var stateLabel = state switch
            {
                PanelDockState.Docked => "● Acoplado",
                PanelDockState.Floating => "🗗 Flotante",
                PanelDockState.Minimized => "🗕 Minimizado",
                _ => "✕ Cerrado"
            };

            menuItem.Header = $"{title,-22}  {stateLabel}";
            menuItem.ItemsSource = null;
            menuItem.Tag = (panelId, state);
        }
    }

    private void MenuItem_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.Tag is ValueTuple<string, PanelDockState> data)
        {
            PanelDirectActionRequested?.Invoke(data.Item1, data.Item2);
        }
    }
}
