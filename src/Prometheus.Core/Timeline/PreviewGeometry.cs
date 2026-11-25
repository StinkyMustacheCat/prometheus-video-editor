using System;
using Prometheus.Core.Model;

namespace Prometheus.Core.Timeline;

public readonly record struct PreviewRectangle(double X, double Y, double Width, double Height);

/// <summary>
/// Maps a sequence canvas into a monitor viewport, then a clip into that canvas.
/// Monitor zoom and panel size do not change the clip's saved transform.
/// </summary>
public static class PreviewGeometry
{
    public static (double X, double Y) RotateDelta(double x, double y, double degrees)
    {
        var angle = degrees * Math.PI / 180;
        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        return (x * cosine - y * sine, x * sine + y * cosine);
    }

    public static (double X, double Y) RotateLayerPoint(PreviewRectangle rect,
        ClipVisualState state, double relativeX, double relativeY)
    {
        var anchorX = state.FlipX ? 1 - state.AnchorX : state.AnchorX;
        var anchorY = state.FlipY ? 1 - state.AnchorY : state.AnchorY;
        var pivotX = rect.X + anchorX * rect.Width;
        var pivotY = rect.Y + anchorY * rect.Height;
        var offset = RotateDelta((relativeX - anchorX) * rect.Width,
            (relativeY - anchorY) * rect.Height, state.RotationDegrees);
        return (pivotX + offset.X, pivotY + offset.Y);
    }

    public static (double X, double Y)? MapDisplayPointToSource(PreviewRectangle rect,
        ClipVisualState state, double displayX, double displayY)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return null;
        var anchorX = state.FlipX ? 1 - state.AnchorX : state.AnchorX;
        var anchorY = state.FlipY ? 1 - state.AnchorY : state.AnchorY;
        var pivotX = rect.X + anchorX * rect.Width;
        var pivotY = rect.Y + anchorY * rect.Height;
        var unrotated = RotateDelta(displayX - pivotX, displayY - pivotY, -state.RotationDegrees);
        var x = (pivotX + unrotated.X - rect.X) / rect.Width;
        var y = (pivotY + unrotated.Y - rect.Y) / rect.Height;
        if (x < 0 || x >= 1 || y < 0 || y >= 1) return null;
        return (state.FlipX ? 1 - x : x, state.FlipY ? 1 - y : y);
    }

    public static PreviewRectangle FitCanvas(double sequenceWidth, double sequenceHeight,
        double viewportWidth, double viewportHeight)
    {
        if (sequenceWidth <= 0 || sequenceHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
            return default;
        var scale = Math.Min(viewportWidth / sequenceWidth, viewportHeight / sequenceHeight);
        var width = sequenceWidth * scale;
        var height = sequenceHeight * scale;
        return new PreviewRectangle((viewportWidth - width) / 2, (viewportHeight - height) / 2, width, height);
    }

    public static PreviewRectangle ZoomCanvas(double sequenceWidth, double sequenceHeight,
        double viewportWidth, double viewportHeight, double scale, double panX, double panY)
    {
        if (sequenceWidth <= 0 || sequenceHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0 || scale <= 0)
            return default;
        var width = sequenceWidth * scale;
        var height = sequenceHeight * scale;
        return new PreviewRectangle((viewportWidth - width) / 2 + panX,
            (viewportHeight - height) / 2 + panY, width, height);
    }

    public static (double X, double Y) PanForZoom(PreviewRectangle previous,
        double sequenceWidth, double sequenceHeight, double viewportWidth, double viewportHeight,
        double newScale, double pivotX, double pivotY)
    {
        if (previous.Width <= 0 || previous.Height <= 0 || sequenceWidth <= 0 || sequenceHeight <= 0)
            return default;
        var width = sequenceWidth * newScale;
        var height = sequenceHeight * newScale;
        var newX = pivotX - (pivotX - previous.X) / previous.Width * width;
        var newY = pivotY - (pivotY - previous.Y) / previous.Height * height;
        return (newX - (viewportWidth - width) / 2, newY - (viewportHeight - height) / 2);
    }

    public static PreviewRectangle PlaceLayer(double canvasWidth, double canvasHeight,
        double sourceWidth, double sourceHeight, ClipVisualState state)
    {
        if (canvasWidth <= 0 || canvasHeight <= 0 || sourceWidth <= 0 || sourceHeight <= 0)
            return default;
        var fit = Math.Min(canvasWidth / sourceWidth, canvasHeight / sourceHeight);
        var factor = state.Framing switch
        {
            "width" => canvasWidth / sourceWidth,
            "height" => canvasHeight / sourceHeight,
            "fill" => Math.Max(canvasWidth / sourceWidth, canvasHeight / sourceHeight),
            "stretch" => 1,
            _ => fit
        };
        var width = (state.Framing == "stretch" ? canvasWidth : sourceWidth * factor) * Math.Max(0.01, state.ScaleX);
        var height = (state.Framing == "stretch" ? canvasHeight : sourceHeight * factor) * Math.Max(0.01, state.ScaleY);
        return new PreviewRectangle(
            (canvasWidth - width) / 2 + state.PositionX * canvasWidth -
                ((state.FlipX ? 1 - state.AnchorX : state.AnchorX) - 0.5) * width,
            (canvasHeight - height) / 2 + state.PositionY * canvasHeight -
                ((state.FlipY ? 1 - state.AnchorY : state.AnchorY) - 0.5) * height,
            width, height);
    }
}
