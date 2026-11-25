using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public partial class MediaPoolPanelView
{
    private string? _selectedFolder;
    private int _viewMode;
    private string _selectedFilter = "Todos";
    private int _sortMode;
    private readonly int[] _viewSizes = { 68, 164, 26 };
    private Asset[] _visibleAssets = Array.Empty<Asset>();
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private bool _syncingSearch;
    private int _renderStart = -1, _renderEnd = -1;
    private double _renderWidth;
    private string? _selectedAssetId;
    private Button? _selectedButton;

    private void InitializeMediaFeatures()
    {
        AddButton.Click += (_, _) => ShowAddMenu();
        PreviousFolderButton.Click += (_, _) => ScrollFolderTabs(-150);
        NextFolderButton.Click += (_, _) => ScrollFolderTabs(150);
        AddFolderTabButton.Click += (_, _) => ShowCreateFolderFlyout(AddFolderTabButton);
        LinkShelfButton.Click += (_, _) => OpenLinkShelf();
        FilterButton.Click += (_, _) => ShowFilterMenu(FilterButton);
        ViewButton.Click += (_, _) => ShowViewPopup(ViewButton);
        MoreButton.Click += (_, _) => ShowMoreMenu();
        SearchButton.Click += (_, _) => { ExpandedSearchBox.IsVisible = !ExpandedSearchBox.IsVisible; if (ExpandedSearchBox.IsVisible) ExpandedSearchBox.Focus(); };
        SearchBox.TextChanged += (_, _) => SearchChanged(SearchBox, ExpandedSearchBox);
        ExpandedSearchBox.TextChanged += (_, _) => SearchChanged(ExpandedSearchBox, SearchBox);
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); Refresh(); };
        SizeChanged += (_, _) => { UpdateResponsiveLayout(); UpdateFolderNavigation(); };
        MediaScroll.SizeChanged += (_, _) => RenderVisibleAssets(true);
        MediaScroll.ScrollChanged += (_, _) => RenderVisibleAssets();
        MediaScroll.AddHandler(DragDrop.DropEvent, OnFilesDropped);
        MediaScroll.AddHandler(DragDrop.DragOverEvent, OnFilesDragOver);
        DragDrop.SetAllowDrop(AddButton, true);
        AddButton.AddHandler(DragDrop.DragOverEvent, (_, e) => {
            e.DragEffects = e.Data.Get("Prometheus.LinkShelfId") is string ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
        AddButton.AddHandler(DragDrop.DropEvent, async (_, e) => {
            if (e.Data.Get("Prometheus.LinkShelfId") is string id) await ImportShelfToMediaAsync(id, _selectedFolder, _selectedFolder != null);
            e.Handled = true;
        });
        RootGrid.AddHandler(PointerPressedEvent, (_, e) => {
            if (!LinkShelfOverlay.IsVisible || IsWithin(e.Source as Control, LinkShelfOverlay) ||
                IsWithin(e.Source as Control, LinkShelfButton)) return;
            ShowLinkShelf(false);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        MediaScroll.ContextMenu = new ContextMenu { ItemsSource = new[] {
            Menu("Importar archivos…", async () => await ImportAsync()),
            Menu("Importar enlace…", async () => await ImportLinkAsync()),
            Menu("Nueva carpeta…", () => ShowCreateFolderFlyout(AddButton))
        }};
        AddHandler(KeyDownEvent, MediaPoolKeyDown);
        Loaded += (_, _) => UpdateResponsiveLayout();
    }

    private void SearchChanged(TextBox source, TextBox other)
    {
        if (_syncingSearch) return;
        _syncingSearch = true;
        other.Text = source.Text;
        _syncingSearch = false;
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private async void MediaPoolKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox) return;
        if (e.Key == Key.F2 && e.KeyModifiers == KeyModifiers.None &&
            _controller?.CurrentProject.Assets.FirstOrDefault(a => a.Id == _selectedAssetId) is { } asset)
        {
            e.Handled = true;
            await EditAssetAsync(asset);
            return;
        }
        if (e.Key != Key.V || !e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.Source is TextBox) return;
        if (PanelRegistry.GetExistingPanelContent("timeline") is TimelinePanelView timeline)
            timeline.PasteContextual();
        else _controller?.ReportStatus("Abre la línea de tiempo para pegar en el cabezal");
        e.Handled = true;
    }

    private static MenuItem Menu(string title, Action action)
    {
        var item = new MenuItem { Header = title };
        item.Click += (_, _) => action();
        return item;
    }

    private async void OnFilesDropped(object? sender, DragEventArgs e)
    {
        try
        {
        if (e.Data.Get("Prometheus.LinkShelfId") is string shelfId)
        {
            await ImportShelfToMediaAsync(shelfId, _selectedFolder, _selectedFolder != null);
            e.Handled = true;
            return;
        }
        var files = e.Data.GetFiles()?.Where(f => f.Path.IsFile).Select(f => f.Path.LocalPath).ToArray();
        if (BrowserMediaData.TryGetHtmlMediaUrl(e.Data, out var htmlMedia)) await ImportLinkAsync(htmlMedia.ToString());
        else if (files?.Length > 0) await ImportFilesAsync(files);
        else
        {
            var image = BrowserMediaData.SaveImage(BrowserMediaData.GetImageData(e.Data));
            if (image != null) await ImportFilesAsync(new[] { image });
            else if (BrowserMediaData.TryGetUrl(e.Data, out var uri)) await ImportLinkAsync(uri.ToString());
            else _controller?.ReportStatus("El navegador no entregó una imagen, archivo o enlace compatible");
        }
        e.Handled = true;
        }
        catch (Exception ex) { _controller?.ReportStatus("No se pudo importar el arrastre: " + ex.Message); e.Handled = true; }
    }

    private void OnFilesDragOver(object? sender, DragEventArgs e)
    {
        try { e.DragEffects = e.Data.Get("Prometheus.LinkShelfId") is string || e.Data.GetFiles()?.Any() == true || BrowserMediaData.TryGetUrl(e.Data, out _) || BrowserMediaData.HasImage(e.Data) ? DragDropEffects.Copy : DragDropEffects.None; }
        catch { e.DragEffects = DragDropEffects.None; }
        e.Handled = true;
    }

    private static bool IsWithin(Control? source, Control ancestor)
    {
        for (var node = source; node != null; node = node.Parent as Control)
            if (ReferenceEquals(node, ancestor)) return true;
        return false;
    }

    private async Task ImportShelfToMediaAsync(string itemId, string? folderId, bool assignFolder)
    {
        if (_controller == null) return;
        var projectId = _controller.CurrentProject.Id;
        var source = _controller.CurrentProject.LinkShelf.FirstOrDefault(x => x.Id == itemId)?.Source;
        if (source == null) return;
        try
        {
            var task = LinkShelfService.PromoteAsync(_controller, itemId);
            if (assignFolder && _controller.CurrentProject.Assets.FirstOrDefault(a => a.Source.Uri == source) is { } pending)
                MoveAsset(pending, folderId);
            var asset = await task;
            if (asset != null && assignFolder && _controller.CurrentProject.Id == projectId)
                MoveAsset(asset, folderId);
        }
        catch (Exception ex) { _controller.ReportStatus("No se pudo importar desde enlaces: " + ex.Message); }
    }

    private async Task ImportLinkAsync(string? initial = null)
    {
        AttachController();
        if (_controller == null) return;
        var request = initial != null
            ? new WebLinkDialog.Request(initial, WebDownloadSettings.Current.Quality)
            : await WebLinkDialog.AskAsync(this);
        if (request == null) return;
        try { await WebMediaService.ImportAsync(_controller, request.Url, quality: request.Quality); }
        catch (Exception ex) { _controller.ReportStatus("No se pudo importar el enlace: " + ex.Message); }
    }

    private async Task ResolveAllWebAsync(bool usedOnly, bool redownload)
    {
        if (_controller == null) return;
        var project = _controller.CurrentProject;
        var ids = usedOnly ? project.ActiveSequence!.Clips.Select(c => c.AssetId).ToHashSet() : null;
        foreach (var asset in project.Assets.Where(a => a.Source.Kind == "web" && (ids == null || ids.Contains(a.Id))).ToArray())
            await WebMediaService.ResolveAsync(_controller, asset.Id, redownload: redownload);
    }

    private async Task ImportFilesAsync(IEnumerable<string> files)
    {
        AttachController();
        if (_controller == null) return;
        foreach (var file in files)
        {
            try
            {
                if (_controller.CurrentProject.Assets.Any(a => string.Equals(a.Source.Uri, file, StringComparison.OrdinalIgnoreCase))) continue;
                var asset = await LocalMediaService.ProbeAsync(file);
                asset.FolderId = _selectedFolder == "root" ? null : _selectedFolder;
                _controller.UndoRedo.Commit(p => p.Assets.Add(asset), "Importar " + asset.Name);
                _controller.ReportStatus("Importado: " + asset.Name);
            }
            catch (Exception ex) { _controller.ReportStatus("No se pudo importar " + Path.GetFileName(file) + ": " + ex.Message); }
        }
    }

    private async Task<string?> AskNameAsync(string title, string initial, string? description = null)
    {
        if (TopLevel.GetTopLevel(this) is not Window parent) return null;
        var input = new TextBox { Text = initial, Margin = new Thickness(12) };
        var save = new Button { Content = "Guardar", Width = 82 };
        var cancel = new Button { Content = "Cancelar", Width = 82 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(12) };
        row.Children.Add(save); row.Children.Add(cancel);
        var body = new StackPanel();
        if (description != null) body.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12, 12, 12, 0), FontSize = 11 });
        body.Children.Add(input); body.Children.Add(row);
        var dialog = new Window { Title = title, Width = 380, Height = description == null ? 145 : 190, Content = body, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        save.Click += (_, _) => dialog.Close(input.Text?.Trim());
        cancel.Click += (_, _) => dialog.Close(null);
        input.KeyDown += (_, e) => {
            if (e.Key == Key.Enter) { dialog.Close(input.Text?.Trim()); e.Handled = true; }
            else if (e.Key == Key.Escape) { dialog.Close(null); e.Handled = true; }
        };
        dialog.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
        return await dialog.ShowDialog<string?>(parent);
    }

    private void ShowCreateFolderFlyout(Control anchor)
    {
        AttachController();
        if (_controller == null) return;
        var projectId = _controller.CurrentProject.Id;
        var names = _controller.CurrentProject.MediaFolders.Select(f => f.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var number = 1;
        while (names.Contains("Nueva carpeta " + number)) number++;
        var input = new TextBox { Text = "Nueva carpeta " + number, Width = 195, Watermark = "Nombre de carpeta" };
        var accept = new Button { Content = "Crear", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(10, 3) };
        var body = new StackPanel { Spacing = 6, Margin = new Thickness(9) };
        body.Children.Add(input); body.Children.Add(accept);
        var flyout = new Flyout { Content = new Border { Background = Brushes.Black,
            BorderBrush = new SolidColorBrush(Color.Parse("#B56A00")), BorderThickness = new Thickness(1), Child = body } };
        void Commit()
        {
            if (_controller.CurrentProject.Id != projectId) { flyout.Hide(); return; }
            var name = input.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name)) { input.Focus(); return; }
            if (_controller.CurrentProject.MediaFolders.Any(f => string.Equals(f.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            { _controller.ReportStatus("Ya existe una carpeta con ese nombre"); input.Focus(); return; }
            flyout.Hide();
            MediaFolder? folder = null;
            try { _controller.UndoRedo.Commit(p => folder = MediaPoolOperations.CreateFolder(p, name), "Crear carpeta " + name); }
            catch (ArgumentException error) { _controller.ReportStatus(error.Message); return; }
            _selectedFolder = folder!.Id;
            Refresh();
        }
        accept.Click += (_, _) => Commit();
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { flyout.Hide(); e.Handled = true; } };
        flyout.ShowAt(anchor);
        input.Focus();
        input.SelectAll();
    }

    private async Task RenameFolderAsync(MediaFolder folder)
    {
        if (_controller == null) return;
        var projectId = _controller.CurrentProject.Id;
        var name = await AskNameAsync("Renombrar carpeta", folder.Name);
        if (string.IsNullOrWhiteSpace(name) || name == folder.Name) return;
        if (_controller.CurrentProject.Id != projectId) return;
        var id = folder.Id;
        try { _controller.UndoRedo.Commit(p => MediaPoolOperations.RenameFolder(p, id, name), "Renombrar carpeta"); }
        catch (ArgumentException error) { _controller.ReportStatus(error.Message); }
    }

    private void DeleteFolder(MediaFolder folder)
    {
        if (_controller == null) return;
        var id = folder.Id;
        _controller.UndoRedo.Commit(p => {
            p.MediaFolders.RemoveAll(f => f.Id == id);
            foreach (var asset in p.Assets.Where(a => a.FolderId == id)) asset.FolderId = null;
        }, "Eliminar carpeta " + folder.Name);
        _selectedFolder = null;
        Refresh();
    }

    private void MoveAsset(Asset asset, string? folderId)
    {
        if (_controller == null) return;
        if (_controller.CurrentProject.Assets.FirstOrDefault(a => a.Id == asset.Id)?.FolderId == folderId) return;
        var id = asset.Id;
        try { _controller.UndoRedo.Commit(p => MediaPoolOperations.MoveAssets(p, [id], folderId), "Mover medio a carpeta"); }
        catch (ArgumentException error) { _controller.ReportStatus(error.Message); }
    }

    private async Task RelinkAsync(Asset asset)
    {
        if (_controller == null) return;
        var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Reconectar " + asset.Name, AllowMultiple = false });
        if (files.Count == 0) return;
        var path = files[0].Path.LocalPath;
        try
        {
            var projectId = _controller.CurrentProject.Id;
            var probed = await MediaRelinkService.CheckAsync(_controller.CurrentProject, asset, path);
            if (_controller.CurrentProject.Id != projectId) return;
            var id = asset.Id;
            _controller.UndoRedo.Commit(p => {
                var target = p.Assets.First(a => a.Id == id);
                ProjectMediaRecovery.Relink(p, target, path, probed);
            }, "Reconectar " + asset.Name);
            _controller.ReportStatus("Reconectado: " + asset.Name);
        }
        catch (Exception ex) { _controller.ReportStatus("No se pudo reconectar: " + ex.Message); }
    }

    private async Task ReconnectFolderAsync()
    {
        AttachController();
        if (_controller == null) return;
        var folders = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions {
            Title = "Reconectar medios de una carpeta", AllowMultiple = false });
        if (folders.Count == 0) return;
        var root = folders[0].Path.LocalPath;
        var projectId = _controller.CurrentProject.Id;
        try
        {
            var missing = _controller.CurrentProject.Assets.Where(a => a.Source.Kind is "local" or "mgt" ||
                a.Source.Kind == "web" && a.Representations.Count > 0)
                .Where(a => !File.Exists(a.EffectiveLocator?.Path)).ToArray();
            var candidates = await Task.Run(() => ProjectMediaRecovery.EnumerateFiles(root));
            if (_controller.CurrentProject.Id != projectId) return;
            var matches = new List<(string Id, string Path, Asset Probed)>();
            var unmatched = 0;
            foreach (var asset in missing)
            {
                var path = ProjectMediaRecovery.FindUnique(asset, candidates, root);
                if (path == null) { unmatched++; continue; }
                try
                {
                    var probed = await MediaRelinkService.CheckAsync(_controller.CurrentProject, asset, path);
                    matches.Add((asset.Id, path, probed));
                }
                catch (Exception) { unmatched++; }
            }
            if (_controller.CurrentProject.Id != projectId) return;
            if (matches.Count > 0) _controller.UndoRedo.Commit(p => {
                foreach (var match in matches)
                {
                    var target = p.Assets.FirstOrDefault(a => a.Id == match.Id);
                    if (target == null) continue;
                    ProjectMediaRecovery.Relink(p, target, match.Path, match.Probed);
                }
            }, "Reconectar carpeta de medios");
            _controller.ReportStatus($"Reconectados {matches.Count} de {missing.Length} medios" +
                (unmatched > 0 ? $" · {unmatched} sin coincidencia única" : ""));
        }
        catch (Exception ex) { _controller.ReportStatus("No se pudo revisar la carpeta: " + ex.Message); }
    }

    private async Task EditAssetAsync(Asset asset)
    {
        if (_controller == null) return;
        var projectId = _controller.CurrentProject.Id;
        var name = await AskNameAsync("Renombrar medio", asset.Name,
            "Nombre visible en la bandeja. El archivo original no cambia.");
        if (string.IsNullOrWhiteSpace(name) || name == asset.Name) return;
        if (_controller.CurrentProject.Id != projectId) return;
        var id = asset.Id;
        try { _controller.UndoRedo.Commit(p => MediaPoolOperations.RenameAsset(p, id, name), "Renombrar medio"); }
        catch (ArgumentException error) { _controller.ReportStatus(error.Message); }
    }

    private async Task RemoveAssetAsync(Asset asset)
    {
        if (_controller == null) return;
        var id = asset.Id;
        var usages = NestedMediaOperations.FindAssetOccurrences(_controller.CurrentProject, id);
        if (usages.Count > 0)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            var dialog = new Window { Title = "Eliminar medio utilizado", Width = 520, Height = 230,
                CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var message = new TextBlock { Text = $"«{asset.Name}» se utiliza en {usages.Count} clip(s) del proyecto.\n" +
                "Eliminar de todas partes quitará el medio y sus apariciones en todos los montajes. Puedes deshacer la operación.",
                TextWrapping = TextWrapping.Wrap };
            var cancel = new Button { Content = "Cancelar", MinWidth = 82 };
            var locate = new Button { Content = "Ubicar medio", MinWidth = 105 };
            var remove = new Button { Content = "Eliminar de todas partes", MinWidth = 158,
                Foreground = Brushes.IndianRed };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            buttons.Children.Add(cancel); buttons.Children.Add(locate); buttons.Children.Add(remove);
            var body = new StackPanel { Margin = new Thickness(18), Spacing = 18 };
            body.Children.Add(message); body.Children.Add(buttons); dialog.Content = body;
            cancel.Click += (_, _) => dialog.Close("cancel");
            locate.Click += (_, _) => dialog.Close("locate");
            remove.Click += (_, _) => dialog.Close("remove");
            var choice = await dialog.ShowDialog<string?>(owner);
            if (choice == "locate")
            {
                var first = usages.OrderBy(usage => usage.SequenceId == _controller.CurrentProject.ActiveSequenceId ? 0 : 1)
                    .ThenBy(usage => usage.RootStartSeconds).First();
                _controller.LocateClip(first.SequenceId, first.RootClipId, first.RootStartSeconds);
                _controller.ReportStatus(first.InsideNested
                    ? "Medio ubicado dentro del contenedor seleccionado" : "Medio ubicado en la línea de tiempo");
                return;
            }
            if (choice != "remove") return;
        }
        try
        {
            _controller.Transport.Pause();
            WebMediaService.Cancel(id);
            _controller.UndoRedo.Commit(p => NestedMediaOperations.RemoveAssetEverywhere(p, id),
                "Eliminar medio y apariciones: " + asset.Name);
            _controller.SetSelectedClip(null);
            _controller.ReportStatus(usages.Count > 0
                ? $"Medio eliminado de {usages.Count} clip(s) · Ctrl+Z para deshacer"
                : "Medio eliminado del proyecto · Ctrl+Z para deshacer");
        }
        catch (Exception ex) { _controller.ReportStatus("No se pudo eliminar el medio: " + ex.Message); }
    }

    private void ShowInfo(Asset asset)
    {
        var streams = string.Join("; ", asset.Streams.Select(s => s.Kind + (s.Width is > 0 ? $" {s.Width}×{s.Height}" : "")));
        var state = asset.Source.Kind == "generated" ? "Generado" : asset.Source.Kind == "web" ? WebMediaService.IsPending(asset) ? WebMediaService.GetState(asset.Id)?.Message ?? "Pendiente" : "Resuelto" : File.Exists(asset.EffectiveLocator?.Path) ? "Conectado" : "Sin conexión";
        var web = asset.WebMetadata;
        var details = web == null ? "" : $"\nTítulo original: {web.OriginalTitle ?? "—"}\nCuenta: {web.AccountName ?? "—"} ({web.AccountHandle ?? "sin usuario"})\nCreador: {web.CreatorName ?? "—"}\nPublicado: {web.PublishedAt ?? "sin fecha"}";
        var info = $"Nombre: {asset.Name}\nTipo: {streams}\nDuración: {asset.RecommendedDuration?.Seconds:0.00} s\nOrigen: {asset.Source.Uri}\nEstado: {state}{details}";
        var window = new Window { Title = "Información del medio", Width = 540, Height = web == null ? 215 : 330, Content = new TextBlock { Text = info, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16) }, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        if (TopLevel.GetTopLevel(this) is Window parent) window.ShowDialog(parent);
    }

    private static string TypeOf(Asset asset)
    {
        if (asset.Source.Kind == "mgt") return "MGT";
        if (asset.Source.Kind == "web" && asset.Streams.Count == 0) return "Web pendiente";
        if (Prometheus.Core.Timeline.PlaceholderService.IsPlaceholder(asset)) return asset.Streams.Any(s => s.Kind == "audio") ? "Reserva audio" : "Reserva vídeo";
        if (new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff", ".avif" }.Contains((Path.GetExtension(asset.EffectiveLocator?.Path) ?? "").ToLowerInvariant())) return "Imagen";
        if (asset.Streams.Any(s => s.Kind == "video")) return "Vídeo";
        if (asset.Streams.Any(s => s.Kind == "image")) return "Imagen";
        return "Audio";
    }

    private static bool AppearsInMediaPool(Asset asset) => MediaPoolOperations.AppearsInPool(asset);

    private IEnumerable<Asset> VisibleAssets()
    {
        if (_controller == null) return Array.Empty<Asset>();
        var assets = _controller.CurrentProject.Assets.Where(AppearsInMediaPool);
        if (_selectedFolder == "root") assets = assets.Where(a => a.FolderId == null);
        else if (_selectedFolder != null) assets = assets.Where(a => a.FolderId == _selectedFolder);
        var search = SearchBox.Text?.Trim();
        assets = _selectedFilter switch {
            "Vídeo" => assets.Where(a => TypeOf(a) == "Vídeo"),
            "Audio" => assets.Where(a => TypeOf(a) == "Audio"),
            "Imagen" => assets.Where(a => TypeOf(a) == "Imagen"),
            "Sin conexión" => assets.Where(a => a.Source.Kind == "local" && !File.Exists(a.EffectiveLocator?.Path)),
            "Web pendientes" => assets.Where(a => a.Source.Kind == "web" && WebMediaService.IsPending(a)),
            "Web resueltos" => assets.Where(a => a.Source.Kind == "web" && !WebMediaService.IsPending(a)),
            _ => assets
        };
        var ranked = assets.Select(a => (Asset: a, Rank: SearchRank(a.Name, search ?? ""))).Where(x => x.Rank < 99);
        return _sortMode switch {
            1 => ranked.OrderBy(x => x.Rank).ThenByDescending(x => x.Asset.Name, StringComparer.CurrentCultureIgnoreCase).Select(x => x.Asset),
            2 => ranked.OrderBy(x => x.Rank).ThenBy(x => TypeOf(x.Asset)).ThenBy(x => x.Asset.Name, StringComparer.CurrentCultureIgnoreCase).Select(x => x.Asset),
            3 => ranked.OrderBy(x => x.Rank).ThenBy(x => x.Asset.RecommendedDuration?.Seconds ?? 0).Select(x => x.Asset),
            _ => ranked.OrderBy(x => x.Rank).ThenBy(x => x.Asset.Name, StringComparer.CurrentCultureIgnoreCase).Select(x => x.Asset)
        };
    }

    private static int SearchRank(string name, string query)
    {
        if (query.Length == 0) return 0;
        name = (name ?? "").ToLowerInvariant(); query = query.ToLowerInvariant();
        if (name == query) return 0;
        if (name.StartsWith(query)) return 1;
        if (name.Contains(query)) return 2;
        var limit = query.Length >= 5 ? 2 : query.Length >= 4 ? 1 : 0;
        if (limit == 0) return 99;
        return name.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(word => EditDistanceWithin(word, query, limit)) ? 3 : 99;
    }

    private static bool EditDistanceWithin(string word, string query, int limit)
    {
        if (Math.Abs(word.Length - query.Length) > limit) return false;
        var previous = Enumerable.Range(0, query.Length + 1).ToArray();
        for (var i = 1; i <= word.Length; i++)
        {
            var current = new int[query.Length + 1]; current[0] = i;
            var minimum = i;
            for (var j = 1; j <= query.Length; j++)
            {
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + (word[i - 1] == query[j - 1] ? 0 : 1));
                minimum = Math.Min(minimum, current[j]);
            }
            if (minimum > limit) return false;
            previous = current;
        }
        return previous[query.Length] <= limit;
    }

    private void RefreshMediaFeatures()
    {
        if (_controller == null) return;
        var project = _controller.CurrentProject;
        if (_selectedFolder != null && _selectedFolder != "root" && project.MediaFolders.All(f => f.Id != _selectedFolder)) _selectedFolder = null;
        FolderBar.Children.Clear();
        AddFolderButton("Todos", null, null);
        foreach (var id in OrderedFolderTabIds(project))
        {
            var folder = project.MediaFolders.FirstOrDefault(f => f.Id == id);
            if (id == "root") AddFolderButton("Sin carpeta", id, null);
            else if (folder != null) AddFolderButton(folder.Name, id, folder);
        }
        Dispatcher.UIThread.Post(UpdateFolderNavigation, DispatcherPriority.Background);
        var shown = VisibleAssets().ToArray();
        _visibleAssets = shown;
        CountLabel.Text = $"{shown.Length}/{project.Assets.Count(AppearsInMediaPool)}";
        _renderStart = _renderEnd = -1;
        RenderVisibleAssets(true);
    }

    private void ScrollFolderTabs(double distance)
    {
        var maximum = Math.Max(0, FolderTabScroll.Extent.Width - FolderTabScroll.Viewport.Width);
        FolderTabScroll.Offset = new Vector(Math.Clamp(FolderTabScroll.Offset.X + distance, 0, maximum), 0);
    }

    private void UpdateFolderNavigation()
    {
        if (Bounds.Width <= 0) return;
        var available = Math.Max(70, Bounds.Width - 100);
        FolderTabScroll.MaxWidth = available;
        FolderBar.Measure(new Size(double.PositiveInfinity, 32));
        var overflow = FolderBar.DesiredSize.Width > available + 1;
        PreviousFolderButton.IsVisible = NextFolderButton.IsVisible = overflow;
        if (!overflow) FolderTabScroll.Offset = new Vector(0, 0);
    }

    private static List<string> OrderedFolderTabIds(Project project)
    {
        var available = new[] { "root" }.Concat(project.MediaFolders.Select(f => f.Id)).ToArray();
        var valid = available.ToHashSet(StringComparer.Ordinal);
        return project.MediaFolderTabOrder.Where(valid.Contains).Distinct(StringComparer.Ordinal)
            .Concat(available.Where(id => !project.MediaFolderTabOrder.Contains(id, StringComparer.Ordinal)))
            .ToList();
    }

    private void MoveFolderTab(string source, string target, bool after)
    {
        if (_controller == null || source == target) return;
        var order = OrderedFolderTabIds(_controller.CurrentProject);
        if (!order.Remove(source) || !order.Contains(target)) return;
        order.Insert(order.IndexOf(target) + (after ? 1 : 0), source);
        _controller.UndoRedo.Commit(p => p.MediaFolderTabOrder = order, "Reordenar carpetas");
        Refresh();
    }

    private void RenderVisibleAssets(bool force = false)
    {
        var width = Math.Max(100, MediaScroll.Bounds.Width - 14);
        UpdateMediaListHeader(width);
        var itemWidth = _viewMode == 1 ? _viewSizes[1] : width;
        var columns = _viewMode == 1 ? Math.Max(1, (int)(width / itemWidth)) : 1;
        var rowHeight = _viewMode == 0 ? _viewSizes[0] : _viewMode == 1 ? Math.Max(125, itemWidth * 0.85) : _viewSizes[2];
        var rowCount = (_visibleAssets.Length + columns - 1) / columns;
        MediaCanvas.Width = width;
        MediaCanvas.Height = rowCount * rowHeight;
        var scrollY = MediaScroll.Offset.Y;
        var firstRow = Math.Max(0, (int)(scrollY / rowHeight) - 2);
        var lastRow = Math.Min(rowCount, (int)Math.Ceiling((scrollY + Math.Max(80, MediaScroll.Bounds.Height)) / rowHeight) + 2);
        var start = firstRow * columns;
        var end = Math.Min(_visibleAssets.Length, lastRow * columns);
        if (!force && start == _renderStart && end == _renderEnd && Math.Abs(width - _renderWidth) < 1) return;
        _renderStart = start; _renderEnd = end; _renderWidth = width;
        MediaCanvas.Children.Clear();
        _selectedButton = null;
        PruneThumbnailCache(start, end);
        for (var index = start; index < end; index++)
        {
            var asset = _visibleAssets[index];
            var button = CreateAssetButton(asset, itemWidth - 5, rowHeight - 3);
            Canvas.SetLeft(button, _viewMode == 1 ? (index % columns) * itemWidth : 0);
            Canvas.SetTop(button, (index / columns) * rowHeight);
            MediaCanvas.Children.Add(button);
        }
    }

    private void UpdateMediaListHeader(double width)
    {
        MediaListHeader.IsVisible = _viewMode == 0;
        var expanded = width >= 650;
        MediaListHeader.ColumnDefinitions = new ColumnDefinitions(expanded ? "*,125,75,72" : "*,80");
        MediaHeadOrigin.IsVisible = expanded;
        MediaHeadType.IsVisible = expanded;
        Grid.SetColumn(MediaHeadDuration, expanded ? 2 : 1);
    }

    private Button CreateAssetButton(Asset asset, double width, double height)
    {
        var exists = asset.Source.Kind == "generated" || asset.Source.Kind == "web" && !WebMediaService.IsPending(asset) ||
            asset.Source.Kind is "local" or "mgt" && File.Exists(asset.EffectiveLocator?.Path);
        var webState = asset.Source.Kind == "web" ? WebMediaService.GetState(asset.Id) : null;
        var stateText = asset.Source.Kind == "web" && !exists ? webState?.Message ?? "Pendiente · reintentar" : null;
        var title = asset.Name;
        var duration = (asset.RecommendedDuration?.Seconds ?? 0).ToString("0.0") + " s";
        var content = _viewMode == 0 ? BuildMediaListRow(asset, title, stateText, width) :
            _viewMode == 2 ? BuildCompactRow(asset, title, stateText ?? duration, width) :
            BuildMediaCard(asset, title + "\n" + (stateText ?? TypeOf(asset) + " · " + duration));
        var selected = _selectedAssetId == asset.Id;
        var labelBrush = new SolidColorBrush(MediaLabelPalette.ForAsset(asset));
        var defaultBorder = _viewMode == 0 ? new SolidColorBrush(Color.Parse("#303030")) : labelBrush;
        var pendingColor = webState?.Stage == "failed" ? "#C96E6E" : "#AEB1B5";
        var button = new Button { Content = content, Tag = defaultBorder, HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = new SolidColorBrush(Color.Parse(selected ? "#282018" : "#101010")), Foreground = new SolidColorBrush(Color.Parse(exists ? "#DDDDDD" : pendingColor)), BorderBrush = selected ? new SolidColorBrush(Color.Parse("#FF8A00")) : defaultBorder, BorderThickness = new Thickness(1), Width = width, Height = height, Padding = _viewMode == 0 ? new Thickness(0) : new Thickness(4, 1) };
        if (selected) _selectedButton = button;
        void SelectAsset()
        {
            if (_selectedButton != null) { _selectedButton.BorderBrush = _selectedButton.Tag as IBrush ?? Brushes.Gray; _selectedButton.Background = new SolidColorBrush(Color.Parse("#101010")); }
            _selectedAssetId = asset.Id; _selectedButton = button;
            button.BorderBrush = new SolidColorBrush(Color.Parse("#FF8A00"));
            button.Background = new SolidColorBrush(Color.Parse("#282018"));
        }
        button.Click += (_, _) => SelectAsset();
        button.DoubleTapped += (_, _) => Insert(asset);
        Point dragStart = default;
        button.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return;
            dragStart = e.GetPosition(button);
            SelectAsset();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        button.PointerMoved += async (_, e) => {
            if (!e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return;
            var point = e.GetPosition(button);
            if (Math.Abs(point.X - dragStart.X) + Math.Abs(point.Y - dragStart.Y) < 8) return;
            var data = new DataObject(); data.Set("Prometheus.AssetId", asset.Id);
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy | DragDropEffects.Move);
        };
        button.ContextMenu = BuildAssetMenu(asset);
        return button;
    }

    private static Control BuildCompactRow(Asset asset, string title, string duration, double width)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(width > 370 ? "*,80,60" : width > 250 ? "*,65" : "*") };
        grid.Children.Add(new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 10 });
        if (grid.ColumnDefinitions.Count > 1)
        {
            var label = new TextBlock { Text = TypeOf(asset), Foreground = Brushes.Gray, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 1); grid.Children.Add(label);
        }
        if (grid.ColumnDefinitions.Count > 2)
        {
            var time = new TextBlock { Text = duration, Foreground = Brushes.Gray, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(time, 2); grid.Children.Add(time);
        }
        return grid;
    }

    private void AddFolderButton(string title, string? id, MediaFolder? folder)
    {
        var selected = _selectedFolder == id;
        var tabBorder = new Border { CornerRadius = new CornerRadius(12, 12, 0, 0),
            BorderThickness = new Thickness(1, 1, 1, 0),
            BorderBrush = new SolidColorBrush(Color.Parse(selected ? "#FF8A00" : "#666666")),
            Background = new SolidColorBrush(Color.Parse(selected ? "#FF8A00" : "#181818")),
            MinHeight = 30,
            Child = new TextBlock { Text = title, FontSize = 11, Margin = new Thickness(12, 8, 12, 4),
                Foreground = selected ? Brushes.Black : Brushes.LightGray } };
        var button = new Button { Content = tabBorder, Padding = new Thickness(0), Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Margin = new Thickness(id == null ? 0 : -5, 0, 0, 0),
            ZIndex = selected ? 10 : 0 };
        button.Click += (_, _) => { _selectedFolder = id; Refresh(); };
        if (id != null)
        {
            Point dragStart = default;
            button.PointerPressed += (_, e) => dragStart = e.GetPosition(button);
            button.PointerMoved += async (_, e) => {
                if (!e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return;
                var point = e.GetPosition(button);
                if (Math.Abs(point.X - dragStart.X) + Math.Abs(point.Y - dragStart.Y) < 12) return;
                var data = new DataObject(); data.Set("Prometheus.MediaFolderTabId", id);
                await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
            };
            DragDrop.SetAllowDrop(button, true);
            button.AddHandler(DragDrop.DragOverEvent, (_, e) => {
                if (e.Data.Get("Prometheus.MediaFolderTabId") is string tabId)
                {
                    e.DragEffects = tabId == id ? DragDropEffects.None : DragDropEffects.Move;
                    tabBorder.BorderBrush = new SolidColorBrush(Color.Parse("#FFFFFF"));
                    e.Handled = true;
                    return;
                }
                var assetId = e.Data.Get("Prometheus.AssetId") as string;
                e.DragEffects = _controller?.CurrentProject.Assets.Any(a => a.Id == assetId) == true
                    ? DragDropEffects.Move : e.Data.Get("Prometheus.LinkShelfId") is string ? DragDropEffects.Copy : DragDropEffects.None;
                tabBorder.BorderBrush = new SolidColorBrush(Color.Parse(e.DragEffects == DragDropEffects.None ? "#666666" : "#FF8A00"));
                if (e.DragEffects != DragDropEffects.None) tabBorder.Background = new SolidColorBrush(Color.Parse("#503523"));
                e.Handled = true;
            });
            button.AddHandler(DragDrop.DragLeaveEvent, (_, e) => {
                tabBorder.BorderBrush = new SolidColorBrush(Color.Parse(selected ? "#FF8A00" : "#666666"));
                tabBorder.Background = new SolidColorBrush(Color.Parse(selected ? "#FF8A00" : "#181818"));
                e.Handled = true;
            });
            button.AddHandler(DragDrop.DropEvent, async (_, e) => {
                e.Handled = true;
                if (e.Data.Get("Prometheus.MediaFolderTabId") is string sourceTab)
                {
                    MoveFolderTab(sourceTab, id, e.GetPosition(button).X > button.Bounds.Width / 2);
                    return;
                }
                var assetId = e.Data.Get("Prometheus.AssetId") as string;
                var asset = _controller?.CurrentProject.Assets.FirstOrDefault(a => a.Id == assetId);
                if (asset != null) MoveAsset(asset, id == "root" ? null : id);
                else if (e.Data.Get("Prometheus.LinkShelfId") is string shelfId)
                    await ImportShelfToMediaAsync(shelfId, id == "root" ? null : id, true);
            });
        }
        if (folder != null) button.ContextMenu = new ContextMenu { ItemsSource = new[] {
            Menu("Renombrar carpeta…", async () => await RenameFolderAsync(folder)),
            Menu("Eliminar carpeta", () => DeleteFolder(folder))
        }};
        FolderBar.Children.Add(button);
    }

    private ContextMenu BuildAssetMenu(Asset asset)
    {
        var web = asset.Source.Kind == "web";
        var pending = web && WebMediaService.IsPending(asset);
        var local = asset.Source.Kind == "local";
        var connected = File.Exists(asset.EffectiveLocator?.Path);
        var move = new MenuItem { Header = "Mover a carpeta" };
        var targets = new List<MenuItem> { Menu("Sin carpeta", () => MoveAsset(asset, null)) };
        targets.AddRange(_controller!.CurrentProject.MediaFolders.Select(f => Menu(f.Name, () => MoveAsset(asset, f.Id))));
        move.ItemsSource = targets;
        var options = new List<MenuItem>();
        if (!pending) options.Add(Menu("Añadir a la línea de tiempo", () => Insert(asset)));
        options.Add(Menu("Renombrar medio… (F2)", async () => await EditAssetAsync(asset)));
        options.Add(move);
        options.Add(Menu("Ver información del medio", () => ShowInfo(asset)));
        if (web)
        {
            var webOptions = new List<MenuItem>();
            if (pending) webOptions.Add(Menu("Descargar / reintentar", async () => await WebMediaService.ResolveAsync(_controller!, asset.Id)));
            webOptions.Add(Menu("Descargar vista ligera", async () =>
                await WebMediaService.ResolveAsync(_controller!, asset.Id, redownload: true, quality: WebDownloadQuality.Light)));
            webOptions.Add(Menu("Descargar vista completa", async () =>
                await WebMediaService.ResolveAsync(_controller!, asset.Id, redownload: true, quality: WebDownloadQuality.Complete)));
            webOptions.Add(Menu("Abrir página original", () => Process.Start(new ProcessStartInfo(asset.Source.PageUrl ?? asset.Source.Uri) { UseShellExecute = true })));
            webOptions.Add(Menu("Copiar URL de origen", async () => await TopLevel.GetTopLevel(this)!.Clipboard!.SetTextAsync(asset.Source.Uri)));
            if (pending) webOptions.Add(Menu("Cancelar descarga", () => WebMediaService.Cancel(asset.Id)));
            options.Add(new MenuItem { Header = "Opciones web", ItemsSource = webOptions });
        }
        else if (local)
        {
            var fileOptions = new List<MenuItem>();
            if (connected) fileOptions.Add(Menu("Abrir ubicación del archivo", () => { var path = asset.EffectiveLocator!.Path; Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }));
            fileOptions.Add(Menu(connected ? "Cambiar archivo…" : "Reconectar archivo…", async () => await RelinkAsync(asset)));
            options.Add(new MenuItem { Header = "Archivo local", ItemsSource = fileOptions });
        }
        options.Add(Menu("Eliminar del proyecto", async () => await RemoveAssetAsync(asset)));
        return new ContextMenu { ItemsSource = options };
    }

    private void SetAssetLabel(string assetId, string? labelId)
    {
        _controller?.UndoRedo.Commit(p => p.Assets.First(a => a.Id == assetId).LabelId = labelId, "Etiquetar medio");
    }
}
