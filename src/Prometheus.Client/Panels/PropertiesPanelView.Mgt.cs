using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Prometheus.Client.Services;
using Prometheus.Core.Model;

namespace Prometheus.Client.Panels;

public partial class PropertiesPanelView
{
    private string? _mgtEditorKey;
    private readonly Dictionary<string, Control> _mgtInputs = new(StringComparer.Ordinal);
    private bool _mgtSyncing;

    private void RefreshMgtEditors(Clip? clip)
    {
        var asset = clip == null ? null : _controller?.CurrentProject.Assets
            .FirstOrDefault(item => item.Id == clip.AssetId && item.Source.Kind == "mgt");
        var key = clip == null || asset == null ? null : clip.Id + ":" + asset.Id;
        if (_mgtEditorKey != key)
        {
            _mgtEditorKey = key;
            _mgtInputs.Clear();
            MgtParameterRows.Children.Clear();
            if (asset != null)
                foreach (var parameter in asset.MgtParameters)
                    AddMgtParameter(parameter);
            if (asset != null && asset.MgtParameters.Count == 0)
                MgtParameterRows.Children.Add(new TextBlock { Text = "Este MGT no declara parámetros editables.",
                    FontSize = 10, Foreground = Brushes.Gray });
        }
        if (clip == null || asset == null) return;
        _mgtSyncing = true;
        try
        {
            foreach (var parameter in asset.MgtParameters)
            {
                if (!_mgtInputs.TryGetValue(parameter.Id, out var input)) continue;
                var value = clip.MgtOverrides.TryGetValue(parameter.Id, out var stored) ? stored : parameter.Default;
                switch (input)
                {
                    case Slider slider when value.ValueKind == JsonValueKind.Number:
                        slider.Value = value.GetDouble(); break;
                    case CheckBox check when value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                        check.IsChecked = value.GetBoolean(); break;
                    case ComboBox combo when value.ValueKind == JsonValueKind.String:
                        combo.SelectedIndex = parameter.Options.IndexOf(value.GetString() ?? ""); break;
                    case ColorField color when value.ValueKind == JsonValueKind.String:
                        color.Hex = value.GetString() ?? ""; break;
                    case TextBox box when !box.IsFocused:
                        box.Text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText(); break;
                }
            }
        }
        finally { _mgtSyncing = false; }
    }

    private void AddMgtParameter(MgtParameter parameter)
    {
        var row = new StackPanel { Spacing = 3 };
        row.Children.Add(new TextBlock { Text = parameter.Label, FontSize = 10,
            Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap });
        Control input;
        switch (parameter.Type)
        {
            case "number" when parameter.Min is { } min && parameter.Max is { } max && max > min:
                var slider = new Slider { Minimum = min, Maximum = max,
                    TickFrequency = parameter.Step ?? 0.01, IsSnapToTickEnabled = parameter.Step is > 0 };
                slider.ValueChanged += (_, _) => {
                    if (!_mgtSyncing) SaveMgtParameter(parameter, slider.Value);
                };
                input = slider;
                break;
            case "boolean":
                var check = new CheckBox { Content = "Activado", FontSize = 10 };
                check.IsCheckedChanged += (_, _) => {
                    if (!_mgtSyncing) SaveMgtParameter(parameter, check.IsChecked == true);
                };
                input = check;
                break;
            case "select" or "image":
                var combo = new ComboBox { ItemsSource = parameter.Options, FontSize = 10, Height = 26 };
                combo.SelectionChanged += (_, _) => {
                    if (!_mgtSyncing && combo.SelectedItem is string selected)
                        SaveMgtParameter(parameter, selected);
                };
                input = combo;
                break;
            case "color":
                var color = new ColorField { AllowAlpha = true,
                    Hex = parameter.Default.GetString() ?? "#FFFFFF" };
                color.HexChanged += (_, _) => {
                    if (!_mgtSyncing) SaveMgtParameter(parameter, color.Hex);
                };
                color.PickRequested += (_, _) =>
                {
                    if (_selectedId == null || _controller == null) return;
                    var clipId = _selectedId;
                    ChromaColorPicker.BeginComposite(picked =>
                    {
                        if (_selectedId != clipId) return;
                        color.Hex = $"#{picked.R:X2}{picked.G:X2}{picked.B:X2}";
                        SaveMgtParameter(parameter, color.Hex);
                    });
                    _controller.ReportStatus("Gotero activo: elige un color en Previsualización");
                };
                input = color;
                break;
            case "text" or "font":
                var box = new TextBox { FontSize = 10, MinHeight = 25,
                    AcceptsReturn = parameter.Type == "text", TextWrapping = TextWrapping.Wrap };
                if (parameter.Type == "text") box.MaxLength = 1000;
                box.TextChanged += (_, _) => {
                    if (!_mgtSyncing) SaveMgtParameter(parameter, box.Text ?? "");
                };
                input = box;
                break;
            default:
                input = new TextBlock { Text = "Este recurso se habilitará con la compatibilidad MGT ampliada.",
                    FontSize = 10, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap };
                break;
        }
        row.Children.Add(input);
        MgtParameterRows.Children.Add(row);
        _mgtInputs[parameter.Id] = input;
    }

    private void SaveMgtParameter(MgtParameter parameter, object value)
    {
        if (_controller == null || _selectedId == null || _mgtSyncing) return;
        if (!ValidMgtValue(parameter, value)) return;
        var clipId = _selectedId;
        var element = JsonSerializer.SerializeToElement(value);
        try
        {
            _controller.UndoRedo.CommitCoalesced(project =>
            {
                var target = Locate(project, clipId)?.Clip;
                if (target != null) target.MgtOverrides[parameter.Id] = element;
            }, "Editar " + parameter.Label, "mgt:" + clipId + ":" + parameter.Id);
        }
        catch (Exception ex) { _controller.ReportStatus("Parámetro MGT: " + ex.Message); }
    }

    private static bool ValidMgtValue(MgtParameter parameter, object value) => parameter.Type switch
    {
        "number" => value is double number && double.IsFinite(number) &&
            number >= (parameter.Min ?? double.NegativeInfinity) &&
            number <= (parameter.Max ?? double.PositiveInfinity),
        "boolean" => value is bool,
        "select" or "image" => value is string choice && parameter.Options.Contains(choice),
        "text" => value is string text && text.Length <= 1000,
        "font" => value is string font && !string.IsNullOrWhiteSpace(font) && font.Length <= 160,
        "color" => value is string color && Regex.IsMatch(color, "^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$"),
        _ => false
    };
}
