using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Prometheus.Client.Services;
using Prometheus.Core.Model;

namespace Prometheus.Client.Views;

internal sealed record ExportSetupResult(string Path, GpuExportOptions Options);

internal sealed class ExportSetupDialog : Window
{
    private static readonly (ExportFormat Format, string Label)[] Formats =
    [
        (ExportFormat.Mp4, "MP4 · H.264 + AAC"),
        (ExportFormat.Hevc, "MP4 · HEVC/H.265 + AAC"),
        (ExportFormat.ProRes422, "MOV · ProRes 422 + PCM"),
        (ExportFormat.ProRes4444, "MOV · ProRes 4444 con alfa + PCM"),
        (ExportFormat.Wav, "WAV · solo audio")
    ];

    private ExportFormat SelectedFormat => Formats[Math.Clamp(_format.SelectedIndex, 0, Formats.Length - 1)].Format;
    private readonly Project _project;
    private readonly Sequence _sequence;
    private readonly string? _projectPath;
    private readonly ComboBox _format;
    private readonly ComboBox _encoder;
    private readonly ComboBox _resolution;
    private readonly TextBlock _resolutionLabel;
    private readonly TextBlock _encoderLabel;
    private readonly TextBlock _qualityLabel;
    private readonly TextBlock _extension;
    private readonly CheckBox _offline;
    private readonly CheckBox _recoverable;
    private RecoverableExport.Checkpoint? _checkpoint;
    private RecoverableExport.Space? _space;
    private readonly TextBox _folder;
    private readonly TextBox _name;
    private readonly ComboBox _range;
    private readonly ComboBox _quality;
    private readonly NumericUpDown _videoRate;
    private readonly ComboBox _audioRate;
    private readonly TextBlock _summary;
    private readonly TextBlock _readiness;
    private readonly Button _start;
    private bool _advanced;
    private bool _overwriteArmed;
    private ExportSetupResult? _validatedRequest;
    private CancellationTokenSource? _validationCancellation;
    private int _validationVersion;
    private bool _closed;
    private sealed record Validation(ExportSetupResult? Request, string Issue, ExportInspection? Inspection,
        RecoverableExport.Checkpoint? Checkpoint = null, RecoverableExport.Space? Space = null);
    private readonly StackPanel _advancedPanel;
    private readonly Button _advancedButton;

    public ExportSetupDialog(Project project, string? projectPath, string? outputPath = null, ExportFormat? suggestedFormat = null)
    {
        _project = Prometheus.Core.Storage.ProjectStore.Clone(project);
        _sequence = _project.ActiveSequence ?? throw new InvalidOperationException("No hay montaje activo.");
        _projectPath = projectPath;
        Title = "Exportar montaje";
        Width = 560;
        Height = 680;
        MinWidth = 500;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#121212"));

        var root = new StackPanel { Spacing = 10, Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = "Exportar montaje", FontSize = 21, FontWeight = FontWeight.Bold });
        root.Children.Add(new TextBlock { Text = $"{_sequence.Name}  ·  {_sequence.Width} × {_sequence.Height}  ·  {_sequence.Fps.Seconds:0.##} FPS",
            Foreground = new SolidColorBrush(Color.Parse("#AAAAAA")) });
        root.Children.Add(Caption("Formato"));
        _format = new ComboBox { ItemsSource = Formats.Select(item => item.Label).ToArray(),
            SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        root.Children.Add(_format);
        _resolutionLabel = Caption("Resolución de salida · conserva la proporción");
        root.Children.Add(_resolutionLabel);
        _resolution = new ComboBox { Name = "ExportResolution", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { $"Del montaje · {_sequence.Width} × {_sequence.Height}" }.Concat(CanvasResolutions.Levels.Select(level =>
            {
                try { var size = CanvasResolutions.AtShortEdge(_sequence.Width, _sequence.Height, level);
                    return $"{level} · {size.Width} × {size.Height}"; }
                catch (ArgumentException) { return $"{level} · supera el límite de dimensiones"; }
            })).ToArray(), SelectedIndex = 0 };
        _resolution.SelectionChanged += (_, _) =>
        {
            if (_videoRate != null && _quality is { SelectedIndex: >= 0 and < 3 })
                _videoRate.Value = PresetRate(_quality.SelectedIndex);
            Refresh();
        };
        ToolTip.SetTip(_resolution, "Compone directamente a esta resolución desde los originales. No cambia el lienzo del proyecto.");
        root.Children.Add(_resolution);
        _encoderLabel = Caption("Codificador de vídeo");
        root.Children.Add(_encoderLabel);
        _encoder = new ComboBox { ItemsSource = new[] { "Automático · según Procesamiento", "GPU del equipo", "CPU" },
            SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        root.Children.Add(_encoder);
        _format.SelectionChanged += (_, _) =>
        {
            if (_videoRate != null && _quality is { SelectedIndex: >= 0 and < 3 })
                _videoRate.Value = PresetRate(_quality.SelectedIndex);
            Refresh();
        };
        _encoder.SelectionChanged += (_, _) => Refresh();

        root.Children.Add(Caption("Carpeta de salida"));
        var folderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _folder = new TextBox { Text = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) };
        if (string.IsNullOrWhiteSpace(_folder.Text))
            _folder.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _folder.TextChanged += (_, _) => Refresh();
        Grid.SetColumn(_folder, 0);
        folderRow.Children.Add(_folder);
        var browse = new Button { Content = "Examinar…", Classes = { "nle-btn" } };
        browse.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                { Title = "Elegir carpeta de exportación", AllowMultiple = false });
            if (folders.Count > 0) _folder.Text = folders[0].Path.LocalPath;
        };
        Grid.SetColumn(browse, 1);
        folderRow.Children.Add(browse);
        root.Children.Add(folderRow);

        root.Children.Add(Caption("Nombre del archivo"));
        var nameRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _name = new TextBox { Name = "ExportName", Text = ExportPresentation.SuggestedName(project, _sequence) };
        _name.TextChanged += (_, _) => Refresh();
        nameRow.Children.Add(_name);
        _extension = new TextBlock { Text = ".mp4", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_extension, 1);
        nameRow.Children.Add(_extension);
        root.Children.Add(nameRow);

        root.Children.Add(Caption("Rango"));
        _range = new ComboBox { ItemsSource = new[] { "Montaje completo", "Entre entrada y salida" }, SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        _range.SelectionChanged += (_, _) => Refresh();
        root.Children.Add(_range);

        _offline = new CheckBox { Content = "Permitir medios sin conexión" };
        ToolTip.SetTip(_offline, "Usa vistas ligeras locales y conserva el tiempo cuando faltan medios.");
        _offline.IsCheckedChanged += (_, _) => Refresh();
        root.Children.Add(_offline);
        _recoverable = new CheckBox { Content = "Guardar tramos para reanudar" };
        ToolTip.SetTip(_recoverable, "Conserva los tramos terminados al pausar. Para reanudar, elige la misma carpeta y nombre.");
        _recoverable.IsCheckedChanged += (_, _) => Refresh();
        root.Children.Add(_recoverable);

        _qualityLabel = Caption("Calidad de vídeo");
        root.Children.Add(_qualityLabel);
        _quality = new ComboBox { ItemsSource = new[] { "Archivo ligero", "Equilibrada", "Alta", "Personalizada" },
            SelectedIndex = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        _quality.SelectionChanged += (_, _) =>
        {
            if (_videoRate != null && _quality.SelectedIndex is >= 0 and < 3)
                _videoRate.Value = PresetRate(_quality.SelectedIndex);
            Refresh();
        };
        root.Children.Add(_quality);

        _advancedButton = new Button { Content = "▸ Avanzado", Classes = { "nle-btn" }, HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(_advancedButton);
        _advancedPanel = new StackPanel { Spacing = 7, IsVisible = false };
        _advancedPanel.Children.Add(Caption("Vídeo · bitrate objetivo (Mbps)"));
        _videoRate = new NumericUpDown { Minimum = 1, Maximum = 100, Increment = 1,
            Value = PresetRate(1), Width = 130, HorizontalAlignment = HorizontalAlignment.Left };
        _videoRate.ValueChanged += (_, _) =>
        {
            if (_quality != null && _quality.SelectedIndex is >= 0 and < 3 &&
                _videoRate.Value != PresetRate(_quality.SelectedIndex)) _quality.SelectedIndex = 3;
            Refresh();
        };
        _advancedPanel.Children.Add(_videoRate);
        _advancedPanel.Children.Add(Caption("Audio AAC"));
        _audioRate = new ComboBox { ItemsSource = new[] { "128 kbps", "192 kbps", "256 kbps", "320 kbps" }, SelectedIndex = 1,
            Width = 150, HorizontalAlignment = HorizontalAlignment.Left };
        _audioRate.SelectionChanged += (_, _) => Refresh();
        _advancedPanel.Children.Add(_audioRate);
        root.Children.Add(_advancedPanel);
        _advancedButton.Click += (_, _) =>
        {
            _advanced = !_advanced;
            _advancedButton.Content = _advanced ? "▾ Avanzado" : "▸ Avanzado";
            Refresh();
        };

        _summary = new TextBlock { TextWrapping = TextWrapping.Wrap };
        root.Children.Add(_summary);
        _readiness = new TextBlock { TextWrapping = TextWrapping.Wrap, MinHeight = 38 };
        root.Children.Add(_readiness);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancelar", Classes = { "nle-btn" } };
        cancel.Click += (_, _) => Close(null);
        _start = new Button { Content = "Exportar", Classes = { "nle-accent" }, MinWidth = 115 };
        _start.Click += (_, _) =>
        {
            if (_validatedRequest is not { } result || !_start.IsEnabled) return;
            if (File.Exists(result.Path) && !_overwriteArmed)
            {
                _overwriteArmed = true;
                _start.Content = "Confirmar reemplazo";
                _readiness.Text = "El archivo ya existe. Confirma para reemplazarlo.";
                return;
            }
            Close(result! with { Options = result.Options with { Overwrite = _overwriteArmed } });
        };
        actions.Children.Add(cancel);
        actions.Children.Add(_start);
        root.Children.Add(actions);
        Content = new ScrollViewer { Content = root };
        Closed += (_, _) => { _closed = true; _validationVersion++; _validationCancellation?.Cancel(); };
        if (outputPath != null)
        {
            _folder.Text = Path.GetDirectoryName(outputPath);
            _name.Text = Path.GetFileNameWithoutExtension(outputPath);
            var preferred = suggestedFormat ?? (Path.GetExtension(outputPath).ToLowerInvariant() switch
            {
                ".wav" => ExportFormat.Wav,
                ".mov" => ExportFormat.ProRes422,
                _ => ExportFormat.Mp4
            });
            _format.SelectedIndex = Math.Max(0, Array.FindIndex(Formats, item => item.Format == preferred));
        }
        Refresh();
    }

    private static TextBlock Caption(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold };

    private int PresetRate(int index)
    {
        var size = OutputSize();
        var scale = SelectedFormat == ExportFormat.Hevc ? 0.65 : 1;
        // 12 Mbps at 1080p30; scale with the actual output, capped at 60 for Equilibrada.
        var baseline = Math.Clamp((int)((long)size.Width * size.Height * _sequence.Fps.Seconds * scale / 5_184_000),
            SelectedFormat == ExportFormat.Hevc ? 8 : 12, 60);
        return index switch { 0 => Math.Max(4, baseline / 2), 2 => Math.Min(100, baseline * 2), _ => baseline };
    }

    private (int Width, int Height) OutputSize()
    {
        var index = _resolution?.SelectedIndex ?? 0;
        if (index <= 0) return (_sequence.Width, _sequence.Height);
        try { return CanvasResolutions.AtShortEdge(_sequence.Width, _sequence.Height, CanvasResolutions.Levels[index - 1]); }
        catch (ArgumentException) { return (0, 0); } // TryBuild reports the unsupported dimensions.
    }


    private bool TryReadRequest(out ExportSetupResult? result, out string issue)
    {
        result = null;
        issue = "";
        var folder = (_folder.Text ?? "").Trim();
        var name = (_name.Text ?? "").Trim();
        var format = SelectedFormat;
        var extension = SequenceExportService.Extension(format);
        if (!Directory.Exists(folder)) issue = "Selecciona una carpeta existente.";
        else if (name.Length == 0 || name is "." or ".." || name != Path.GetFileName(name) ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".mov", StringComparison.OrdinalIgnoreCase))
            issue = $"Escribe un nombre válido, sin la extensión {extension}.";
        if (issue.Length > 0) return false;
        var path = Path.GetFullPath(Path.Combine(folder, name + extension));
        var size = OutputSize();
        var options = new GpuExportOptions(_range.SelectedIndex == 1, (int)(_videoRate.Value ?? 0),
            new[] { 128, 192, 256, 320 }[Math.Clamp(_audioRate.SelectedIndex, 0, 3)], format,
            SequenceExportService.IsProRes(format) ? ExportEncoder.Cpu : (ExportEncoder)Math.Clamp(_encoder.SelectedIndex, 0, 2),
            _offline.IsChecked == true, Recoverable: SequenceExportService.SupportsRecovery(format) && _recoverable.IsChecked == true,
            Width: format == ExportFormat.Wav ? null : size.Width, Height: format == ExportFormat.Wav ? null : size.Height);
        result = new ExportSetupResult(path, options);
        return true;
    }

    private Task<Validation> ValidateRequestAsync(ExportSetupResult request, CancellationToken cancellation) => Task.Run(() =>
    {
        cancellation.ThrowIfCancellationRequested();
        var checkpoint = SequenceExportService.SupportsRecovery(request.Options.Format) ? RecoverableExport.Find(request.Path) : null;
        if (checkpoint != null)
            return new Validation(request with { Options = checkpoint.Options with { Recoverable = true, Resume = true } }, "", null, checkpoint, checkpoint.Space);
        var inspection = SequenceExportService.Prepare(_project, _projectPath, request.Path, request.Options, cancellation).Inspection;
        RecoverableExport.Space? space = null;
        var issue = string.Join(Environment.NewLine, inspection.Issues);
        if (request.Options.Recoverable && inspection.Ready)
        {
            space = RecoverableExport.Estimate(request.Path, inspection.EndSeconds - inspection.StartSeconds,
                request.Options.VideoBitrateMbps, request.Options.AudioBitrateKbps);
            if (!space.Fits) issue = "Espacio insuficiente para el MP4 y sus tramos.";
        }
        cancellation.ThrowIfCancellationRequested();
        return new Validation(issue.Length == 0 ? request : null, issue, inspection, Space: space);
    }, cancellation);

    private async void Refresh()
    {
        if (_closed || _start == null || _summary == null || _readiness == null || _videoRate == null || _audioRate == null) return;
        var version = ++_validationVersion;
        _validationCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _validationCancellation = cancellation;
        _overwriteArmed = false;
        _validatedRequest = null; _checkpoint = null; _space = null;
        _start.IsEnabled = false;
        _readiness.Text = "Comprobando medios…";
        _summary.Text = "Revisando los ajustes de salida…";
        var format = SelectedFormat;
        var wave = format == ExportFormat.Wav;
        var proRes = SequenceExportService.IsProRes(format);
        var size = OutputSize();
        _resolution.IsVisible = _resolutionLabel.IsVisible = !wave;
        _extension.Text = SequenceExportService.Extension(format);
        _recoverable.IsVisible = SequenceExportService.SupportsRecovery(format);
        _encoderLabel.IsVisible = _encoder.IsVisible = _qualityLabel.IsVisible = _quality.IsVisible =
            _advancedButton.IsVisible = !wave && !proRes;
        _advancedPanel.IsVisible = !wave && !proRes && _advanced;
        Validation validation;
        try
        {
            if (!TryReadRequest(out var input, out var problem)) validation = new(null, problem, null);
            else
            {
                await Task.Delay(100, cancellation.Token);
                validation = await ValidateRequestAsync(input!, cancellation.Token);
            }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception error) { validation = new(null, error.Message, null); }
        finally
        {
            if (ReferenceEquals(_validationCancellation, cancellation)) _validationCancellation = null;
            cancellation.Dispose();
        }
        if (_closed || version != _validationVersion) return;
        _checkpoint = validation.Checkpoint; _space = validation.Space;
        var request = _validatedRequest = validation.Request;
        var issue = validation.Issue; var inspection = validation.Inspection;
        var ready = request != null;
        _start.IsEnabled = ready;
        _start.Content = _checkpoint != null ? "Reanudar" :
            ready && File.Exists(request!.Path) ? "Reemplazar y exportar" : "Exportar";
        var warnings = inspection?.Warnings;
        var notes = warnings == null || warnings.Count == 0 ? "" :
            Environment.NewLine + string.Join(Environment.NewLine, warnings.Take(4).Select(warning =>
                $"{ExportMediaPlan.Timecode(warning.StartSeconds)}–{ExportMediaPlan.Timecode(warning.EndSeconds)} · " +
                $"{warning.ClipName} · {warning.Substitution switch { "proxy" or "web_preview" => "vista ligera", "silence" => "silencio", _ => "imagen omitida" }}")) +
            (warnings.Count > 4 ? $"{Environment.NewLine}y {warnings.Count - 4} tramo(s) más." : "");
        _readiness.Text = _checkpoint is { } saved
            ? $"Montaje guardado: {saved.ProjectName} · revisión {saved.Revision} · " +
                $"{saved.CompletedFrames} de {saved.TotalFrames} fotogramas en tramos terminados."
            : (ready ? "Listo para exportar" : issue) + notes;
        _readiness.Foreground = new SolidColorBrush(Color.Parse(ready && notes.Length == 0 ? "#A8D8B0" : "#FFAE91"));
        var duration = _range.SelectedIndex == 1 && _sequence.MarkInSeconds != null && _sequence.MarkOutSeconds != null
            ? _sequence.MarkOutSeconds.Value - _sequence.MarkInSeconds.Value : _sequence.DurationSeconds;
        var sizeMb = duration * ((double)(_videoRate.Value ?? 0) + new[] { 0.128, 0.192, 0.256, 0.320 }[Math.Clamp(_audioRate.SelectedIndex, 0, 3)]) / 8;
        _summary.Text = wave
            ? $"WAV PCM · 48 kHz · estéreo · duración {Math.Max(0, duration):0.##} s"
            : proRes
                ? $"MOV · {size.Width} × {size.Height} · {_sequence.Fps.Seconds:0.##} FPS · PCM estéreo · codificación CPU"
                : $"Salida: {size.Width} × {size.Height} · {_sequence.Fps.Seconds:0.##} FPS · " +
            $"{_videoRate.Value:0} Mbps · duración {Math.Max(0, duration):0.##} s · tamaño aproximado {Math.Max(0, sizeMb):0} MB";
        if (format == ExportFormat.ProRes4444)
            _summary.Text += Environment.NewLine + "Conserva la transparencia de las zonas sin fondo opaco.";
        if (_checkpoint is { } savedRange)
            _summary.Text = $"Se reanudará la exportación con su resolución y ajustes guardados" +
                (savedRange.Options.Width is { } sw ? $" · {sw} × {savedRange.Options.Height}" : "") +
                $". Rango: {ExportMediaPlan.Timecode(savedRange.StartSeconds)}–{ExportMediaPlan.Timecode(savedRange.EndSeconds)}.";
        if (_space != null) _summary.Text += Environment.NewLine + _space.Description;
    }

}
