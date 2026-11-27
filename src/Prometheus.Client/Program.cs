using Avalonia;
using System;
using Prometheus.Client.Services;

namespace Prometheus.Client;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is [RecoverableExport.WorkerFlag, var folder, var mode])
            return RecoverableExport.WorkerEntry(folder, mode == "overwrite");
        if (args is ["--graphics-diagnostics", var report])
            return GraphicsDiagnostics.Write(report);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
