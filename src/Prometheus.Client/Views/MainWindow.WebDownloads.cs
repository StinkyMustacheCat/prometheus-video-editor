using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Prometheus.Client.Services;
using Prometheus.Client.Dialogs;

namespace Prometheus.Client.Views;

public partial class MainWindow
{
    private Task OpenWebDownloadsAsync() => ShowWebDownloadsAsync(this);

    internal static async Task ShowWebDownloadsAsync(Window owner)
    {
        var current = WebDownloadSettings.Current;
        var dialog = new Window { Title = "Descargas web", Width = 535, Height = 690, MaxHeight = 760,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var quality = WebLinkDialog.QualitySelector();
        var paste = new ComboBox { Name = "WebPasteDefault", ItemsSource = new[] { "Completo, directamente", "Elegir tramo antes de insertar" },
            SelectedIndex = current.PasteChooseRange ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var po = new CheckBox { Content = "Sin PO Token", IsChecked = current.WithoutPoToken };
        var auth = new ComboBox { Name = "WebAuthentication", ItemsSource = new[] { "Sin cuenta (predeterminado)", "Sesión del navegador", "Archivo de cookies" },
            SelectedIndex = !string.IsNullOrWhiteSpace(current.CookieFile) ? 2 : string.IsNullOrWhiteSpace(current.CookieBrowser) ? 0 : 1,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        var profile = new WebBrowserProfilePicker(current.CookieBrowser, current.CookieProfile);
        var fallback = new CheckBox { Name = "WebAllowPoTokenFallback",
            Content = "Permitir un proveedor instalado como alternativa", IsChecked = current.AllowPoTokenFallback,
            IsEnabled = po.IsChecked == true };
        po.IsCheckedChanged += (_, _) => fallback.IsEnabled = po.IsChecked == true;
        var file = new TextBox { Name = "WebCookieFile", Text = current.CookieFile, IsReadOnly = true, Watermark = "Selecciona cookies.txt en formato Netscape" };
        var chooseFile = new Button { Content = "Elegir archivo…" };
        chooseFile.Click += async (_, _) => {
            var files = await dialog.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Archivo de cookies de YouTube",
                AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType("Cookies Netscape") { Patterns = new[] { "*.txt" } } } });
            if (files.Count > 0) file.Text = files[0].Path.LocalPath;
        };
        void UpdateAuthentication()
        {
            profile.IsVisible = auth.SelectedIndex == 1;
            file.IsVisible = chooseFile.IsVisible = auth.SelectedIndex == 2;
        }
        auth.SelectionChanged += (_, _) => UpdateAuthentication();
        UpdateAuthentication();
        var login = new Button { Content = "Abrir YouTube en el navegador" };
        var error = new TextBlock { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
        login.Click += (_, _) => {
            try { Process.Start(new ProcessStartInfo("https://www.youtube.com") { UseShellExecute = true }); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            { error.Text = "No se pudo abrir el navegador. Abre YouTube manualmente."; }
        };
        WebDownloadSettings ReadSettings()
        {
            var picked = auth.SelectedIndex == 1 ? profile.Selection() : null;
            if (auth.SelectedIndex == 1 && picked == null)
                throw new ArgumentException("Elige un navegador y su perfil.");
            if (auth.SelectedIndex == 2 && !File.Exists(file.Text))
                throw new ArgumentException("Selecciona un archivo de cookies disponible.");
            return new WebDownloadSettings((WebDownloadQuality)quality.SelectedIndex, po.IsChecked == true,
                picked?.Id, picked?.Profile, paste.SelectedIndex == 1, auth.SelectedIndex == 2 ? file.Text : null,
                fallback.IsChecked == true);
        }

        var testLink = new TextBox { Name = "WebSessionTestLink", Watermark = "Enlace de YouTube para comprobar la sesión" };
        var test = new Button { Name = "WebTestSession", Content = "Probar sesión" };
        var testResult = Note("");
        using var dialogLifetime = new CancellationTokenSource();
        dialog.Closed += (_, _) => dialogLifetime.Cancel();
        test.Click += async (_, _) =>
        {
            if (!WebMediaService.TryUrl(testLink.Text, out var uri) || !WebDownloadSettings.IsYouTube(uri))
            { testResult.Text = "Introduce un enlace de YouTube."; return; }
            test.IsEnabled = false;
            testResult.Text = "Consultando el enlace…";
            try
            {
                var settings = ReadSettings();
                await WebSessionProbe.CheckAsync(uri, settings, dialogLifetime.Token);
                testResult.Text = settings.HasSession
                    ? "El enlace se pudo consultar con esta selección. Para confirmar el acceso de la cuenta, prueba un enlace que lo requiera."
                    : "El enlace se pudo consultar sin cuenta.";
            }
            catch (WebDownloadFailure failure) when (failure.Kind == WebFailureKind.Verification)
            {
                testResult.Text = failure.Message;
                if (await WebSessionDialogs.ConfirmVerificationAsync(dialog, uri))
                {
                    YouTubeRequestQueue.Shared.ResumeAfterVerification();
                    testResult.Text = "Cola reanudada. Pulsa Probar sesión para volver a consultar.";
                }
            }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { testResult.Text = "La consulta superó el tiempo de espera."; }
            catch (Exception failure) when (failure is WebDownloadFailure or FileNotFoundException or ArgumentException)
            { testResult.Text = failure.Message; }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { testResult.Text = "No se pudo ejecutar la prueba de sesión."; }
            finally { test.IsEnabled = true; }
        };
        var save = new Button { Content = "Guardar", MinWidth = 85 };
        var cancel = new Button { Content = "Cancelar", MinWidth = 85 };
        cancel.Click += (_, _) => dialog.Close();
        save.Click += (_, _) =>
        {
            try
            {
                WebDownloadSettings.Save(ReadSettings());
                dialog.Close();
            }
            catch (ArgumentException invalid) { error.Text = invalid.Message; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { error.Text = "No se pudieron guardar los ajustes de descarga."; }
        };
        dialog.Content = new ScrollViewer { Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 9,
            Children =
            {
                new TextBlock { Text = "Calidad predeterminada" }, quality,
                Note("La vista completa conserva la calidad disponible y puede ocupar más espacio. La ligera descarga hasta 720p de alto cuando el sitio lo permite."),
                new TextBlock { Text = "Al pegar un enlace en la línea de tiempo (Ctrl+V)" }, paste,
                Note("Elegir tramo cambia lo que se inserta. La fuente se descarga completa para permitir ampliar los bordes después."),
                new TextBlock { Text = "Cuenta de YouTube (opcional)" }, auth, profile, file, chooseFile, login,
                Note("Inicia sesión en tu navegador habitual. Prometheus no solicita tu contraseña ni guarda la sesión en el proyecto. El archivo de cookies se usa mediante una copia temporal y el original no cambia."),
                new Expander { Header = "Probar acceso a un enlace", Content = new StackPanel { Spacing = 6, Children = {
                    testLink, test, testResult } } },
                new Expander { Header = "Avanzado", Content = new StackPanel { Spacing = 6, Children = {
                    po, fallback, Note("Si un intento falla, otra configuración puede probarse solo para ese enlace. El ajuste de proveedores instalados es opcional.") } } }, error,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8, Children = { cancel, save } }
            }
        } };
        await dialog.ShowDialog(owner);

        static TextBlock Note(string text) => new() { Text = text, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
    }
}
