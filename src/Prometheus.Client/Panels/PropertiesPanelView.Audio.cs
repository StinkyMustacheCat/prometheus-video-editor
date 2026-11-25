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
    private readonly DispatcherTimer _audioTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private readonly List<AudioEditor> _audioEditors = new();
    private string _audioSignature = "";
    private bool _syncingAudio;
    private bool _audioPending;

    private sealed class AudioEditor
    {
        public required string InstanceId { get; init; }
        public required ToggleButton Toggle { get; init; }
        public Dictionary<string, Slider> Sliders { get; } = new();
        public Dictionary<string, TextBlock> Values { get; } = new();
    }

    private void InitializeAudioEditors() => _audioTimer.Tick += (_, _) => CommitAudioEdits();

    private void ScheduleAudioEdit()
    {
        if (_syncingAudio || _audioClipId == null) return;
        _audioPending = true;
        _audioTimer.Stop();
        _audioTimer.Start();
    }

    private void CommitAudioEdits()
    {
        _audioTimer.Stop();
        if (!_audioPending || _controller == null || _audioClipId == null) return;
        _audioPending = false;
        var clipId = _audioClipId;
        var state = _audioEditors.Select(editor => (editor.InstanceId,
            Enabled: editor.Toggle.IsChecked == true,
            Values: editor.Sliders.ToDictionary(pair => pair.Key, pair => pair.Value.Value))).ToArray();
        try
        {
            _controller.UndoRedo.Commit(project =>
            {
                var clip = Locate(project, clipId)?.Clip;
                if (clip == null) return;
                foreach (var item in state)
                {
                    var effect = clip.Effects.FirstOrDefault(e => e.Id == item.InstanceId);
                    if (effect == null) continue;
                    effect.Enabled = item.Enabled;
                    foreach (var parameter in item.Values) AudioEffects.Set(effect, parameter.Key, parameter.Value);
                }
            }, "Ajustar efectos de audio");
        }
        catch (Exception ex) { _controller.ReportStatus("Audio: " + ex.Message); }
    }

    private void RefreshAudioEditors(Clip? clip)
    {
        var effects = clip?.Effects.Where(e => AudioEffects.Find(e.EffectId) != null).ToArray() ?? [];
        var signature = (clip?.Id ?? "") + ":" + string.Join("|", effects.Select(e => e.Id));
        if (_audioSignature != signature)
        {
            _audioSignature = signature;
            _audioEditors.Clear();
            AudioEffectsHost.Children.Clear();
            for (var i = 0; i < effects.Length; i++)
                AudioEffectsHost.Children.Add(CreateAudioCard(effects[i], i, effects.Length));
        }
        else if (_audioPending) return;
        _syncingAudio = true;
        try
        {
            foreach (var editor in _audioEditors)
            {
                var effect = effects.FirstOrDefault(e => e.Id == editor.InstanceId);
                var definition = effect == null ? null : AudioEffects.Find(effect.EffectId);
                if (effect == null || definition == null) continue;
                editor.Toggle.IsChecked = effect.Enabled;
                editor.Toggle.Content = effect.Enabled ? "Activo" : "Desactivado";
                foreach (var parameter in definition.Parameters)
                {
                    var slider = editor.Sliders[parameter.Key];
                    slider.Value = AudioEffects.Read(effect, parameter);
                    editor.Values[parameter.Key].Text = Display(slider.Value, parameter.Unit);
                }
            }
        }
        finally { _syncingAudio = false; }
    }

    private Control CreateAudioCard(EffectInstance effect, int index, int count)
    {
        var definition = AudioEffects.Find(effect.EffectId)!;
        var editor = new AudioEditor { InstanceId = effect.Id,
            Toggle = new ToggleButton { Content = "Activo", FontSize = 10, Height = 22 } };
        _audioEditors.Add(editor);
        var body = new StackPanel { Spacing = 7 };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };
        heading.Children.Add(new TextBlock { Text = definition.Name, FontSize = 10, FontWeight = FontWeight.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse("#FF8A00")) });
        Grid.SetColumn(editor.Toggle, 1); heading.Children.Add(editor.Toggle);
        var up = new Button { Content = "↑", Width = 22, Height = 22, Padding = new Thickness(0), IsEnabled = index > 0 };
        var down = new Button { Content = "↓", Width = 22, Height = 22, Padding = new Thickness(0), IsEnabled = index < count - 1 };
        up.Click += (_, _) => MoveAudioEffect(effect.Id, -1);
        down.Click += (_, _) => MoveAudioEffect(effect.Id, 1);
        var reset = new Button { Content = "↺", Width = 22, Height = 22, Padding = new Thickness(0) };
        ToolTip.SetTip(reset, "Restablecer efecto");
        Grid.SetColumn(reset, 2); heading.Children.Add(reset);
        var remove = new Button { Content = "×", Width = 22, Height = 22, Padding = new Thickness(0) };
        ToolTip.SetTip(remove, "Quitar efecto");
        Grid.SetColumn(remove, 3); heading.Children.Add(remove);
        body.Children.Add(heading);
        var order = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4,
            Children = { new TextBlock { Text = "Orden", FontSize = 10, VerticalAlignment = VerticalAlignment.Center }, up, down } };
        body.Children.Add(order);
        foreach (var parameter in definition.Parameters) AddAudioSlider(body, editor, parameter);
        editor.Toggle.IsCheckedChanged += (_, _) =>
        {
            editor.Toggle.Content = editor.Toggle.IsChecked == true ? "Activo" : "Desactivado";
            ScheduleAudioEdit();
        };
        reset.Click += (_, _) => ResetEffectInstance(effect.Id);
        remove.Click += (_, _) => RemoveAudioEffect(effect.Id);
        return CreateEffectCard(body, effect.Id);
    }

    private void AddAudioSlider(StackPanel body, AudioEditor editor, AudioEffectParameter parameter)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*,66") };
        row.Children.Add(new TextBlock { Text = parameter.Label, FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center });
        var slider = new Slider { Minimum = parameter.Minimum, Maximum = parameter.Maximum,
            Value = parameter.Default };
        ResetOnThumbDoubleClick(slider, parameter.Default);
        Grid.SetColumn(slider, 1); row.Children.Add(slider);
        var value = new TextBlock { Text = Display(slider.Value, parameter.Unit), FontSize = 10,
            TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(value, 2); row.Children.Add(value);
        editor.Sliders[parameter.Key] = slider;
        editor.Values[parameter.Key] = value;
        slider.ValueChanged += (_, _) => { value.Text = Display(slider.Value, parameter.Unit); ScheduleAudioEdit(); };
        body.Children.Add(row);
    }

    private static string Display(double value, string unit) => value.ToString("0.##") + unit;

    private void MoveAudioEffect(string instanceId, int direction)
    {
        CommitAudioEdits();
        if (_controller == null || _audioClipId == null) return;
        var clipId = _audioClipId;
        _controller.UndoRedo.Commit(project =>
        {
            var effects = Locate(project, clipId)?.Clip.Effects;
            if (effects == null) return;
            var index = effects.FindIndex(e => e.Id == instanceId);
            var target = index + direction;
            if (index < 0 || target < 0 || target >= effects.Count) return;
            (effects[index], effects[target]) = (effects[target], effects[index]);
        }, "Ordenar efecto de audio");
    }

    private void RemoveAudioEffect(string instanceId)
    {
        CommitAudioEdits();
        if (_controller == null || _audioClipId == null) return;
        var clipId = _audioClipId;
        _controller.UndoRedo.Commit(project =>
        {
            var effects = Locate(project, clipId)?.Clip.Effects;
            if (effects == null) return;
            var clip = Locate(project, clipId)?.Clip;
            if (clip != null) KeyframeAnimation.RemoveForEffect(clip, instanceId);
            effects.RemoveAll(effect => effect.Id == instanceId);
        }, "Quitar efecto de audio");
    }
}
