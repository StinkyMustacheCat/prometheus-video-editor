using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Prometheus.Client.Services;
using Prometheus.Core.Timeline;
using Prometheus.Core.Model;

namespace Prometheus.Client.Views;

public partial class MainWindow
{
    private async Task OpenPreviewSettingsAsync()
    {
        await ShowPreviewSettingsAsync(this, _projectController);
    }

    internal static async Task ShowPreviewSettingsAsync(Window owner, ProjectController controller)
    {
        var projectId = controller.CurrentProject.Id;
        var sequence = controller.CurrentProject.ActiveSequence;
        if (sequence == null) return;
        var sequenceId = sequence.Id;
        var presets = CanvasResolutions.Aspects.Select(a => (Name: a.Label, a.Width, a.Height))
            .Append((Name: "Personalizada", Width: 0, Height: 0)).ToArray();
        var dialog = new Window { Title = "Previsualización y lienzo", Width = 460, Height = 540,
            MinWidth = 380, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var quality = new ComboBox { ItemsSource = new[] { "Baja (320 px)", "Media (640 px)", "Alta (original)" },
            SelectedIndex = PreviewUserSettings.QualityIndex };
        var refresh = new ComboBox { ItemsSource = new[] { "15 FPS", "30 FPS", "60 FPS" },
            SelectedIndex = PreviewUserSettings.RefreshIndex };
        var aspectIndex = Array.FindIndex(presets, p => CanvasResolutions.SameAspect(p.Width, p.Height, sequence.Width, sequence.Height));
        var ratio = new ComboBox { Name = "CanvasAspect", ItemsSource = presets.Select(p => p.Name).ToArray(),
            SelectedIndex = aspectIndex >= 0 ? aspectIndex : presets.Length - 1 };
        var resolutionIndex = CanvasResolutions.Levels.ToList().FindIndex(level =>
        {
            var selected = presets[ratio.SelectedIndex];
            try { return CanvasResolutions.AtShortEdge(selected.Width > 0 ? selected.Width : sequence.Width,
                selected.Height > 0 ? selected.Height : sequence.Height, level) == (sequence.Width, sequence.Height); }
            catch (ArgumentException) { return false; }
        });
        var resolution = new ComboBox { Name = "CanvasResolution", ItemsSource = CanvasResolutions.Levels.Select(n =>
            n == 2160 ? "2160 · UHD / 4K en 16:9 y 9:16" : n.ToString()).Append("Personalizada").ToArray(),
            SelectedIndex = resolutionIndex >= 0 ? resolutionIndex : CanvasResolutions.Levels.Count };
        var width = new TextBox { Name = "CanvasWidth", Text = sequence.Width.ToString(), Width = 100 };
        var height = new TextBox { Name = "CanvasHeight", Text = sequence.Height.ToString(), Width = 100 };
        var fitAll = new CheckBox { Content = "Ajustar todos los medios visuales a Fit", IsChecked = false };
        var error = new TextBlock { Foreground = Avalonia.Media.Brushes.OrangeRed, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        void UpdateDimensions(bool aspectChanged = false)
        {
            width.IsEnabled = height.IsEnabled = resolution.SelectedIndex == CanvasResolutions.Levels.Count;
            if (resolution.SelectedIndex < 0) return;
            var selected = presets[Math.Clamp(ratio.SelectedIndex, 0, presets.Length - 1)];
            var w = int.TryParse(width.Text, out var cw) && cw > 0 ? cw : sequence.Width;
            var h = int.TryParse(height.Text, out var ch) && ch > 0 ? ch : sequence.Height;
            if (resolution.SelectedIndex >= CanvasResolutions.Levels.Count && (!aspectChanged || selected.Width == 0)) return;
            try
            {
                var size = CanvasResolutions.AtShortEdge(selected.Width > 0 ? selected.Width : w,
                    selected.Height > 0 ? selected.Height : h, resolution.SelectedIndex < CanvasResolutions.Levels.Count
                        ? CanvasResolutions.Levels[resolution.SelectedIndex] : Math.Min(w, h));
                width.Text = size.Width.ToString(); height.Text = size.Height.ToString(); error.Text = "";
            }
            catch (ArgumentException e) { width.Text = height.Text = ""; error.Text = e.Message; }
        }
        ratio.SelectionChanged += (_, _) => UpdateDimensions(true);
        resolution.SelectionChanged += (_, _) => UpdateDimensions();
        UpdateDimensions();
        var apply = new Button { Name = "ApplyCanvas", Content = "Aplicar", MinWidth = 85 };
        var cancel = new Button { Content = "Cancelar", MinWidth = 85 };
        cancel.Click += (_, _) => dialog.Close(false);
        apply.Click += (_, _) =>
        {
            if (!int.TryParse(width.Text, out var w) || !int.TryParse(height.Text, out var h))
            { error.Text = "Escribe un ancho y un alto enteros."; return; }
            try { CanvasResolutions.ValidateDimensions(w, h); }
            catch (ArgumentException e) { error.Text = e.Message; return; }
            if (controller.CurrentProject.Id != projectId || controller.CurrentProject.ActiveSequenceId != sequenceId)
            { error.Text = "El proyecto o montaje cambió. Abre los ajustes de nuevo."; return; }
            var current = controller.CurrentProject.ActiveSequence!;
            if (w != current.Width || h != current.Height)
            {
                controller.Transport.Pause();
                controller.UndoRedo.Commit(p => SequenceOperations.SetCanvas(p, sequenceId, w, h,
                    fitAll.IsChecked == true), "Cambiar resolución del lienzo");
            }
            PreviewUserSettings.Set(quality.SelectedIndex, refresh.SelectedIndex);
            controller.ReportStatus($"Montaje {w}×{h}; refresco del monitor independiente de {sequence.Fps.Seconds:0.##} FPS del montaje");
            dialog.Close(true);
        };
        var dimensions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        dimensions.Children.Add(width);
        dimensions.Children.Add(new TextBlock { Text = "×", VerticalAlignment = VerticalAlignment.Center });
        dimensions.Children.Add(height);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        actions.Children.Add(cancel);
        actions.Children.Add(apply);
        var body = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
        body.Children.Add(new TextBlock { Text = "Calidad de la previsualización" });
        body.Children.Add(quality);
        body.Children.Add(new TextBlock { Text = "Refresco de la previsualización (independiente de los FPS del montaje)" });
        body.Children.Add(refresh);
        body.Children.Add(new TextBlock { Text = "Proporción del lienzo" });
        body.Children.Add(ratio);
        body.Children.Add(new TextBlock { Text = "Resolución del lienzo · lado corto" });
        body.Children.Add(resolution);
        body.Children.Add(dimensions);
        body.Children.Add(new TextBlock { Text = "La calidad del monitor no limita la exportación. La salida puede tener otra resolución sin cambiar el lienzo.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        body.Children.Add(fitAll);
        body.Children.Add(error);
        body.Children.Add(actions);
        dialog.Content = new ScrollViewer { Content = body };
        await dialog.ShowDialog<bool>(owner);
    }
}
