using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Prometheus.Client.Panels;
using Prometheus.Client.Services;
using Prometheus.Core.Storage;
using SkiaSharp;

namespace Prometheus.Client.Views;

public partial class MainWindow
{
    private async Task ExportActiveSequenceAsync(string? outputPath = null, ExportFormat? format = null)
    {
        if (!ExportTopButton.IsEnabled) return;
        var current = _projectController.CurrentProject;
        var sequence = current.ActiveSequence;
        if (sequence == null)
        {
            StatusBarStatus.Text = "No hay montaje activo";
            return;
        }
        var stateVersion = _projectController.StateVersion;
        var setup = await new ExportSetupDialog(current, _projectController.ResourceProjectPath, outputPath, format).ShowDialog<ExportSetupResult?>(this);
        if (setup == null) return;
        if (_projectController.StateVersion != stateVersion || _projectController.CurrentProject.ActiveSequenceId != sequence.Id)
        { _projectController.ReportStatus("El proyecto o montaje cambió. Abre Exportar de nuevo para comprobar los ajustes actuales."); return; }
        var path = setup.Path;

        _projectController.Transport.Pause();
        var snapshot = ProjectStore.Clone(current);
        var history = _projectController.CaptureHistoryTarget();
        var sourceProjectPath = _projectController.ResourceProjectPath;
        using var cancellation = new CancellationTokenSource();
        var status = new TextBlock { Text = "Preparando audio…", Margin = new Avalonia.Thickness(14, 12, 14, 6) };
        var cancel = new Button { Content = setup.Options.Recoverable ? "Pausar" : "Cancelar", HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(14, 4, 14, 12) };
        var body = new StackPanel();
        body.Children.Add(status);
        body.Children.Add(cancel);
        var dialog = new Window { Title = "Exportando montaje", Width = 380, Height = 110,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = body };
        var finished = false;
        ExportResult? export = null;
        string? failure = null;
        cancel.Click += (_, _) => { cancellation.Cancel(); status.Text = "Cancelando…"; };
        dialog.Closing += (_, e) =>
        {
            if (finished) return;
            e.Cancel = true;
            cancellation.Cancel();
            status.Text = "Cancelando…";
        };
        MenuExportProject.IsEnabled = false;
        ExportTopButton.IsEnabled = false;
        dialog.Opened += async (_, _) =>
        {
            try
            {
                _projectController.RecordAction(history, setup.Options.Resume ? "Reanudar exportación" : "Iniciar exportación",
                    Path.GetFileName(path));
                var progress = new Progress<double>(fraction =>
                    status.Text = fraction >= 0.95 ? "Uniendo vídeo y audio…" :
                        $"Exportando fotogramas… {fraction / 0.95:P0}");
                export = await Task.Run(() => SequenceExportService.ExportGpuAsync(snapshot, path, sourceProjectPath,
                    progress: progress, cancellation: cancellation.Token, options: setup.Options), cancellation.Token);
                StatusBarStatus.Text = "Archivo exportado: " + Path.GetFileName(path) +
                    (export.VideoEncoder is { } backend ? $" · {backend}" : "");
                _projectController.RecordAction(history, "Exportar montaje", Path.GetFileName(path));
            }
            catch (OperationCanceledException)
            {
                StatusBarStatus.Text = setup.Options.Recoverable
                    ? "Exportación pausada. Puedes reanudar con el mismo destino."
                    : "Exportación cancelada";
                _projectController.RecordAction(history, setup.Options.Recoverable ? "Pausar exportación" : "Cancelar exportación",
                    Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                failure = ex.Message;
                StatusBarStatus.Text = "Exportación: " + ex.Message;
                _projectController.ReportExportFailure(snapshot.Id, path, ex.Message, history, setup.Options.Format);
            }
            finally
            {
                MenuExportProject.IsEnabled = true;
                ExportTopButton.IsEnabled = true;
                finished = true;
                dialog.Close();
            }
        };
        await dialog.ShowDialog(this);
        if (export != null || failure != null)
        {
            var result = new Window { Title = failure == null ? "Exportación terminada" : "Error de exportación", Width = 470, Height = 190,
                CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var resultBody = new StackPanel { Spacing = 12, Margin = new Avalonia.Thickness(16) };
            resultBody.Children.Add(new TextBlock { Text = failure ?? ("Archivo exportado: " + Path.GetFileName(path) +
                (export?.Encoder is { } encoder ? $" · {encoder.ToString().ToUpperInvariant()}" : "") +
                (export?.VideoEncoder is { } backend ? $" ({backend})" : "") +
                (export?.Warnings.Count > 0 ? $" · {export.Warnings.Count} aviso(s) de medios" : "")),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right };
            var openFolder = new Button { Content = "Abrir carpeta", Classes = { "nle-accent" } };
            openFolder.Click += (_, _) =>
            {
                Process.Start(new ProcessStartInfo(Path.GetDirectoryName(path)!) { UseShellExecute = true });
                result.Close();
            };
            var close = new Button { Content = "Cerrar", Classes = { "nle-btn" } };
            close.Click += (_, _) => result.Close();
            actions.Children.Add(close);
            if (export != null) actions.Children.Add(openFolder);
            resultBody.Children.Add(actions);
            result.Content = resultBody;
            await result.ShowDialog(this);
        }
    }
}
