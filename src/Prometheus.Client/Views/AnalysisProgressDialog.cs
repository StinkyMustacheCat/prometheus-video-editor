using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Prometheus.Client.Services;
using Prometheus.Core.Model;

namespace Prometheus.Client.Views;

internal sealed class AnalysisProgressDialog : Window
{
    private readonly HeavyAnalysisWork.Session _work;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, Height = 8, IsIndeterminate = true };
    private readonly Button _cancel = new() { Content = "Cancelar", Name = "CancelAnalysisButton", MinWidth = 115 };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private bool _finished;

    internal AnalysisProgressDialog(string title, Clip clip, HeavyAnalysisWork.Session work)
    {
        _work = work;
        Title = title + " · Prometheus";
        Width = 480;
        Focusable = true;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var body = new StackPanel { Spacing = 12, Margin = new Thickness(20) };
        body.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.SemiBold });
        body.Children.Add(new TextBlock { Text = $"{clip.Name} · {TimeSpan.FromSeconds(clip.Duration.Seconds):g}",
            TextTrimming = TextTrimming.CharacterEllipsis, Opacity = .8 });
        body.Children.Add(_status);
        body.Children.Add(_progress);
        _cancel.HorizontalAlignment = HorizontalAlignment.Right;
        _cancel.Click += (_, _) => work.Cancel();
        body.Children.Add(_cancel);
        Content = body;

        Closing += (_, args) =>
        {
            if (_finished) return;
            args.Cancel = true;
            work.Cancel();
        };
        _timer.Tick += (_, _) => RefreshProgress();
        Opened += (_, _) =>
        {
            Focus();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
        RefreshProgress();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _work.Cancel();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    internal void RefreshProgress()
    {
        var state = _work.Progress;
        var cancelling = _work.IsCancelling;
        _status.Text = cancelling ? "Cancelando y liberando recursos…" : state.Message;
        _progress.IsIndeterminate = cancelling || !state.Fraction.HasValue;
        _progress.Value = Math.Clamp(state.Fraction ?? 0, 0, 1);
        _cancel.IsEnabled = !cancelling;
    }

    internal void Finish()
    {
        _finished = true;
        _timer.Stop();
        Close();
    }
}
