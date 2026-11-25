using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Prometheus.Client.Services;
using Prometheus.Core.Model;

namespace Prometheus.Client.Panels;

public partial class TimelinePanelView
{
    private static readonly IReadOnlyDictionary<string, AudioEffectParameter[]> VisualTrackParameters =
        new Dictionary<string, AudioEffectParameter[]>
        {
            [BasicColorEffect.Id] = [new("brightness", "Brillo", 0, -1, 1),
                new("contrast", "Contraste", 1, 0, 2), new("saturation", "Saturación", 1, 0, 3)],
            [ExposureEffect.Id] = [new("stops", "Exposición", 0, -3, 3), new("black", "Negros", 0, -0.5, 0.5)],
            [LevelsEffect.Id] = [new("black", "Negro", 0, -0.5, 0.9), new("white", "Blanco", 1, 0.1, 1.5),
                new("gamma", "Gamma", 1, 0.1, 5)],
            [MonochromeEffect.Id] = [],
            [ImageGeometryEffect.FlipHorizontalId] = [],
            [ImageGeometryEffect.FlipVerticalId] = [],
            [GpuFilterEffect.BlurId] = [new("radius", "Radio", 6, 0, 30, " px")],
            [GpuDetailEffect.SharpenId] = [new("amount", "Intensidad", 0.7, 0, 2)],
            [GpuDetailEffect.GlowId] = [new("amount", "Intensidad", 0.35, 0, 1), new("radius", "Radio", 5, 1, 30, " px")]
        };

    internal static bool SupportsTrackEffect(Track track, EffectCatalogEntry entry) => entry.Kind == track.Kind &&
        (track.Kind == "audio" ? AudioEffects.Find(entry.Id) != null : VisualTrackParameters.ContainsKey(entry.Id));

    private async Task ShowTrackEffectsAsync(string trackId)
    {
        var controller = _controller;
        var project = controller?.CurrentProject;
        var sequence = project?.ActiveSequence;
        var track = sequence?.Tracks.FirstOrDefault(item => item.Id == trackId);
        if (controller == null || project == null || sequence == null || track is not { Locked: false } ||
            track.Role == "subtitle" || TopLevel.GetTopLevel(this) is not Window owner) return;
        var choices = EffectCatalog.Available.Where(entry => SupportsTrackEffect(track, entry)).ToArray();
        var draft = JsonSerializer.Deserialize<List<EffectInstance>>(JsonSerializer.Serialize(track.Effects))!;
        var original = JsonSerializer.Serialize(draft);
        var picker = new ComboBox { MinWidth = 230 };
        var add = new Button { Content = "Añadir" };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        toolbar.Children.Add(picker);
        toolbar.Children.Add(add);
        var entries = new StackPanel { Spacing = 7 };
        var list = new ScrollViewer { Content = entries, Margin = new Thickness(0, 10) };
        var cancel = new Button { Content = "Cancelar" };
        var apply = new Button { Content = "Aplicar", Classes = { "nle-accent" } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 7 };
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(12) };
        Grid.SetRow(list, 1);
        Grid.SetRow(buttons, 2);
        body.Children.Add(toolbar);
        body.Children.Add(list);
        body.Children.Add(buttons);
        var dialog = new Window { Title = $"Efectos de pista · {track.Name}", Width = 450, Height = 425,
            MinWidth = 365, MinHeight = 275, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = body };
        cancel.Click += (_, _) => dialog.Close(false);
        apply.Click += (_, _) => dialog.Close(true);

        void Refresh()
        {
            var available = choices.Where(choice => draft.All(effect => effect.EffectId != choice.Id)).ToArray();
            picker.ItemsSource = available.Select(choice => new ComboBoxItem { Content = choice.Name, Tag = choice.Id }).ToArray();
            picker.SelectedIndex = available.Length > 0 ? 0 : -1;
            picker.IsEnabled = add.IsEnabled = available.Length > 0;
            entries.Children.Clear();
            if (draft.Count == 0) entries.Children.Add(new TextBlock { Text = "Esta pista no tiene efectos." });
            foreach (var effect in draft)
            {
                var card = new StackPanel { Spacing = 4, Margin = new Thickness(8) };
                var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                var active = new CheckBox { Content = EffectCatalog.Available.FirstOrDefault(entry => entry.Id == effect.EffectId)?.Name
                    ?? effect.EffectId, IsChecked = effect.Enabled };
                active.IsCheckedChanged += (_, _) => effect.Enabled = active.IsChecked == true;
                var remove = new Button { Content = "Quitar", FontSize = 10 };
                remove.Click += (_, _) => { draft.Remove(effect); Refresh(); };
                Grid.SetColumn(remove, 1);
                header.Children.Add(active);
                header.Children.Add(remove);
                card.Children.Add(header);
                var parameters = AudioEffects.Find(effect.EffectId)?.Parameters ??
                    VisualTrackParameters.GetValueOrDefault(effect.EffectId) ?? [];
                foreach (var parameter in parameters)
                {
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,105") };
                    var label = new TextBlock { Text = parameter.Label + parameter.Unit,
                        VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
                    var number = new NumericUpDown { Minimum = (decimal)parameter.Minimum, Maximum = (decimal)parameter.Maximum,
                        Value = (decimal)AudioEffects.Read(effect, parameter), FormatString = "0.##",
                        Increment = parameter.Maximum - parameter.Minimum <= 10 ? 0.05m : 1m };
                    number.ValueChanged += (_, _) =>
                    {
                        if (number.Value is { } value)
                            AudioEffects.Set(effect, parameter.Key, Math.Clamp((double)value, parameter.Minimum, parameter.Maximum));
                    };
                    Grid.SetColumn(number, 1);
                    row.Children.Add(label);
                    row.Children.Add(number);
                    card.Children.Add(row);
                }
                entries.Children.Add(new Border { Classes = { "capsule" }, Child = card });
            }
        }

        add.Click += (_, _) =>
        {
            if (picker.SelectedItem is not ComboBoxItem { Tag: string effectId }) return;
            EffectApplicationService.ApplyToClip(new Clip { Effects = draft }, [effectId]);
            Refresh();
        };
        Refresh();
        if (!await dialog.ShowDialog<bool>(owner) || original == JsonSerializer.Serialize(draft)) return;
        CommitTimelineEdit(current =>
        {
            if (current.Id != project.Id || current.ActiveSequenceId != sequence.Id ||
                current.ActiveSequence?.Tracks.FirstOrDefault(item => item.Id == trackId) is not { Locked: false } target)
                throw new InvalidOperationException("La pista cambió mientras editabas sus efectos.");
            target.Effects = draft;
        }, "Cambiar efectos de pista");
    }
}
