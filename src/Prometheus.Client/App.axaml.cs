using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Prometheus.Client.ViewModels;
using Prometheus.Client.Views;
using Prometheus.Client.Services;
using System.Linq;

namespace Prometheus.Client;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _ = ProcessingService.InitializeAsync();
            if (desktop.Args?.Contains("--processing-settings") == true)
                desktop.MainWindow = new ProcessingSettingsDialog();
            else desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
                StartupProjectPath = desktop.Args?.Length > 0 ? desktop.Args[0] : null,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
