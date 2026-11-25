using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Prometheus.Client.Views;

public partial class MainWindow
{
    private void SetupNotificationsButton()
    {
        var notifications = _projectController.Notifications;
        notifications.Changed += () => NotificationsDot.IsVisible = notifications.HasUnread;
        _projectController.OpenExportRequested += (path, format) => ExportActiveSequenceAsync(path, format);
        NotificationsButton.Click += (_, _) =>
        {
            var panel = new StackPanel { Width = 360, Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = "Notificaciones", FontWeight = FontWeight.Bold });
            var entries = new StackPanel { Spacing = 10 };
            var flyout = new Flyout
            {
                Placement = PlacementMode.BottomEdgeAlignedRight,
                Content = panel
            };
            if (notifications.Entries.Count == 0)
                entries.Children.Add(new TextBlock { Text = "No hay notificaciones.", Opacity = 0.7 });
            foreach (var entry in notifications.Entries)
            {
                var content = new StackPanel { Spacing = 3 };
                content.Children.Add(new TextBlock
                {
                    Text = $"{entry.At:HH:mm}  ·  {entry.Title}" + (entry.Count > 1 ? $" ×{entry.Count}" : ""),
                    FontWeight = FontWeight.SemiBold
                });
                content.Children.Add(new TextBlock { Text = entry.Message, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
                if (entry.Action != null)
                {
                    var button = new Button { Content = entry.ActionLabel, Classes = { "nle-btn" } };
                    button.Click += async (_, _) =>
                    {
                        flyout.Hide();
                        try { await entry.Action(); }
                        catch (Exception error) { _projectController.ReportStatus(error.Message); }
                    };
                    content.Children.Add(button);
                }
                entries.Children.Add(content);
            }
            panel.Children.Add(new ScrollViewer { Content = entries, MaxHeight = 415 });
            notifications.MarkRead();
            flyout.ShowAt(NotificationsButton);
        };
    }
}
