using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;

namespace Prometheus.Client.Views;

public partial class MainWindow
{
    private void UpdateRecentProjectsMenu()
    {
        var items = new List<Control>();
        foreach (var path in _projectController.RecentProjects)
        {
            var available = File.Exists(path);
            var item = new MenuItem { Header = Path.GetFileNameWithoutExtension(path) +
                (available ? "" : " (no disponible)"), IsEnabled = available };
            ToolTip.SetTip(item, path);
            item.Click += async (_, _) => await _projectController.OpenRecentProjectAsync(path);
            items.Add(item);
        }
        if (items.Count == 0) items.Add(new MenuItem { Header = "No hay proyectos recientes", IsEnabled = false });
        else
        {
            items.Add(new Separator());
            var clear = new MenuItem { Header = "Limpiar recientes" };
            clear.Click += (_, _) => { _projectController.ClearRecentProjects(); UpdateRecentProjectsMenu(); };
            items.Add(clear);
        }
        MenuRecentProjects.ItemsSource = items;
    }
}
