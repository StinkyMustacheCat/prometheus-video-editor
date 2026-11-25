using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public partial class PropertiesPanelView : UserControl
{
    private readonly System.Collections.Generic.HashSet<string> _collapsedCardKeys = new();
    private readonly Dictionary<string, Control> _effectCards = new(StringComparer.Ordinal);
    private string _visualChainSignature = "";
    private string _audioChainSignature = "";
    private readonly DispatcherTimer _commitTimer = new() { Interval = TimeSpan.FromMilliseconds(110) };
    private ProjectController? _controller;
    private string? _selectedId;
    private string? _selectedNestedRootId;
    private bool _syncing;
    private bool _pending;
    private bool _linkingScale;
    private double _scaleLinkRatio = 1;
    private double _rotationValue;
    private bool _updatingRotationSlider;
    private string _section = "all";
    private TabReorderGesture? _sectionTabReorder;
    private bool _selectedIsVideo;
    private bool _selectedHasAudio;
    private string? _audioClipId;
    private static readonly string[] Framing = ["fit", "width", "height", "fill", "stretch"];

    public PropertiesPanelView()
    {
        InitializeComponent();
        InitializeTransitionControls();
        SetupMaskTarget();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, InspectorDragOver);
        AddHandler(DragDrop.DropEvent, InspectorDrop);
        AllTab.Click += (_, _) => SetSection("all");
        BasicsTab.Click += (_, _) => SetSection("basics");
        VisualTab.Click += (_, _) => SetSection("visual");
        AudioTab.Click += (_, _) => SetSection("audio");
        InitializeSectionTabs();
        ResetPositionButton.Click += (_, _) =>
        {
            PosXSlider.Value = 0; PosYSlider.Value = 0;
            _rotationValue = 0; RotationSlider.Value = 0; UpdateLabels(); Changed();
        };
        ResetAnchorButton.Click += (_, _) => { AnchorXSlider.Value = 0.5; AnchorYSlider.Value = 0.5; };
        ResetScaleButton.Click += (_, _) =>
        {
            _linkingScale = true;
            ScaleXSlider.Value = 1;
            ScaleYSlider.Value = 1;
            _linkingScale = false;
            _scaleLinkRatio = 1;
            AspectLockToggle.IsChecked = true;
        };
        ResetOpacityButton.Click += (_, _) => OpacitySlider.Value = 1;
        ClipBlendModeBox.ItemsSource = ClipBlendModes.All.Select(mode => mode.Name).ToArray();
        ClipBlendModeBox.SelectionChanged += (_, _) => Changed();
        ResetOnThumbDoubleClick(PosXSlider, 0);
        ResetOnThumbDoubleClick(PosYSlider, 0);
        ResetOnThumbDoubleClick(RotationSlider, 0);
        RotationSlider.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.ClickCount != 2) return;
            _rotationValue = 0; UpdateLabels(); Changed();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        RotationText.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            CommitRotationInput();
            e.Handled = true;
        };
        RotationText.LostFocus += (_, _) => CommitRotationInput();
        ResetOnThumbDoubleClick(AnchorXSlider, 0.5);
        ResetOnThumbDoubleClick(AnchorYSlider, 0.5);
        ResetOnThumbDoubleClick(ScaleXSlider, 1);
        ResetOnThumbDoubleClick(ScaleYSlider, 1);
        ResetOnThumbDoubleClick(OpacitySlider, 1);
        ConfigureCapsules();
        InitializeMotionEditors();
        InitializeColorEditors();
        InitializeImageEditors();
        InitializeAudioEditors();
        InitializeKeyframeEditors();
        InitializeTextEditors();
        InitializePropertyGestures();
        ResetBasicColorButton.Click += (_, _) => ResetEffectByType(BasicColorEffect.Id);
        ResetExposureButton.Click += (_, _) => ResetEffectByType(ExposureEffect.Id);
        ResetLevelsButton.Click += (_, _) => ResetEffectByType(LevelsEffect.Id);
        ResetOnThumbDoubleClick(BrightnessSlider, 0);
        ResetOnThumbDoubleClick(ContrastSlider, 1);
        ResetOnThumbDoubleClick(SaturationSlider, 1);
        ResetOnThumbDoubleClick(ExposureStopsSlider, 0);
        ResetOnThumbDoubleClick(ExposureBlackSlider, 0);
        ResetOnThumbDoubleClick(LevelsBlackSlider, 0);
        ResetOnThumbDoubleClick(LevelsWhiteSlider, 1);
        ResetOnThumbDoubleClick(LevelsGammaSlider, 1);
        _commitTimer.Tick += (_, _) => CommitPending();
        RotationSlider.ValueChanged += (_, e) =>
        {
            if (!_syncing && !_updatingRotationSlider) _rotationValue += e.NewValue - e.OldValue;
        };
        foreach (var slider in new[] { PosXSlider, PosYSlider, RotationSlider, AnchorXSlider, AnchorYSlider,
                     ScaleXSlider, ScaleYSlider, OpacitySlider,
                     GainSlider, BalanceSlider, BrightnessSlider, ContrastSlider, SaturationSlider,
                     ExposureStopsSlider, ExposureBlackSlider, LevelsBlackSlider, LevelsWhiteSlider, LevelsGammaSlider })
            slider.ValueChanged += (_, _) => Changed();
        ScaleXSlider.ValueChanged += (_, e) => MatchScale(ScaleYSlider, e.NewValue);
        ScaleYSlider.ValueChanged += (_, e) => MatchScale(ScaleXSlider, e.NewValue);
        AspectLockToggle.IsCheckedChanged += (_, _) =>
        {
            if (!_syncing && AspectLockToggle.IsChecked == true)
                _scaleLinkRatio = ScaleYSlider.Value / Math.Max(0.01, ScaleXSlider.Value);
            Changed();
        };
        BasicColorToggle.IsCheckedChanged += (_, _) => Changed();
        ExposureToggle.IsCheckedChanged += (_, _) => Changed();
        LevelsToggle.IsCheckedChanged += (_, _) => Changed();
        MonochromeToggle.IsCheckedChanged += (_, _) => Changed();
        RemoveBasicColorButton.Click += (_, _) => RemoveEffect(BasicColorEffect.Id);
        RemoveExposureButton.Click += (_, _) => RemoveEffect(ExposureEffect.Id);
        RemoveLevelsButton.Click += (_, _) => RemoveEffect(LevelsEffect.Id);
        RemoveMonochromeButton.Click += (_, _) => RemoveEffect(MonochromeEffect.Id);
        foreach (var slider in new[] { BrightnessSlider, ContrastSlider, SaturationSlider })
            slider.ValueChanged += (_, _) => { if (!_syncing) BasicColorToggle.IsChecked = true; };
        foreach (var slider in new[] { ExposureStopsSlider, ExposureBlackSlider })
            slider.ValueChanged += (_, _) => { if (!_syncing) ExposureToggle.IsChecked = true; };
        foreach (var slider in new[] { LevelsBlackSlider, LevelsWhiteSlider, LevelsGammaSlider })
            slider.ValueChanged += (_, _) => { if (!_syncing) LevelsToggle.IsChecked = true; };
        FramingBox.SelectionChanged += (_, _) => Changed();
        AttachedToVisualTree += (_, _) => Attach();
        DetachedFromVisualTree += (_, _) => { CommitSamRefinement(); CommitPending(); ChromaColorPicker.Cancel(); };
        UpdateLabels();
        UpdateSection();
    }

    private void SetSection(string section)
    {
        if (_sectionTabReorder?.SuppressClick == true) return;
        CommitPending(); CommitTextEdits();
        _section = section;
        InspectorScroll.Offset = new Avalonia.Vector(0, 0);
        UpdateSection();
    }

    private static string InspectorTabOrderPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Prometheus", "inspector-tabs.json");

    private void InitializeSectionTabs()
    {
        var tabs = new Dictionary<string, Button> { ["basics"] = BasicsTab,
            ["visual"] = VisualTab, ["audio"] = AudioTab };
        string[] saved;
        try { saved = JsonSerializer.Deserialize<string[]>(File.ReadAllText(InspectorTabOrderPath)) ?? []; }
        catch { saved = []; }
        var order = saved.Where(tabs.ContainsKey).Distinct().Concat(tabs.Keys.Where(k => !saved.Contains(k))).ToArray();
        foreach (var id in order)
        {
            SectionTabBar.Children.Remove(tabs[id]);
            SectionTabBar.Children.Add(tabs[id]);
        }
        _sectionTabReorder = new TabReorderGesture(SectionTabBar,
            SectionTabBar.Children.OfType<Button>(), 1, (oldIndex, newIndex) =>
            {
                var button = (Button)SectionTabBar.Children[oldIndex];
                SectionTabBar.Children.RemoveAt(oldIndex);
                SectionTabBar.Children.Insert(newIndex, button);
                _sectionTabReorder?.UpdateOrder(SectionTabBar.Children.OfType<Button>());
                var newOrder = SectionTabBar.Children.OfType<Button>().Skip(1)
                    .Select(b => b == BasicsTab ? "basics" : b == VisualTab ? "visual" : "audio").ToArray();
                Directory.CreateDirectory(Path.GetDirectoryName(InspectorTabOrderPath)!);
                File.WriteAllText(InspectorTabOrderPath, JsonSerializer.Serialize(newOrder));
            });
    }

    private void UpdateSection()
    {
        foreach (var (button, key) in new[] { (AllTab, "all"), (BasicsTab, "basics"),
                     (VisualTab, "visual"), (AudioTab, "audio") })
        {
            var selected = _section == key;
            button.Background = new SolidColorBrush(Color.Parse(selected ? "#FF8A00" : "#171717"));
            button.BorderBrush = new SolidColorBrush(Color.Parse(selected ? "#FF8A00" : "#5A5A5A"));
            button.Foreground = selected ? Brushes.Black : Brushes.LightGray;
        }
        SummaryCapsule.IsVisible = _selectedId != null;
        var showVisualBasics = _selectedIsVideo && _section is "all" or "basics";
        var showVisual = _selectedIsVideo && _section is "all" or "visual";
        foreach (var capsule in new[] { FramingCapsule, PositionCapsule, AnchorCapsule,
                     ScaleCapsule, CompositionCapsule })
            capsule.IsVisible = showVisualBasics;
        var clip = _controller == null ? null : Locate(_controller.CurrentProject, _selectedId)?.Clip;
        TextCapsule.IsVisible = showVisual && clip != null &&
            _controller?.CurrentProject.Assets.FirstOrDefault(asset => asset.Id == clip.AssetId)?.TextOverlay != null;
        SubtitleAnimationCapsule.IsVisible = (showVisual || showVisualBasics) && clip != null &&
            _controller?.CurrentProject.ActiveSequence?.Tracks.Any(t => t.Id == clip.TrackId && t.Role == "subtitle") == true;
        MgtParametersCapsule.IsVisible = showVisualBasics && clip != null &&
            _controller?.CurrentProject.Assets.FirstOrDefault(asset => asset.Id == clip.AssetId)?.Source.Kind == "mgt";
        MaskTargetCapsule.IsVisible = showVisual && clip is { } masked &&
            ClipMaskStack.Read(masked).Count > 0;
        BasicColorCapsule.IsVisible = showVisual && clip?.Effects.Any(e => e.EffectId == BasicColorEffect.Id) == true;
        ExposureCapsule.IsVisible = showVisual && clip?.Effects.Any(e => e.EffectId == ExposureEffect.Id) == true;
        LevelsCapsule.IsVisible = showVisual && clip?.Effects.Any(e => e.EffectId == LevelsEffect.Id) == true;
        MonochromeCapsule.IsVisible = showVisual && clip?.Effects.Any(e => e.EffectId == MonochromeEffect.Id) == true;
        AppliedEffectsHint.IsVisible = false;
        VisualEffectChain.IsVisible = showVisual;
        MotionEffectsHost.IsVisible = showVisual;
        ColorEffectsHost.IsVisible = showVisual;
        ImageEffectsHost.IsVisible = showVisual;
        AudioCapsule.IsVisible = _selectedHasAudio && _section is "all" or "basics" or "audio";
        AudioEffectsHost.IsVisible = _selectedHasAudio && _section is "all" or "audio";
        AudioEffectChain.IsVisible = _selectedHasAudio && _section is "all" or "audio";
        EmptySectionText.IsVisible = _selectedId == null ||
            _section == "basics" && !_selectedIsVideo && !_selectedHasAudio ||
            _section == "visual" && !_selectedIsVideo || _section == "audio" && !_selectedHasAudio;
        EmptySectionText.Text = _selectedId == null ? "Selecciona un medio en la línea de tiempo" :
            _section == "visual" ? "Este medio no tiene imagen" :
            _section == "audio" ? "Este medio no tiene audio" : "Este medio no tiene parámetros básicos";
    }

    private void ShowQuickAdd(string kind, Button anchor, string? afterEffectId)
    {
        var search = new TextBox { Watermark = "Buscar efecto…", Width = 235 };
        var results = new StackPanel { Spacing = 2 };
        var list = new ScrollViewer { MaxHeight = 280, Content = results };
        var content = new StackPanel { Spacing = 7, Children = { search, list } };
        var flyout = new Flyout { Content = new Border { Background = Brushes.Black,
            BorderBrush = new SolidColorBrush(Color.Parse("#FF8A00")), BorderThickness = new Avalonia.Thickness(1),
            Padding = new Avalonia.Thickness(9), Child = content } };
        void Refresh()
        {
            results.Children.Clear();
            var query = search.Text?.Trim();
            foreach (var effect in EffectCatalog.Available.Where(effect => effect.Kind == kind &&
                (string.IsNullOrEmpty(query) || effect.Name.Contains(query, StringComparison.OrdinalIgnoreCase))))
            {
                var button = new Button { Content = effect.Name, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                    Padding = new Avalonia.Thickness(7, 4), FontSize = 11 };
                button.Click += (_, _) =>
                {
                    if (_controller != null) EffectApplicationService.ApplyAt(_controller, _selectedId, effect.Id, afterEffectId);
                    RefreshSelection();
                    flyout.Hide();
                };
                results.Children.Add(button);
            }
            if (results.Children.Count == 0) results.Children.Add(new TextBlock { Text = "Sin resultados", FontSize = 11 });
        }
        search.TextChanged += (_, _) => Refresh();
        Refresh();
        flyout.ShowAt(anchor);
        search.Focus();
    }

    private void InspectorDragOver(object? sender, DragEventArgs e)
    {
        var ids = EffectApplicationService.ReadDragIds(e.Data);
        if (ids.Length == 0) return;
        e.DragEffects = _controller != null && EffectApplicationService.CanApply(_controller.CurrentProject, _selectedId, ids)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void InspectorDrop(object? sender, DragEventArgs e)
    {
        var ids = EffectApplicationService.ReadDragIds(e.Data);
        if (ids.Length == 0) return;
        if (_controller != null) EffectApplicationService.Apply(_controller, _selectedId, ids);
        e.Handled = true;
    }

    public const double MinimumPanelWidth = 270;

    private void ConfigureCapsules()
    {
        foreach (var capsule in new[] { FramingCapsule, PositionCapsule, AnchorCapsule, ScaleCapsule,
                     CompositionCapsule, BasicColorCapsule, ExposureCapsule, LevelsCapsule,
                     MonochromeCapsule, AudioCapsule, TextCapsule, MaskTargetCapsule, MgtParametersCapsule, SubtitleAnimationCapsule })
            ConfigureCard(capsule, capsule.Name ?? "basic");
    }

    private Border CreateEffectCard(StackPanel body, string key)
    {
        var card = new Border { Classes = { "capsule", "inspector-card" }, Child = body };
        ConfigureCard(card, key);
        _effectCards[key] = card;
        return card;
    }

    private void ArrangeEffectChains(Clip? visualClip, Clip? audioClip)
    {
        UpdateEffectChain(VisualEffectChain, visualClip, "video", ref _visualChainSignature);
        UpdateEffectChain(AudioEffectChain, audioClip, "audio", ref _audioChainSignature);
    }

    private void UpdateEffectChain(StackPanel host, Clip? clip, string kind, ref string signature)
    {
        var firstId = EffectCatalog.Available.FirstOrDefault(effect => effect.Kind == kind)?.Id;
        var canAdd = _controller != null && _selectedId != null && firstId != null &&
            EffectApplicationService.CanApply(_controller.CurrentProject, _selectedId, [firstId]);
        var next = (_controller?.CurrentProject.Id ?? "") + ":" + (_selectedId ?? "") + ":" +
            (_selectedNestedRootId ?? "") + ":" + (clip?.Id ?? "") + ":" + canAdd + ":" +
            string.Join("|", clip?.Effects.Where(effect => EffectCatalog.Available.Any(entry =>
                entry.Id == effect.EffectId && entry.Kind == kind))
                .Select(effect => effect.Id + ":" + effect.EffectId) ?? []);
        if (signature == next) return;
        signature = next;
        host.Children.Clear();
        if (canAdd) FillEffectChain(host, clip, kind);
    }

    private void FillEffectChain(StackPanel host, Clip? clip, string kind)
    {
        if (_controller == null || _selectedId == null || clip == null) return;
        host.Children.Add(CreateInsertionButton(kind, null, true));
        string? lastEffectId = null;
        foreach (var effect in clip.Effects.Where(effect => EffectCatalog.Available.Any(entry =>
                     entry.Id == effect.EffectId && entry.Kind == kind)))
        {
            var card = effect.EffectId switch
            {
                BasicColorEffect.Id => BasicColorCapsule,
                ExposureEffect.Id => ExposureCapsule,
                LevelsEffect.Id => LevelsCapsule,
                MonochromeEffect.Id => MonochromeCapsule,
                _ => _effectCards.GetValueOrDefault(effect.Id)
            };
            if (card == null) continue;
            if (card.Parent is Panel previous) previous.Children.Remove(card);
            host.Children.Add(card);
            lastEffectId = effect.Id;
        }
        if (lastEffectId != null) host.Children.Add(CreateInsertionButton(kind, lastEffectId, false));
    }

    private Button CreateInsertionButton(string kind, string? afterEffectId, bool first)
    {
        var button = new Button { Content = first ? "+  Añadir efecto" : "+",
            FontSize = 10, Height = 23, Padding = new Avalonia.Thickness(7, 2),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.Parse("#242424")),
            Foreground = new SolidColorBrush(Color.Parse("#FF8A00")),
            BorderBrush = new SolidColorBrush(Color.Parse("#3A3A3A")),
            BorderThickness = new Avalonia.Thickness(1) };
        ToolTip.SetTip(button, "Añadir efecto de " + (kind == "video" ? "vídeo" : "audio") +
            (afterEffectId == null ? "" : " después de este"));
        button.Click += (_, _) => ShowQuickAdd(kind, button, afterEffectId);
        return button;
    }

    private void ConfigureCard(Border capsule, string key)
    {
            if (capsule.Child is not StackPanel body || body.Children.Count == 0) return;
            var heading = body.Children[0];
            ArrangeEffectHeading(heading);
            body.Children.RemoveAt(0);
            var arrow = new Button { Content = "⌃", Width = 23, Height = 21, Padding = new Avalonia.Thickness(0) };
            ToolTip.SetTip(arrow, "Replegar controles");
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            Grid.SetColumn(heading, 0);
            Grid.SetColumn(arrow, 1);
            header.Children.Add(heading);
            header.Children.Add(arrow);
            var card = new StackPanel { Spacing = body.Spacing };
            capsule.Child = null;
            card.Children.Add(header);
            card.Children.Add(body);
            capsule.Child = card;
            var collapsed = false;
            void SetCollapsed(bool value)
            {
                collapsed = value;
                if (value) _collapsedCardKeys.Add(key);
                else _collapsedCardKeys.Remove(key);
                body.IsVisible = !value;
                arrow.Content = value ? "⌄" : "⌃";
                ToolTip.SetTip(arrow, value ? "Desplegar controles" : "Replegar controles");
            }
            arrow.Click += (_, _) => SetCollapsed(!collapsed);
            capsule.PointerPressed += (_, e) =>
            {
                if (!collapsed || !e.GetCurrentPoint(capsule).Properties.IsLeftButtonPressed) return;
                for (var source = e.Source as Control; source != null && source != capsule;
                     source = source.Parent as Control)
                    if (source is Button or ToggleButton) return;
                SetCollapsed(false);
                e.Handled = true;
            };
            if (_collapsedCardKeys.Contains(key)) SetCollapsed(true);
    }

    private static void ArrangeEffectHeading(Control heading)
    {
        if (heading is not Grid grid) return;
        var children = grid.Children.ToArray();
        var remove = children.OfType<Button>().FirstOrDefault(button => Equals(button.Content, "×"));
        if (remove == null) return;
        var title = children.OfType<TextBlock>().FirstOrDefault();
        if (title == null) return;
        var reset = children.OfType<Button>().FirstOrDefault(button => Equals(button.Content, "↺"));
        var toggle = children.OfType<ToggleButton>().FirstOrDefault();
        var others = children.Where(child => child != remove && child != title && child != reset && child != toggle);
        var ordered = new System.Collections.Generic.List<Control> { remove, title };
        ordered.AddRange(others);
        if (reset != null) ordered.Add(reset);
        if (toggle != null) ordered.Add(toggle);
        grid.Children.Clear();
        grid.ColumnDefinitions.Clear();
        for (var index = 0; index < ordered.Count; index++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(index == 1 ? GridLength.Star : GridLength.Auto));
            Grid.SetColumn(ordered[index], index);
            grid.Children.Add(ordered[index]);
        }
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.MinWidth = 0;
        remove.Foreground = Brushes.IndianRed;
        remove.Margin = new Avalonia.Thickness(0, 0, 5, 0);
        if (toggle != null) toggle.MinWidth = 58;
    }

    private static void ResetOnThumbDoubleClick(Slider slider, double value)
    {
        var reset = new MenuItem { Header = "Restablecer este parámetro aquí" };
        reset.Click += (_, _) => slider.Value = value;
        slider.ContextMenu = new ContextMenu { ItemsSource = new[] { reset } };
        slider.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.ClickCount != 2) return;
            for (var source = e.Source as Control; source != null && source != slider;
                 source = source.Parent as Control)
            {
                if (source is not Thumb) continue;
                slider.Value = value;
                e.Handled = true;
                return;
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void Attach()
    {
        var controller = ProjectController.Instance;
        if (controller == null || ReferenceEquals(controller, _controller)) return;
        if (_controller != null)
        {
            _controller.ClipSelectionChanged -= RefreshSelection;
            _controller.BeforeHistoryNavigation -= FinishPropertyGesture;
            _controller.StateChanged -= RefreshSelection;
            _controller.MaskSelectionChanged -= OnMaskSelectionChanged;
            _controller.Transport.PositionChanged -= OnKeyframeTimeChanged;
        }
        _controller = controller;
        controller.ClipSelectionChanged += RefreshSelection;
        controller.BeforeHistoryNavigation += FinishPropertyGesture;
        controller.StateChanged += RefreshSelection;
        controller.MaskSelectionChanged += OnMaskSelectionChanged;
        controller.Transport.PositionChanged += OnKeyframeTimeChanged;
        RefreshSelection();
    }

    private void OnMaskSelectionChanged()
    {
        if (!_syncing) RefreshSelection();
    }

    private ClipLocation? Locate(Project project, string? clipId) =>
        ProjectClipLookup.Find(project, clipId, _controller?.Transport.PositionSeconds ?? 0,
            _selectedNestedRootId);

    private double SelectedLocalTime => _controller == null ? 0 :
        Locate(_controller.CurrentProject, _selectedId)?.LocalTime ?? _controller.Transport.PositionSeconds;

    private void MatchScale(Slider other, double value)
    {
        if (_syncing || _linkingScale || AspectLockToggle.IsChecked != true) return;
        _linkingScale = true;
        other.Value = ReferenceEquals(other, ScaleYSlider)
            ? Math.Clamp(value * _scaleLinkRatio, other.Minimum, other.Maximum)
            : Math.Clamp(value / Math.Max(0.01, _scaleLinkRatio), other.Minimum, other.Maximum);
        _linkingScale = false;
    }

    private static double DisplayAngle(double value)
    {
        var angle = value % 360;
        return angle > 180 ? angle - 360 : angle < -180 ? angle + 360 : angle;
    }

    private void CommitRotationInput()
    {
        var text = RotationText.Text?.Trim().TrimEnd('°').Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            if (double.IsFinite(value))
            {
                _rotationValue = Math.Clamp(value, -1_000_000, 1_000_000);
                _updatingRotationSlider = true;
                RotationSlider.Value = DisplayAngle(_rotationValue);
                _updatingRotationSlider = false;
                _dirtyVisualTargets.Add("visual.rotation");
                Changed();
            }
        }
        RotationText.Text = _rotationValue.ToString("0.##", CultureInfo.CurrentCulture);
    }

    private void Changed()
    {
        UpdateLabels();
        UpdateEffectToggleLabels();
        if (_syncing || _selectedId == null) return;
        _pending = true;
        _commitTimer.Stop();
        _commitTimer.Start();
    }

    private void UpdateEffectToggleLabels()
    {
        BasicColorToggle.Content = BasicColorToggle.IsChecked == true ? "Activo" : "Desactivado";
        ExposureToggle.Content = ExposureToggle.IsChecked == true ? "Activo" : "Desactivado";
        LevelsToggle.Content = LevelsToggle.IsChecked == true ? "Activo" : "Desactivado";
        MonochromeToggle.Content = MonochromeToggle.IsChecked == true ? "Activo" : "Desactivado";
    }

    private void RemoveEffect(string effectId)
    {
        CommitPending();
        CommitMotionEdits();
        CommitColorEdits();
        CommitImageEdits();
        CommitAudioEdits();
        if (_controller == null || _selectedId == null) return;
        var clipId = _selectedId;
        var current = Locate(_controller.CurrentProject, clipId)?.Clip;
        if (current?.Effects.Any(e => e.EffectId == effectId) != true) return;
        _controller.UndoRedo.Commit(project =>
        {
            var clip = Locate(project, clipId)?.Clip;
            if (clip == null) return;
            foreach (var effect in clip.Effects.Where(e => e.EffectId == effectId))
                KeyframeAnimation.RemoveForEffect(clip, effect.Id);
            if (clip.Effects.Any(effect => effect.EffectId == effectId &&
                clip.SamMaskTarget == SamMaskTarget.ForEffect(effect)))
                clip.SamMaskTarget = SamMaskTarget.Opacity;
            clip.Effects.RemoveAll(e => e.EffectId == effectId);
        }, "Quitar efecto");
        RefreshSelection();
    }

    private void ResetEffectByType(string effectId)
    {
        if (_controller == null || _selectedId == null) return;
        var instanceId = Locate(_controller.CurrentProject, _selectedId)?.Clip?.Effects
            .FirstOrDefault(effect => effect.EffectId == effectId)?.Id;
        if (instanceId != null) ResetEffectInstance(instanceId);
    }

    private void ResetEffectInstance(string instanceId)
    {
        CommitPending(); CommitMotionEdits(); CommitColorEdits(); CommitImageEdits(); CommitAudioEdits();
        if (_controller == null || _selectedId == null) return;
        var clipId = Locate(_controller.CurrentProject, _selectedId)?.Clip?.Effects
            .Any(effect => effect.Id == instanceId) == true ? _selectedId : _audioClipId;
        if (clipId == null) return;
        _controller.UndoRedo.Commit(project =>
        {
            var clip = Locate(project, clipId)?.Clip;
            if (clip != null) EffectApplicationService.ResetToDefaults(clip, instanceId);
        }, "Restablecer efecto completo");
        RefreshSelection();
    }

    private void UpdateLabels()
    {
        PosXText.Text = $"{PosXSlider.Value:F2}";
        PosYText.Text = $"{PosYSlider.Value:F2}";
        if (!RotationText.IsFocused)
            RotationText.Text = _rotationValue.ToString("0.##", CultureInfo.CurrentCulture);
        AnchorXText.Text = $"{AnchorXSlider.Value * 100:F0}%";
        AnchorYText.Text = $"{AnchorYSlider.Value * 100:F0}%";
        ScaleXText.Text = $"{ScaleXSlider.Value * 100:F0}%";
        ScaleYText.Text = $"{ScaleYSlider.Value * 100:F0}%";
        OpacityText.Text = $"{OpacitySlider.Value * 100:F0}%";
        GainText.Text = $"{GainSlider.Value:F1} dB";
        BalanceText.Text = BalanceSlider.Value switch { < -0.05 => $"L {-BalanceSlider.Value:F1}", > 0.05 => $"R {BalanceSlider.Value:F1}", _ => "C" };
        BrightnessText.Text = $"{BrightnessSlider.Value:F2}";
        ContrastText.Text = $"{ContrastSlider.Value:F2}";
        SaturationText.Text = $"{SaturationSlider.Value:F2}";
        ExposureStopsText.Text = $"{ExposureStopsSlider.Value:F2}";
        ExposureBlackText.Text = $"{ExposureBlackSlider.Value:F2}";
        LevelsBlackText.Text = $"{LevelsBlackSlider.Value:F2}";
        LevelsWhiteText.Text = $"{LevelsWhiteSlider.Value:F2}";
        LevelsGammaText.Text = $"{LevelsGammaSlider.Value:F2}";
    }

    private void CommitPending()
    {
        _commitTimer.Stop();
        if (!_pending || _controller == null || _selectedId == null) return;
        _pending = false;
        var dirtyVisualTargets = _dirtyVisualTargets.ToArray();
        _dirtyVisualTargets.Clear();
        var dirtyEffectTargets = _dirtyEffectTargets.ToArray();
        var editTime = SelectedLocalTime;
        var id = _selectedId;
        var state = new ClipVisualState { Framing = Framing[Math.Clamp(FramingBox.SelectedIndex, 0, Framing.Length - 1)],
            PositionX = PosXSlider.Value, PositionY = PosYSlider.Value, ScaleX = ScaleXSlider.Value,
            ScaleY = ScaleYSlider.Value, ScaleLinked = AspectLockToggle.IsChecked == true,
            AnchorX = AnchorXSlider.Value, AnchorY = AnchorYSlider.Value,
            RotationDegrees = _rotationValue, Opacity = OpacitySlider.Value,
            BlendMode = ClipBlendModes.All[Math.Clamp(ClipBlendModeBox.SelectedIndex, 0, ClipBlendModes.All.Count - 1)].Id };
        var gain = GainSlider.Value;
        var balance = BalanceSlider.Value;
        var colorEnabled = BasicColorToggle.IsChecked == true;
        var color = new BasicColorSettings(BrightnessSlider.Value, ContrastSlider.Value, SaturationSlider.Value);
        var exposureEnabled = ExposureToggle.IsChecked == true;
        var exposure = new ExposureSettings(ExposureStopsSlider.Value, ExposureBlackSlider.Value);
        var levelsEnabled = LevelsToggle.IsChecked == true;
        var levels = new LevelsSettings(LevelsBlackSlider.Value, LevelsWhiteSlider.Value, LevelsGammaSlider.Value);
        var monochromeEnabled = MonochromeToggle.IsChecked == true;
        try
        {
            _controller.UndoRedo.Commit(project =>
            {
                var sequence = project.ActiveSequence;
                var location = Locate(project, id);
                var clip = location?.Clip;
                if (clip == null || sequence == null || location == null) return;
                var audio = AudioTarget(location);
                if (audio != null) { audio.GainDb = gain; audio.Balance = balance; }
                if (location.Track.Kind == "video")
                {
                    state.FlipX = clip.Visual.FlipX;
                    state.FlipY = clip.Visual.FlipY;
                    foreach (var target in KeyframeAnimation.VisualTargets.Keys)
                    {
                        if (KeyframeAnimation.Find(clip, target) == null) continue;
                        if (dirtyVisualTargets.Contains(target))
                            KeyframeAnimation.Set(clip, target, editTime, BaseVisualValue(state, target));
                        SetBaseVisualValue(state, target, BaseVisualValue(clip.Visual, target));
                    }
                    if (Math.Abs(state.AnchorX - clip.Visual.AnchorX) > 0.000001 ||
                        Math.Abs(state.AnchorY - clip.Visual.AnchorY) > 0.000001)
                    {
                        var asset = project.Assets.FirstOrDefault(a => a.Id == clip.AssetId);
                        var stream = asset?.Streams.FirstOrDefault(s => s.Id == clip.StreamId) ??
                            asset?.Streams.FirstOrDefault(s => s.Kind is "video" or "image");
                        var sourceWidth = clip.NestedContent != null ? sequence.Width :
                            stream?.Width is > 0 ? stream.Width.Value : sequence.Width;
                        var sourceHeight = clip.NestedContent != null ? sequence.Height :
                            stream?.Height is > 0 ? stream.Height.Value : sequence.Height;
                        var rect = PreviewGeometry.PlaceLayer(sequence.Width, sequence.Height,
                            sourceWidth, sourceHeight, state);
                        var beforeX = state.FlipX ? 1 - clip.Visual.AnchorX : clip.Visual.AnchorX;
                        var afterX = state.FlipX ? 1 - state.AnchorX : state.AnchorX;
                        var beforeY = state.FlipY ? 1 - clip.Visual.AnchorY : clip.Visual.AnchorY;
                        var afterY = state.FlipY ? 1 - state.AnchorY : state.AnchorY;
                        var moved = PreviewGeometry.RotateDelta((afterX - beforeX) * rect.Width,
                            (afterY - beforeY) * rect.Height, state.RotationDegrees);
                        state.PositionX += moved.X / sequence.Width;
                        state.PositionY += moved.Y / sequence.Height;
                    }
                    clip.Visual = state;
                    var staticEffectIds = new[] { BasicColorEffect.Id, ExposureEffect.Id, LevelsEffect.Id };
                    var originalStaticValues = clip.KeyframeTracks.Where(track =>
                        staticEffectIds.Any(effectId => clip.Effects.Any(effect => effect.EffectId == effectId &&
                            track.Target.StartsWith("effect." + effect.Id + ".", StringComparison.Ordinal))))
                        .ToDictionary(track => track.Target, track => BaseTargetValue(clip, track.Target));
                    BasicColorEffect.Set(clip, colorEnabled, color);
                    ExposureEffect.Set(clip, exposureEnabled, exposure);
                    LevelsEffect.Set(clip, levelsEnabled, levels);
                    MonochromeEffect.Set(clip, monochromeEnabled);
                    foreach (var (target, originalValue) in originalStaticValues)
                    {
                        if (dirtyEffectTargets.Contains(target))
                            KeyframeAnimation.Set(clip, target, editTime, BaseTargetValue(clip, target));
                        SetBaseTargetValue(clip, target, originalValue);
                    }
                }
            }, "Ajustar controles del medio");
        }
        catch (Exception ex) { _controller.ReportStatus("Controles de medio: " + ex.Message); }
        foreach (var target in dirtyEffectTargets)
            if (new[] { BrightnessSlider, ContrastSlider, SaturationSlider, ExposureStopsSlider,
                ExposureBlackSlider, LevelsBlackSlider, LevelsWhiteSlider, LevelsGammaSlider }
                .Any(slider => _keyframeSliders.TryGetValue(target, out var registered) &&
                    registered == slider))
                _dirtyEffectTargets.Remove(target);
    }

    private void RefreshSelection()
    {
        if (_controller == null) return;
        if (_selectedId != _controller.SelectedClipId ||
            _selectedNestedRootId != _controller.SelectedNestedRootId)
        { FinishPropertyGesture(); }
        if (_pending) return;
        _selectedId = _controller.SelectedClipId;
        _selectedNestedRootId = _controller.SelectedNestedRootId;
        var sequence = _controller.CurrentProject.ActiveSequence;
        var location = Locate(_controller.CurrentProject, _selectedId);
        var clip = location?.Clip;
        var video = location?.Track.Kind == "video";
        var audio = location == null ? null : AudioTarget(location);
        _syncing = true;
        try
        {
            _selectedIsVideo = video;
            _selectedHasAudio = audio != null;
            _audioClipId = audio?.Id;
            SummaryName.Text = clip?.Name is { Length: > 0 } name ? name : "Medio seleccionado";
            var trackName = location?.Track.Name ?? "";
            SummaryDetails.Text = clip == null ? "" : $"{trackName} · Inicio {clip.TimelineStart.Seconds:0.00} s · Duración {clip.Duration.Seconds:0.00} s · Velocidad ×{clip.Speed.Seconds:0.##}";
            foreach (var control in new Control[] { FramingBox, PosXSlider, PosYSlider, RotationSlider, AnchorXSlider,
                         AnchorYSlider, ScaleXSlider, ScaleYSlider, RotationText,
                         AspectLockToggle, OpacitySlider, BasicColorToggle, BrightnessSlider, ContrastSlider, SaturationSlider,
                         ExposureToggle, ExposureStopsSlider, ExposureBlackSlider, LevelsToggle,
                         LevelsBlackSlider, LevelsWhiteSlider, LevelsGammaSlider, MonochromeToggle })
                control.IsEnabled = video;
            GainSlider.IsEnabled = BalanceSlider.IsEnabled = audio != null;
            var visual = clip?.Visual ?? new ClipVisualState();
            FramingBox.SelectedIndex = Math.Max(0, Array.IndexOf(Framing, visual.Framing));
            PosXSlider.Value = visual.PositionX;
            PosYSlider.Value = visual.PositionY;
            _rotationValue = visual.RotationDegrees;
            _updatingRotationSlider = true;
            RotationSlider.Value = DisplayAngle(_rotationValue);
            _updatingRotationSlider = false;
            AnchorXSlider.Value = visual.AnchorX;
            AnchorYSlider.Value = visual.AnchorY;
            AspectLockToggle.IsChecked = visual.ScaleLinked;
            ScaleXSlider.Value = visual.ScaleX;
            ScaleYSlider.Value = visual.ScaleY;
            _scaleLinkRatio = ScaleYSlider.Value / Math.Max(0.01, ScaleXSlider.Value);
            OpacitySlider.Value = visual.Opacity;
            ClipBlendModeBox.IsEnabled = video;
            ClipBlendModeBox.SelectedIndex = ClipBlendModes.GpuId(visual.BlendMode);
            GainSlider.Value = audio?.GainDb ?? 0;
            BalanceSlider.Value = audio?.Balance ?? 0;
            var effect = clip == null ? null : BasicColorEffect.Find(clip);
            var color = clip == null ? BasicColorSettings.Neutral : BasicColorEffect.ReadStored(clip);
            BasicColorToggle.IsChecked = effect?.Enabled == true;
            BrightnessSlider.Value = color.Brightness;
            ContrastSlider.Value = color.Contrast;
            SaturationSlider.Value = color.Saturation;
            var exposureEffect = clip == null ? null : ExposureEffect.Find(clip);
            var exposure = clip == null ? ExposureSettings.Neutral : ExposureEffect.ReadStored(clip);
            ExposureToggle.IsChecked = exposureEffect?.Enabled == true;
            ExposureStopsSlider.Value = exposure.Stops;
            ExposureBlackSlider.Value = exposure.Black;
            var levelsEffect = clip == null ? null : LevelsEffect.Find(clip);
            var levels = clip == null ? LevelsSettings.Neutral : LevelsEffect.ReadStored(clip);
            LevelsToggle.IsChecked = levelsEffect?.Enabled == true;
            LevelsBlackSlider.Value = levels.Black;
            LevelsWhiteSlider.Value = levels.White;
            LevelsGammaSlider.Value = levels.Gamma;
            MonochromeToggle.IsChecked = clip != null && MonochromeEffect.IsEnabled(clip);
            UpdateLabels();
            UpdateEffectToggleLabels();
            RefreshMotionEditors(clip);
            RefreshColorEditors(clip);
            RefreshImageEditors(clip);
            RefreshTextEditors(clip);
            RefreshMgtEditors(clip);
            RefreshMaskTargetChoices(clip);
            RefreshSamRefinement(clip);
            RefreshAudioEditors(audio);
            ArrangeEffectChains(video ? clip : null, audio);
            RefreshStaticEffectClocks(clip);
            RefreshKeyframeControls(clip);
        }
        finally { _syncing = false; }
        UpdateSection();
        RefreshTransitionControls();
        RefreshKeyframeValues(clip);
        if (_keyframeDrawerOpen) RefreshKeyframeRows();
    }

    private static Clip? AudioTarget(ClipLocation location)
    {
        var selected = location.Clip;
        bool IsAudio(Clip clip) => location.Tracks.Any(t => t.Id == clip.TrackId && t.Kind == "audio");
        if (IsAudio(selected)) return selected;
        var group = location.LinkGroups.FirstOrDefault(g => g.ClipIds.Contains(selected.Id));
        return group == null ? null : location.Siblings.FirstOrDefault(c => group.ClipIds.Contains(c.Id) && IsAudio(c));
    }
}
