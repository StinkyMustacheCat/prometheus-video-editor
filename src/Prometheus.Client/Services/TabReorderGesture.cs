using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace Prometheus.Client.Services;

/// <summary>Reorders compact tabs with a live drag and an animated insertion preview.</summary>
public sealed class TabReorderGesture
{
    private readonly StackPanel _bar;
    private readonly List<Button> _tabs;
    private readonly int _fixedCount;
    private readonly Action<int, int> _commit;
    private Button? _source;
    private Point _start;
    private int _sourceIndex;
    private int _targetIndex;
    private bool _dragging;

    public bool SuppressClick { get; private set; }

    public void UpdateOrder(IEnumerable<Button> tabs)
    {
        _tabs.Clear();
        _tabs.AddRange(tabs);
    }

    public TabReorderGesture(StackPanel bar, IEnumerable<Button> tabs, int fixedCount,
        Action<int, int> commit)
    {
        _bar = bar;
        _tabs = tabs.ToList();
        _fixedCount = fixedCount;
        _commit = commit;
        for (var i = fixedCount; i < _tabs.Count; i++)
        {
            var tab = _tabs[i];
            tab.PointerPressed += (_, e) => Press(tab, e);
            tab.PointerMoved += (_, e) => Move(tab, e);
            tab.AddHandler(InputElement.PointerReleasedEvent, (_, e) => Release(tab, e),
                RoutingStrategies.Tunnel, handledEventsToo: true);
            tab.PointerCaptureLost += (_, _) => { if (_source == tab) Reset(); };
        }
    }

    private void Press(Button tab, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(tab).Properties.IsLeftButtonPressed) return;
        _source = tab;
        _sourceIndex = _tabs.IndexOf(tab);
        _targetIndex = _sourceIndex;
        _start = e.GetPosition(_bar);
        _dragging = false;
        e.Pointer.Capture(tab);
    }

    private void Move(Button tab, PointerEventArgs e)
    {
        if (_source != tab || !e.GetCurrentPoint(tab).Properties.IsLeftButtonPressed) return;
        var current = e.GetPosition(_bar);
        if (!_dragging && Math.Abs(current.X - _start.X) < 6) return;
        _dragging = true;
        tab.ZIndex = 100;
        tab.Opacity = 0.9;
        tab.RenderTransform = new TranslateTransform(current.X - _start.X, 0);
        var insertion = 0;
        for (var i = 0; i < _tabs.Count; i++)
        {
            if (i == _sourceIndex) continue;
            if (current.X > _tabs[i].Bounds.Center.X) insertion++;
        }
        _targetIndex = Math.Clamp(insertion, _fixedCount, _tabs.Count - 1);
        var shift = Math.Max(0, tab.Bounds.Width - 4);
        for (var i = 0; i < _tabs.Count; i++)
        {
            if (i == _sourceIndex) continue;
            var neighbor = _tabs[i];
            if (neighbor.RenderTransform is not TranslateTransform transform)
            {
                transform = new TranslateTransform();
                transform.Transitions = new Transitions
                {
                    new DoubleTransition { Property = TranslateTransform.XProperty,
                        Duration = TimeSpan.FromMilliseconds(115) }
                };
                neighbor.RenderTransform = transform;
            }
            transform.X = _targetIndex > _sourceIndex && i > _sourceIndex && i <= _targetIndex ? -shift :
                _targetIndex < _sourceIndex && i >= _targetIndex && i < _sourceIndex ? shift : 0;
        }
        e.Handled = true;
    }

    private void Release(Button tab, PointerReleasedEventArgs e)
    {
        if (_source != tab) return;
        var moved = _dragging;
        var oldIndex = _sourceIndex;
        var newIndex = _targetIndex;
        if (moved)
        {
            SuppressClick = true;
            Dispatcher.UIThread.Post(() => SuppressClick = false, DispatcherPriority.Background);
        }
        Reset();
        e.Pointer.Capture(null);
        if (moved)
        {
            if (oldIndex != newIndex) _commit(oldIndex, newIndex);
            e.Handled = true;
        }
    }

    private void Reset()
    {
        foreach (var tab in _tabs)
        {
            tab.RenderTransform = null;
            tab.Opacity = 1;
            tab.ZIndex = 0;
        }
        _source = null;
        _dragging = false;
    }
}
