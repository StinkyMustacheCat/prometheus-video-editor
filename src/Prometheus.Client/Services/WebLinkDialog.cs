using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Prometheus.Client.Services;

public static class WebLinkDialog
{
    public sealed record Request(string Url, WebDownloadQuality Quality, double SourceIn = 0, double? Duration = null);

    internal static ComboBox QualitySelector() => new()
    {
        Name = "WebQuality", ItemsSource = new[] { "Vista completa", "Vista ligera (hasta 720p)" },
        SelectedIndex = (int)WebDownloadSettings.Current.Quality,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    internal static Task<Request?> ForPasteAsync(Control owner, string url, double? knownDuration, double fps, WebDownloadQuality? preferredQuality = null) =>
        WebDownloadSettings.Current.PasteChooseRange
            ? AskAsync(owner, url, allowRange: true, chooseRange: true, knownDuration: knownDuration, fps: fps, initialQuality: preferredQuality)
            : Task.FromResult<Request?>(new Request(url, preferredQuality ?? WebDownloadSettings.Current.Quality));

    internal static bool TryRange(string? from, string? to, double? knownDuration, double fps,
        out double start, out double duration)
    {
        start = duration = 0;
        if (!TryTime(from, out start) || !TryTime(to, out var end) || start < 0 || end <= start) return false;
        duration = end - start;
        var frame = 1 / (double.IsFinite(fps) && fps > 0 ? fps : 60);
        return duration >= frame - .000001 && (knownDuration is not > 0 || end <= knownDuration.Value + .000001);
    }

    private static bool TryTime(string? text, out double seconds)
    {
        seconds = 0;
        text = text?.Trim().Replace(',', '.');
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)) return double.IsFinite(seconds);
        var parts = text?.Split(':');
        if (parts is not { Length: 2 or 3 } || parts.Any(p => !double.TryParse(p, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || value < 0)) return false;
        var numbers = parts.Select(p => double.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        if (numbers[^1] >= 60 || numbers.Length == 3 && numbers[1] >= 60) return false;
        seconds = numbers.Length == 2 ? numbers[0] * 60 + numbers[1] : numbers[0] * 3600 + numbers[1] * 60 + numbers[2];
        return double.IsFinite(seconds);
    }

    public static async Task<Request?> AskAsync(Control owner, string? initial = null, bool allowRange = false,
        bool chooseRange = false, double? knownDuration = null, double fps = 60, WebDownloadQuality? initialQuality = null)
    {
        if (TopLevel.GetTopLevel(owner) is not Window parent) return null;
        var input = new TextBox { Name = "WebUrl", Text = initial, Watermark = "https://…" };
        var accept = new Button { Content = "Importar", MinWidth = 85 };
        var cancel = new Button { Content = "Cancelar", MinWidth = 85 };
        var quality = QualitySelector();
        if (initialQuality.HasValue) quality.SelectedIndex = (int)initialQuality.Value;
        var mode = new ComboBox { Name = "WebPlacement", ItemsSource = new[] { "Medio completo", "Elegir tramo" },
            SelectedIndex = chooseRange ? 1 : 0, IsVisible = allowRange, HorizontalAlignment = HorizontalAlignment.Stretch };
        var from = new TextBox { Name = "WebFrom", Text = "0", Watermark = "Inicio" };
        var to = new TextBox { Name = "WebTo", Text = knownDuration?.ToString("0.###", CultureInfo.InvariantCulture), Watermark = "Final" };
        var range = new StackPanel { Spacing = 5, IsVisible = allowRange && chooseRange, Children = {
            new TextBlock { Text = "Inicio y final en segundos o HH:MM:SS" }, from, to,
            new TextBlock { Text = "Se inserta solo el tramo. La fuente completa se conserva para poder ampliar o recortar después.",
                FontSize = 11, Opacity = .7, TextWrapping = TextWrapping.Wrap } } };
        mode.SelectionChanged += (_, _) => range.IsVisible = allowRange && mode.SelectedIndex == 1;
        var error = new TextBlock { Name = "WebRangeError", Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
        var body = new StackPanel { Margin = new Thickness(16), Spacing = 9, Children = {
            input, new TextBlock { Text = "Calidad de descarga" }, quality, mode, range, error,
            new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8, Children = { cancel, accept } } } };
        var dialog = new Window { Title = "Importar enlace", Width = 520, SizeToContent = SizeToContent.Height, Content = body, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        accept.Click += (_, _) => {
            if (!WebMediaService.TryUrl(input.Text, out var uri)) { error.Text = "Indica un enlace HTTP o HTTPS válido."; return; }
            double start = 0; double? duration = null;
            if (allowRange && mode.SelectedIndex == 1)
            {
                if (!TryRange(from.Text, to.Text, knownDuration, fps, out start, out var length))
                { error.Text = "Indica un inicio y final válidos dentro del medio, con al menos un fotograma de duración."; return; }
                duration = length;
            }
            dialog.Close(new Request(uri.ToString(), (WebDownloadQuality)quality.SelectedIndex, start, duration));
        };
        cancel.Click += (_, _) => dialog.Close(null);
        return await dialog.ShowDialog<Request?>(parent);
    }
}
