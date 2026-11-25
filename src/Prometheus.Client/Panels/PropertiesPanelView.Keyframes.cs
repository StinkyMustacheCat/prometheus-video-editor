using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Prometheus.Core.Model;

namespace Prometheus.Client.Panels;

public partial class PropertiesPanelView
{
    private readonly Dictionary<string, Slider> _keyframeSliders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ToggleButton> _keyframeClocks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _keyframeAddButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _keyframeLabels = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dirtyVisualTargets = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dirtyEffectTargets = new(StringComparer.Ordinal);
    private readonly List<Border> _keyframePlayheads = new();
    private double _keyframeCanvasWidth;
    private string? _keyframeViewClip;
    private double _keyframeViewStart;
    private double _keyframeViewSpan;
    private bool _syncingKeyframePan;
    private bool _keyframeDrawerOpen;
    private IPointer? _keyframeDragPointer;
    private Canvas? _keyframeDragCanvas;
    private Border? _keyframeDragDot;
    private string? _keyframeDragTarget;
    private double _keyframeDragFrom;
    private double _keyframeDragX;
    private string? _selectedKeyframeTarget;
    private double _selectedKeyframeTime;
    private Border? _selectedKeyframeDot;

    private void InitializeKeyframeEditors()
    {
        foreach (var (target, slider) in new (string, Slider)[]
        {
            ("visual.position_x", PosXSlider), ("visual.position_y", PosYSlider),
            ("visual.scale_x", ScaleXSlider), ("visual.scale_y", ScaleYSlider),
            ("visual.rotation", RotationSlider), ("visual.opacity", OpacitySlider),
            ("visual.anchor_x", AnchorXSlider), ("visual.anchor_y", AnchorYSlider)
        })
        {
            _keyframeSliders[target] = slider;
            _keyframeLabels[target] = KeyframeAnimation.VisualTargets[target];
            if (slider.Parent is not Grid row) continue;
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var clock = new ToggleButton { Content = "◷", Width = 24, Height = 22,
                Padding = new Thickness(0), FontSize = 15, Margin = new Thickness(5, 0, 0, 0) };
            ToolTip.SetTip(clock, "Activar o quitar fotogramas clave para " + KeyframeAnimation.VisualTargets[target]);
            Grid.SetColumn(clock, 3);
            row.Children.Add(clock);
            _keyframeClocks[target] = clock;
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var add = new Button { Content = "◇", Width = 23, Height = 22,
                Padding = new Thickness(0), FontSize = 13, Margin = new Thickness(3, 0, 0, 0) };
            ToolTip.SetTip(add, "Crear fotograma clave en el lector");
            Grid.SetColumn(add, 4);
            row.Children.Add(add);
            _keyframeAddButtons[target] = add;
            add.Click += (_, _) => AddKeyframe(target);
            slider.ValueChanged += (_, _) => { if (!_syncing) _dirtyVisualTargets.Add(target); };
            clock.IsCheckedChanged += (_, _) => ToggleKeyframeTrack(target, clock.IsChecked == true);
        }
        KeyframeDrawerBody.IsVisible = false;
        KeyframeDrawerToggle.Click += (_, _) => SetKeyframeDrawer(!_keyframeDrawerOpen);
        KeyframeZoomIn.Click += (_, _) => ZoomKeyframes(0.5);
        KeyframeZoomOut.Click += (_, _) => ZoomKeyframes(2);
        KeyframeFit.Click += (_, _) => FitKeyframes();
        KeyframeScroll.SizeChanged += (_, args) =>
        {
            if (_keyframeDrawerOpen && Math.Abs(args.NewSize.Width - args.PreviousSize.Width) > 1)
                RefreshKeyframeRows();
        };
        KeyframePan.ValueChanged += (_, _) =>
        {
            if (_syncingKeyframePan || VisibleKeyframeClip() is not { } clip) return;
            _keyframeViewStart = Math.Max(0, clip.Duration.Seconds - _keyframeViewSpan) * KeyframePan.Value;
            RefreshKeyframeRows();
        };
    }

    private void RegisterEffectSlider(EffectInstance effect, string key, string label, Slider slider)
    {
        var target = KeyframeAnimation.EffectTarget(effect, key);
        if (_keyframeSliders.ContainsKey(target)) return;
        if (slider.Parent is not Grid row) return;
        _keyframeSliders[target] = slider;
        _keyframeLabels[target] = label;
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        var clock = new ToggleButton { Content = "◷", Width = 24, Height = 22,
            Padding = new Thickness(0), FontSize = 15, Margin = new Thickness(5, 0, 0, 0) };
        ToolTip.SetTip(clock, "Animar " + label);
        Grid.SetColumn(clock, 3); row.Children.Add(clock);
        _keyframeClocks[target] = clock;
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        var add = new Button { Content = "◇", Width = 23, Height = 22,
            Padding = new Thickness(0), FontSize = 13, Margin = new Thickness(3, 0, 0, 0) };
        ToolTip.SetTip(add, "Crear fotograma clave en el lector");
        Grid.SetColumn(add, 4); row.Children.Add(add);
        _keyframeAddButtons[target] = add;
        slider.ValueChanged += (_, _) =>
        {
            if (!_syncing && _keyframeSliders.ContainsKey(target)) _dirtyEffectTargets.Add(target);
        };
        clock.IsCheckedChanged += (_, _) => ToggleKeyframeTrack(target, clock.IsChecked == true);
        add.Click += (_, _) => AddKeyframe(target);
    }

    private void RefreshStaticEffectClocks(Clip? clip)
    {
        var definitions = new (string EffectId, string Key, string Label, Slider Slider)[]
        {
            (BasicColorEffect.Id, "brightness", "Brillo", BrightnessSlider),
            (BasicColorEffect.Id, "contrast", "Contraste", ContrastSlider),
            (BasicColorEffect.Id, "saturation", "Saturación", SaturationSlider),
            (ExposureEffect.Id, "stops", "Pasos de exposición", ExposureStopsSlider),
            (ExposureEffect.Id, "black", "Nivel de negro", ExposureBlackSlider),
            (LevelsEffect.Id, "black", "Negro", LevelsBlackSlider),
            (LevelsEffect.Id, "white", "Blanco", LevelsWhiteSlider),
            (LevelsEffect.Id, "gamma", "Gamma", LevelsGammaSlider)
        };
        var desired = definitions.Select(item => (item.Key, item.Label, item.Slider,
            Effect: clip?.Effects.FirstOrDefault(effect => effect.EffectId == item.EffectId))).ToArray();
        var active = desired.Where(item => item.Effect != null)
            .Select(item => KeyframeAnimation.EffectTarget(item.Effect!, item.Key))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var slider in definitions.Select(item => item.Slider))
        foreach (var target in _keyframeSliders.Where(item => ReferenceEquals(item.Value, slider) &&
            !active.Contains(item.Key)).Select(item => item.Key).ToArray())
        {
            RemoveRegisteredKeyframeTarget(target);
        }
        foreach (var (key, label, slider, effect) in desired)
            if (effect != null) RegisterEffectSlider(effect, key, label, slider);
    }

    private void RemoveRegisteredKeyframeTarget(string target)
    {
        if (_keyframeClocks.GetValueOrDefault(target)?.Parent is Grid row)
        {
            row.Children.Remove(_keyframeClocks[target]);
            row.Children.Remove(_keyframeAddButtons[target]);
            if (row.ColumnDefinitions.Count >= 5)
            { row.ColumnDefinitions.RemoveAt(4); row.ColumnDefinitions.RemoveAt(3); }
        }
        _keyframeSliders.Remove(target);
        _keyframeClocks.Remove(target);
        _keyframeAddButtons.Remove(target);
        _keyframeLabels.Remove(target);
        _dirtyEffectTargets.Remove(target);
    }

    private void UnregisterEffectSliders(string instanceId)
    {
        var prefix = "effect." + instanceId + ".";
        foreach (var target in _keyframeSliders.Keys.Where(key => key.StartsWith(prefix,
            StringComparison.Ordinal)).ToArray())
            RemoveRegisteredKeyframeTarget(target);
    }

    private void CommitEffectValue(Clip clip, EffectInstance effect, string key, double value,
        double time)
    {
        var target = KeyframeAnimation.EffectTarget(effect, key);
        if (KeyframeAnimation.Find(clip, target) != null)
        {
            if (_dirtyEffectTargets.Contains(target)) KeyframeAnimation.Set(clip, target, time, value);
            return;
        }
        ImageGeometryEffect.Number(effect, key, value);
    }

    private void SetKeyframeDrawer(bool open)
    {
        _keyframeDrawerOpen = open;
        KeyframeDrawerBody.IsVisible = open;
        KeyframeDrawerToggle.Content = open ? "Fotogramas clave  ⌃" : "Fotogramas clave  ⌄";
        if (open) RefreshKeyframeRows();
    }

    private void ToggleKeyframeTrack(string target, bool enabled)
    {
        if (_syncing || _controller == null || _selectedId == null) return;
        CommitPending();
        var clipId = _selectedId;
        var time = SelectedLocalTime;
        var value = target == "visual.rotation" ? _rotationValue : _keyframeSliders[target].Value;
        _controller.UndoRedo.Commit(project =>
        {
            var clip = Locate(project, clipId)?.Clip;
            if (clip == null) return;
            if (enabled) KeyframeAnimation.Enable(clip, target, time, value);
            else
            {
                var visible = KeyframeAnimation.Evaluate(clip, target, time, BaseTargetValue(clip, target));
                SetBaseTargetValue(clip, target, visible);
                KeyframeAnimation.Disable(clip, target);
            }
        }, enabled ? "Activar fotogramas clave" : "Quitar fotogramas clave");
        if (enabled) SetKeyframeDrawer(true);
        RefreshKeyframeRows();
    }

    private void RefreshKeyframeControls(Clip? clip)
    {
        foreach (var (target, clock) in _keyframeClocks)
        {
            clock.IsEnabled = clip != null && (_selectedIsVideo || target.StartsWith("effect.", StringComparison.Ordinal));
            clock.IsChecked = clip != null && KeyframeAnimation.Find(clip, target) != null;
            clock.Background = clock.IsChecked == true ? Brushes.DarkOrange : Brushes.Transparent;
            _keyframeAddButtons[target].IsVisible = clock.IsChecked == true;
        }
    }

    private void AddKeyframe(string target)
    {
        CommitPending();
        if (_controller == null || _selectedId == null) return;
        var clipId = _selectedId;
        var time = SelectedLocalTime;
        var value = target == "visual.rotation" ? _rotationValue : _keyframeSliders[target].Value;
        _controller.UndoRedo.Commit(project =>
        {
            var clip = Locate(project, clipId)?.Clip;
            if (clip != null) KeyframeAnimation.Set(clip, target, time, value);
        }, "Crear fotograma clave");
        SetKeyframeDrawer(true);
    }

    private void OnKeyframeTimeChanged(double _)
    {
        var clip = _controller == null ? null : Locate(_controller.CurrentProject, _selectedId)?.Clip;
        RefreshKeyframeValues(clip);
        var shape = SelectedMask(clip);
        if (clip != null && shape?.Kind == "shape")
            RefreshShapeKeyframeStatus(clip, shape);
    }

    private void RefreshKeyframeValues(Clip? clip)
    {
        if (clip == null || _controller == null || _pending) return;
        var previous = _syncing;
        var previousColor = _syncingColor;
        var previousImage = _syncingImage;
        var previousMotion = _syncingMotion;
        _syncing = true;
        _syncingColor = _syncingImage = _syncingMotion = true;
        try
        {
            var time = SelectedLocalTime;
            foreach (var (target, slider) in _keyframeSliders)
                if (KeyframeAnimation.Find(clip, target) != null)
                {
                    var value = KeyframeAnimation.Evaluate(clip, target, time,
                        BaseTargetValue(clip, target));
                    if (target == "visual.rotation")
                    {
                        _rotationValue = value;
                        _updatingRotationSlider = true;
                        slider.Value = DisplayAngle(value);
                        _updatingRotationSlider = false;
                    }
                    else slider.Value = value;
                }
            _scaleLinkRatio = ScaleYSlider.Value / Math.Max(0.01, ScaleXSlider.Value);
            UpdateLabels();
        }
        finally
        {
            _syncing = previous;
            _syncingColor = previousColor;
            _syncingImage = previousImage;
            _syncingMotion = previousMotion;
        }
        RefreshKeyframeCursor();
    }

    private Clip? VisibleKeyframeClip() =>
        _controller == null ? null : Locate(_controller.CurrentProject, _selectedId)?.Clip;

    private void SetKeyframeView(Clip clip, double center, double span)
    {
        var duration = clip.Duration.Seconds;
        var minimum = Math.Min(duration, 0.2);
        _keyframeViewSpan = Math.Clamp(span, minimum, duration);
        _keyframeViewStart = Math.Clamp(center - _keyframeViewSpan * 0.5, 0, duration - _keyframeViewSpan);
        RefreshKeyframeRows();
    }

    private void ZoomKeyframes(double factor)
    {
        if (VisibleKeyframeClip() is not { } clip) return;
        var playhead = SelectedLocalTime - clip.TimelineStart.Seconds;
        var center = playhead >= _keyframeViewStart && playhead <= _keyframeViewStart + _keyframeViewSpan
            ? playhead : _keyframeViewStart + _keyframeViewSpan * 0.5;
        SetKeyframeView(clip, center, _keyframeViewSpan * factor);
    }

    private void FitKeyframes()
    {
        if (VisibleKeyframeClip() is not { } clip) return;
        var duration = clip.Duration.Seconds;
        var times = clip.KeyframeTracks.Where(track => _keyframeLabels.ContainsKey(track.Target))
            .SelectMany(track => track.Points).Select(point => point.TimeSeconds)
            .Where(time => time >= 0 && time <= duration).ToArray();
        if (times.Length == 0) { SetKeyframeView(clip, duration * 0.5, duration); return; }
        var start = times.Min();
        var end = times.Max();
        SetKeyframeView(clip, (start + end) * 0.5, Math.Max(Math.Min(1, duration), (end - start) * 1.18));
    }

    private void RefreshKeyframeRows()
    {
        KeyframeRows.Children.Clear();
        _keyframePlayheads.Clear();
        _selectedKeyframeDot = null;
        var clip = VisibleKeyframeClip();
        if (clip == null)
        {
            KeyframeTimeLabel.Text = "Selecciona un medio";
            _keyframeViewClip = null;
            KeyframePan.IsEnabled = KeyframeZoomIn.IsEnabled = KeyframeZoomOut.IsEnabled = KeyframeFit.IsEnabled = false;
            return;
        }
        var duration = clip.Duration.Seconds;
        if (_keyframeViewClip != clip.Id)
        {
            _keyframeViewClip = clip.Id;
            _keyframeViewStart = 0;
            _keyframeViewSpan = duration;
        }
        _keyframeViewSpan = Math.Clamp(_keyframeViewSpan, Math.Min(0.2, duration), duration);
        var travel = duration - _keyframeViewSpan;
        _keyframeViewStart = Math.Clamp(_keyframeViewStart, 0, travel);
        _syncingKeyframePan = true;
        KeyframePan.IsEnabled = KeyframeZoomOut.IsEnabled = travel > 1e-6;
        KeyframeZoomIn.IsEnabled = _keyframeViewSpan > Math.Min(0.2, duration) + 1e-6;
        KeyframeFit.IsEnabled = true;
        KeyframePan.Value = travel > 1e-6 ? _keyframeViewStart / travel : 0;
        _syncingKeyframePan = false;
        _keyframeCanvasWidth = Math.Max(40, KeyframeScroll.Bounds.Width - 116);
        var tracks = clip.KeyframeTracks.Where(track => _keyframeLabels.ContainsKey(track.Target)).ToArray();
        if (tracks.Length == 0)
        {
            KeyframeRows.Children.Add(new TextBlock { Text = "Activa el reloj de un parámetro para animarlo",
                FontSize = 10, Foreground = Brushes.Gray });
            RefreshKeyframeCursor();
            return;
        }
        foreach (var track in tracks)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("86,Auto"), Height = 26 };
            row.Children.Add(new TextBlock { Text = _keyframeLabels[track.Target],
                FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center });
            var canvas = new Canvas { Width = _keyframeCanvasWidth, Height = 24, Background = Brushes.Transparent };
            var rule = new Border { Background = new SolidColorBrush(Color.Parse("#555555")),
                Height = 1, Width = _keyframeCanvasWidth, IsHitTestVisible = false };
            Canvas.SetTop(rule, 12); canvas.Children.Add(rule);
            var playhead = new Border { Background = Brushes.DarkOrange, Width = 1, Height = 24,
                IsHitTestVisible = false };
            canvas.Children.Add(playhead);
            _keyframePlayheads.Add(playhead);
            foreach (var point in track.Points.Where(point => point.TimeSeconds >= _keyframeViewStart - 1e-6 &&
                point.TimeSeconds <= _keyframeViewStart + _keyframeViewSpan + 1e-6).OrderBy(point => point.TimeSeconds))
            {
                var selected = _selectedKeyframeTarget == track.Target &&
                    Math.Abs(_selectedKeyframeTime - point.TimeSeconds) < 1e-6;
                var dot = new Border { Width = 11, Height = 11,
                    Background = selected ? Brushes.White : Brushes.Orange,
                    BorderBrush = selected ? Brushes.DarkOrange : Brushes.Black,
                    BorderThickness = new Thickness(1), RenderTransform = new RotateTransform(45) };
                if (selected) _selectedKeyframeDot = dot;
                Canvas.SetLeft(dot, PointToX(clip, point.TimeSeconds) - 5);
                Canvas.SetTop(dot, 7);
                var target = track.Target;
                var from = point.TimeSeconds;
                dot.PointerEntered += (_, _) =>
                {
                    if (!ReferenceEquals(_selectedKeyframeDot, dot)) dot.Background = Brushes.LightGoldenrodYellow;
                };
                dot.PointerExited += (_, _) =>
                {
                    if (!ReferenceEquals(_selectedKeyframeDot, dot)) dot.Background = Brushes.Orange;
                };
                dot.PointerPressed += (_, e) => StartKeyframeDrag(e, canvas, dot, target, from);
                dot.PointerMoved += (_, e) => MoveKeyframeDrag(e);
                dot.PointerReleased += (_, e) => EndKeyframeDrag(e);
                var remove = new MenuItem { Header = "Eliminar fotograma clave" };
                remove.Click += (_, _) => EditKeyframePoint(target, from, null);
                var linear = new MenuItem { Header = "Interpolación lineal" };
                linear.Click += (_, _) => EditKeyframePoint(target, from, "linear");
                var hold = new MenuItem { Header = "Mantener valor" };
                hold.Click += (_, _) => EditKeyframePoint(target, from, "hold");
                var ease = new MenuItem { Header = "Suavizar" };
                ease.Click += (_, _) => EditKeyframePoint(target, from, "ease");
                var easeIn = new MenuItem { Header = "Suavizar entrada" };
                easeIn.Click += (_, _) => EditKeyframePoint(target, from, "ease-in");
                var easeOut = new MenuItem { Header = "Suavizar salida" };
                easeOut.Click += (_, _) => EditKeyframePoint(target, from, "ease-out");
                var bezier = new MenuItem { Header = "Curva Bézier…" };
                bezier.Click += (_, _) => ShowBezierEditor(dot, target, from);
                dot.ContextMenu = new ContextMenu { ItemsSource = new[]
                    { remove, linear, hold, easeIn, easeOut, ease, bezier } };
                canvas.Children.Add(dot);
            }
            canvas.PointerPressed += (_, e) =>
            {
                if (e.Handled || !e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
                if (e.ClickCount == 2)
                {
                    AddKeyframeAtCanvas(track.Target, e.GetPosition(canvas).X);
                    e.Handled = true;
                    return;
                }
                if (_controller != null && Locate(_controller.CurrentProject, _selectedId) is { } location)
                    _controller.Transport.Seek(location.ToRootTime(clip.TimelineStart.Seconds +
                        XToPoint(clip, e.GetPosition(canvas).X)));
            };
            Grid.SetColumn(canvas, 1); row.Children.Add(canvas);
            KeyframeRows.Children.Add(row);
        }
        RefreshKeyframeCursor();
    }

    private double PointToX(Clip clip, double localSeconds) =>
        Math.Clamp((localSeconds - _keyframeViewStart) / Math.Max(1e-6, _keyframeViewSpan), 0, 1) * _keyframeCanvasWidth;

    private double XToPoint(Clip clip, double x) =>
        Math.Min(clip.Duration.Seconds, _keyframeViewStart +
            Math.Clamp(x / Math.Max(1, _keyframeCanvasWidth), 0, 1) * _keyframeViewSpan);

    private void RefreshKeyframeCursor()
    {
        var clip = _controller == null ? null : Locate(_controller.CurrentProject, _selectedId)?.Clip;
        if (clip == null) return;
        var time = SelectedLocalTime;
        var local = Math.Clamp(time - clip.TimelineStart.Seconds, 0, clip.Duration.Seconds);
        KeyframeTimeLabel.Text = $"{local:0.00} s · vista {_keyframeViewStart:0.00}–{_keyframeViewStart + _keyframeViewSpan:0.00} s";
        foreach (var playhead in _keyframePlayheads)
        {
            playhead.IsVisible = local >= _keyframeViewStart && local <= _keyframeViewStart + _keyframeViewSpan;
            Canvas.SetLeft(playhead, PointToX(clip, local));
        }
    }

    private void EditKeyframePoint(string target, double localSeconds, string? interpolation)
    {
        if (_controller == null || _selectedId == null) return;
        var clipId = _selectedId;
        _controller.UndoRedo.Commit(project =>
        {
            var clip = Locate(project, clipId)?.Clip;
            if (clip == null) return;
            var absolute = clip.TimelineStart.Seconds + localSeconds;
            if (interpolation == null) KeyframeAnimation.Remove(clip, target, absolute);
            else
            {
                var point = KeyframeAnimation.Find(clip, target)?.Points.FirstOrDefault(point =>
                    Math.Abs(point.TimeSeconds - localSeconds) < 1e-6);
                if (point != null) point.Interpolation = interpolation;
            }
        }, interpolation == null ? "Eliminar fotograma clave" : "Cambiar interpolación");
        if (interpolation == null && _selectedKeyframeTarget == target &&
            Math.Abs(_selectedKeyframeTime - localSeconds) < 1e-6) _selectedKeyframeTarget = null;
        RefreshKeyframeRows();
    }

    private void SelectKeyframe(string target, double localSeconds, Border dot)
    {
        if (_selectedKeyframeDot != null && !ReferenceEquals(_selectedKeyframeDot, dot))
        {
            _selectedKeyframeDot.Background = Brushes.Orange;
            _selectedKeyframeDot.BorderBrush = Brushes.Black;
        }
        _selectedKeyframeTarget = target;
        _selectedKeyframeTime = localSeconds;
        _selectedKeyframeDot = dot;
        dot.Background = Brushes.White;
        dot.BorderBrush = Brushes.DarkOrange;
    }

    private void AddKeyframeAtCanvas(string target, double x)
    {
        if (_controller == null || _selectedId == null) return;
        CommitPending();
        var location = Locate(_controller.CurrentProject, _selectedId);
        var clip = location?.Clip;
        if (clip == null) return;
        var local = XToPoint(clip, x);
        var absolute = clip.TimelineStart.Seconds + local;
        if (KeyframeAnimation.Find(clip, target)?.Points.Any(point =>
            Math.Abs(point.TimeSeconds - local) < 1e-6) != true)
        {
            var value = KeyframeAnimation.Evaluate(clip, target, absolute, BaseTargetValue(clip, target));
            var clipId = _selectedId;
            _controller.UndoRedo.Commit(project =>
            {
                var edited = Locate(project, clipId)?.Clip;
                if (edited != null) KeyframeAnimation.Set(edited, target, absolute, value);
            }, "Crear fotograma clave");
        }
        _selectedKeyframeTarget = target;
        _selectedKeyframeTime = local;
        _controller.Transport.Seek(location!.ToRootTime(absolute));
        RefreshKeyframeRows();
    }

    private void ShowBezierEditor(Control anchor, string target, double localSeconds)
    {
        var clip = _controller == null ? null : Locate(_controller.CurrentProject, _selectedId)?.Clip;
        var point = clip == null ? null : KeyframeAnimation.Find(clip, target)?.Points.FirstOrDefault(item =>
            Math.Abs(item.TimeSeconds - localSeconds) < 1e-6);
        if (point == null) return;
        var fields = new[] { ("X1", point.BezierX1), ("Y1", point.BezierY1),
            ("X2", point.BezierX2), ("Y2", point.BezierY2) };
        var body = new StackPanel { Spacing = 5 };
        body.Children.Add(new TextBlock { Text = "Curva Bézier · salida de este punto", FontSize = 11,
            FontWeight = FontWeight.SemiBold });
        var inputs = new List<TextBox>();
        foreach (var (label, value) in fields)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new TextBlock { Text = label, Width = 22, VerticalAlignment = VerticalAlignment.Center });
            var input = new TextBox { Text = value.ToString("0.###", CultureInfo.CurrentCulture), Width = 65 };
            inputs.Add(input); row.Children.Add(input); body.Children.Add(row);
        }
        var save = new Button { Content = "Aplicar curva", HorizontalAlignment = HorizontalAlignment.Right };
        body.Children.Add(save);
        var flyout = new Flyout { Content = new Border { Background = Brushes.Black,
            BorderBrush = Brushes.DarkOrange, BorderThickness = new Thickness(1),
            Padding = new Thickness(10), Child = body } };
        save.Click += (_, _) =>
        {
            var values = new double[4];
            for (var index = 0; index < inputs.Count; index++)
                if (!double.TryParse(inputs[index].Text, NumberStyles.Float, CultureInfo.CurrentCulture,
                    out values[index]) || !double.IsFinite(values[index])) return;
            if (_controller == null || _selectedId == null) return;
            var clipId = _selectedId;
            _controller.UndoRedo.Commit(project =>
            {
                var edited = Locate(project, clipId)?.Clip;
                if (edited != null) KeyframeAnimation.SetBezier(edited, target,
                    edited.TimelineStart.Seconds + localSeconds, values[0], values[1], values[2], values[3]);
            }, "Editar curva Bézier");
            flyout.Hide();
            RefreshKeyframeRows();
        };
        flyout.ShowAt(anchor);
    }

    private void StartKeyframeDrag(PointerPressedEventArgs e, Canvas canvas, Border dot,
        string target, double localSeconds)
    {
        if (!e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
        SelectKeyframe(target, localSeconds, dot);
        _keyframeDragPointer = e.Pointer;
        _keyframeDragCanvas = canvas;
        _keyframeDragDot = dot;
        _keyframeDragTarget = target;
        _keyframeDragFrom = localSeconds;
        _keyframeDragX = e.GetPosition(canvas).X;
        e.Pointer.Capture(dot);
        e.Handled = true;
    }

    private void MoveKeyframeDrag(PointerEventArgs e)
    {
        if (_keyframeDragPointer != e.Pointer || _keyframeDragCanvas == null || _keyframeDragDot == null) return;
        var x = Math.Clamp(e.GetPosition(_keyframeDragCanvas).X, 0, _keyframeCanvasWidth);
        Canvas.SetLeft(_keyframeDragDot, x - 5);
        e.Handled = true;
    }

    private void EndKeyframeDrag(PointerReleasedEventArgs e)
    {
        if (_keyframeDragPointer != e.Pointer || _keyframeDragCanvas == null || _keyframeDragTarget == null ||
            _controller == null || _selectedId == null) return;
        var x = Math.Clamp(e.GetPosition(_keyframeDragCanvas).X, 0, _keyframeCanvasWidth);
        var moved = Math.Abs(x - _keyframeDragX) > 3;
        var target = _keyframeDragTarget;
        var from = _keyframeDragFrom;
        var clipId = _selectedId;
        e.Pointer.Capture(null);
        _keyframeDragPointer = null; _keyframeDragCanvas = null; _keyframeDragDot = null; _keyframeDragTarget = null;
        var location = Locate(_controller.CurrentProject, clipId);
        var clip = location?.Clip;
        if (clip == null) return;
        var absolute = clip.TimelineStart.Seconds + (moved ? XToPoint(clip, x) : from);
        if (moved)
            _controller.UndoRedo.Commit(project =>
            {
                var edited = Locate(project, clipId)?.Clip;
                if (edited != null) KeyframeAnimation.Move(edited, target,
                    edited.TimelineStart.Seconds + from, absolute);
            }, "Mover fotograma clave");
        if (moved) _selectedKeyframeTime = XToPoint(clip, x);
        _controller.Transport.Seek(location!.ToRootTime(absolute));
        RefreshKeyframeRows();
        e.Handled = true;
    }

    private static double BaseVisualValue(ClipVisualState state, string target) => target switch
    {
        "visual.position_x" => state.PositionX, "visual.position_y" => state.PositionY,
        "visual.scale_x" => state.ScaleX, "visual.scale_y" => state.ScaleY,
        "visual.rotation" => state.RotationDegrees, "visual.opacity" => state.Opacity,
        "visual.anchor_x" => state.AnchorX, "visual.anchor_y" => state.AnchorY,
        _ => 0
    };

    private static void SetBaseVisualValue(ClipVisualState state, string target, double value)
    {
        switch (target)
        {
            case "visual.position_x": state.PositionX = value; break;
            case "visual.position_y": state.PositionY = value; break;
            case "visual.scale_x": state.ScaleX = value; break;
            case "visual.scale_y": state.ScaleY = value; break;
            case "visual.rotation": state.RotationDegrees = value; break;
            case "visual.opacity": state.Opacity = value; break;
            case "visual.anchor_x": state.AnchorX = value; break;
            case "visual.anchor_y": state.AnchorY = value; break;
        }
    }

    private static double BaseTargetValue(Clip clip, string target)
    {
        if (!target.StartsWith("effect.", StringComparison.Ordinal)) return BaseVisualValue(clip.Visual, target);
        var remainder = target[7..];
        var split = remainder.IndexOf('.');
        if (split < 0) return 0;
        var effect = clip.Effects.FirstOrDefault(item => item.Id == remainder[..split]);
        return effect?.Parameters.GetValueOrDefault(remainder[(split + 1)..])?.Number ?? 0;
    }

    private static void SetBaseTargetValue(Clip clip, string target, double value)
    {
        if (!target.StartsWith("effect.", StringComparison.Ordinal))
        { SetBaseVisualValue(clip.Visual, target, value); return; }
        var remainder = target[7..];
        var split = remainder.IndexOf('.');
        if (split < 0) return;
        var effect = clip.Effects.FirstOrDefault(item => item.Id == remainder[..split]);
        if (effect != null) ImageGeometryEffect.Number(effect, remainder[(split + 1)..], value);
    }
}
