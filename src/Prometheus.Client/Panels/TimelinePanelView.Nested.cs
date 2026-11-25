using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Prometheus.Client.Timeline;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public partial class TimelinePanelView
{
    private NestedInlineEditorControl? _nestedEditor;
    private NestedInlineEditorControl? _audioNestedEditor;
    private string? _editingNestedId;
    private string? _editingAudioNestedId;
    private string? _lastNestedAutoSwitchId;
    private DateTime _lastNestedAutoSwitchAt;

    private void SetupNestedInline()
    {
        _nestedEditor = new NestedInlineEditorControl(TimelineCanvas);
        _audioNestedEditor = new NestedInlineEditorControl(TimelineCanvas);
        ConfigureNestedEditor(_nestedEditor, "video");
        ConfigureNestedEditor(_audioNestedEditor, "audio");
        TimelineCanvas.NestedClipDoubleClicked += ToggleNestedInline;
        TimelineCanvas.ClipSelectionCompleted += SwitchNestedOnSelection;
        TimelineCanvas.SizeChanged += (_, _) => PositionNestedInline();
        TimelineCanvas.VisibleRangeChanged += (_, _) => PositionNestedInline();
        TimelineCanvas.TrackLayoutChanged += PositionNestedInline;
    }

    private void ConfigureNestedEditor(NestedInlineEditorControl editor, string kind)
    {
        editor.CloseRequested += () => CloseNestedInline(kind);
        editor.LayoutChanged += () =>
        {
            var id = EditingId(kind);
            if (id != null)
            {
                TimelineCanvas.SetInlineNested(kind, id, editor.DesiredHeight);
                PositionNestedInline();
            }
        };
        editor.ChildChanged += (outerId, childId, start, duration, trackId) =>
        {
            if (_controller == null) return;
            try
            {
                _controller.UndoRedo.Commit(p => NestedMediaOperations.UpdateChild(p, outerId, childId, start, duration, trackId), "Editar componente anidado");
                _controller.ReportStatus("Componente actualizado · Ctrl+Z para deshacer");
            }
            catch (Exception ex) { _controller.ReportStatus("No se pudo editar el componente: " + ex.Message); RefreshNestedInline(); }
        };
        editor.ChildRemoveRequested += childId =>
        {
            if (_controller == null || editor.OuterId == null) return;
            var outerId = editor.OuterId;
            try { _controller.UndoRedo.Commit(p => NestedMediaOperations.RemoveChild(p, outerId, childId), "Quitar componente anidado"); }
            catch (Exception ex) { _controller.ReportStatus("No se pudo quitar el componente: " + ex.Message); }
        };
        editor.ChildPropertiesRequested += childId => _ = EditNestedChildPropertiesAsync(editor.OuterId, childId);
        editor.ChildInspectorRequested += childId => _controller?.SetSelectedClip(childId, editor.OuterId);
        editor.NestedPeerSelected += childId => editor.ExpandChild(childId);
        NestedInlineOverlay.Children.Add(editor);
        editor.IsVisible = false;
    }

    private void NestSelection()
    {
        if (_controller == null || !CanEditSelectedClips()) return;
        var selected = SelectedIds();
        if (selected.Length == 0) return;
        try
        {
            string? created = null;
            _controller.UndoRedo.Commit(p => created = NestedMediaOperations.Create(p, selected), "Anidar medios");
            if (created != null)
            {
                TimelineCanvas.Selection.SelectClip(created);
                _controller.ReportStatus("Medio anidado creado · abre sus minipistas con doble clic");
            }
        }
        catch (Exception ex) { _controller.ReportStatus("No se pudo anidar: " + ex.Message); }
    }

    private void UnnestSelected(string clipId)
    {
        if (_controller == null) return;
        try
        {
            var sequence = _controller.CurrentProject.ActiveSequence;
            var clicked = sequence?.Clips.FirstOrDefault(c => c.Id == clipId);
            var linked = clicked != null && sequence != null && NestedMediaOperations.Companion(sequence, clicked) != null;
            string[] revealed = Array.Empty<string>();
            _controller.UndoRedo.Commit(p => revealed = NestedMediaOperations.Unnest(p, clipId).ToArray(), "Desanidar medio");
            if (linked) CloseNestedInline();
            else CloseNestedInline(clicked?.NestedRole == "audio" ? "audio" : "video");
            TimelineCanvas.Selection.SelectClips(revealed);
            _controller.ReportStatus("Medio desanidado · los efectos del contenedor se descartaron");
        }
        catch (Exception ex) { _controller.ReportStatus("No se pudo desanidar: " + ex.Message); }
    }

    private void ToggleNestedInline(string clipId)
    {
        var clip = _controller?.CurrentProject.ActiveSequence?.Clips.FirstOrDefault(c => c.Id == clipId);
        if (!NestedMediaOperations.IsNested(clip)) return;
        var kind = clip!.NestedRole == "audio" ? "audio" : "video";
        if (EditingId(kind) == clipId)
        {
            if (_lastNestedAutoSwitchId == clipId && DateTime.UtcNow - _lastNestedAutoSwitchAt < TimeSpan.FromMilliseconds(600)) return;
            CloseNestedInline(kind);
            return;
        }
        SetEditingId(kind, clipId);
        RefreshNestedInline();
    }

    private string? EditingId(string kind) => kind == "audio" ? _editingAudioNestedId : _editingNestedId;
    private void SetEditingId(string kind, string? id)
    {
        if (kind == "audio") _editingAudioNestedId = id;
        else _editingNestedId = id;
    }

    private void SwitchNestedOnSelection(string clipId)
    {
        if (_controller?.CurrentProject.ActiveSequence is not { } sequence) return;
        var clip = sequence.Clips.FirstOrDefault(c => c.Id == clipId && c.NestedContent != null);
        if (clip == null) return;
        var kind = clip.NestedRole == "audio" ? "audio" : "video";
        var active = sequence.Clips.FirstOrDefault(c => c.Id == EditingId(kind));
        if (active == null || active.Id == clip.Id || active.TrackId != clip.TrackId) return;
        _lastNestedAutoSwitchId = clip.Id;
        _lastNestedAutoSwitchAt = DateTime.UtcNow;
        SetEditingId(kind, clip.Id);
        var linked = NestedMediaOperations.Companion(sequence, clip);
        if (linked?.NestedContent != null)
        {
            var otherKind = kind == "video" ? "audio" : "video";
            var otherActive = sequence.Clips.FirstOrDefault(c => c.Id == EditingId(otherKind));
            if (otherActive?.TrackId == linked.TrackId) SetEditingId(otherKind, linked.Id);
        }
        RefreshNestedInline();
    }

    private void CloseNestedInline()
    {
        CloseNestedInline("video");
        CloseNestedInline("audio");
    }

    private void CloseNestedInline(string kind)
    {
        SetEditingId(kind, null);
        var editor = kind == "audio" ? _audioNestedEditor : _nestedEditor;
        editor?.Set(null, null);
        if (editor != null) editor.IsVisible = false;
        TimelineCanvas.SetInlineNested(kind, null, 0);
    }

    private void RefreshNestedInline()
    {
        RefreshNestedInline("video", _nestedEditor);
        RefreshNestedInline("audio", _audioNestedEditor);
    }

    private void RefreshNestedInline(string kind, NestedInlineEditorControl? editor)
    {
        if (editor == null || _controller == null || EditingId(kind) == null) return;
        var clip = _controller.CurrentProject.ActiveSequence?.Clips.FirstOrDefault(c => c.Id == EditingId(kind));
        if (!NestedMediaOperations.IsNested(clip)) { CloseNestedInline(kind); return; }
        editor.Set(_controller.CurrentProject, clip!.Id);
        editor.IsVisible = true;
        TimelineCanvas.SetInlineNested(kind, clip.Id, editor.DesiredHeight);
        PositionNestedInline(kind, editor);
    }

    private void PositionNestedInline()
    {
        if (_nestedEditor != null) PositionNestedInline("video", _nestedEditor);
        if (_audioNestedEditor != null) PositionNestedInline("audio", _audioNestedEditor);
    }

    private void PositionNestedInline(string kind, NestedInlineEditorControl editor)
    {
        if (!editor.IsVisible || _controller?.CurrentProject.ActiveSequence is not { } sequence) return;
        var clip = sequence.Clips.FirstOrDefault(c => c.Id == EditingId(kind));
        var track = sequence.Tracks.FirstOrDefault(t => t.Id == clip?.TrackId);
        if (track == null) return;
        editor.Width = TimelineCanvas.Bounds.Width;
        editor.Height = editor.DesiredHeight;
        var top = TimelineCanvas.TrackTop(track.Id) + TimelineCanvas.TrackBaseHeight(track);
        var viewport = TimelineCanvas.TrackViewport(kind);
        var clipTop = Math.Max(0, viewport.Top - top);
        var clipBottom = Math.Min(editor.DesiredHeight, viewport.Bottom - top);
        editor.Clip = new RectangleGeometry(new Rect(0, clipTop, editor.Width, Math.Max(0, clipBottom - clipTop)));
        editor.IsHitTestVisible = clipBottom > clipTop;
        Canvas.SetLeft(editor, 0);
        Canvas.SetTop(editor, top);
        editor.InvalidateVisual();
    }

    private async Task EditNestedChildPropertiesAsync(string? outerId, string childId)
    {
        if (_controller == null || outerId == null || TopLevel.GetTopLevel(this) is not Window parent) return;
        var found = NestedMediaOperations.FindChild(_controller.CurrentProject, outerId, childId);
        if (found == null) return;
        var (child, track) = found.Value;
        var window = new Window { Title = "Propiedades del componente", Width = 350, Height = track.Kind == "audio" ? 340 : 230,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brushes.Black };
        var body = new StackPanel { Margin = new Thickness(16), Spacing = 9 };
        body.Children.Add(new TextBlock { Text = "Nombre", Foreground = Brushes.White });
        var name = new TextBox { Text = child.Name };
        body.Children.Add(name);
        var enabled = new CheckBox { Content = "Componente activo", IsChecked = child.Enabled, Foreground = Brushes.White };
        body.Children.Add(enabled);
        TextBox? gain = null;
        TextBox? balance = null;
        if (track.Kind == "audio")
        {
            body.Children.Add(new TextBlock { Text = "Volumen (dB)", Foreground = Brushes.White });
            gain = new TextBox { Text = child.GainDb.ToString("0.##", CultureInfo.CurrentCulture) };
            body.Children.Add(gain);
            body.Children.Add(new TextBlock { Text = "Balance (-1 a 1)", Foreground = Brushes.White });
            balance = new TextBox { Text = child.Balance.ToString("0.##", CultureInfo.CurrentCulture) };
            body.Children.Add(balance);
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = "Aplicar" };
        var cancel = new Button { Content = "Cancelar" };
        save.Click += (_, _) =>
        {
            try
            {
                static double Parse(string? value) => double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) ||
                    double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) ? number : throw new FormatException("Número inválido.");
                var nextGain = gain == null ? child.GainDb : Parse(gain.Text);
                var nextBalance = balance == null ? child.Balance : Parse(balance.Text);
                _controller.UndoRedo.Commit(p => NestedMediaOperations.SetChildProperties(p, outerId!, childId,
                    name.Text ?? child.Name, enabled.IsChecked == true, nextGain, nextBalance), "Propiedades del componente anidado");
                window.Close();
            }
            catch (Exception ex) { _controller.ReportStatus("Propiedades internas: " + ex.Message); }
        };
        cancel.Click += (_, _) => window.Close();
        row.Children.Add(save); row.Children.Add(cancel); body.Children.Add(row);
        window.Content = body;
        await window.ShowDialog(parent);
    }
}
