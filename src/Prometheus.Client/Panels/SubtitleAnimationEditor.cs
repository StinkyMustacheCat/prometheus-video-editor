using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public sealed class SubtitleAnimationEditor : UserControl
{
    private readonly StackPanel _rows = new() { Spacing = 7 };
    private readonly TextBlock _name = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.OrangeRed, FontSize = 10 };
    private readonly CheckBox _enabled = new() { Content = "Animación activa" };
    private readonly Dictionary<string, Control> _inputs = new();
    private ProjectController? _controller;
    private string? _key;
    private bool _syncing;

    public SubtitleAnimationEditor()
    {
        var body = new StackPanel { Spacing = 7 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        var load = new Button { Content = "Elegir plantilla…", Classes = { "nle-btn" } };
        var remove = new Button { Content = "Quitar", Classes = { "nle-btn" } };
        load.Click += async (_, _) =>
        {
            if (_controller == null || TopLevel.GetTopLevel(this) is not { } top) return;
            var projectId = _controller.CurrentProject.Id; var sequenceId = _controller.CurrentProject.ActiveSequenceId;
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Plantilla MGT de subtítulos",
                AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Plantillas MGT") { Patterns = ["*.wmgt.json", "*.mgt.json", "*.json"] }] });
            if (files.Count == 0) return;
            try
            {
                if (_controller.CurrentProject.Id != projectId || _controller.CurrentProject.ActiveSequenceId != sequenceId)
                    throw new InvalidOperationException("El montaje cambió. Elige la plantilla de nuevo.");
                var path = files[0].TryGetLocalPath() ?? throw new InvalidOperationException("Selecciona un archivo local.");
                if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new ArgumentException("La plantilla supera 4 MB.");
                var json = File.ReadAllText(path);
                Commit(s => SubtitleAnimation.Set(s, json), "Aplicar plantilla de subtítulos");
            }
            catch (Exception e) { _error.Text = e.Message; }
        };
        remove.Click += (_, _) => Commit(s => s.SubtitleAnimation = null, "Quitar animación de subtítulos");
        _enabled.IsCheckedChanged += (_, _) => { if (!_syncing) Commit(s => { if (s.SubtitleAnimation != null) s.SubtitleAnimation.Enabled = _enabled.IsChecked == true; }, "Activar animación de subtítulos"); };
        buttons.Children.Add(load); buttons.Children.Add(remove);
        body.Children.Add(_name); body.Children.Add(buttons); body.Children.Add(_enabled);
        body.Children.Add(new TextBlock { Text = "Presentación de toda la pista. Las cápsulas conservan el texto y sus tiempos. Sin tiempos de palabra se anima por frase.",
            FontSize = 10, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(_rows); body.Children.Add(_error); Content = body;
        AttachedToVisualTree += (_, _) => { _controller = ProjectController.Instance; if (_controller != null) _controller.StateChanged += Refresh; Refresh(); };
        DetachedFromVisualTree += (_, _) => { if (_controller != null) _controller.StateChanged -= Refresh; _controller = null; };
    }

    private void Commit(Action<Sequence> edit, string description, string? parameter = null)
    {
        if (_controller?.CurrentProject.ActiveSequence == null) return;
        try
        {
            if (parameter == null) _controller.UndoRedo.Commit(p => edit(p.ActiveSequence!), description);
            else _controller.UndoRedo.CommitCoalesced(p => edit(p.ActiveSequence!), description, "subtitle-animation:" + parameter);
            _error.Text = ""; Refresh();
        }
        catch (Exception e) { _error.Text = e.Message; }
    }

    private void Save(MgtParameter parameter, object value)
    {
        if (_syncing) return;
        var json = JsonSerializer.SerializeToElement(value);
        Commit(s => { if (s.SubtitleAnimation is { } settings) SubtitleAnimation.SetParameter(settings, parameter.Id, json); },
            "Editar animación de subtítulos", parameter.Id);
    }

    private void Refresh()
    {
        if (_controller?.CurrentProject.ActiveSequence is not { } sequence) return;
        _syncing = true;
        try
        {
            var settings = sequence.SubtitleAnimation;
            var key = sequence.Id + ":" + settings?.TemplateJson;
            _enabled.IsEnabled = settings != null; _enabled.IsChecked = settings?.Enabled == true;
            _name.Text = "Sin plantilla · texto normal";
            if (settings != null) { using var doc = JsonDocument.Parse(settings.TemplateJson); _name.Text = doc.RootElement.GetProperty("name").GetString(); }
            var parameters = settings == null ? Array.Empty<MgtParameter>() : SubtitleAnimation.Parameters(settings).ToArray();
            if (_key != key)
            {
                _key = key; _rows.Children.Clear(); _inputs.Clear();
                foreach (var p in parameters)
                {
                    var row = new StackPanel { Spacing = 3 }; row.Children.Add(new TextBlock { Text = p.Label, FontSize = 10 });
                    Control input;
                    if (p.Type == "number")
                    {
                        var number = new NumericUpDown { Minimum = (decimal)(p.Min ?? -1000000), Maximum = (decimal)(p.Max ?? 1000000), Increment = (decimal)(p.Step ?? 1) };
                        number.ValueChanged += (_, _) => { if (number.Value is { } value) Save(p, (double)value); }; input = number;
                    }
                    else if (p.Type == "boolean") { var check = new CheckBox(); check.IsCheckedChanged += (_, _) => Save(p, check.IsChecked == true); input = check; }
                    else if (p.Type is "select" or "image") { var combo = new ComboBox { ItemsSource = p.Options }; combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string value) Save(p, value); }; input = combo; }
                    else if (p.Type == "color")
                    {
                        var color = new ColorField { AllowAlpha = true }; color.HexChanged += (_, _) => Save(p, color.Hex);
                        color.PickRequested += (_, _) => ChromaColorPicker.BeginComposite(c => { color.Hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}"; }); input = color;
                    }
                    else { var box = new TextBox(); box.LostFocus += (_, _) => Save(p, box.Text ?? ""); input = box; }
                    input.Name = "SubtitleParameter_" + p.Id; row.Children.Add(input); _rows.Children.Add(row); _inputs[p.Id] = input;
                }
            }
            foreach (var p in parameters)
            {
                var value = settings!.Parameters.GetValueOrDefault(p.Id, p.Default);
                switch (_inputs[p.Id])
                {
                    case NumericUpDown number: number.Value = (decimal)value.GetDouble(); break;
                    case CheckBox check: check.IsChecked = value.GetBoolean(); break;
                    case ComboBox combo: combo.SelectedItem = value.GetString(); break;
                    case ColorField color: color.Hex = value.GetString()!; break;
                    case TextBox box when !box.IsFocused: box.Text = value.GetString(); break;
                }
            }
        }
        catch (Exception e) { _error.Text = e.Message; }
        finally { _syncing = false; }
    }
}
