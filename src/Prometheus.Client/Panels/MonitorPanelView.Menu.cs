using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Prometheus.Client.Services;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public partial class MonitorPanelView
{
    private readonly CaptureUserSettings _captureSettings = CaptureUserSettings.Load();

    private MenuItem CaptureActionsMenu()
    {
        var project = ProjectController.Instance?.CurrentProject;
        var time = _transport?.PositionSeconds ?? _requestedTime;
        var hasVisual = project != null && SubtitleRenderPlan.At(project, time, "video").Count > 0;
        return new MenuItem
        {
            Header = "Capturar fotograma",
            IsEnabled = hasVisual && !_captureBusy,
            ItemsSource = CaptureItems()
        };
    }

    private object[] CaptureItems()
    {
        var format = new MenuItem { Header = "Formato de archivo", ItemsSource = new[]
        {
            Item((_captureSettings.Format == "png" ? "✓  " : "    ") + "PNG", () => { _captureSettings.Format = "png"; _captureSettings.Save(); }),
            Item((_captureSettings.Format == "jpg" ? "✓  " : "    ") + "JPEG", () => { _captureSettings.Format = "jpg"; _captureSettings.Save(); })
        }};
        var background = new MenuItem { Header = "Fondo", ItemsSource = new[]
        {
            Item((_captureSettings.TransparentBackground ? "✓  " : "    ") + "Transparente (PNG)", () => { _captureSettings.TransparentBackground = true; _captureSettings.Save(); }),
            Item((!_captureSettings.TransparentBackground ? "✓  " : "    ") + "Color opaco", () => { _captureSettings.TransparentBackground = false; _captureSettings.Save(); }),
            Item("Elegir color de fondo…", async () => await ChooseCaptureBackgroundAsync())
        }};
        return
        [
            Item("Guardar fotograma…", async () => await CaptureFrameAsync()),
            Item("Copiar fotograma", async () => await CaptureFrameAsync(CaptureAction.Copy)),
            Item("Añadir a Medios", async () => await CaptureFrameAsync(CaptureAction.AddToMedia)),
            Item("Superponer en línea de tiempo", async () => await CaptureFrameAsync(CaptureAction.Overlay)),
            Item("Incrustar en línea de tiempo", async () => await CaptureFrameAsync(CaptureAction.Insert)),
            new Separator(), format, background
        ];
    }

    private void ShowCaptureButtonMenu()
    {
        var menu = new ContextMenu { ItemsSource = CaptureItems() };
        menu.Open(CaptureFrameButton);
    }

    private async System.Threading.Tasks.Task ChooseCaptureBackgroundAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var input = new ColorField { Hex = _captureSettings.BackgroundColor,
            Margin = new Avalonia.Thickness(12) };
        var save = new Button { Content = "Guardar", Margin = new Avalonia.Thickness(12, 0, 12, 12) };
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(input);
        panel.Children.Add(save);
        var dialog = new Window { Title = "Color de fondo de captura", Width = 340, Height = 140,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        void Commit()
        {
            if (!SkiaSharp.SKColor.TryParse(input.Hex, out _)) { input.Hex = _captureSettings.BackgroundColor; return; }
            dialog.Close(input.Hex);
        }
        save.Click += (_, _) => Commit();
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        input.PickRequested += (_, _) =>
        {
            dialog.Close();
            ChromaColorPicker.BeginComposite(color =>
            {
                _captureSettings.BackgroundColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                _captureSettings.TransparentBackground = false;
                _captureSettings.Save();
                ProjectController.Instance?.ReportStatus("Color de captura tomado de la previsualización");
            });
            ProjectController.Instance?.ReportStatus("Gotero activo: elige un color en Previsualización");
        };
        var chosen = await dialog.ShowDialog<string?>(owner);
        if (chosen == null) return;
        _captureSettings.BackgroundColor = chosen;
        _captureSettings.TransparentBackground = false;
        _captureSettings.Save();
    }

    private void SetupMonitorContextMenu()
    {
        var menu = new ContextMenu();
        menu.Opening += (_, args) =>
        {
            if (SamToolEnabled)
            {
                var actions = SamMaskMenuItems();
                args.Cancel = actions == null;
                menu.ItemsSource = actions;
                return;
            }
            var capture = CaptureActionsMenu();
            var quality = new MenuItem { Header = "Calidad del monitor" };
            quality.ItemsSource = PreviewUserSettings.QualityLabels
                .Select((label, index) => Item((PreviewUserSettings.QualityIndex == index ? "✓  " : "    ") + label,
                    () => PreviewUserSettings.Set(index, PreviewUserSettings.RefreshIndex))).ToArray();
            var fps = new MenuItem { Header = "Refresco del monitor" };
            fps.ItemsSource = PreviewUserSettings.RefreshRates
                .Select((value, index) => Item((PreviewUserSettings.RefreshIndex == index ? "✓  " : "    ") + value + " FPS",
                    () => PreviewUserSettings.Set(PreviewUserSettings.QualityIndex, index))).ToArray();
            menu.ItemsSource = new object[]
            {
                capture, new Separator(), quality, fps,
                Item("Ajustar imagen", FitView),
                Item(TransformGizmoToggle.IsChecked == true ? "Desactivar transformación" : "Activar transformación",
                    () => TransformGizmoToggle.IsChecked = TransformGizmoToggle.IsChecked != true),
                Item(MonitorBottomBar.IsVisible ? "Ocultar controles inferiores" : "Mostrar controles inferiores", ToggleMonitorControls)
            };
        };
        ViewportHost.ContextMenu = menu;
    }

    private void ToggleMonitorControls()
    {
        MonitorBottomBar.IsVisible = !MonitorBottomBar.IsVisible;
        FitSequenceFrame();
    }

    private static MenuItem Item(string title, Action action)
    {
        var item = new MenuItem { Header = title };
        item.Click += (_, _) => action();
        return item;
    }
}
