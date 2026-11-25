using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Prometheus.Client.Panels;
using Prometheus.Common.Docking;

namespace Prometheus.Client.Docking;

public enum DropTargetType
{
    None,
    EdgeLeft,
    EdgeTop,
    EdgeRight,
    EdgeBottom,
    PanelCenter,
    PanelLeft,
    PanelTop,
    PanelRight,
    PanelBottom
}

public class DropTargetInfo
{
    public DropTargetType Type { get; set; } = DropTargetType.None;
    public string? TargetPanelId { get; set; }
}

public partial class DockOverlayControl : UserControl
{
    public const double EdgeGutterActiveThreshold = 24.0;

    private static readonly IBrush GutterActiveBrush = new SolidColorBrush(Color.Parse("#FF8A00"));
    private static readonly IBrush GutterInactiveBrush = Brushes.Transparent;

    public DockOverlayControl()
    {
        InitializeComponent();
    }

    public void Reset()
    {
        GutterLeft.Background = GutterInactiveBrush;
        GutterTop.Background = GutterInactiveBrush;
        GutterRight.Background = GutterInactiveBrush;
        GutterBottom.Background = GutterInactiveBrush;
        DropPreviewBorder.IsVisible = false;
        DragGhostCard.IsVisible = false;
    }

    public DropTargetInfo UpdateDragHover(
        Point localPos,
        Size totalSize,
        Func<Point, (string PanelId, Rect Bounds)?> hitTestPanel,
        bool isCtrlPressed,
        string? draggedPanelId,
        bool isFloating = false)
    {
        Reset();

        var info = new DropTargetInfo();

        if (totalSize.Width <= 0 || totalSize.Height <= 0)
        {
            return info;
        }

        // 1. Tarjeta fantasma (solo si el panel arrastrado estaba acoplado y no es ya una ventana flotante visible)
        if (!isFloating)
        {
            Canvas.SetLeft(DragGhostCard, Math.Max(0, localPos.X + 14));
            Canvas.SetTop(DragGhostCard, Math.Max(0, localPos.Y + 14));
            DragGhostText.Text = !string.IsNullOrEmpty(draggedPanelId) ? PanelRegistry.GetPanelTitle(draggedPanelId) : "Panel";
            DragGhostMode.Text = isCtrlPressed ? "Acoplar (Ctrl)" : "Flotante";
            DragGhostCard.IsVisible = true;
        }

        // El acople es explícito: sin Ctrl, el panel permanece flotante.
        if (!isCtrlPressed)
        {
            return info;
        }

        // 2. Canales perimetrales con zona de detección expandida (24 px)
        if (localPos.X >= 0 && localPos.X <= EdgeGutterActiveThreshold)
        {
            info.Type = DropTargetType.EdgeLeft;
            GutterLeft.Background = GutterActiveBrush;
            ShowPreview(4, 4, (totalSize.Width * 0.25) - 8, totalSize.Height - 8, "Acoplar al extremo izquierdo (25%)");
            return info;
        }

        if (localPos.X >= totalSize.Width - EdgeGutterActiveThreshold && localPos.X <= totalSize.Width)
        {
            info.Type = DropTargetType.EdgeRight;
            GutterRight.Background = GutterActiveBrush;
            ShowPreview((totalSize.Width * 0.75) + 4, 4, (totalSize.Width * 0.25) - 8, totalSize.Height - 8, "Acoplar al extremo derecho (75%)");
            return info;
        }

        if (localPos.Y >= 0 && localPos.Y <= EdgeGutterActiveThreshold)
        {
            info.Type = DropTargetType.EdgeTop;
            GutterTop.Background = GutterActiveBrush;
            ShowPreview(4, 4, totalSize.Width - 8, (totalSize.Height * 0.25) - 8, "Acoplar al extremo superior (25%)");
            return info;
        }

        if (localPos.Y >= totalSize.Height - EdgeGutterActiveThreshold && localPos.Y <= totalSize.Height)
        {
            info.Type = DropTargetType.EdgeBottom;
            GutterBottom.Background = GutterActiveBrush;
            ShowPreview(4, (totalSize.Height * 0.75) + 4, totalSize.Width - 8, (totalSize.Height * 0.25) - 8, "Acoplar al extremo inferior (75%)");
            return info;
        }

        // 3. Paneles individuales en el árbol
        var panelHit = hitTestPanel(localPos);
        if (panelHit != null)
        {
            var targetPanelId = panelHit.Value.PanelId;
            var bounds = panelHit.Value.Bounds;
            info.TargetPanelId = targetPanelId;

            var relX = (localPos.X - bounds.X) / bounds.Width;
            var relY = (localPos.Y - bounds.Y) / bounds.Height;

            // Zona de sustitución central (30% a 70%)
            if (relX >= 0.30 && relX <= 0.70 && relY >= 0.30 && relY <= 0.70)
            {
                info.Type = DropTargetType.PanelCenter;
                ShowPreview(bounds.X + 4, bounds.Y + 4, bounds.Width - 8, bounds.Height - 8, $"Sustituir {PanelRegistry.GetPanelTitle(targetPanelId)}");
                return info;
            }

            // Cuadrante más cercano (mínima distancia al borde)
            var distLeft = relX;
            var distRight = 1.0 - relX;
            var distTop = relY;
            var distBottom = 1.0 - relY;
            var minDist = Math.Min(Math.Min(distLeft, distRight), Math.Min(distTop, distBottom));

            if (minDist == distLeft)
            {
                info.Type = DropTargetType.PanelLeft;
                ShowPreview(bounds.X + 2, bounds.Y + 2, (bounds.Width * 0.5) - 4, bounds.Height - 4, "Dividir a la izquierda (50%)");
                return info;
            }

            if (minDist == distRight)
            {
                info.Type = DropTargetType.PanelRight;
                ShowPreview(bounds.X + (bounds.Width * 0.5) + 2, bounds.Y + 2, (bounds.Width * 0.5) - 4, bounds.Height - 4, "Dividir a la derecha (50%)");
                return info;
            }

            if (minDist == distTop)
            {
                info.Type = DropTargetType.PanelTop;
                ShowPreview(bounds.X + 2, bounds.Y + 2, bounds.Width - 4, (bounds.Height * 0.5) - 4, "Dividir arriba (50%)");
                return info;
            }

            if (minDist == distBottom)
            {
                info.Type = DropTargetType.PanelBottom;
                ShowPreview(bounds.X + 2, bounds.Y + (bounds.Height * 0.5) + 2, bounds.Width - 4, (bounds.Height * 0.5) - 4, "Dividir abajo (50%)");
                return info;
            }
        }

        return info;
    }

    private void ShowPreview(double x, double y, double width, double height, string feedback)
    {
        Canvas.SetLeft(DropPreviewBorder, Math.Max(0, x));
        Canvas.SetTop(DropPreviewBorder, Math.Max(0, y));
        DropPreviewBorder.Width = Math.Max(30, width);
        DropPreviewBorder.Height = Math.Max(30, height);
        DropFeedbackText.Text = feedback;
        DropPreviewBorder.IsVisible = true;
    }
}
