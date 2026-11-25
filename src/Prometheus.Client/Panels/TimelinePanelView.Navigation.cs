using System;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Prometheus.Client.Panels;

public partial class TimelinePanelView
{
    private TextBox? _timecodeEditor;
    private bool _editingTimecode;

    private void SetupTimecodeEditor()
    {
        var display = TimecodeDisplay;
        var grid = new Grid();
        TimecodeHost.Child = null;
        grid.Children.Add(display);
        _timecodeEditor = new TextBox
        {
            IsVisible = false,
            MinWidth = 105,
            Padding = new Avalonia.Thickness(0),
            FontFamily = new FontFamily("Consolas, monospace"),
            FontSize = 11,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        grid.Children.Add(_timecodeEditor);
        TimecodeHost.Child = grid;
        display.Cursor = new Cursor(StandardCursorType.Ibeam);
        display.PointerPressed += (_, e) => { BeginTimecodeEdit(); e.Handled = true; };
        _timecodeEditor.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { EndTimecodeEdit(); e.Handled = true; }
            else if (e.Key == Key.Enter)
            {
                if (TryParseTimecode(_timecodeEditor.Text, _transport?.FrameRate ?? 60, out var seconds))
                {
                    EndTimecodeEdit();
                    _transport?.Pause();
                    _transport?.Seek(seconds);
                }
                else _timecodeEditor.BorderBrush = Brushes.IndianRed;
                e.Handled = true;
            }
        };
        _timecodeEditor.TextChanged += (_, _) => _timecodeEditor.BorderBrush = null;
        _timecodeEditor.LostFocus += (_, _) => { if (_editingTimecode) EndTimecodeEdit(); };
    }

    private void BeginTimecodeEdit()
    {
        if (_timecodeEditor == null) return;
        _editingTimecode = true;
        _timecodeEditor.Text = TimecodeDisplay.Text;
        TimecodeDisplay.IsVisible = false;
        _timecodeEditor.IsVisible = true;
        _timecodeEditor.Focus();
        _timecodeEditor.SelectAll();
    }

    private void EndTimecodeEdit()
    {
        if (_timecodeEditor == null) return;
        _editingTimecode = false;
        _timecodeEditor.IsVisible = false;
        TimecodeDisplay.IsVisible = true;
        UpdatePlayheadUI(_transport?.PositionSeconds ?? 0);
    }

    public static bool TryParseTimecode(string? value, double fps, out double seconds)
    {
        seconds = 0;
        var parts = value?.Trim().Split(':');
        if (parts is not { Length: 4 }) return false;
        var values = new long[4];
        for (var i = 0; i < 4; i++)
            if (!long.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out values[i])) return false;
        var nominalFps = Math.Max(1, (long)Math.Round(fps));
        if (values[0] > 100000 || values[1] > 59 || values[2] > 59 || values[3] >= nominalFps) return false;
        var frames = (((values[0] * 60 + values[1]) * 60 + values[2]) * nominalFps) + values[3];
        seconds = frames / Math.Max(1, fps);
        return true;
    }

    private void JumpCut(bool forward)
    {
        var seq = _controller?.CurrentProject.ActiveSequence;
        if (seq == null || _transport == null) return;
        var position = _transport.PositionSeconds;
        var tolerance = 0.5 / _transport.FrameRate;
        var boundaries = seq.Clips.SelectMany(c => new[] { c.TimelineStart.Seconds, c.EndSeconds })
            .Append(0).Append(seq.DurationSeconds).Distinct();
        var candidate = forward
            ? boundaries.Where(t => t > position + tolerance).OrderBy(t => t).FirstOrDefault(double.NaN)
            : boundaries.Where(t => t < position - tolerance).OrderByDescending(t => t).FirstOrDefault(double.NaN);
        if (!double.IsNaN(candidate)) { _transport.Pause(); _transport.Seek(candidate); }
    }
}
