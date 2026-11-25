using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Prometheus.Core.Model;

namespace Prometheus.Client.Panels;

public partial class PropertiesPanelView
{
    private readonly DispatcherTimer _colorTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private readonly List<ColorEditor> _colorEditors = new();
    private string _colorSignature = "";
    private bool _syncingColor;
    private bool _colorPending;

    private sealed class ColorEditor
    {
        public required string InstanceId { get; init; }
        public required string EffectId { get; init; }
        public required ToggleButton Toggle { get; init; }
        public Dictionary<string, Slider> Sliders { get; } = new();
        public Dictionary<string, TextBlock> Values { get; } = new();
    }

    private void InitializeColorEditors() => _colorTimer.Tick += (_, _) => CommitColorEdits();

    private void ScheduleColorEdit()
    {
        if (_syncingColor || _selectedId == null) return;
        _colorPending = true;
        _colorTimer.Stop();
        _colorTimer.Start();
    }

    private void CommitColorEdits()
    {
        _colorTimer.Stop();
        if (!_colorPending || _controller == null || _selectedId == null) return;
        _colorPending = false;
        var clipId = _selectedId;
        var editTime = SelectedLocalTime;
        var state = _colorEditors.Select(editor => (editor.EffectId, Enabled: editor.Toggle.IsChecked == true,
            Values: editor.Sliders.ToDictionary(pair => pair.Key, pair => pair.Value.Value))).ToArray();
        try
        {
            _controller.UndoRedo.Commit(project =>
            {
                var clip = Locate(project, clipId)?.Clip;
                if (clip == null) return;
                foreach (var item in state)
                {
                    var effect = clip.Effects.FirstOrDefault(e => e.EffectId == item.EffectId);
                    if (effect == null) continue;
                    effect.Enabled = item.Enabled;
                    foreach (var parameter in item.Values)
                        CommitEffectValue(clip, effect, parameter.Key, parameter.Value, editTime);
                }
            }, "Ajustar color");
        }
        catch (Exception ex) { _controller.ReportStatus("Color: " + ex.Message); }
        foreach (var editor in _colorEditors)
            _dirtyEffectTargets.RemoveWhere(target => target.StartsWith("effect." + editor.InstanceId + ".",
                StringComparison.Ordinal));
    }

    private void RefreshColorEditors(Clip? clip)
    {
        var effects = clip?.Effects.Where(e => ColorFinishingEffect.Supports(e.EffectId)).ToArray() ?? [];
        var signature = (clip?.Id ?? "") + ":" + string.Join("|", effects.Select(e => e.Id + e.EffectId));
        if (_colorSignature != signature)
        {
            foreach (var editor in _colorEditors) UnregisterEffectSliders(editor.InstanceId);
            _colorSignature = signature;
            _colorEditors.Clear();
            ColorEffectsHost.Children.Clear();
            foreach (var effect in effects) ColorEffectsHost.Children.Add(CreateColorCard(effect));
        }
        else if (_colorPending) return;
        _syncingColor = true;
        try
        {
            foreach (var editor in _colorEditors)
            {
                var effect = effects.FirstOrDefault(e => e.EffectId == editor.EffectId);
                if (effect == null) continue;
                editor.Toggle.IsChecked = effect.Enabled;
                editor.Toggle.Content = effect.Enabled ? "Activo" : "Desactivado";
                foreach (var (key, slider) in editor.Sliders)
                {
                    var fallback = ColorDefault(key);
                    slider.Value = ColorFinishingEffect.ReadNumber(effect, key, fallback);
                    editor.Values[key].Text = ColorDisplay(key, slider.Value);
                }
            }
        }
        finally { _syncingColor = false; }
    }

    private static double ColorDefault(string key) => key switch
    {
        "temperature" => 6500, "amount" => 0.35, _ => 0
    };

    private static string ColorDisplay(string key, double value) => key == "temperature"
        ? value.ToString("0") + " K" : value.ToString("0.##");

    private Control CreateColorCard(EffectInstance effect)
    {
        var title = effect.EffectId switch
        {
            ColorFinishingEffect.WhiteBalanceId => "Temperatura y matiz",
            ColorFinishingEffect.ShadowsHighlightsId => "Sombras y luces",
            ColorFinishingEffect.HueId => "Tono",
            ColorFinishingEffect.VibranceId => "Intensidad de color",
            _ => "Viñeta"
        };
        var editor = new ColorEditor
        {
            InstanceId = effect.Id,
            EffectId = effect.EffectId,
            Toggle = new ToggleButton { Content = "Activo", FontSize = 10, Height = 22 }
        };
        _colorEditors.Add(editor);
        var body = new StackPanel { Spacing = 7 };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };
        heading.Children.Add(new TextBlock { Text = title, FontSize = 10, FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#FF8A00")), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(editor.Toggle, 1); heading.Children.Add(editor.Toggle);
        var reset = new Button { Content = "↺", Width = 23, Height = 22, Padding = new Thickness(0), Margin = new Thickness(5, 0, 0, 0) };
        ToolTip.SetTip(reset, "Restablecer efecto");
        Grid.SetColumn(reset, 2); heading.Children.Add(reset);
        var remove = new Button { Content = "×", Width = 23, Height = 22, Padding = new Thickness(0), Margin = new Thickness(5, 0, 0, 0) };
        ToolTip.SetTip(remove, "Quitar efecto del medio");
        Grid.SetColumn(remove, 3); heading.Children.Add(remove);
        body.Children.Add(heading);
        switch (effect.EffectId)
        {
            case ColorFinishingEffect.WhiteBalanceId:
                AddColorSlider(body, editor, "temperature", "Temperatura", 1000, 40000);
                AddColorSlider(body, editor, "tint", "Matiz", -1, 1);
                break;
            case ColorFinishingEffect.ShadowsHighlightsId:
                AddColorSlider(body, editor, "shadows", "Sombras", -1, 1);
                AddColorSlider(body, editor, "highlights", "Luces", -1, 1);
                break;
            case ColorFinishingEffect.HueId:
                AddColorSlider(body, editor, "degrees", "Grados", -180, 180);
                break;
            case ColorFinishingEffect.VibranceId:
                AddColorSlider(body, editor, "intensity", "Intensidad", -2, 2);
                break;
            default:
                AddColorSlider(body, editor, "amount", "Cantidad", 0, 1);
                break;
        }
        editor.Toggle.IsCheckedChanged += (_, _) =>
        {
            editor.Toggle.Content = editor.Toggle.IsChecked == true ? "Activo" : "Desactivado";
            ScheduleColorEdit();
        };
        reset.Click += (_, _) => ResetEffectInstance(effect.Id);
        remove.Click += (_, _) => RemoveEffect(effect.EffectId);
        return CreateEffectCard(body, effect.Id);
    }

    private void AddColorSlider(StackPanel body, ColorEditor editor, string key, string label, double min, double max)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*,50") };
        row.Children.Add(new TextBlock { Text = label, FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
        var slider = new Slider { Minimum = min, Maximum = max, Value = ColorDefault(key) };
        ResetOnThumbDoubleClick(slider, ColorDefault(key));
        Grid.SetColumn(slider, 1); row.Children.Add(slider);
        var value = new TextBlock { Text = ColorDisplay(key, slider.Value), FontSize = 10,
            TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(value, 2); row.Children.Add(value);
        editor.Sliders[key] = slider;
        editor.Values[key] = value;
        slider.ValueChanged += (_, _) => { value.Text = ColorDisplay(key, slider.Value); ScheduleColorEdit(); };
        body.Children.Add(row);
        var effect = (_controller == null ? null : Locate(_controller.CurrentProject, _selectedId)?.Clip)?
            .Effects.FirstOrDefault(item => item.Id == editor.InstanceId);
        if (effect != null) RegisterEffectSlider(effect, key, label, slider);
    }
}
