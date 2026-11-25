using Avalonia.Controls;
using Avalonia.Media;
using Prometheus.Client.Services;

namespace Prometheus.Client.Views;

/// <summary>Fallback for the global shortcut when the media panel is not visible.</summary>
public sealed class LinkShelfWindow : Window
{
    private static LinkShelfWindow? _current;
    private readonly LinkShelfView _view;

    public static void ShowFor(Window owner, ProjectController controller, string? captured = null)
    {
        if (_current == null || !_current.IsVisible)
        {
            _current = new LinkShelfWindow(controller);
            _current.Show(owner);
        }
        _current.Activate();
        if (!string.IsNullOrWhiteSpace(captured)) _current._view.AddCaptured(captured);
    }

    private LinkShelfWindow(ProjectController controller)
    {
        Title = "Bandeja de enlaces y archivos";
        Width = 620;
        Height = 560;
        MinWidth = 360;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Black;
        _view = new LinkShelfView(controller);
        Content = _view;
        Closed += (_, _) => { if (ReferenceEquals(_current, this)) _current = null; };
    }
}
