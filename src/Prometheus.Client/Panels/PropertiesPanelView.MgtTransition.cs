using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Prometheus.Client.Services;
using Prometheus.Core.Model;

namespace Prometheus.Client.Panels;

public partial class PropertiesPanelView
{
    private string? _transitionParameterKey;
    private bool _syncingTransitionParameters;
    private readonly Dictionary<string, Control> _transitionParameterInputs = new(StringComparer.Ordinal);

    private void RefreshTransitionParameters(VideoTransition? transition, bool editable)
    {
        var definition = transition?.Mgt;
        var key = definition == null ? null : transition!.Id + ":" + definition.Id;
        _syncingTransitionParameters = true;
        try
        {
            if (_transitionParameterKey != key)
            {
                _transitionParameterKey = key;
                _transitionParameterInputs.Clear();
                TransitionParameterRows.Children.Clear();
                if (definition != null)
                    foreach (var parameter in definition.Parameters)
                        AddTransitionParameter(transition!.Id, definition, parameter);
            }
            if (definition == null) return;
            TransitionParameterRows.IsEnabled = editable;
            foreach (var parameter in definition.Parameters)
            {
                if (!_transitionParameterInputs.TryGetValue(parameter.Id, out var input)) continue;
                var value = transition!.MgtOverrides.TryGetValue(parameter.Id, out var current) &&
                    MgtGpuTransition.Accepts(parameter, current) ? current : parameter.Default;
                switch (input)
                {
                    case NumericUpDown editor when !editor.IsKeyboardFocusWithin:
                        editor.Value = (decimal)value.GetDouble(); break;
                    case ComboBox picker:
                        picker.SelectedIndex = parameter.Options.IndexOf(value.GetString()!); break;
                    case ColorField color:
                        color.Hex = value.GetString()!; break;
                    case CheckBox flag:
                        flag.IsChecked = value.GetBoolean(); break;
                    case TextBox text when !text.IsFocused:
                        text.Text = value.GetString(); break;
                }
            }
        }
        finally { _syncingTransitionParameters = false; }
    }

    private void AddTransitionParameter(string id, MgtGpuTransition definition, MgtParameter parameter)
    {
        var row = new StackPanel { Spacing = 3 };
        row.Children.Add(new TextBlock { Text = parameter.Label, FontSize = 10, TextWrapping = TextWrapping.Wrap });
        Control input;
        void Save(object value) => SetTransitionParameter(id, parameter, value);
        switch (parameter.Type)
        {
            case "number":
                var number = new NumericUpDown { Minimum = (decimal)(parameter.Min ?? -1e8),
                    Maximum = (decimal)(parameter.Max ?? 1e8), Increment = (decimal)(parameter.Step ?? 0.01),
                    FormatString = "0.####" };
                number.ValueChanged += (_, _) => { if (number.Value is { } value) Save((double)value); };
                input = number;
                break;
            case "select":
                var labels = definition.OptionLabels.GetValueOrDefault(parameter.Id);
                var combo = new ComboBox
                {
                    ItemsSource = parameter.Options.Select(v => labels?.GetValueOrDefault(v) ?? v).ToArray()
                };
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedIndex >= 0) Save(parameter.Options[combo.SelectedIndex]);
                };
                input = combo;
                break;
            case "color":
                var color = new ColorField { AllowAlpha = true };
                color.HexChanged += (_, _) => Save(color.Hex);
                color.PickRequested += (_, _) =>
                {
                    if (_controller == null || _controller.SelectedTransitionId != id) return;
                    ChromaColorPicker.BeginComposite(picked =>
                    {
                        if (_controller.SelectedTransitionId != id) return;
                        color.Hex = $"#{picked.R:X2}{picked.G:X2}{picked.B:X2}";
                        Save(color.Hex);
                    });
                    _controller.ReportStatus("Gotero activo: elige un color en Previsualización");
                };
                input = color;
                break;
            case "boolean":
                var check = new CheckBox { Content = "Activado" };
                check.IsCheckedChanged += (_, _) => Save(check.IsChecked == true);
                input = check;
                break;
            default:
                var text = new TextBox { MaxLength = 300 };
                text.TextChanged += (_, _) => Save(text.Text ?? "");
                input = text;
                break;
        }
        row.Children.Add(input);
        TransitionParameterRows.Children.Add(row);
        _transitionParameterInputs[parameter.Id] = input;
    }

    private void SetTransitionParameter(string id, MgtParameter parameter, object value)
    {
        if (_controller == null || _syncingTransitionParameters || _controller.SelectedTransitionId != id) return;
        var json = JsonSerializer.SerializeToElement(value);
        if (!MgtGpuTransition.Accepts(parameter, json)) return;
        try
        {
            _controller.UndoRedo.CommitCoalesced(project =>
            {
                var sequence = project.ActiveSequence!;
                var transition = sequence.VideoTransitions.First(t => t.Id == id);
                var track = sequence.Clips.First(c => c.Id == transition.LeftClipId).TrackId;
                if (sequence.Tracks.Any(t => t.Id == track && t.Locked))
                    throw new InvalidOperationException("Desbloquea la pista para ajustar la transición.");
                transition.MgtOverrides[parameter.Id] = json;
            }, "Editar " + parameter.Label, "transition:" + id + ":" + parameter.Id);
        }
        catch (Exception ex) { _controller.ReportStatus(ex.Message); }
    }
}
