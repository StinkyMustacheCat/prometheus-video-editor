using System;
using System.Linq;
using System.Text.Json;
using Avalonia.Threading;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public partial class PropertiesPanelView
{
    private readonly DispatcherTimer _textEditTimer = new()
        { Interval = TimeSpan.FromMilliseconds(320) };
    private string? _pendingTextClipId;

    private void InitializeTextEditors()
    {
        _textEditTimer.Tick += (_, _) => CommitTextEdits();
        void Changed()
        {
            TextSizeValue.Text = Math.Round(TextSizeSlider.Value).ToString();
            TextOutlineValue.Text = $"{TextOutlineSlider.Value:0.#} px";
            TextShadowValue.Text = $"{TextShadowSlider.Value:P0}";
            TextXValue.Text = $"{TextXSlider.Value:P0}";
            TextYValue.Text = $"{TextYSlider.Value:P0}";
            TextWidthValue.Text = $"{TextWidthSlider.Value:P0}";
            if (_syncing || _controller == null || _selectedId == null) return;
            _pendingTextClipId = _selectedId;
            _textEditTimer.Stop();
            _textEditTimer.Start();
        }
        TextContentBox.TextChanged += (_, _) => Changed();
        TextFontBox.TextChanged += (_, _) => Changed();
        TextColorBox.HexChanged += (_, _) => Changed();
        TextOutlineColorBox.HexChanged += (_, _) => Changed();
        TextColorBox.PickRequested += (_, _) => BeginTextColorPick(TextColorBox);
        TextOutlineColorBox.PickRequested += (_, _) => BeginTextColorPick(TextOutlineColorBox);
        TextSizeSlider.ValueChanged += (_, _) => Changed();
        TextOutlineSlider.ValueChanged += (_, _) => Changed();
        TextShadowSlider.ValueChanged += (_, _) => Changed();
        TextXSlider.ValueChanged += (_, _) => Changed();
        TextYSlider.ValueChanged += (_, _) => Changed();
        TextWidthSlider.ValueChanged += (_, _) => Changed();
        TextAlignmentBox.SelectionChanged += (_, _) => Changed();
        TextContentBox.LostFocus += (_, _) => CommitTextEdits();
        TextFontBox.LostFocus += (_, _) => CommitTextEdits();
        TextColorBox.LostFocus += (_, _) => CommitTextEdits();
        TextOutlineColorBox.LostFocus += (_, _) => CommitTextEdits();
        TextFadeInButton.Click += (_, _) => ApplyTextAnimation("fade-in");
        TextFadeOutButton.Click += (_, _) => ApplyTextAnimation("fade-out");
        TextSlideInButton.Click += (_, _) => ApplyTextAnimation("slide-in");
    }

    private void RefreshTextEditors(Clip? clip)
    {
        var asset = _controller?.CurrentProject.Assets.FirstOrDefault(item => item.Id == clip?.AssetId);
        var text = asset?.TextOverlay;
        if (text == null) return;
        if (_pendingTextClipId == clip?.Id) return;
        TextContentBox.Text = text.Content;
        TextFontBox.Text = text.FontFamily;
        TextSizeSlider.Value = text.FontSize;
        TextColorBox.Hex = text.Color;
        TextOutlineSlider.Value = text.OutlinePixels;
        TextOutlineColorBox.Hex = text.OutlineColor;
        TextShadowSlider.Value = text.ShadowOpacity;
        TextAlignmentBox.SelectedIndex = text.Alignment switch
        { "left" => 0, "right" => 2, _ => 1 };
        TextXSlider.Value = text.PositionX;
        TextYSlider.Value = text.PositionY;
        TextWidthSlider.Value = text.WrapWidth;
        TextSizeValue.Text = Math.Round(text.FontSize).ToString();
        TextOutlineValue.Text = $"{text.OutlinePixels:0.#} px";
        TextShadowValue.Text = $"{text.ShadowOpacity:P0}";
        TextXValue.Text = $"{text.PositionX:P0}";
        TextYValue.Text = $"{text.PositionY:P0}";
        TextWidthValue.Text = $"{text.WrapWidth:P0}";
    }

    private void CommitTextEdits()
    {
        _textEditTimer.Stop();
        var clipId = _pendingTextClipId;
        _pendingTextClipId = null;
        if (clipId == null || _controller == null) return;
        var clip = Locate(_controller.CurrentProject, clipId)?.Clip;
        var asset = _controller.CurrentProject.Assets.FirstOrDefault(item => item.Id == clip?.AssetId);
        if (asset?.TextOverlay == null) return;
        var updated = new TextOverlay
        {
            Kind = asset.TextOverlay.Kind,
            Content = TextContentBox.Text?.Trim() ?? "",
            FontFamily = TextFontBox.Text?.Trim() ?? "",
            FontSize = Math.Round(TextSizeSlider.Value),
            Color = NormalizeTextColor(TextColorBox.Hex),
            OutlineColor = NormalizeTextColor(TextOutlineColorBox.Hex),
            OutlinePixels = Math.Round(TextOutlineSlider.Value, 1),
            ShadowOpacity = Math.Round(TextShadowSlider.Value, 2),
            Alignment = TextAlignmentBox.SelectedIndex switch
                { 0 => "left", 2 => "right", _ => "center" },
            PositionX = TextXSlider.Value,
            PositionY = TextYSlider.Value,
            BoxWidth = Math.Abs(TextWidthSlider.Value - asset.TextOverlay.WrapWidth) < 1e-6
                ? asset.TextOverlay.BoxWidth : TextWidthSlider.Value
        };
        try { updated.Validate(); }
        catch
        {
            _controller.ReportStatus("Texto: revisa el contenido, la fuente y los colores #RRGGBB");
            return;
        }
        if (JsonSerializer.Serialize(asset.TextOverlay) == JsonSerializer.Serialize(updated)) return;
        var assetId = asset.Id;
        _controller.UndoRedo.Commit(project =>
        {
            var target = project.Assets.FirstOrDefault(item => item.Id == assetId);
            var selected = Locate(project, clipId)?.Clip;
            if (target == null || selected == null || selected.AssetId != assetId) return;
            if (NestedMediaOperations.FindAssetOccurrences(project, assetId).Count > 1)
            {
                target = JsonSerializer.Deserialize<Asset>(JsonSerializer.Serialize(target))!;
                target.Id = Guid.NewGuid().ToString("N");
                target.Source.Uri = "text:" + target.Id;
                target.Streams.ForEach(stream => stream.Id = Guid.NewGuid().ToString("N"));
                project.Assets.Add(target);
                selected.AssetId = target.Id;
                selected.StreamId = target.Streams[0].Id;
            }
            target.TextOverlay = updated;
            target.Name = GeneratedText.DisplayName(updated);
            selected.Name = target.Name;
        }, "Editar texto");
    }

    private static string NormalizeTextColor(string? value)
    {
        var color = (value ?? "").Trim().ToUpperInvariant();
        return color.Length == 6 ? "#" + color : color;
    }

    private void BeginTextColorPick(ColorField field)
    {
        if (_selectedId == null || _controller == null) return;
        var clipId = _selectedId;
        ChromaColorPicker.BeginComposite(color =>
        {
            if (_selectedId != clipId) return;
            field.Hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            _pendingTextClipId = clipId;
            CommitTextEdits();
        });
        _controller.ReportStatus("Gotero activo: elige un color en Previsualización");
    }

    private void ApplyTextAnimation(string mode)
    {
        CommitTextEdits();
        if (_controller == null || _selectedId == null) return;
        var clipId = _selectedId;
        var selected = Locate(_controller.CurrentProject, clipId)?.Clip;
        if (selected == null || _controller.CurrentProject.Assets
            .FirstOrDefault(asset => asset.Id == selected.AssetId)?.TextOverlay == null) return;
        _controller.UndoRedo.Commit(project =>
        {
            var clip = Locate(project, clipId)?.Clip;
            if (clip == null) return;
            var start = clip.TimelineStart.Seconds;
            var end = clip.EndSeconds;
            var span = Math.Min(0.45, clip.Duration.Seconds * 0.25);
            if (mode == "fade-in")
            {
                KeyframeAnimation.Set(clip, "visual.opacity", start, 0, "ease-out");
                KeyframeAnimation.Set(clip, "visual.opacity", start + span,
                    clip.Visual.Opacity);
            }
            else if (mode == "fade-out")
            {
                KeyframeAnimation.Set(clip, "visual.opacity", end - span,
                    clip.Visual.Opacity, "ease-in");
                KeyframeAnimation.Set(clip, "visual.opacity", end, 0);
            }
            else
            {
                KeyframeAnimation.Set(clip, "visual.position_y", start,
                    clip.Visual.PositionY + 0.12, "ease-out");
                KeyframeAnimation.Set(clip, "visual.position_y", start + span,
                    clip.Visual.PositionY);
            }
        }, "Animar texto");
        SetKeyframeDrawer(true);
    }
}
