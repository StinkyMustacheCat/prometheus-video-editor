using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Prometheus.Client.Services;

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
        var browsers = new[] { "chrome", "edge", "firefox" };
        var browser = new ComboBox { Name = "WebCookieBrowser", ItemsSource = new[] { "Chrome", "Edge", "Firefox" },
            SelectedIndex = Math.Max(0, Array.IndexOf(browsers, current.CookieBrowser)), HorizontalAlignment = HorizontalAlignment.Stretch };
        var profile = new TextBox { Name = "WebCookieProfile", Watermark = "Perfil del navegador (opcional)", Text = current.CookieProfile };
        var file = new TextBox { Name = "WebCookieFile", Text = current.CookieFile, IsReadOnly = true, Watermark = "Selecciona cookies.txt en formato Netscape" };
        var chooseFile = new Button { Content = "Elegir archivo…" };
        chooseFile.Click += async (_, _) => {
            var files = await dialog.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Archivo de cookies de YouTube",
                AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType("Cookies Netscape") { Patterns = new[] { "*.txt" } } } });
            if (files.Count > 0) file.Text = files[0].Path.LocalPath;
        };
        void UpdateAuthentication()
        {
            browser.IsVisible = profile.IsVisible = auth.SelectedIndex == 1;
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
        var save = new Button { Content = "Guardar", MinWidth = 85 };
        var cancel = new Button { Content = "Cancelar", MinWidth = 85 };
        cancel.Click += (_, _) => dialog.Close();
        save.Click += (_, _) =>
        {
            try
            {
                if (auth.SelectedIndex == 2 && !File.Exists(file.Text))
                { error.Text = "Selecciona un archivo de cookies disponible."; return; }
                WebDownloadSettings.Save(new((WebDownloadQuality)quality.SelectedIndex,
                    po.IsChecked == true, auth.SelectedIndex == 1 ? browsers[Math.Max(0, browser.SelectedIndex)] : null,
                    auth.SelectedIndex == 1 ? profile.Text?.Trim() : null, paste.SelectedIndex == 1,
                    auth.SelectedIndex == 2 ? file.Text : null));
                dialog.Close();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                error.Text = "No se pudieron guardar los ajustes de descarga.";
            }
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
                new TextBlock { Text = "Cuenta de YouTube (opcional)" }, auth, browser, profile, file, chooseFile, login,
                Note("Inicia sesión en tu navegador habitual. Prometheus no solicita tu contraseña ni guarda la sesión en el proyecto. El archivo de cookies se usa mediante una copia temporal y el original no cambia."),
                new Expander { Header = "Avanzado", Content = new StackPanel { Spacing = 6, Children = {
                    po, Note("Sin proveedores de PO Token por defecto. Al desactivarlo, yt-dlp puede usar proveedores instalados. Una cuenta no elimina todas las restricciones del sitio.") } } }, error,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8, Children = { cancel, save } }
            }
        } };
        await dialog.ShowDialog(owner);

        static TextBlock Note(string text) => new() { Text = text, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
    }
}
