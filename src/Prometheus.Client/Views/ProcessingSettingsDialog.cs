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
using Avalonia.Threading;
using Prometheus.Client.Services;

namespace Prometheus.Client.Views;

internal sealed class ProcessingSettingsDialog : Window
{
    private sealed record Option(string Id, string Label) { public override string ToString() => Label; }
    private sealed record Row(ProcessingFunction Function, ComboBox Provider, ComboBox? Adapter, TextBlock Actual);
    private readonly Dictionary<ProcessingFunction, Row> _rows = new();
    private readonly StackPanel _body = new() { Spacing = 12 };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Orange };
    private readonly NumericUpDown _threads = new() { Minimum = 0, Maximum = 64, Increment = 1, Width = 100 };
    private ProcessingSettings _draft;

    internal ProcessingSettingsDialog()
    {
        Title = "Procesamiento · Prometheus";
        Width = 900; Height = 740; MinWidth = 720; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _draft = ProcessingService.GetSettings();
        _threads.Value = _draft.CpuThreads;
        var apply = Button("Aplicar", "ApplyProcessing");
        var cancel = Button("Cerrar", "CloseProcessing");
        var reset = Button("Restaurar automático", "ResetProcessing");
        var refresh = Button("Actualizar detección", "RefreshProcessing");
        var export = Button("Exportar diagnóstico…", "ExportProcessing");
        apply.Click += (_, _) =>
        {
            try
            {
                var value = ReadDraft();
                foreach (var row in _rows.Values)
                    if (value.Functions.GetValueOrDefault(row.Function.ToString())?.Backend != "auto")
                        ProcessingService.Evaluate(row.Function, ProcessingService.Hardware, ProcessingService.Installed(), value);
                ProcessingService.Save(value);
                _draft = value;
                _message.Text = "Guardado. Se aplicará a las siguientes tareas; los trabajos activos conservan su configuración.";
                UpdateEffective();
            }
            catch (Exception error) { _message.Text = error.Message; }
        };
        cancel.Click += (_, _) => Close();
        reset.Click += (_, _) => { _draft = new(); _threads.Value = 0; BuildRows(); _message.Text = "Automático seleccionado. Pulsa Aplicar para guardarlo."; };
        refresh.Click += async (_, _) =>
        {
            try { _draft = ReadDraft(); refresh.IsEnabled = false; await ProcessingService.InitializeAsync(true); BuildRows(); _message.Text = "Detección actualizada sin cargar modelos de IA."; }
            catch (Exception error) { _message.Text = error.Message; }
            finally { refresh.IsEnabled = true; }
        };
        export.Click += async (_, _) =>
        {
            try
            {
                var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
                    Title = "Guardar diagnóstico de procesamiento", SuggestedFileName = "Prometheus-procesamiento-3.22.2.json",
                    FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }] });
                if (target == null) return;
                await using var output = await target.OpenWriteAsync();
                output.SetLength(0);
                await JsonSerializer.SerializeAsync(output, ProcessingService.Describe(), new JsonSerializerOptions { WriteIndented = true });
                _message.Text = "Diagnóstico guardado. No contiene cookies ni un volcado de variables de entorno.";
            }
            catch (Exception error) { _message.Text = error.Message; }
        };
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { reset, refresh, export, cancel, apply }) { button.Margin = new Thickness(0, 4, 6, 0); actions.Children.Add(button); }
        var footer = new StackPanel { Spacing = 6, Margin = new Thickness(16, 4, 16, 12) };
        footer.Children.Add(_message); footer.Children.Add(actions);
        var layout = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        layout.Children.Add(new ScrollViewer { Content = _body, Margin = new Thickness(16, 12) });
        Content = layout;
        BuildRows();
        ProcessingService.Changed += ProcessingChanged;
        Closed += (_, _) => ProcessingService.Changed -= ProcessingChanged;
    }

    private static Button Button(string text, string name) => new() { Content = text, Name = name, Padding = new Thickness(10, 6) };
    private static string Memory(ulong bytes) => bytes == 0 ? "No disponible" : $"{bytes / 1073741824d:0.0} GB";
    private static string Label(ProcessingFunction function) => function switch {
        ProcessingFunction.Graphics => "Previsualización y MGT", ProcessingFunction.H264 => "Exportación H.264",
        ProcessingFunction.Hevc => "Exportación HEVC", ProcessingFunction.SamImage => "Máscaras SAM",
        ProcessingFunction.SamTrack => "Seguimiento SAM", _ => "Transcripción" };

    private void BuildRows()
    {
        if (_threads.Parent is Panel previousParent) previousParent.Children.Remove(_threads);
        _body.Children.Clear(); _rows.Clear();
        var hardware = ProcessingService.Hardware;
        _body.Children.Add(new TextBlock { Text = "Procesamiento", FontSize = 22, FontWeight = FontWeight.Bold, Foreground = Brushes.Orange });
        _body.Children.Add(new TextBlock { Text = $"{hardware.Cpu}\nRAM instalada: {Memory(hardware.InstalledRamBytes)} · disponible al detectar: {Memory(hardware.AvailableRamBytes)}", TextWrapping = TextWrapping.Wrap });
        foreach (var adapter in hardware.Adapters.Where(a => !a.Software))
            _body.Children.Add(new TextBlock { Text = $"{adapter.Name} · {adapter.Kind} · memoria dedicada: {Memory(adapter.DedicatedBytes)}\nControlador: {adapter.Driver}" +
                (adapter.Luid == hardware.CompositionLuid ? " · Adaptador del compositor" : ""), TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        _body.Children.Add(new TextBlock { Text = "Automático elige por función. Los motores se comprueban al utilizarlos; abrir estos ajustes no carga SAM ni Whisper. La memoria compartida forma parte de la RAM.", TextWrapping = TextWrapping.Wrap, Opacity = .75 });
        foreach (var function in Enum.GetValues<ProcessingFunction>())
        {
            var preference = _draft.Functions.GetValueOrDefault(function.ToString()) ?? new();
            var options = new List<Option> { new("auto", "Automático") };
            var backends = function switch {
                ProcessingFunction.Graphics => Array.Empty<string>(),
                ProcessingFunction.H264 or ProcessingFunction.Hevc => new[] { "nvidia", "amf", "cpu" },
                ProcessingFunction.SamImage => new[] { "cuda", "directml", "webgpu", "onnx-cpu" },
                ProcessingFunction.SamTrack => new[] { "cuda", "directml", "webgpu", "torch-cpu" },
                _ => new[] { "cuda", "cpu" } };
            foreach (var backend in backends)
            {
                try
                {
                    var settings = new ProcessingSettings(); settings.Functions[function.ToString()] = new(backend);
                    var choice = ProcessingService.Evaluate(function, hardware, ProcessingService.Installed(), settings);
                    options.Add(new(backend, choice.Label));
                }
                catch (InvalidOperationException) { }
            }
            var provider = new ComboBox { ItemsSource = options, Name = "Provider" + function, HorizontalAlignment = HorizontalAlignment.Stretch,
                SelectedIndex = Math.Max(0, options.FindIndex(o => o.Id == preference.Backend)) };
            ComboBox? device = null;
            var panel = new StackPanel { Spacing = 5 };
            panel.Children.Add(new TextBlock { Text = Label(function), FontWeight = FontWeight.Bold });
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
            provider.Margin = new Thickness(0, 0, 8, 0); grid.Children.Add(provider);
            if (function is ProcessingFunction.SamImage or ProcessingFunction.SamTrack or ProcessingFunction.Whisper)
            {
                var devices = new List<Option> { new("", "GPU automática") };
                devices.AddRange(hardware.Adapters.Where(a => !a.Software && a.D3D11).Select(a => new Option(a.Key, a.Name + " · " + a.Index)));
                device = new ComboBox { ItemsSource = devices, Name = "Device" + function, HorizontalAlignment = HorizontalAlignment.Stretch,
                    SelectedIndex = Math.Max(0, devices.FindIndex(d => d.Id == preference.AdapterKey)) };
                Grid.SetColumn(device, 1); grid.Children.Add(device);
            }
            panel.Children.Add(grid);
            var actual = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .8 };
            panel.Children.Add(actual);
            if (function == ProcessingFunction.Graphics)
                panel.Children.Add(new TextBlock { Text = "Se conserva la GPU efectiva para compartir texturas con CEF. Un cambio gráfico fuera del programa requiere reiniciar.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = .7 });
            _rows.Add(function, new Row(function, provider, device, actual));
            _body.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.Parse("#373737")), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 6, 0, 12), Child = panel });
        }
        var advanced = new StackPanel { Spacing = 6 };
        advanced.Children.Add(new TextBlock { Text = "Hilos de CPU para nuevas tareas: 0 = automático", TextWrapping = TextWrapping.Wrap });
        advanced.Children.Add(_threads);
        _body.Children.Add(new Expander { Header = "Avanzado", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch });
        UpdateEffective();
    }

    private ProcessingSettings ReadDraft()
    {
        var value = new ProcessingSettings { CpuThreads = (int)(_threads.Value ?? 0) };
        foreach (var row in _rows.Values)
        {
            var backend = (row.Provider.SelectedItem as Option)?.Id ?? "auto";
            var adapter = (row.Adapter?.SelectedItem as Option)?.Id;
            value.Functions[row.Function.ToString()] = new(backend, backend is "cpu" or "onnx-cpu" or "torch-cpu" || string.IsNullOrEmpty(adapter) ? null : adapter);
        }
        return value;
    }

    private void ProcessingChanged() => Dispatcher.UIThread.Post(UpdateEffective);
    private void UpdateEffective()
    {
        foreach (var row in _rows.Values)
        {
            try
            {
                var choice = ProcessingService.Resolve(row.Function);
                var last = ProcessingService.LastExecution(row.Function);
                row.Actual.Text = $"Selección guardada: {choice.Label}" + (choice.Adapter == null ? "" : " · " + choice.Adapter.Name) + $" · {choice.Origin}\n" +
                    (last == null ? "Pendiente de comprobar al utilizar esta función." : $"Última ejecución: {last.Backend} · {last.Status} · {last.Time.ToLocalTime():HH:mm:ss}" +
                        (string.IsNullOrWhiteSpace(last.Reason) ? "" : "\n" + last.Reason));
            }
            catch (Exception error) { row.Actual.Text = error.Message; }
        }
    }
}
