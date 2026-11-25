using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public partial class TimelinePanelView
{
    private SequenceMarker? MarkerAt(double x) => _controller?.CurrentProject.ActiveSequence?.Markers
        .Where(m => Math.Abs(TimelineCanvas.TimeToPixels(m.Time.Seconds) - x) <= 8)
        .OrderBy(m => Math.Abs(TimelineCanvas.TimeToPixels(m.Time.Seconds) - x)).FirstOrDefault();

    private double SnapMarkerTime(double requested, string movingId)
    {
        var sequence = _controller?.CurrentProject.ActiveSequence;
        if (sequence == null) return Math.Max(0, requested);
        var points = sequence.Clips.SelectMany(c => new[] { c.TimelineStart.Seconds, c.EndSeconds })
            .Concat(sequence.Markers.Where(m => m.Id != movingId).Select(m => m.Time.Seconds))
            .Append(TimelineCanvas.PlayheadSeconds).Append(0);
        var nearest = points.OrderBy(t => Math.Abs(t - requested)).FirstOrDefault();
        if (TimelineCanvas.IsSnappingEnabled && Math.Abs(nearest - requested) * TimelineCanvas.ZoomPixelsPerSecond <= 8)
            requested = nearest;
        return MarkerOperations.FrameTime(sequence, requested);
    }

    private void DrawRulerMarks()
    {
        var sequence = _controller?.CurrentProject.ActiveSequence;
        if (sequence == null) return;
        foreach (var marker in sequence.Markers)
        {
            var at = marker.Id == _draggingMarkerId ? _markerDragTime : marker.Time.Seconds;
            var x = TimelineCanvas.TimeToPixels(at);
            if (x < -8 || x > RulerCanvas.Bounds.Width + 8) continue;
            var color = Argb(marker.ColorArgb);
            var indicator = new Border { Width = 10, Height = 8, Background = new SolidColorBrush(color),
                BorderBrush = Brushes.Black, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2),
                IsHitTestVisible = false };
            Canvas.SetLeft(indicator, x - 5);
            Canvas.SetTop(indicator, 13);
            RulerCanvas.Children.Add(indicator);
            _rulerMarks.Add(indicator);
            ToolTip.SetTip(indicator, marker.Name);
        }
        AddRangeMark(sequence.MarkInSeconds, "I", Brushes.LightGreen);
        AddRangeMark(sequence.MarkOutSeconds, "O", Brushes.LightSkyBlue);
    }

    private void AddRangeMark(double? time, string label, IBrush brush)
    {
        if (!time.HasValue) return;
        var x = TimelineCanvas.TimeToPixels(time.Value);
        if (x < -18 || x > RulerCanvas.Bounds.Width + 18) return;
        var mark = new TextBlock { Text = label, Foreground = brush, FontSize = 10, FontWeight = FontWeight.Bold,
            IsHitTestVisible = false };
        Canvas.SetLeft(mark, x + 2);
        Canvas.SetTop(mark, 11);
        RulerCanvas.Children.Add(mark);
        _rulerMarks.Add(mark);
    }

    private static Color Argb(uint value) => Color.FromArgb((byte)(value >> 24), (byte)(value >> 16),
        (byte)(value >> 8), (byte)value);

    private void ShowRulerContext(Point point)
    {
        if (_controller?.CurrentProject.ActiveSequence is not { } sequence) return;
        var marker = MarkerAt(point.X);
        var time = TimelineCanvas.PixelsToTime(point.X);
        var items = new List<MenuItem>();
        if (marker != null)
        {
            var id = marker.Id;
            items.Add(Menu("Ir a " + marker.Name, () => { _transport?.Pause(); _transport?.Seek(marker.Time.Seconds); }));
            items.Add(Menu("Renombrar marcador…", async () => await RenameMarkerAsync(id)));
            items.Add(Submenu("Color", new[] {
                MarkerColorItem(id, "Naranja", 0xFFFFB95F), MarkerColorItem(id, "Azul", 0xFF83C8E9),
                MarkerColorItem(id, "Verde", 0xFF9CDDA4), MarkerColorItem(id, "Rosa", 0xFFF1A3BB),
                MarkerColorItem(id, "Rojo", 0xFFF39287) }));
            items.Add(Menu("Eliminar marcador", () => _controller.UndoRedo.Commit(p =>
                p.ActiveSequence!.Markers.RemoveAll(m => m.Id == id), "Eliminar marcador")));
        }
        else
        {
            items.Add(Menu("Añadir marcador aquí", () => AddMarkerAt(time)));
            items.Add(Menu("Marcar entrada aquí", () => SetRangeMark(true, time)));
            items.Add(Menu("Marcar salida aquí", () => SetRangeMark(false, time)));
            if (sequence.MarkInSeconds.HasValue || sequence.MarkOutSeconds.HasValue)
                items.Add(Menu("Borrar entrada y salida", () => _controller.UndoRedo.Commit(p =>
                    MarkerOperations.ClearRange(p.ActiveSequence!), "Borrar entrada y salida")));
            if (sequence.MarkInSeconds is { } markIn && sequence.MarkOutSeconds is { } markOut)
                items.Add(Menu("Ajustar zoom al rango", () => TimelineCanvas.FitTimeRange(markIn, markOut)));
            items.Add(Menu("Marcador anterior", () => JumpMarker(false)));
            items.Add(Menu("Marcador siguiente", () => JumpMarker(true)));
        }
        ShowMenu(RulerCanvas, items);
    }

    private MenuItem MarkerColorItem(string id, string name, uint color) => Menu(name, () =>
        _controller?.UndoRedo.Commit(p => p.ActiveSequence!.Markers.Single(m => m.Id == id).ColorArgb = color,
            "Cambiar color de marcador"));

    private void AddMarkerAt(double time) => _controller?.UndoRedo.Commit(p =>
        MarkerOperations.Add(p.ActiveSequence!, time), "Añadir marcador");

    private void SetRangeMark(bool markIn, double time) => _controller?.UndoRedo.Commit(p =>
    {
        if (markIn) MarkerOperations.SetIn(p.ActiveSequence!, time);
        else MarkerOperations.SetOut(p.ActiveSequence!, time);
    }, markIn ? "Marcar entrada" : "Marcar salida");

    private void JumpMarker(bool forward)
    {
        var markers = _controller?.CurrentProject.ActiveSequence?.Markers;
        if (markers == null) return;
        var now = TimelineCanvas.PlayheadSeconds;
        var next = forward ? markers.Where(m => m.Time.Seconds > now + 0.001).OrderBy(m => m.Time.Seconds).FirstOrDefault()
            : markers.Where(m => m.Time.Seconds < now - 0.001).OrderByDescending(m => m.Time.Seconds).FirstOrDefault();
        if (next != null) { _transport?.Pause(); _transport?.Seek(next.Time.Seconds); }
    }

    private async Task RenameMarkerAsync(string id)
    {
        if (_controller == null || TopLevel.GetTopLevel(this) is not Window parent) return;
        var marker = _controller.CurrentProject.ActiveSequence?.Markers.FirstOrDefault(m => m.Id == id);
        if (marker == null) return;
        var input = new TextBox { Text = marker.Name, Margin = new Thickness(12) };
        var accept = new Button { Content = "Guardar", Width = 82 };
        var cancel = new Button { Content = "Cancelar", Width = 82 };
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(12) };
        row.Children.Add(accept); row.Children.Add(cancel);
        var body = new StackPanel(); body.Children.Add(input); body.Children.Add(row);
        var dialog = new Window { Title = "Renombrar marcador", Width = 370, Height = 145, Content = body,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        accept.Click += (_, _) => dialog.Close(input.Text);
        cancel.Click += (_, _) => dialog.Close(null);
        var name = await dialog.ShowDialog<string?>(parent);
        if (name == null) return;
        try { _controller.UndoRedo.Commit(p => MarkerOperations.Rename(p.ActiveSequence!, id, name), "Renombrar marcador"); }
        catch (Exception ex) { _controller.ReportStatus(ex.Message); }
    }
}
