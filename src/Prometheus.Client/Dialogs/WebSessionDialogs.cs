using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Prometheus.Client.Services;

namespace Prometheus.Client.Dialogs;

internal sealed class WebBrowserProfilePicker : StackPanel
{
    private readonly ComboBox _browser;
    private readonly ComboBox _profile;

    internal WebBrowserProfilePicker(string? currentBrowser = null, string? currentProfile = null)
    {
        Spacing = 5;
        var browsers = WebBrowserProfiles.Find(currentBrowser);
        _browser = new ComboBox { Name = "WebCookieBrowser", ItemsSource = browsers.Select(b => b.Label).ToArray(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Navegador con perfil disponible" };
        _profile = new ComboBox { Name = "WebCookieProfile", HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Perfil del navegador" };

        WebBrowserProfiles.Profile? PickedProfile(int index) => index == 0
            ? new("Perfil automático", "") : browsers[_browser.SelectedIndex].Profiles[index - 1];

        void UpdateProfiles(string? configured)
        {
            if (_browser.SelectedIndex < 0)
            {
                _profile.ItemsSource = Array.Empty<string>();
                return;
            }
            var options = browsers[_browser.SelectedIndex].Profiles;
            var names = options.Select(profile => profile.Name).ToList();
            if (!string.IsNullOrWhiteSpace(configured) &&
                !options.Any(profile => Matches(profile, configured)))
                names.Add("Perfil configurado");
            names.Insert(0, "Perfil automático");
            _profile.ItemsSource = names;
            var match = Array.FindIndex(options.ToArray(), profile => Matches(profile, configured));
            _profile.SelectedIndex = match >= 0 ? match + 1 :
                !string.IsNullOrWhiteSpace(configured) ? names.Count - 1 : 0;
        }

        string? custom = currentProfile;
        _browser.SelectionChanged += (_, _) => { custom = null; UpdateProfiles(null); };
        _browser.SelectedIndex = currentBrowser == null ? -1
            : Array.FindIndex(browsers.ToArray(), browser => browser.Id == currentBrowser);
        custom = currentProfile;
        UpdateProfiles(custom);

        Selection = () =>
        {
            if (_browser.SelectedIndex < 0 || _profile.SelectedIndex < 0) return null;
            var browser = browsers[_browser.SelectedIndex];
            var index = _profile.SelectedIndex;
            var profile = index > browser.Profiles.Count ? custom : PickedProfile(index)?.Directory;
            return new(browser.Id, string.IsNullOrWhiteSpace(profile) ? null : profile);
        };
        Children.Add(_browser);
        Children.Add(_profile);
        if (browsers.Count == 0)
            Children.Add(new TextBlock { Text = "Abre YouTube en un navegador compatible para crear un perfil.", TextWrapping = TextWrapping.Wrap });
    }

    internal Func<WebBrowserProfiles.BrowserSelection?> Selection { get; }
    private static bool Matches(WebBrowserProfiles.Profile profile, string? selected) =>
        profile.Directory.Equals(selected, StringComparison.OrdinalIgnoreCase) ||
        profile.Name.Equals(selected, StringComparison.OrdinalIgnoreCase);
}

internal static class WebSessionDialogs
{
    private static readonly SemaphoreSlim Prompts = new(1, 1);

    internal static async Task<WebBrowserProfiles.BrowserSelection?> AskProfileAsync(Window owner, Uri uri, CancellationToken token)
    {
        await Prompts.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            if (!owner.IsVisible) return null;
            var dialog = Create("Usar sesión de YouTube");
            var picker = new WebBrowserProfilePicker();
            var message = Message("Este enlace requiere una cuenta autorizada. El perfil elegido se usará solo en esta descarga.");
            var error = Message("");
            var accept = new Button { Content = "Usar para este enlace", Name = "AllowForLink" };
            accept.Click += (_, _) =>
            {
                if (picker.Selection() is { } selection) dialog.Close(selection);
                else error.Text = "Elige un navegador y un perfil.";
            };
            var cancel = new Button { Content = "Cancelar" };
            cancel.Click += (_, _) => dialog.Close();
            dialog.Content = new StackPanel { Margin = new Thickness(16), Spacing = 8,
                Children = { message, picker, error, Actions(OpenButton(uri, error), cancel, accept) } };
            using var registration = token.Register(() => Dispatcher.UIThread.Post(() => { if (dialog.IsVisible) dialog.Close(); }));
            return await dialog.ShowDialog<WebBrowserProfiles.BrowserSelection?>(owner);
        }
        finally { Prompts.Release(); }
    }

    internal static async Task<bool> ConfirmVerificationAsync(Window owner, Uri uri)
    {
        await Prompts.WaitAsync();
        try
        {
            if (!owner.IsVisible) return false;
            var dialog = Create("Verificación de YouTube");
            var error = Message("");
            var resume = new Button { Content = "Ya verifiqué · Reanudar", Name = "ResumeVerified" };
            resume.Click += (_, _) => dialog.Close(true);
            var cancel = new Button { Content = "Más tarde" };
            cancel.Click += (_, _) => dialog.Close(false);
            dialog.Content = new StackPanel { Margin = new Thickness(16), Spacing = 8,
                Children = { Message("YouTube pidió verificar la sesión. Abre el enlace en el navegador y completa la comprobación para reanudar la cola."),
                    error, Actions(OpenButton(uri, error), cancel, resume) } };
            return await dialog.ShowDialog<bool>(owner);
        }
        finally { Prompts.Release(); }
    }

    private static Window Create(string title) => new()
    { Title = title, Width = 535, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };

    private static TextBlock Message(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
    private static StackPanel Actions(params Button[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var button in buttons) row.Children.Add(button);
        return row;
    }

    private static Button OpenButton(Uri uri, TextBlock error)
    {
        var button = new Button { Content = "Abrir YouTube" };
        button.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            { error.Text = "No se pudo abrir el navegador. Abre YouTube manualmente."; }
        };
        return button;
    }
}
