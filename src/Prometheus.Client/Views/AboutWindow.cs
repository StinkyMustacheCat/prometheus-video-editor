using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Prometheus.Client.Views;

internal sealed class AboutWindow : Window
{
    internal AboutWindow()
    {
        Title = "Acerca de Prometheus y licencias";
        Width = 760;
        Height = 600;
        MinWidth = 640;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var version = typeof(AboutWindow).Assembly.GetName().Version;
        var content = new TextBox { IsReadOnly = true, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        void Read(string relative)
        {
            var path = Path.Combine(AppContext.BaseDirectory, relative);
            content.Text = File.Exists(path) ? File.ReadAllText(path) :
                "Este documento no se encuentra en esta compilación. Consulta LICENSE y licenses/ en las fuentes o en la distribución completa.";
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var (label, path) in new[] { ("Licencia de Prometheus", "LICENSE"),
            ("Componentes de terceros", Path.Combine("licenses", "TERCEROS.md")),
            ("Estado de la entrega", Path.Combine("docs", "PUBLICACION.md")) })
        {
            var button = new Button { Content = label };
            button.Click += (_, _) => Read(path);
            buttons.Children.Add(button);
        }
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"), Margin = new Thickness(18) };
        layout.Children.Add(new TextBlock { Text = $"Prometheus {version?.ToString(3)}", FontSize = 22, FontWeight = FontWeight.Bold });
        var description = new TextBlock { Text = "Copyright © 2026 Nicolas & Prometheus Contributors.\nGPL-3.0-or-later. Sin garantía. Los componentes de terceros conservan sus propias licencias.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12) };
        Grid.SetRow(description, 1); layout.Children.Add(description);
        Grid.SetRow(buttons, 2); layout.Children.Add(buttons);
        Grid.SetRow(content, 3); content.Margin = new Thickness(0, 12, 0, 0); layout.Children.Add(content);
        Content = layout;
        Read("LICENSE");
    }
}
