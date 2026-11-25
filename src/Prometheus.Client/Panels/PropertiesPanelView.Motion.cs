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
    private static readonly string[] MotionEaseValues = ["linear", "ease-in", "ease-out", "ease-in-out"];
    private static readonly string[] MotionEaseLabels = ["Lineal", "Entrada suave", "Salida suave", "Entrada y salida suaves"];
    private static readonly string[] MotionDirections = ["Izquierda → derecha", "Derecha → izquierda", "Arriba → abajo",
        "Abajo → arriba", "Diagonal ↘", "Diagonal ↗", "Diagonal ↙", "Diagonal ↖"];
    private readonly DispatcherTimer _motionTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private readonly List<MotionEditor> _motionEditors = new();
    private string _motionSignature = "";
    private bool _syncingMotion;
    private bool _motionPending;

    private sealed class MotionEditor
    {
        public required string InstanceId { get; init; }
        public required string EffectId { get; init; }
        public required ToggleButton Toggle { get; init; }
        public required ComboBox Ease { get; init; }
        public ComboBox? Direction { get; set; }
        public Dictionary<string, Slider> Sliders { get; } = new();
        public Dictionary<string, TextBlock> Values { get; } = new();
    }

    private void InitializeMotionEditors() => _motionTimer.Tick += (_, _) => CommitMotionEdits();

    private void ScheduleMotionEdit()
    {
        if (_syncingMotion || _selectedId == null) return;
        _motionPending = true;
        _motionTimer.Stop();
        _motionTimer.Start();
    }

    private void CommitMotionEdits()
    {
        _motionTimer.Stop();
        if (!_motionPending || _controller == null || _selectedId == null) return;
        _motionPending = false;
        var clipId = _selectedId;
        var editTime = SelectedLocalTime;
        var state = _motionEditors.Select(editor => (editor.EffectId, Enabled: editor.Toggle.IsChecked == true,
            Ease: MotionEaseValues[Math.Clamp(editor.Ease.SelectedIndex, 0, MotionEaseValues.Length - 1)],
            Direction: editor.Direction == null ? null : MotionDirections[Math.Clamp(editor.Direction.SelectedIndex, 0, MotionDirections.Length - 1)],
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
                    DynamicMotionEffect.Text(effect, "motion", item.Ease);
                    if (item.Direction != null) DynamicMotionEffect.Text(effect, "direction", item.Direction);
                    foreach (var parameter in item.Values)
                        CommitEffectValue(clip, effect, parameter.Key, parameter.Value, editTime);
                }
            }, "Ajustar movimiento");
        }
        catch (Exception ex) { _controller.ReportStatus("Movimiento: " + ex.Message); }
        foreach (var editor in _motionEditors)
            _dirtyEffectTargets.RemoveWhere(target => target.StartsWith("effect." + editor.InstanceId + ".",
                StringComparison.Ordinal));
    }

    private void RefreshMotionEditors(Clip? clip)
    {
        var effects = clip?.Effects.Where(e => DynamicMotionEffect.Supports(e.EffectId)).ToArray() ?? [];
        var signature = (clip?.Id ?? "") + ":" + string.Join("|", effects.Select(e => e.Id + e.EffectId));
        if (_motionSignature != signature)
        {
            foreach (var editor in _motionEditors) UnregisterEffectSliders(editor.InstanceId);
            _motionSignature = signature;
            _motionEditors.Clear();
            MotionEffectsHost.Children.Clear();
            foreach (var effect in effects) MotionEffectsHost.Children.Add(CreateMotionCard(effect));
        }
        else if (_motionPending) return;
        _syncingMotion = true;
        try
        {
            foreach (var editor in _motionEditors)
            {
                var effect = effects.FirstOrDefault(e => e.EffectId == editor.EffectId);
                if (effect == null) continue;
                editor.Toggle.IsChecked = effect.Enabled;
                editor.Toggle.Content = effect.Enabled ? "Activo" : "Desactivado";
                var ease = DynamicMotionEffect.ReadText(effect, "motion", "ease-in-out");
                editor.Ease.SelectedIndex = ease switch
                {
                    "linear" or "Lineal" => 0, "ease-in" or "Acelerado al final" => 1,
                    "ease-out" => 2, _ => 3
                };
                if (editor.Direction != null)
                {
                    var direction = DynamicMotionEffect.ReadText(effect, "direction", MotionDirections[0]);
                    editor.Direction.SelectedIndex = Math.Max(0, Array.IndexOf(MotionDirections, direction));
                }
                foreach (var (key, slider) in editor.Sliders)
                {
                    var fallback = key switch { "zoom_percent" or "distance_percent" => 15,
                        "start_scale" => 100, "end_scale" => 115, _ => 0 };
                    slider.Value = DynamicMotionEffect.ReadNumber(effect, key, fallback);
                    editor.Values[key].Text = slider.Value.ToString("0.#");
                }
            }
        }
        finally { _syncingMotion = false; }
    }

    private Control CreateMotionCard(EffectInstance effect)
    {
        var title = effect.EffectId switch
        {
            DynamicMotionEffect.ZoomInId => "Zoom In dinámico",
            DynamicMotionEffect.ZoomOutId => "Zoom Out dinámico",
            DynamicMotionEffect.PanId => "Paneo dinámico",
            _ => "Recorrido"
        };
        var editor = new MotionEditor
        {
            InstanceId = effect.Id,
            EffectId = effect.EffectId,
            Toggle = new ToggleButton { Content = "Activo", FontSize = 10, Height = 22 },
            Ease = new ComboBox { ItemsSource = MotionEaseLabels, SelectedIndex = 3, FontSize = 10,
                Height = 26, HorizontalAlignment = HorizontalAlignment.Stretch }
        };
        _motionEditors.Add(editor);
        var body = new StackPanel { Spacing = 7 };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };
        heading.Children.Add(new TextBlock { Text = title, FontSize = 10, FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#FF8A00")), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(editor.Toggle, 1); heading.Children.Add(editor.Toggle);
        var reset = new Button { Content = "↺", Width = 23, Height = 22, Padding = new Thickness(0), Margin = new Thickness(5, 0, 0, 0) };
        ToolTip.SetTip(reset, "Restablecer movimiento");
        Grid.SetColumn(reset, 2); heading.Children.Add(reset);
        var remove = new Button { Content = "×", Width = 23, Height = 22, Padding = new Thickness(0), Margin = new Thickness(5, 0, 0, 0) };
        ToolTip.SetTip(remove, "Quitar efecto del medio");
        Grid.SetColumn(remove, 3); heading.Children.Add(remove);
        body.Children.Add(heading);
        if (effect.EffectId is DynamicMotionEffect.ZoomInId or DynamicMotionEffect.ZoomOutId)
            AddMotionSlider(body, editor, "zoom_percent", "Zoom %", 1, 100, 15);
        else if (effect.EffectId == DynamicMotionEffect.PanId)
        {
            AddMotionSlider(body, editor, "distance_percent", "Distancia %", 1, 100, 15);
            editor.Direction = new ComboBox { ItemsSource = MotionDirections, SelectedIndex = 0,
                FontSize = 10, Height = 26, HorizontalAlignment = HorizontalAlignment.Stretch };
            body.Children.Add(new TextBlock { Text = "Dirección", FontSize = 10 });
            body.Children.Add(editor.Direction);
            editor.Direction.SelectionChanged += (_, _) => ScheduleMotionEdit();
        }
        else
        {
            AddMotionSlider(body, editor, "start_x", "Inicio X %", -100, 100, 0);
            AddMotionSlider(body, editor, "start_y", "Inicio Y %", -100, 100, 0);
            AddMotionSlider(body, editor, "start_scale", "Inicio escala %", 1, 400, 100);
            AddMotionSlider(body, editor, "end_x", "Final X %", -100, 100, 0);
            AddMotionSlider(body, editor, "end_y", "Final Y %", -100, 100, 0);
            AddMotionSlider(body, editor, "end_scale", "Final escala %", 1, 400, 115);
        }
        body.Children.Add(new TextBlock { Text = "Suavizado", FontSize = 10 });
        body.Children.Add(editor.Ease);
        editor.Toggle.IsCheckedChanged += (_, _) =>
        {
            editor.Toggle.Content = editor.Toggle.IsChecked == true ? "Activo" : "Desactivado";
            ScheduleMotionEdit();
        };
        editor.Ease.SelectionChanged += (_, _) => ScheduleMotionEdit();
        reset.Click += (_, _) => ResetEffectInstance(effect.Id);
        remove.Click += (_, _) => RemoveEffect(effect.EffectId);
        return CreateEffectCard(body, effect.Id);
    }

    private void AddMotionSlider(StackPanel body, MotionEditor editor, string key, string label,
        double min, double max, double initial)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("95,*,43") };
        row.Children.Add(new TextBlock { Text = label, FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
        var slider = new Slider { Minimum = min, Maximum = max, Value = initial };
        ResetOnThumbDoubleClick(slider, initial);
        Grid.SetColumn(slider, 1); row.Children.Add(slider);
        var value = new TextBlock { Text = initial.ToString("0.#"), FontSize = 10,
            TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(value, 2); row.Children.Add(value);
        editor.Sliders[key] = slider;
        editor.Values[key] = value;
        slider.ValueChanged += (_, _) => { value.Text = slider.Value.ToString("0.#"); ScheduleMotionEdit(); };
        body.Children.Add(row);
        var effect = (_controller == null ? null : Locate(_controller.CurrentProject, _selectedId)?.Clip)?
            .Effects.FirstOrDefault(item => item.Id == editor.InstanceId);
        if (effect != null) RegisterEffectSlider(effect, key, label, slider);
    }
}
