using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Preflight;
using Prometheus.Core.Storage;

namespace Prometheus.Client.Dialogs;

public class MissingAssetViewModel : INotifyPropertyChanged
{
    private string _statusText = "Desconectado";
    private string? _resolvedPath;
    private bool _isResolved;

    public string AssetId { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public string ExpectedPath { get; set; } = string.Empty;
    public string Kind { get; set; } = "local";
    public Asset? Asset { get; set; }
    public Asset? ProbedAsset { get; set; }

    public string? ResolvedPath
    {
        get => _resolvedPath;
        set
        {
            _resolvedPath = value;
            OnPropertyChanged(nameof(ResolvedPath));
        }
    }

    public string StatusText
    {
        get => _statusText;
        set
        {
            _statusText = value;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public bool IsResolved
    {
        get => _isResolved;
        set
        {
            _isResolved = value;
            OnPropertyChanged(nameof(IsResolved));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}

public partial class OfflineMediaDialog : Window
{
    public ObservableCollection<MissingAssetViewModel> Items { get; } = new();
    public bool WasCancelled { get; private set; } = true;
    private Project? _project;

    public OfflineMediaDialog()
    {
        InitializeComponent();
    }

    public void Initialize(IEnumerable<MissingAssetInfo> missingAssets, Project project)
    {
        _project = project;
        Items.Clear();
        foreach (var m in missingAssets)
        {
            Items.Add(new MissingAssetViewModel
            {
                AssetId = m.AssetId,
                AssetName = m.AssetName,
                ExpectedPath = m.ExpectedPath,
                Kind = m.Kind,
                Asset = project.Assets.FirstOrDefault(asset => asset.Id == m.AssetId),
                StatusText = "Desconectado",
                IsResolved = false
            });
        }

        MissingAssetsList.ItemsSource = Items;
        if (Items.Count > 0)
        {
            MissingAssetsList.SelectedIndex = 0;
        }

        UpdateSummary();

        BrowseManualButton.Click += async (_, _) => await BrowseManualAsync();
        SearchFolderButton.Click += async (_, _) => await SearchFolderAsync();
        KeepOfflineButton.Click += (_, _) => MarkSelectedAsOffline();

        ApplyButton.Click += (_, _) =>
        {
            WasCancelled = false;
            Close();
        };

        CancelButton.Click += (_, _) =>
        {
            WasCancelled = true;
            Close();
        };
    }

    private void UpdateSummary()
    {
        int total = Items.Count;
        int resolved = Items.Count(i => i.IsResolved);
        int pending = total - resolved;

        SummaryText.Text = pending > 0
            ? $"{pending} de {total} medios pendientes de resolución"
            : $"Todos los {total} medios resueltos";
    }

    private async System.Threading.Tasks.Task BrowseManualAsync()
    {
        var selected = MissingAssetsList.SelectedItem as MissingAssetViewModel;
        if (selected == null)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Seleccionar archivo para {selected.AssetName}",
            AllowMultiple = false
        });

        if (files.Count > 0)
        {
            string newPath = files[0].Path.LocalPath;
            if (File.Exists(newPath) && await CompatibleAsync(selected, newPath))
            {
                selected.ResolvedPath = newPath;
                selected.StatusText = "Reconectado";
                selected.IsResolved = true;
                UpdateSummary();
            }
        }
    }

    private async System.Threading.Tasks.Task SearchFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Seleccionar carpeta para búsqueda automática recursiva"
        });

        if (folders.Count == 0)
            return;

        string searchDir = folders[0].Path.LocalPath;
        if (!Directory.Exists(searchDir))
            return;

        var candidates = await System.Threading.Tasks.Task.Run(() => ProjectMediaRecovery.EnumerateFiles(searchDir));
        foreach (var item in Items.Where(i => !i.IsResolved))
        {
            try
            {
                var match = item.Asset == null ? null : ProjectMediaRecovery.FindUnique(item.Asset, candidates, searchDir);
                if (match != null && await CompatibleAsync(item, match))
                {
                    item.ResolvedPath = match;
                    item.StatusText = "Reconectado (carpeta)";
                    item.IsResolved = true;
                }
                else if (match == null) item.StatusText = "Sin coincidencia única";
            }
            catch
            {
                // Permiso o ruta larga, ignorar
            }
        }

        UpdateSummary();
    }

    private async System.Threading.Tasks.Task<bool> CompatibleAsync(MissingAssetViewModel item, string path)
    {
        if (item.Asset == null || _project == null) return false;
        try
        {
            item.ProbedAsset = await MediaRelinkService.CheckAsync(_project, item.Asset, path);
            return true;
        }
        catch (Exception ex) { item.StatusText = ex.Message; }
        return false;
    }

    private void MarkSelectedAsOffline()
    {
        var selected = MissingAssetsList.SelectedItem as MissingAssetViewModel;
        if (selected != null)
        {
            selected.ResolvedPath = null;
            selected.ProbedAsset = null;
            selected.StatusText = "Conservado Offline";
            selected.IsResolved = true;
            UpdateSummary();
        }
        else
        {
            foreach (var item in Items.Where(i => !i.IsResolved))
            {
                item.StatusText = "Conservado Offline";
                item.IsResolved = true;
            }
            UpdateSummary();
        }
    }
}
