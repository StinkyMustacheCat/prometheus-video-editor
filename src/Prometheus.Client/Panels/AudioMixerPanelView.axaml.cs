using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Prometheus.Client.Services;
using Prometheus.Core.Model;

namespace Prometheus.Client.Panels;

public partial class AudioMixerPanelView : UserControl
{
    private sealed record MeterView(Border Left, Border Right, TextBlock Label);
    private readonly Dictionary<string, MeterView> _meters = new();
    private readonly DispatcherTimer _meterTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private ProjectController? _controller;

    public AudioMixerPanelView()
    {
        InitializeComponent();
        _meterTimer.Tick += (_, _) => RefreshMeters();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    private void Attach()
    {
        var controller = ProjectController.Instance;
        if (controller == null || ReferenceEquals(_controller, controller)) return;
        Detach();
        _controller = controller;
        controller.StateChanged += RefreshChannels;
        RefreshChannels();
        _meterTimer.Start();
    }

    private void Detach()
    {
        _meterTimer.Stop();
        if (_controller != null) _controller.StateChanged -= RefreshChannels;
        _controller = null;
    }

    private void RefreshChannels()
    {
        var project = _controller?.CurrentProject;
        var sequence = project?.ActiveSequence;
        ChannelPanel.Children.Clear();
        _meters.Clear();
        if (sequence == null) return;
        foreach (var track in sequence.Tracks.Where(t => t.Kind == "audio").OrderBy(t => t.Order))
            ChannelPanel.Children.Add(BuildChannel(track, false, sequence.MasterGainDb));
        ChannelPanel.Children.Add(BuildChannel(null, true, sequence.MasterGainDb));
        AudioLevelCache.Warm(project!);
        RefreshMeters();
    }

    private Control BuildChannel(Track? track, bool master, double masterGain)
    {
        var id = master ? "master" : track!.Id;
        var name = master ? "MASTER" : track!.Name;
        var gain = new Slider { Orientation = Orientation.Vertical, Minimum = -60, Maximum = 12,
            Value = master ? masterGain : track!.GainDb, Height = 105, Width = 25 };
        var gainLabel = new TextBlock { Text = $"{gain.Value:0.0} dB", FontSize = 10,
            Foreground = Brushes.Orange, HorizontalAlignment = HorizontalAlignment.Center };
        gain.ValueChanged += (_, _) => gainLabel.Text = $"{gain.Value:0.0} dB";
        void SaveGain() => CommitGain(id, gain.Value);
        gain.PointerReleased += (_, _) => SaveGain();
        gain.LostFocus += (_, _) => SaveGain();

        var meterLeft = MeterBar();
        var meterRight = MeterBar();
        var meterLabel = new TextBlock { Text = "−∞ dB", FontSize = 9, Foreground = Brushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Center };
        _meters[id] = new MeterView(meterLeft, meterRight, meterLabel);
        var meterRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Bottom };
        meterRow.Children.Add(MeterHousing(meterLeft));
        meterRow.Children.Add(MeterHousing(meterRight));
        var center = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7,
            HorizontalAlignment = HorizontalAlignment.Center };
        center.Children.Add(gain);
        center.Children.Add(meterRow);

        var body = new StackPanel { Spacing = 5 };
        body.Children.Add(new TextBlock { Text = name, FontSize = 10, FontWeight = FontWeight.SemiBold,
            Foreground = master ? Brushes.Orange : Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
        body.Children.Add(center);
        body.Children.Add(gainLabel);
        body.Children.Add(meterLabel);

        if (!master)
        {
            var balance = new Slider { Minimum = -1, Maximum = 1, Value = track!.Balance, Width = 88 };
            balance.PointerReleased += (_, _) => CommitBalance(id, balance.Value);
            balance.LostFocus += (_, _) => CommitBalance(id, balance.Value);
            body.Children.Add(new TextBlock { Text = "Balance L · R", FontSize = 9,
                Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center });
            body.Children.Add(balance);
            var mute = new Button { Content = track.Enabled ? "M" : "M ●", Width = 35, Height = 24 };
            var solo = new Button { Content = track.Solo ? "S ●" : "S", Width = 35, Height = 24 };
            ToolTip.SetTip(mute, "Silenciar pista");
            ToolTip.SetTip(solo, "Escuchar solo esta pista");
            mute.Click += (_, _) => CommitTrack(id, t => t.Enabled = !t.Enabled, "Silenciar pista de audio");
            solo.Click += (_, _) => CommitTrack(id, t => t.Solo = !t.Solo, "Solo de pista de audio");
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5,
                HorizontalAlignment = HorizontalAlignment.Center };
            buttons.Children.Add(mute);
            buttons.Children.Add(solo);
            body.Children.Add(buttons);
        }

        return new Border { Width = 118, Padding = new Thickness(7), CornerRadius = new CornerRadius(4),
            BorderBrush = master ? Brushes.DarkOrange : Brushes.DimGray, BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.Parse("#161616")), Child = body };
    }

    private static Border MeterBar() => new() { Width = 10, Height = 0, Background = Brushes.LimeGreen,
        VerticalAlignment = VerticalAlignment.Bottom };

    private static Border MeterHousing(Border fill) => new() { Width = 12, Height = 105,
        Background = new SolidColorBrush(Color.Parse("#080808")), BorderBrush = Brushes.DimGray,
        BorderThickness = new Thickness(1), Child = fill };

    private void CommitGain(string id, double value)
    {
        var controller = _controller;
        if (controller == null) return;
        var sequence = controller.CurrentProject.ActiveSequence;
        if (sequence == null) return;
        if (id == "master")
        {
            if (Math.Abs(sequence.MasterGainDb - value) < 0.05) return;
            controller.UndoRedo.Commit(p => p.ActiveSequence!.MasterGainDb = value, "Ganancia master");
        }
        else
        {
            var track = sequence.Tracks.FirstOrDefault(t => t.Id == id);
            if (track == null || track.Locked || Math.Abs(track.GainDb - value) < 0.05) return;
            CommitTrack(id, t => t.GainDb = value, "Ganancia de pista de audio");
        }
    }

    private void CommitBalance(string id, double value)
    {
        var track = _controller?.CurrentProject.ActiveSequence?.Tracks.FirstOrDefault(t => t.Id == id);
        if (track == null || track.Locked || Math.Abs(track.Balance - value) < 0.01) return;
        CommitTrack(id, t => t.Balance = value, "Balance de pista de audio");
    }

    private void CommitTrack(string id, Action<Track> change, string description)
    {
        var controller = _controller;
        if (controller?.CurrentProject.ActiveSequence?.Tracks.FirstOrDefault(t => t.Id == id) is not { Locked: false }) return;
        controller.UndoRedo.Commit(p => change(p.ActiveSequence!.Tracks.First(t => t.Id == id)), description);
    }

    private void RefreshMeters()
    {
        var controller = _controller;
        var project = controller?.CurrentProject;
        var sequence = project?.ActiveSequence;
        if (controller == null || project == null || sequence == null) return;
        var time = controller.Transport.PositionSeconds;
        var levels = controller.Transport.IsPlaying ? AudioLevelCache.TrackLevels(project, time) : null;
        foreach (var track in sequence.Tracks.Where(t => t.Kind == "audio"))
            if (_meters.TryGetValue(track.Id, out var meter))
                SetMeter(meter, levels != null && levels.TryGetValue(track.Id, out var level) ? level : default);
        if (_meters.TryGetValue("master", out var master)) SetMeter(master, controller.Transport.MasterLevel);
    }

    private static void SetMeter(MeterView meter, StereoLevel level)
    {
        static double Height(double peak) => Math.Clamp((20 * Math.Log10(Math.Max(peak, 0.001)) + 60) / 60, 0, 1) * 101;
        meter.Left.Background = level.Left >= 1 ? Brushes.OrangeRed : Brushes.LimeGreen;
        meter.Right.Background = level.Right >= 1 ? Brushes.OrangeRed : Brushes.LimeGreen;
        meter.Left.Height = Height(level.Left);
        meter.Right.Height = Height(level.Right);
        var peak = Math.Max(level.Left, level.Right);
        meter.Label.Text = peak < 0.001 ? "−∞ dB" : $"{20 * Math.Log10(peak):0} dB";
    }
}
