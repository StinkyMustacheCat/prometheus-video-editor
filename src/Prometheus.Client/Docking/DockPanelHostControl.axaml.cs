using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Prometheus.Client.Panels;

namespace Prometheus.Client.Docking;

public partial class DockPanelHostControl : UserControl
{
    private Point _dragStartPoint;
    private bool _isDragging;
    private bool _headerPressActive;
    private DispatcherTimer? _pulseTimer;
    private int _pulseTicks;

    private static readonly IBrush NormalBorderBrush = new SolidColorBrush(Color.Parse("#1A1A1A"));
    private static readonly IBrush GlowBorderBrush = new SolidColorBrush(Color.Parse("#FFA126"));

    public string PanelId { get; private set; } = string.Empty;

    public event Action<string>? FloatRequested;
    public event Action<string>? MinimizeRequested;
    public event Action<string>? CloseRequested;
    public event Action<string, string>? ReplaceRequested;
    public event Action<string, PointerEventArgs>? DragStarted;
    public event Action<string, PointerEventArgs>? DragDelta;
    public event Action<string, PointerReleasedEventArgs>? DragCompleted;

    public DockPanelHostControl()
    {
        InitializeComponent();
    }

    public void Initialize(string panelId)
    {
        PanelId = panelId;
        if (panelId == "timeline") { MinWidth = 900; MinHeight = 270; }
        TitleTextBlock.Text = PanelRegistry.GetPanelTitle(panelId);

        var content = PanelRegistry.GetOrCreatePanelContent(panelId);
        ContentContainer.Child = content;
        if (content is TimelinePanelView timeline)
        {
            if (timeline.HeaderTabs.Parent is Decorator oldSlot) oldSlot.Child = null;
            SequenceHeaderSlot.Child = timeline.HeaderTabs;
            SequenceHeaderSlot.IsVisible = true;
        }

        // Acciones directas
        MinimizeButton.Click += (_, _) => MinimizeRequested?.Invoke(PanelId);
        CloseButton.Click += (_, _) => CloseRequested?.Invoke(PanelId);

        // Menú hamburguesa
        HamburgerButton.Click += (_, _) =>
        {
            if (HamburgerButton.ContextMenu != null)
            {
                HamburgerButton.ContextMenu.Open(HamburgerButton);
            }
        };

        MenuFloatOption.Click += (_, _) => FloatRequested?.Invoke(PanelId);
        MenuMinimizeOption.Click += (_, _) => MinimizeRequested?.Invoke(PanelId);
        MenuCloseOption.Click += (_, _) => CloseRequested?.Invoke(PanelId);
        MenuReplaceOption.ItemsSource = Prometheus.Common.Docking.FactoryPresets.StandardPanels
            .Where(panel => panel.Id != PanelId)
            .Select(panel =>
            {
                var option = new MenuItem { Header = panel.Title };
                option.Click += (_, _) => ReplaceRequested?.Invoke(PanelId, panel.Id);
                return option;
            }).ToArray();

        // Gestos de arrastre en cabecera
        HeaderBorder.PointerPressed += Header_PointerPressed;
        HeaderBorder.PointerMoved += Header_PointerMoved;
        HeaderBorder.PointerReleased += Header_PointerReleased;
    }

    public void TriggerGlowPulse()
    {
        _pulseTimer?.Stop();
        _pulseTicks = 0;
        HostOuterBorder.BorderBrush = GlowBorderBrush;
        HostOuterBorder.BorderThickness = new Thickness(2);

        _pulseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _pulseTimer.Tick += (_, _) =>
        {
            _pulseTicks++;
            if (_pulseTicks % 2 == 0)
            {
                HostOuterBorder.BorderBrush = GlowBorderBrush;
            }
            else
            {
                HostOuterBorder.BorderBrush = NormalBorderBrush;
            }

            if (_pulseTicks >= 6)
            {
                _pulseTimer.Stop();
                HostOuterBorder.BorderBrush = NormalBorderBrush;
                HostOuterBorder.BorderThickness = new Thickness(1);
            }
        };
        _pulseTimer.Start();
    }

    private void Header_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsSequenceControl(e.Source)) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _headerPressActive = true;
            _dragStartPoint = e.GetPosition(this);
            _isDragging = false;
            e.Pointer.Capture(HeaderBorder);
        }
    }

    private bool IsSequenceControl(object? source)
    {
        for (var control = source as Control; control != null; control = control.Parent as Control)
            if (control == SequenceHeaderSlot) return true;
        return false;
    }

    private void Header_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_headerPressActive) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var currentPoint = e.GetPosition(this);
            var delta = currentPoint - _dragStartPoint;

            if (!_isDragging && (Math.Abs(delta.X) > 4 || Math.Abs(delta.Y) > 4))
            {
                _isDragging = true;
                DragStarted?.Invoke(PanelId, e);
            }

            if (_isDragging)
            {
                DragDelta?.Invoke(PanelId, e);
            }
        }
    }

    private void Header_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_headerPressActive) return;
        _headerPressActive = false;
        e.Pointer.Capture(null);
        if (_isDragging)
        {
            _isDragging = false;
            DragCompleted?.Invoke(PanelId, e);
        }
    }
}
