using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public partial class TranscriptPanelView : UserControl
{
    private sealed record WordRow(string Id, string Label)
    {
        public override string ToString() => Label;
    }
    private ProjectController? _controller;
    private CancellationTokenSource? _transcribing;
    private string? _documentId;
    private string? _wordId;
    private bool _syncing;

    public TranscriptPanelView()
    {
        InitializeComponent();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        DocumentBox.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            _documentId = (DocumentBox.SelectedItem as ComboBoxItem)?.Tag as string;
            RefreshWords();
        };
        WordList.SelectionChanged += (_, _) => SelectWord();
        SearchBox.TextChanged += (_, _) => RefreshWords();
        ModeBox.SelectionChanged += (_, _) => SetMode();
        TranscribeButton.Click += async (_, _) => await TranscribeAsync();
        ImportButton.Click += async (_, _) => await ImportAsync();
        FromMediaButton.Click += async (_, _) => await FromMediaAsync();
        CancelButton.Click += (_, _) => _transcribing?.Cancel();
        SaveWordButton.Click += (_, _) => SaveWord();
        ApplyButton.Click += (_, _) => Apply();
        ExportButton.Click += async (_, _) => await ExportAsync();
    }

    private void Attach()
    {
        var controller = ProjectController.Instance;
        if (controller == null || ReferenceEquals(controller, _controller)) return;
        Detach();
        _controller = controller;
        controller.StateChanged += Refresh;
        controller.ClipSelectionChanged += Refresh;
        Refresh();
    }

    private void Detach()
    {
        if (_controller != null)
        {
            _controller.StateChanged -= Refresh;
            _controller.ClipSelectionChanged -= Refresh;
        }
        _controller = null;
    }

    private TranscriptDocument? Current => _controller?.CurrentProject.ActiveSequence?.Transcriptions
        .FirstOrDefault(item => item.Id == _documentId);

    private void Refresh()
    {
        var sequence = _controller?.CurrentProject.ActiveSequence;
        _syncing = true;
        try
        {
            DocumentBox.Items.Clear();
            if (sequence == null) return;
            var selected = sequence.Clips.FirstOrDefault(item => item.Id == _controller?.SelectedClipId);
            if (selected?.TranscriptId is { } fromCue) _documentId = fromCue;
            else if (sequence.Transcriptions.All(item => item.Id != _documentId) && selected != null &&
                sequence.Transcriptions.FirstOrDefault(item => TranscriptOperations.IsSource(item, selected)) is { } fromSource)
                _documentId = fromSource.Id;
            if (sequence.Transcriptions.All(item => item.Id != _documentId))
                _documentId = sequence.Transcriptions.LastOrDefault()?.Id;
            foreach (var item in sequence.Transcriptions)
                DocumentBox.Items.Add(new ComboBoxItem { Content = item.Name, Tag = item.Id });
            DocumentBox.SelectedItem = DocumentBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag as string == _documentId);
        }
        finally { _syncing = false; }
        RefreshWords();
    }

    private void RefreshWords()
    {
        var document = Current;
        _syncing = true;
        try
        {
            WordList.ItemsSource = null;
            WordTextBox.Text = WordStartBox.Text = WordEndBox.Text = "";
            if (document == null)
            {
                InfoText.Text = "Selecciona un clip con audio para transcribir o importa Wordline, SRT o VTT.";
                return;
            }
            ModeBox.SelectedIndex = document.CaptionMode == "word" ? 1 : 0;
            var cueOnly = document.TimingPrecision == "cue";
            InfoText.Text = $"{document.Words.Count} {(cueOnly ? "frases" : "palabras")} · " +
                (document.Language ?? "Idioma sin especificar") +
                (cueOnly ? " · Este archivo no tiene tiempos por palabra" : " · Texto editable antes de actualizar subtítulos");
            var search = SearchBox.Text?.Trim();
            var rows = document.Words.Where(word => string.IsNullOrEmpty(search) ||
                word.Text.Contains(search, StringComparison.OrdinalIgnoreCase))
                .Select(word => new WordRow(word.Id,
                    $"{word.Start:0.000}–{word.End:0.000}  {word.Text}")).ToArray();
            WordList.ItemsSource = rows;
            WordList.SelectedItem = rows.FirstOrDefault(item => item.Id == _wordId);
        }
        finally { _syncing = false; }
        FillWordDetails();
    }

    private void SelectWord()
    {
        if (_syncing) return;
        _wordId = (WordList.SelectedItem as WordRow)?.Id;
        FillWordDetails();
        var word = Current?.Words.FirstOrDefault(item => item.Id == _wordId);
        if (word != null && Current is { } document && _controller != null)
        {
            var sequence = _controller.CurrentProject.ActiveSequence;
            if (sequence != null && TranscriptOperations.TimelinePositionForWord(sequence, document, word) is { } at)
                _controller.Transport.Seek(at);
        }
    }

    private void FillWordDetails()
    {
        var word = Current?.Words.FirstOrDefault(item => item.Id == _wordId);
        WordTextBox.Text = word?.Text ?? "";
        WordStartBox.Text = word?.Start.ToString("0.000", CultureInfo.InvariantCulture) ?? "";
        WordEndBox.Text = word?.End.ToString("0.000", CultureInfo.InvariantCulture) ?? "";
    }

    private void SaveWord()
    {
        if (_controller == null || _documentId == null || _wordId == null) return;
        var value = WordTextBox.Text?.Trim() ?? "";
        if (!double.TryParse(WordStartBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var start) ||
            !double.TryParse(WordEndBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var end) ||
            string.IsNullOrWhiteSpace(value))
        { _controller.ReportStatus("Revisa el texto y los tiempos en segundos."); return; }
        try
        {
            var docId = _documentId; var wordId = _wordId;
            _controller.UndoRedo.Commit(project =>
            {
                var document = project.ActiveSequence!.Transcriptions.Single(item => item.Id == docId);
                var word = document.Words.Single(item => item.Id == wordId);
                var old = (word.Text, word.Start, word.End);
                word.Text = value; word.Start = start; word.End = end;
                try { document.Validate(); }
                catch { (word.Text, word.Start, word.End) = old; throw; }
            }, "Editar transcripción");
            _controller.ReportStatus("Transcripción guardada · pulsa Actualizar subtítulos para reflejarla en pantalla");
        }
        catch (Exception ex) { _controller.ReportStatus(ex.Message); }
    }

    private void SetMode()
    {
        if (_syncing || _controller == null || _documentId == null) return;
        var document = Current;
        if (document == null) return;
        var mode = ModeBox.SelectedIndex == 1 ? "word" : "phrase";
        if (mode == "word" && document.TimingPrecision != "word")
        { _controller.ReportStatus("Esta transcripción no tiene tiempos por palabra."); RefreshWords(); return; }
        if (document.CaptionMode == mode) return;
        var id = _documentId;
        _controller.UndoRedo.Commit(project => project.ActiveSequence!.Transcriptions
            .Single(item => item.Id == id).CaptionMode = mode, "Elegir vista de subtítulos");
        _controller.ReportStatus("Pulsa Actualizar subtítulos para aplicar el modo elegido");
    }

    private void Apply()
    {
        if (_controller == null || _documentId == null) return;
        try
        {
            var id = _documentId;
            _controller.UndoRedo.Commit(project => TranscriptOperations.Apply(project, id),
                "Actualizar subtítulos desde transcripción");
            _controller.ReportStatus("Subtítulos actualizados desde la transcripción");
        }
        catch (Exception ex) { _controller.ReportStatus("Subtítulos: " + ex.Message); }
    }

    private async System.Threading.Tasks.Task ImportAsync()
    {
        if (_controller == null) return;
        var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importar transcripción", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Wordline, SRT o VTT")
                { Patterns = ["*.txt", "*.srt", "*.vtt", "*.wav", "*.mp4", "*.m4a", "*.mov", "*.mka"] }]
        });
        var file = files.FirstOrDefault();
        if (file == null) return;
        try
        {
            var content = await ReadTranscriptTextAsync(file.Path.LocalPath);
            var doc = TranscriptOperations.Parse(content, file.Name,
                _controller.Transport.PositionSeconds);
            var projectId = _controller.CurrentProject.Id;
            _controller.UndoRedo.Commit(project =>
            {
                if (project.Id != projectId) return;
                project.ActiveSequence!.Transcriptions.Add(doc);
            }, "Importar transcripción");
            _documentId = doc.Id;
            Refresh();
            Apply();
        }
        catch (Exception ex) { _controller.ReportStatus("Importar transcripción: " + ex.Message); }
    }

    private async System.Threading.Tasks.Task FromMediaAsync()
    {
        if (_controller == null) return;
        var selection = SelectedAudio();
        if (selection == null)
        { _controller.ReportStatus("Selecciona un clip con audio en la línea de tiempo."); return; }
        try
        {
            var (clip, asset, path) = selection.Value;
            var text = await ReadTranscriptTextAsync(path);
            var source = TranscriptOperations.Parse(text, "Wordline · " + asset.Name, 0);
            var document = TranscriptOperations.AlignToClip(source, clip);
            var sequenceId = _controller.CurrentProject.ActiveSequenceId;
            _controller.UndoRedo.Commit(project =>
            {
                if (project.ActiveSequenceId != sequenceId ||
                    project.ActiveSequence!.Clips.All(item => item.Id != clip.Id)) return;
                project.ActiveSequence.Transcriptions.Add(document);
            }, "Cargar transcripción del medio");
            _documentId = document.Id;
            Refresh();
            Apply();
        }
        catch (Exception ex) { _controller.ReportStatus("Transcripción del medio: " + ex.Message); }
    }

    private static async System.Threading.Tasks.Task<string> ReadTranscriptTextAsync(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var content = extension is ".txt" or ".srt" or ".vtt"
            ? await File.ReadAllTextAsync(path)
            : await WordlineMediaReader.ReadAsync(path)
                ?? throw new InvalidOperationException("Este medio no contiene metadatos Wordline compatibles.");
        if (content.StartsWith("WORDLINE_TIMED_TEXT_VERSION=1\n", StringComparison.Ordinal))
            content = content["WORDLINE_TIMED_TEXT_VERSION=1\n".Length..];
        else if (content.StartsWith("WORDLINE_SUBTITLES_FORMAT=", StringComparison.Ordinal))
        {
            var newline = content.IndexOf('\n');
            if (newline < 0) throw new FormatException("Metadatos Wordline incompletos.");
            content = content[(newline + 1)..];
        }
        return content;
    }

    private (Clip Clip, Asset Asset, string Path)? SelectedAudio()
    {
        var project = _controller?.CurrentProject;
        var sequence = project?.ActiveSequence;
        var selected = sequence?.Clips.FirstOrDefault(item => item.Id == _controller?.SelectedClipId);
        if (project == null || sequence == null || selected == null) return null;
        var audio = sequence.Tracks.FirstOrDefault(item => item.Id == selected.TrackId)?.Kind == "audio"
            ? selected : sequence.Clips.FirstOrDefault(item => item.AssetId == selected.AssetId &&
                sequence.Tracks.FirstOrDefault(track => track.Id == item.TrackId)?.Kind == "audio" &&
                Math.Abs(item.TimelineStart.Seconds - selected.TimelineStart.Seconds) < 0.02);
        var asset = project.Assets.FirstOrDefault(item => item.Id == audio?.AssetId);
        var path = asset?.EffectiveLocator?.Path;
        return audio == null || asset == null || string.IsNullOrWhiteSpace(path)
            ? null : (audio, asset, path);
    }

    private async System.Threading.Tasks.Task TranscribeAsync()
    {
        if (_controller == null || _transcribing != null) return;
        var controller = _controller;
        var project = controller.CurrentProject;
        var sequence = project.ActiveSequence;
        var selection = SelectedAudio();
        if (selection == null)
        { controller.ReportStatus("Selecciona un clip con audio en la línea de tiempo."); return; }
        var (audio, asset, path) = selection.Value;
        var token = new CancellationTokenSource();
        _transcribing = token;
        TranscribeButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        InfoText.Text = "Preparando la transcripción local…";
        try
        {
            var stream = LocalMediaService.AudioOrdinal(asset, audio, path);
            var progress = new Progress<double>(value =>
            {
                if (ReferenceEquals(_transcribing, token) && !token.IsCancellationRequested)
                    InfoText.Text = $"Whisper · {value:P0}";
            });
            var result = await WhisperTranscriptionService.TranscribeAsync(path, audio,
                "Whisper · " + asset.Name, token.Token, progress, stream);
            if (controller.CurrentProject.Id != project.Id ||
                controller.CurrentProject.ActiveSequenceId != sequence!.Id || token.IsCancellationRequested)
                return;
            var currentAudio = controller.CurrentProject.ActiveSequence!.Clips.FirstOrDefault(clip => clip.Id == audio.Id);
            if (currentAudio == null || currentAudio.AssetId != audio.AssetId || currentAudio.StreamId != audio.StreamId || currentAudio.SourceIn != audio.SourceIn ||
                currentAudio.Duration != audio.Duration || currentAudio.Speed != audio.Speed)
            {
                controller.ReportStatus("El audio cambió durante la transcripción. Selecciona el clip y vuelve a transcribir.");
                return;
            }
            result.Document.TimelineStart = currentAudio.TimelineStart.Seconds;
            controller.UndoRedo.Commit(current =>
            {
                current.ActiveSequence!.Transcriptions.Add(result.Document);
            }, "Transcribir clip con Whisper");
            _documentId = result.Document.Id;
            Refresh();
            Apply();
            controller.ReportStatus($"Whisper: {result.Document.Words.Count} palabras · {result.Device.ToUpperInvariant()}");
        }
        catch (OperationCanceledException)
        {
            InfoText.Text = "Transcripción cancelada";
            controller.ReportStatus(InfoText.Text);
        }
        catch (Exception ex) { controller.ReportStatus("Whisper: " + ex.Message); InfoText.Text = ex.Message; }
        finally
        {
            _transcribing = null;
            token.Dispose();
            TranscribeButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
        }
    }

    private async System.Threading.Tasks.Task ExportAsync()
    {
        var doc = Current;
        if (doc == null || _controller == null) return;
        try
        {
            var content = TranscriptOperations.ToWordline(doc);
            var file = await TopLevel.GetTopLevel(this)!.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Guardar transcripción Wordline", SuggestedFileName = doc.Name + ".txt",
                DefaultExtension = "txt", FileTypeChoices = [new FilePickerFileType("Wordline TXT")
                    { Patterns = ["*.txt"] }]
            });
            if (file == null) return;
            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek) stream.SetLength(0);
            var bytes = new UTF8Encoding(false).GetBytes(content);
            await stream.WriteAsync(bytes);
            _controller.ReportStatus("Wordline guardado: " + file.Name);
        }
        catch (Exception ex) { _controller.ReportStatus("Wordline: " + ex.Message); }
    }
}
