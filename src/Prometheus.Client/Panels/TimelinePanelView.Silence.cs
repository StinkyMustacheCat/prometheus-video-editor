using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public partial class TimelinePanelView
{
    private static readonly (string Name, SilenceSettings Settings)[] SilencePresets =
    [
        ("Suave", new(-42, 0.80, 0.20, 0.25)),
        ("Equilibrado", new(-36, 0.50, 0.12, 0.18)),
        ("Agresivo", new(-30, 0.30, 0.07, 0.10))
    ];

    private static Clip? FindSilenceAudio(Sequence sequence, Clip clip)
    {
        var audioTracks = sequence.Tracks.Where(t => t.Kind == "audio").Select(t => t.Id).ToHashSet();
        if (audioTracks.Contains(clip.TrackId)) return clip;
        var link = sequence.LinkGroups.FirstOrDefault(g => g.ClipIds.Contains(clip.Id));
        return link == null ? null : sequence.Clips.Where(c => link.ClipIds.Contains(c.Id) && audioTracks.Contains(c.TrackId) &&
                c.TimelineStart.Seconds < clip.EndSeconds && c.EndSeconds > clip.TimelineStart.Seconds)
            .OrderByDescending(c => Math.Min(c.EndSeconds, clip.EndSeconds) -
                                    Math.Max(c.TimelineStart.Seconds, clip.TimelineStart.Seconds)).FirstOrDefault();
    }

    private async Task ShowSilenceDialogAsync(string clipId)
    {
        if (_controller is not { } controller || TopLevel.GetTopLevel(this) is not Window parent) return;
        var project = controller.CurrentProject;
        var sequence = project.ActiveSequence;
        var target = sequence?.Clips.FirstOrDefault(c => c.Id == clipId);
        var audio = target == null ? null : FindSilenceAudio(sequence!, target);
        var asset = project.Assets.FirstOrDefault(a => a.Id == audio?.AssetId);
        var stream = asset?.Streams.FirstOrDefault(s => s.Id == audio?.StreamId && s.Kind == "audio");
        var path = asset?.EffectiveLocator?.Path;
        if (audio == null || stream == null || !File.Exists(path))
        {
            controller.ReportStatus("Elimina silencios desde un clip con audio disponible.");
            return;
        }

        var preset = new ComboBox { ItemsSource = SilencePresets.Select(p => p.Name).ToArray(),
            SelectedIndex = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        var threshold = NumberField(-80, 0, 1, "0");
        var minimum = NumberField(0.05m, 60, 0.05m, "0.00");
        var before = NumberField(0, 5, 0.05m, "0.00");
        var after = NumberField(0, 5, 0.05m, "0.00");
        ApplyPreset();
        var advanced = new StackPanel { Spacing = 5, Margin = new Thickness(0, 8, 0, 0) };
        advanced.Children.Add(Field("Umbral (dBFS)", threshold));
        advanced.Children.Add(Field("Silencio mínimo (s)", minimum));
        advanced.Children.Add(Field("Margen antes de retomar el audio (s)", before));
        advanced.Children.Add(Field("Margen después del audio anterior (s)", after));
        var options = new Expander { Header = "Avanzado", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch };
        var status = new TextBlock { Text = "Analiza el audio para revisar los cortes.", TextWrapping = TextWrapping.Wrap };
        var rows = new StackPanel { Spacing = 2 };
        var results = new ScrollViewer { Content = rows, Margin = new Thickness(0, 10), VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        var analyze = new Button { Content = "Volver a analizar", MinWidth = 112 };
        var cancel = new Button { Content = "Cancelar", MinWidth = 88 };
        var apply = new Button { Content = "Aplicar cortes", IsEnabled = false, MinWidth = 100, Classes = { "nle-accent" } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Children = { analyze, cancel, apply } };
        var cutMode = new ComboBox { ItemsSource = new[] { "Extraer", "Dejar huecos" }, SelectedIndex = 0 };
        var modeHint = new TextBlock
        {
            Text = sequence!.CloseGapAllTracks ? "Cierre de huecos · Clásico" : "Cierre de huecos · Por pista"
        };
        cutMode.SelectionChanged += (_, _) => modeHint.IsVisible = cutMode.SelectedIndex == 0;
        var controls = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = audio.Name, TextTrimming = TextTrimming.CharacterEllipsis },
            Field("Preset", preset), Field("Al aplicar", cutMode), modeHint, options, status } };
        var body = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        body.Children.Add(controls);
        Grid.SetRow(results, 1);
        body.Children.Add(results);
        Grid.SetRow(buttons, 2);
        body.Children.Add(buttons);
        var dialog = new Window { Title = "Eliminar silencios", Width = 530, Height = 530,
            MinWidth = 470, MinHeight = 440, Content = body, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        using var cancellation = new CancellationTokenSource();
        var reviewed = Array.Empty<(SilenceCut Cut, CheckBox Choice)>();
        var analyzing = false;
        dialog.Opened += async (_, _) => await Analyze();
        dialog.Closed += (_, _) => cancellation.Cancel();
        cancel.Click += (_, _) => dialog.Close();
        preset.SelectionChanged += (_, _) => { ApplyPreset(); ClearReview(); };
        foreach (var field in new[] { threshold, minimum, before, after })
            field.ValueChanged += (_, _) => ClearReview();
        analyze.Click += async (_, _) => await Analyze();
        apply.Click += (_, _) =>
        {
            var selected = reviewed.Where(r => r.Choice.IsChecked == true).Select(r => r.Cut).ToArray();
            if (selected.Length == 0) return;
            if (!ReferenceEquals(project, controller.CurrentProject))
            {
                status.Text = "El montaje cambió. Cierra el diálogo y vuelve a analizar el clip.";
                apply.IsEnabled = false;
                return;
            }
            if (CommitTimelineEdit(p => SilenceCutOperations.Apply(p.ActiveSequence!, audio.Id, selected, cutMode.SelectedIndex == 0), "Eliminar silencios"))
            {
                dialog.Close();
                var applied = selected.Length == 1 ? "1 corte aplicado" : $"{selected.Length} cortes aplicados";
                controller.ReportStatus($"{applied} · {selected.Sum(c => c.Duration):0.##} s eliminados");
            }
            else status.Text = "No se aplicaron los cortes. Revisa si hay pistas bloqueadas o medios que se solaparían.";
        };
        await dialog.ShowDialog(parent);

        void ApplyPreset()
        {
            if (preset.SelectedIndex < 0) return;
            var defaults = SilencePresets[preset.SelectedIndex].Settings;
            threshold.Value = (decimal)defaults.ThresholdDb;
            minimum.Value = (decimal)defaults.MinimumSeconds;
            before.Value = (decimal)defaults.KeepBefore;
            after.Value = (decimal)defaults.KeepAfter;
        }

        void ClearReview()
        {
            reviewed = [];
            rows.Children.Clear();
            apply.IsEnabled = false;
            if (!analyzing) status.Text = "Vuelve a analizar para revisar los cortes con estos parámetros.";
        }

        void UpdateReview()
        {
            var chosen = reviewed.Where(r => r.Choice.IsChecked == true).ToArray();
            apply.IsEnabled = chosen.Length > 0;
            status.Text = reviewed.Length == 0 ? "No se detectaron silencios con estos parámetros." :
                $"{chosen.Length} de {reviewed.Length} cortes · {chosen.Sum(r => r.Cut.Duration):0.##} s seleccionados";
        }

        async Task Analyze()
        {
            if (analyzing || cancellation.IsCancellationRequested) return;
            analyzing = true;
            ClearReview();
            analyze.IsEnabled = preset.IsEnabled = options.IsEnabled = false;
            status.Text = "Analizando audio…";
            try
            {
                var settings = new SilenceSettings((double)(threshold.Value ?? -36), (double)(minimum.Value ?? 0.50m),
                    (double)(before.Value ?? 0.12m), (double)(after.Value ?? 0.18m));
                var cuts = await SilenceAnalysisService.AnalyzeAsync(path!, stream.Ordinal, audio,
                    Math.Max(1, sequence.Fps.Seconds), settings, cancellation.Token);
                if (cancellation.IsCancellationRequested) return;
                reviewed = cuts.Select(c => (c, new CheckBox { IsChecked = true,
                    Content = $"{Clock(c.Start)}  –  {Clock(c.End)}   ·   {c.Duration:0.##} s" })).ToArray();
                foreach (var (_, choice) in reviewed)
                {
                    choice.IsCheckedChanged += (_, _) => UpdateReview();
                    rows.Children.Add(choice);
                }
                UpdateReview();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception error) { if (!cancellation.IsCancellationRequested) status.Text = error.Message; }
            finally
            {
                analyzing = false;
                analyze.IsEnabled = preset.IsEnabled = options.IsEnabled = true;
            }
        }
    }

    private static NumericUpDown NumberField(decimal min, decimal max, decimal step, string format) =>
        new() { Minimum = min, Maximum = max, Increment = step, FormatString = format, MinWidth = 100 };

    private static Grid Field(string label, Control input)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(input, 1);
        row.Children.Add(input);
        return row;
    }

    private static string Clock(double seconds) =>
        TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
}
