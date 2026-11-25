using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Prometheus.Client.Services;
using Prometheus.Core.Model;

namespace Prometheus.Client.Panels;

public partial class PropertiesPanelView
{
    private readonly DispatcherTimer _imageTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private readonly List<ImageEditor> _imageEditors = new();
    private string _imageSignature = "";
    private bool _syncingImage;
    private bool _imagePending;

    private sealed class ImageEditor
    {
        public required string InstanceId { get; init; }
        public required string EffectId { get; init; }
        public required ToggleButton Toggle { get; init; }
        public Dictionary<string, Slider> Sliders { get; } = new();
        public Dictionary<string, TextBlock> Values { get; } = new();
        public Dictionary<string, CheckBox> Switches { get; } = new();
    }

    private void InitializeImageEditors() => _imageTimer.Tick += (_, _) => CommitImageEdits();

    private void ScheduleImageEdit()
    {
        if (_syncingImage || _selectedId == null) return;
        _imagePending = true;
        _imageTimer.Stop();
        _imageTimer.Start();
    }

    private void CommitImageEdits()
    {
        _imageTimer.Stop();
        if (!_imagePending || _controller == null || _selectedId == null) return;
        _imagePending = false;
        var clipId = _selectedId;
        var editTime = SelectedLocalTime;
        var state = _imageEditors.Select(editor => (editor.EffectId, Enabled: editor.Toggle.IsChecked == true,
            Values: editor.Sliders.ToDictionary(pair => pair.Key, pair => pair.Value.Value)
                .Concat(editor.Switches.Select(pair => new KeyValuePair<string, double>(pair.Key,
                    pair.Value.IsChecked == true ? 1 : 0))).ToDictionary(pair => pair.Key, pair => pair.Value))).ToArray();
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
                    if (item.EffectId == ImageGeometryEffect.CropId)
                    {
                        var left = ImageGeometryEffect.ReadNumber(effect, "left", 0);
                        var right = ImageGeometryEffect.ReadNumber(effect, "right", 0);
                        var top = ImageGeometryEffect.ReadNumber(effect, "top", 0);
                        var bottom = ImageGeometryEffect.ReadNumber(effect, "bottom", 0);
                        if (left + right > 95) ImageGeometryEffect.Number(effect, "right", Math.Max(0, 95 - left));
                        if (top + bottom > 95) ImageGeometryEffect.Number(effect, "bottom", Math.Max(0, 95 - top));
                    }
                }
            }, "Ajustar imagen");
        }
        catch (Exception ex) { _controller.ReportStatus("Imagen: " + ex.Message); }
        foreach (var editor in _imageEditors)
            _dirtyEffectTargets.RemoveWhere(target => target.StartsWith("effect." + editor.InstanceId + ".",
                StringComparison.Ordinal));
    }

    private void RefreshImageEditors(Clip? clip)
    {
        var effects = clip?.Effects.Where(e => ImageGeometryEffect.Supports(e.EffectId) ||
            GpuFilterEffect.Supports(e.EffectId) || GpuDetailEffect.Supports(e.EffectId) ||
            e.EffectId == GpuLutEffect.Id).ToArray() ?? [];
        var signature = (clip?.Id ?? "") + ":" + string.Join("|", effects.Select(e => e.Id + e.EffectId +
            (e.EffectId == GpuLutEffect.Id ? ":" + e.Parameters.GetValueOrDefault("lut")?.ResourceId +
                ":" + e.Parameters.GetValueOrDefault("interpolation")?.Text : "")));
        if (_imageSignature != signature)
        {
            foreach (var editor in _imageEditors) UnregisterEffectSliders(editor.InstanceId);
            _imageSignature = signature;
            _imageEditors.Clear();
            ImageEffectsHost.Children.Clear();
            foreach (var effect in effects) ImageEffectsHost.Children.Add(CreateImageCard(effect));
        }
        else if (_imagePending) return;
        _syncingImage = true;
        try
        {
            foreach (var editor in _imageEditors)
            {
                var effect = effects.FirstOrDefault(e => e.EffectId == editor.EffectId);
                if (effect == null) continue;
                editor.Toggle.IsChecked = effect.Enabled;
                editor.Toggle.Content = effect.Enabled ? "Activo" : "Desactivado";
                foreach (var (key, slider) in editor.Sliders)
                {
                    var fallback = ImageDefault(editor.EffectId, key);
                    slider.Value = ImageGeometryEffect.ReadNumber(effect, key, fallback);
                    editor.Values[key].Text = ImageDisplay(key, slider.Value);
                }
                foreach (var (key, check) in editor.Switches)
                    check.IsChecked = ImageGeometryEffect.ReadNumber(effect, key, ImageDefault(editor.EffectId, key)) > 0.5;
            }
        }
        finally { _syncingImage = false; }
    }

    private Control CreateImageCard(EffectInstance effect)
    {
        var title = effect.EffectId switch
        {
            ImageGeometryEffect.FlipHorizontalId => "Voltear horizontal",
            ImageGeometryEffect.FlipVerticalId => "Voltear vertical",
            ImageGeometryEffect.CropId => "Recorte de bordes",
            ImageGeometryEffect.PixelizeId => "Pixelar",
            GpuFilterEffect.BlurId => "Desenfoque gaussiano",
            GpuFilterEffect.ChromaId => "Croma por color",
            GpuFilterEffect.ChromaRgbId => "Croma RGB",
            GpuFilterEffect.ChromaMultiId => "Croma múltiple",
            GpuDetailEffect.SharpenId => "Nitidez",
            GpuLutEffect.Id => "LUT 3D",
            _ => "Resplandor suave"
        };
        var editor = new ImageEditor
        {
            InstanceId = effect.Id,
            EffectId = effect.EffectId,
            Toggle = new ToggleButton { Content = "Activo", FontSize = 10, Height = 22 }
        };
        _imageEditors.Add(editor);
        var body = new StackPanel { Spacing = 7 };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };
        heading.Children.Add(new TextBlock { Text = title, FontSize = 10, FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#FF8A00")), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(editor.Toggle, 1); heading.Children.Add(editor.Toggle);
        var reset = new Button { Content = "↺", Width = 23, Height = 22, Padding = new Thickness(0), Margin = new Thickness(5, 0, 0, 0),
            IsVisible = effect.EffectId is ImageGeometryEffect.CropId or ImageGeometryEffect.PixelizeId or
                GpuFilterEffect.BlurId or GpuFilterEffect.ChromaId or
                GpuFilterEffect.ChromaRgbId or GpuFilterEffect.ChromaMultiId or
                GpuDetailEffect.SharpenId or GpuDetailEffect.GlowId };
        ToolTip.SetTip(reset, "Restablecer efecto");
        Grid.SetColumn(reset, 2); heading.Children.Add(reset);
        var remove = new Button { Content = "×", Width = 23, Height = 22, Padding = new Thickness(0), Margin = new Thickness(5, 0, 0, 0) };
        ToolTip.SetTip(remove, "Quitar efecto del medio");
        Grid.SetColumn(remove, 3); heading.Children.Add(remove);
        body.Children.Add(heading);
        if (effect.EffectId == ImageGeometryEffect.CropId)
        {
            AddImageSlider(body, editor, "left", "Izquierda %", 0, 95);
            AddImageSlider(body, editor, "top", "Arriba %", 0, 95);
            AddImageSlider(body, editor, "right", "Derecha %", 0, 95);
            AddImageSlider(body, editor, "bottom", "Abajo %", 0, 95);
        }
        else if (effect.EffectId == ImageGeometryEffect.PixelizeId)
            AddImageSlider(body, editor, "block", "Bloque px", 2, 256);
        else if (effect.EffectId == GpuFilterEffect.BlurId)
            AddImageSlider(body, editor, "radius", "Radio px", 0, 24);
        else if (effect.EffectId == GpuDetailEffect.SharpenId)
            AddImageSlider(body, editor, "amount", "Cantidad", 0, 2);
        else if (effect.EffectId == GpuDetailEffect.GlowId)
        {
            AddImageSlider(body, editor, "amount", "Cantidad", 0, 1);
            AddImageSlider(body, editor, "radius", "Radio px", 1, 30);
        }
        else if (effect.EffectId == GpuLutEffect.Id)
        {
            var resourceId = effect.Parameters.GetValueOrDefault("lut")?.ResourceId;
            var resource = _controller?.CurrentProject.EffectResources.FirstOrDefault(r => r.Id == resourceId);
            var pathLabel = new TextBlock { Text = resource == null ? "Elige un archivo .cube" :
                Path.GetFileName(resource.Uri), FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis };
            body.Children.Add(pathLabel);
            var choose = new Button { Content = "Seleccionar LUT .cube", FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Left };
            choose.Click += async (_, _) =>
            {
                if (_controller == null || _selectedId == null) return;
                var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(
                    new FilePickerOpenOptions { Title = "Seleccionar LUT 3D", AllowMultiple = false,
                        FileTypeFilter = [new FilePickerFileType("LUT 3D") { Patterns = ["*.cube"] }] });
                if (files.Count == 0) return;
                var path = files[0].Path.LocalPath;
                try
                {
                    _ = GpuLutEffect.ParseCube(path);
                    var clipId = _selectedId;
                    _controller.UndoRedo.Commit(project =>
                    {
                        var target = Locate(project, clipId)?.Clip;
                        var instance = target?.Effects.FirstOrDefault(e => e.EffectId == GpuLutEffect.Id);
                        if (instance == null) return;
                        var added = new EffectResource { Kind = "lut3d", Uri = path, Relative = false };
                        project.EffectResources.Add(added);
                        instance.Parameters["lut"] = new EffectValue { Kind = "resource", ResourceId = added.Id };
                        instance.Enabled = true;
                    }, "Seleccionar LUT 3D");
                    pathLabel.Text = Path.GetFileName(path);
                    _controller.ReportStatus("LUT 3D cargada");
                }
                catch (Exception ex) { _controller.ReportStatus("LUT 3D: " + ex.Message); }
            };
            body.Children.Add(choose);
            var interpolation = new ComboBox { ItemsSource = new[] { "Trilineal", "Tetraédrica" },
                SelectedIndex = GpuLutEffect.IsTetrahedral(_controller?.CurrentProject.ActiveSequence?.Clips
                    .FirstOrDefault(c => c.Id == _selectedId) ?? new Clip()) ? 1 : 0,
                Width = 130, HorizontalAlignment = HorizontalAlignment.Left };
            interpolation.SelectionChanged += (_, _) =>
            {
                if (_controller == null || _selectedId == null || _syncingImage) return;
                var clipId = _selectedId;
                var mode = interpolation.SelectedIndex == 1 ? "tetrahedral" : "trilinear";
                _controller.UndoRedo.Commit(project =>
                {
                    var instance = Locate(project, clipId)?.Clip?
                        .Effects.FirstOrDefault(e => e.EffectId == GpuLutEffect.Id);
                    if (instance != null) instance.Parameters["interpolation"] = new EffectValue { Kind = "text", Text = mode };
                }, "Interpolación de LUT");
            };
            body.Children.Add(interpolation);
        }
        else if (effect.EffectId == GpuFilterEffect.ChromaRgbId)
        {
            AddImageSwitch(body, editor, "use_red", "Rojo");
            AddImageSwitch(body, editor, "use_green", "Verde");
            AddImageSwitch(body, editor, "use_blue", "Azul");
            AddImageSlider(body, editor, "tolerance", "Tolerancia", 0, 1);
            AddImageSlider(body, editor, "softness", "Suavidad", 0, 1);
            AddImageSlider(body, editor, "spill", "Derrame", 0, 1);
        }
        else if (effect.EffectId is GpuFilterEffect.ChromaId or GpuFilterEffect.ChromaMultiId)
        {
            var multi = effect.EffectId == GpuFilterEffect.ChromaMultiId;
            AddChromaColor(body, editor, "", multi ? "Color 1" : "Color elegido");
            if (multi)
            {
                AddImageSwitch(body, editor, "enabled2", "Color 2");
                var second = editor.Switches["enabled2"];
                body.Children.Remove(second);
                AddChromaColor(body, editor, "2", "Color 2", second);
                AddImageSwitch(body, editor, "enabled3", "Color 3");
                var third = editor.Switches["enabled3"];
                body.Children.Remove(third);
                AddChromaColor(body, editor, "3", "Color 3", third);
                AddImageSwitch(body, editor, "shared", "Mismos ajustes para todos");
            }
            AddImageSlider(body, editor, "tolerance", "Tolerancia", 0, 1);
            AddImageSlider(body, editor, "softness", "Suavidad", 0, 1);
            AddImageSlider(body, editor, "spill", "Derrame", 0, 1);
            if (multi)
            {
                var individual = new StackPanel { Spacing = 5 };
                AddImageSlider(individual, editor, "tolerance2", "Tolerancia 2", 0, 1);
                AddImageSlider(individual, editor, "softness2", "Suavidad 2", 0, 1);
                AddImageSlider(individual, editor, "spill2", "Derrame 2", 0, 1);
                AddImageSlider(individual, editor, "tolerance3", "Tolerancia 3", 0, 1);
                AddImageSlider(individual, editor, "softness3", "Suavidad 3", 0, 1);
                AddImageSlider(individual, editor, "spill3", "Derrame 3", 0, 1);
                editor.Switches["shared"].IsCheckedChanged += (_, _) =>
                    individual.IsVisible = editor.Switches["shared"].IsChecked != true;
                individual.IsVisible = editor.Switches["shared"].IsChecked != true;
                body.Children.Add(individual);
            }
        }
        editor.Toggle.IsCheckedChanged += (_, _) =>
        {
            editor.Toggle.Content = editor.Toggle.IsChecked == true ? "Activo" : "Desactivado";
            if (!_syncingImage && editor.Toggle.IsChecked == true &&
                editor.EffectId is GpuFilterEffect.ChromaId or GpuFilterEffect.ChromaRgbId or GpuFilterEffect.ChromaMultiId)
                foreach (var other in _imageEditors.Where(other => other != editor &&
                    other.EffectId is GpuFilterEffect.ChromaId or GpuFilterEffect.ChromaRgbId or GpuFilterEffect.ChromaMultiId))
                    other.Toggle.IsChecked = false;
            ScheduleImageEdit();
        };
        reset.Click += (_, _) => ResetEffectInstance(effect.Id);
        remove.Click += (_, _) => RemoveEffect(effect.EffectId);
        return CreateEffectCard(body, effect.Id);
    }

    private void AddImageSwitch(StackPanel body, ImageEditor editor, string key, string label)
    {
        var check = new CheckBox { Content = label, FontSize = 10,
            IsChecked = ImageDefault(editor.EffectId, key) > 0.5 };
        check.IsCheckedChanged += (_, _) => ScheduleImageEdit();
        editor.Switches[key] = check;
        body.Children.Add(check);
    }

    private void AddChromaColor(StackPanel body, ImageEditor editor, string suffix, string title,
        CheckBox? enabled = null)
    {
        foreach (var channel in new[] { "red", "green", "blue" })
        {
            AddImageSlider(body, editor, channel + suffix, channel, 0, 1);
            body.Children[^1].IsVisible = false;
        }
        Color CurrentColor() => Color.FromRgb(
            (byte)Math.Round(editor.Sliders["red" + suffix].Value * 255),
            (byte)Math.Round(editor.Sliders["green" + suffix].Value * 255),
            (byte)Math.Round(editor.Sliders["blue" + suffix].Value * 255));
        var initial = CurrentColor();
        var field = new ColorField { Hex = $"#{initial.R:X2}{initial.G:X2}{initial.B:X2}" };
        var row = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
        if (enabled == null)
            row.Children.Add(new TextBlock { Text = title, FontSize = 10, MinWidth = 52,
                VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DarkGray });
        else
        {
            enabled.MinWidth = 66;
            enabled.IsCheckedChanged += (_, _) => field.IsEnabled = enabled.IsChecked == true;
            field.IsEnabled = enabled.IsChecked == true;
            row.Children.Add(enabled);
        }
        field.HorizontalAlignment = HorizontalAlignment.Stretch;
        row.Children.Add(field);
        body.Children.Add(row);
        var syncing = false;
        field.HexChanged += (_, _) =>
        {
            if (syncing || !Color.TryParse(field.Hex, out var selected)) return;
            syncing = true;
            editor.Sliders["red" + suffix].Value = selected.R / 255.0;
            editor.Sliders["green" + suffix].Value = selected.G / 255.0;
            editor.Sliders["blue" + suffix].Value = selected.B / 255.0;
            syncing = false;
            ScheduleImageEdit();
        };
        foreach (var channel in new[] { "red", "green", "blue" })
            editor.Sliders[channel + suffix].ValueChanged += (_, _) =>
            {
                var color = CurrentColor();
                if (syncing) return;
                syncing = true;
                field.Hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                syncing = false;
            };
        field.PickRequested += (_, _) =>
        {
            if (_selectedId == null || _controller == null) return;
            var clipId = _selectedId;
            var instanceId = editor.InstanceId;
            ChromaColorPicker.Begin(clipId, color =>
            {
                if (_selectedId != clipId) return;
                var target = _imageEditors.FirstOrDefault(item => item.InstanceId == instanceId);
                if (target == null) return;
                target.Sliders["red" + suffix].Value = color.R / 255.0;
                target.Sliders["green" + suffix].Value = color.G / 255.0;
                target.Sliders["blue" + suffix].Value = color.B / 255.0;
                ScheduleImageEdit();
                CommitImageEdits();
            });
            _controller.ReportStatus("Gotero activo: haz clic sobre el medio en Previsualización");
        };
    }

    private void AddImageSlider(StackPanel body, ImageEditor editor, string key, string label, double min, double max)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*,38") };
        row.Children.Add(new TextBlock { Text = label, FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
        var slider = new Slider { Minimum = min, Maximum = max, Value = ImageDefault(editor.EffectId, key) };
        ResetOnThumbDoubleClick(slider, ImageDefault(editor.EffectId, key));
        Grid.SetColumn(slider, 1); row.Children.Add(slider);
        var value = new TextBlock { Text = ImageDisplay(key, slider.Value), FontSize = 10,
            TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(value, 2); row.Children.Add(value);
        editor.Sliders[key] = slider;
        editor.Values[key] = value;
        slider.ValueChanged += (_, _) => { value.Text = ImageDisplay(key, slider.Value); ScheduleImageEdit(); };
        body.Children.Add(row);
        var effect = (_controller == null ? null : Locate(_controller.CurrentProject, _selectedId)?.Clip)?
            .Effects.FirstOrDefault(item => item.Id == editor.InstanceId);
        if (effect != null) RegisterEffectSlider(effect, key, label, slider);
    }

    private static double ImageDefault(string effectId, string key) => key == "block" ? 16 :
        GpuDetailEffect.Supports(effectId) ? GpuDetailEffect.Default(effectId, key) : GpuFilterEffect.Default(key);
    private static string ImageDisplay(string key, double value) => key is "block" or "radius" or
        "left" or "top" or "right" or "bottom" ? value.ToString("0") : value.ToString("0.00");
}
