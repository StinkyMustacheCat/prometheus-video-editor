using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using Avalonia.Controls;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public partial class PropertiesPanelView
{
    private readonly List<(string Target, string Label)> _maskTargetChoices = new();
    private readonly DispatcherTimer _samRefinementTimer = new()
        { Interval = TimeSpan.FromMilliseconds(160) };
    private string? _samPendingClipId;
    private string? _samPendingMaskId;
    private SamMaskRefinement? _samPendingRefinement;

    private void SetupMaskTarget()
    {
        _samRefinementTimer.Tick += (_, _) => CommitSamRefinement();
        MaskNameBox.KeyDown += (_, e) =>
        { if (e.Key == Avalonia.Input.Key.Enter) { CommitMaskName(); e.Handled = true; } };
        MaskNameBox.LostFocus += (_, _) => CommitMaskName();
        MaskBlendBox.SelectionChanged += (_, _) =>
        {
            if (_syncing || _controller == null || _selectedId == null) return;
            var selectedId = _controller.SelectedMaskId;
            var blend = MaskBlendBox.SelectedIndex switch
            { 1 => "subtract", 2 => "intersect", _ => "add" };
            if (selectedId == null) return;
            _controller.UndoRedo.Commit(project =>
            {
                var clip = Locate(project, _selectedId)?.Clip;
                var layer = clip == null ? null : ClipMaskStack.Edit(clip)
                    .FirstOrDefault(item => item.Id == selectedId);
                if (layer != null) layer.Blend = blend;
            }, "Combinar máscara");
        };
        MaskUpButton.Click += (_, _) => MoveSelectedMask(-1);
        MaskDownButton.Click += (_, _) => MoveSelectedMask(1);
        MaskDeleteButton.Click += (_, _) => DeleteSelectedMask();
        MaskAddSamButton.Click += (_, _) => AddSamMaskLayer();
        MaskShapeAddKeyButton.Click += (_, _) => AddShapeKeyframe();
        MaskShapeRemoveKeyButton.Click += (_, _) => RemoveShapeKeyframe();
        void Adjust()
        {
            MaskFeatherText.Text = $"{MaskFeatherSlider.Value:0.#} px";
            MaskExpandText.Text = $"{MaskExpandSlider.Value:+0.#;-0.#;0} px";
            if (_syncing || _controller == null || _selectedId == null) return;
            _samPendingClipId = _selectedId;
            _samPendingMaskId = _controller.SelectedMaskId;
            _samPendingRefinement = new SamMaskRefinement(MaskFeatherSlider.Value,
                MaskExpandSlider.Value, MaskInvertCheck.IsChecked == true).Clamped();
            _samRefinementTimer.Stop();
            _samRefinementTimer.Start();
        }
        MaskFeatherSlider.ValueChanged += (_, _) => Adjust();
        MaskExpandSlider.ValueChanged += (_, _) => Adjust();
        MaskInvertCheck.IsCheckedChanged += (_, _) => Adjust();
        MaskTargetBox.SelectionChanged += (_, _) =>
        {
            if (_syncing || _controller == null || _selectedId == null ||
                MaskTargetBox.SelectedIndex is < 0 ||
                MaskTargetBox.SelectedIndex >= _maskTargetChoices.Count) return;
            var target = _maskTargetChoices[MaskTargetBox.SelectedIndex].Target;
            var label = _maskTargetChoices[MaskTargetBox.SelectedIndex].Label;
            var clip = Locate(_controller.CurrentProject, _selectedId)?.Clip;
            var maskId = _controller.SelectedMaskId;
            if (clip == null || maskId == null ||
                ClipMaskStack.Read(clip).FirstOrDefault(layer => layer.Id == maskId)?.Target == target)
                return;
            CommitSamRefinement();
            CommitPending(); CommitMotionEdits(); CommitColorEdits(); CommitImageEdits();
            var clipId = _selectedId;
            _controller.UndoRedo.Commit(project =>
            {
                var selected = Locate(project, clipId)?.Clip;
                if (selected == null) return;
                var layer = ClipMaskStack.Edit(selected).FirstOrDefault(item => item.Id == maskId);
                if (layer != null) layer.Target = target;
            }, "Asignar máscara");
            _controller.ReportStatus("Máscara asignada a " + label);
        };
    }

    private void RefreshMaskTargetChoices(Clip? clip)
    {
        RefreshMaskRows(clip);
        _maskTargetChoices.Clear();
        _maskTargetChoices.Add((SamMaskTarget.Opacity, "Opacidad"));
        _maskTargetChoices.Add((SamMaskTarget.Unassigned, "Sin aplicar"));
        if (clip != null)
            foreach (var effect in clip.Effects.Where(SamMaskTarget.IsCompatible))
            {
                var name = Services.EffectCatalog.Available.FirstOrDefault(item =>
                    item.Id == effect.EffectId)?.Name ?? effect.EffectId;
                _maskTargetChoices.Add((SamMaskTarget.ForEffect(effect), "Efecto · " + name));
            }
        MaskTargetBox.ItemsSource = _maskTargetChoices.Select(choice => choice.Label).ToArray();
        var current = SelectedMask(clip)?.Target ?? clip?.SamMaskTarget ?? SamMaskTarget.Opacity;
        var index = _maskTargetChoices.FindIndex(choice => choice.Target == current);
        MaskTargetBox.SelectedIndex = index >= 0 ? index : 0;
        var path = _controller?.CurrentProject.Assets.FirstOrDefault(asset =>
            asset.Id == clip?.AssetId)?.EffectiveLocator?.Path;
        var selected = SelectedMask(clip);
        var problem = clip != null && selected?.Kind == "sam" &&
            !StillImageRules.IsStill(_controller?.CurrentProject, clip) &&
            selected.SamMaskTracks.LastOrDefault() is { } track
            ? SamMaskTrackValidity.Problem(clip, track, path) : null;
        MaskStatusText.Text = problem ?? string.Empty;
        MaskStatusText.IsVisible = problem != null;
    }

    private void RefreshSamRefinement(Clip? clip)
    {
        var selected = SelectedMask(clip);
        var settings = _samPendingClipId == clip?.Id &&
            _samPendingMaskId == _controller?.SelectedMaskId &&
            _samPendingRefinement is { } pending
            ? pending : new SamMaskRefinement(selected?.FeatherPixels ??
                clip?.SamMaskFeatherPixels ?? 0, selected?.ExpandPixels ??
                clip?.SamMaskExpandPixels ?? 0, selected?.Inverted ??
                clip?.SamMaskInverted == true).Clamped();
        MaskFeatherSlider.Value = settings.FeatherPixels;
        MaskExpandSlider.Value = settings.ExpandPixels;
        MaskInvertCheck.IsChecked = settings.Inverted;
        MaskFeatherText.Text = $"{settings.FeatherPixels:0.#} px";
        MaskExpandText.Text = $"{settings.ExpandPixels:+0.#;-0.#;0} px";
    }

    private void CommitSamRefinement()
    {
        _samRefinementTimer.Stop();
        var settings = _samPendingRefinement;
        var clipId = _samPendingClipId;
        var selectedId = _samPendingMaskId;
        _samPendingRefinement = null;
        _samPendingClipId = null;
        _samPendingMaskId = null;
        if (settings == null || clipId == null || _controller == null) return;
        var current = Locate(_controller.CurrentProject, clipId)?.Clip;
        var selected = current == null ? null : ClipMaskStack.Read(current)
            .FirstOrDefault(layer => layer.Id == selectedId);
        if (current == null || current.MaskLayersInitialized && selected == null ||
            selected != null &&
            selected.FeatherPixels == settings.Value.FeatherPixels &&
            selected.ExpandPixels == settings.Value.ExpandPixels &&
            selected.Inverted == settings.Value.Inverted) return;
        _controller.UndoRedo.Commit(project =>
        {
            var clip = Locate(project, clipId)?.Clip;
            if (clip == null) return;
            var layer = selectedId == null ? null : ClipMaskStack.Edit(clip)
                .FirstOrDefault(item => item.Id == selectedId);
            if (layer != null)
            {
                layer.FeatherPixels = settings.Value.FeatherPixels;
                layer.ExpandPixels = settings.Value.ExpandPixels;
                layer.Inverted = settings.Value.Inverted;
                return;
            }
            clip.SamMaskFeatherPixels = settings.Value.FeatherPixels;
            clip.SamMaskExpandPixels = settings.Value.ExpandPixels;
            clip.SamMaskInverted = settings.Value.Inverted;
        }, "Ajustar borde de máscara");
    }

    private ClipMaskLayer? SelectedMask(Clip? clip) => clip == null ? null :
        ClipMaskStack.Read(clip).FirstOrDefault(layer => layer.Id ==
            _controller?.SelectedMaskId) ?? ClipMaskStack.Read(clip).LastOrDefault();

    private void RefreshMaskRows(Clip? clip)
    {
        MaskLayerRows.Children.Clear();
        var layers = clip == null ? [] : ClipMaskStack.Read(clip).ToArray();
        if (layers.Length == 0)
        {
            MaskNameBox.IsVisible = MaskBlendBox.IsVisible = false;
            MaskUpButton.IsVisible = MaskDownButton.IsVisible = MaskDeleteButton.IsVisible = false;
            MaskShapeAnimationPanel.IsVisible = false;
            return;
        }
        if (_controller?.SelectedMaskId == null ||
            !layers.Any(layer => layer.Id == _controller.SelectedMaskId))
            _controller?.SelectMask(layers[^1].Id);
        foreach (var layer in layers)
        {
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 4 };
            var enabled = new CheckBox { IsChecked = layer.Enabled,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            enabled.IsCheckedChanged += (_, _) =>
            {
                if (_controller == null || _selectedId == null) return;
                var clipId = _selectedId; var id = layer.Id;
                var active = enabled.IsChecked == true;
                _controller.UndoRedo.Commit(project =>
                {
                    var target = Locate(project, clipId)?.Clip;
                    var item = target == null ? null : ClipMaskStack.Edit(target)
                        .FirstOrDefault(mask => mask.Id == id);
                    if (item != null) item.Enabled = active;
                }, active ? "Activar máscara" : "Desactivar máscara");
            };
            var button = new Button { Content = layer.Name +
                (layer.Kind == "sam" ? " · SAM" : " · " +
                    (layer.Shape?.Kind == "ellipse" ? "Elipse" :
                     layer.Shape?.Kind == "polygon" ? "Pluma" : "Rectángulo")),
                FontSize = 10, Height = 25,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            button.Classes.Add(layer.Id == _controller?.SelectedMaskId ? "nle-accent" : "nle-btn");
            button.Click += (_, _) =>
            {
                CommitSamRefinement();
                _controller?.SelectMask(layer.Id);
                RefreshSelection();
            };
            row.Children.Add(enabled); row.Children.Add(button);
            MaskLayerRows.Children.Add(row);
        }
        var selected = SelectedMask(clip)!;
        MaskNameBox.IsVisible = MaskBlendBox.IsVisible = true;
        MaskUpButton.IsVisible = MaskDownButton.IsVisible = MaskDeleteButton.IsVisible = true;
        MaskNameBox.Text = selected.Name;
        MaskBlendBox.SelectedIndex = selected.Blend switch
        { "subtract" => 1, "intersect" => 2, _ => 0 };
        var index = Array.FindIndex(layers, layer => layer.Id == selected.Id);
        MaskUpButton.IsEnabled = index > 0;
        MaskDownButton.IsEnabled = index < layers.Length - 1;
        MaskBlendBox.IsEnabled = index > 0;
        MaskShapeAnimationPanel.IsVisible = selected.Kind == "shape";
        if (selected.Kind == "shape")
            RefreshShapeKeyframeStatus(clip!, selected);
    }

    private void CommitMaskName()
    {
        if (_syncing || _controller == null || _selectedId == null ||
            _controller.SelectedMaskId is not { } maskId) return;
        var name = (MaskNameBox.Text ?? "").Trim();
        if (name.Length is < 1 or > 64) return;
        var clipId = _selectedId;
        if (SelectedMask(Locate(_controller.CurrentProject, clipId)?.Clip)?.Name == name) return;
        _controller.UndoRedo.Commit(project =>
        {
            var clip = Locate(project, clipId)?.Clip;
            var layer = clip == null ? null : ClipMaskStack.Edit(clip)
                .FirstOrDefault(item => item.Id == maskId);
            if (layer != null) layer.Name = name;
        }, "Renombrar máscara");
    }

    private void MoveSelectedMask(int delta)
    {
        if (_controller == null || _selectedId == null ||
            _controller.SelectedMaskId is not { } maskId) return;
        var clipId = _selectedId;
        _controller.UndoRedo.Commit(project =>
        {
            var clip = Locate(project, clipId)?.Clip;
            if (clip == null) return;
            var layers = ClipMaskStack.Edit(clip);
            var index = layers.FindIndex(layer => layer.Id == maskId);
            if (index < 0 || index + delta < 0 || index + delta >= layers.Count) return;
            (layers[index], layers[index + delta]) = (layers[index + delta], layers[index]);
            layers[0].Blend = "add";
        }, "Reordenar máscaras");
    }

    private void DeleteSelectedMask()
    {
        if (_controller == null || _selectedId == null ||
            _controller.SelectedMaskId is not { } maskId) return;
        var clipId = _selectedId;
        _controller.UndoRedo.Commit(project =>
        {
            var clip = Locate(project, clipId)?.Clip;
            if (clip == null) return;
            var layers = ClipMaskStack.Edit(clip);
            layers.RemoveAll(layer => layer.Id == maskId);
            if (layers.Count > 0) layers[0].Blend = "add";
        }, "Quitar máscara seleccionada");
        var next = Locate(_controller.CurrentProject, clipId)?.Clip;
        _controller.SelectMask(next == null ? null : ClipMaskStack.Read(next).LastOrDefault()?.Id);
    }

    private void AddSamMaskLayer()
    {
        if (_controller == null || _selectedId == null) return;
        CommitSamRefinement();
        var clipId = _selectedId;
        var id = Guid.NewGuid().ToString("N");
        _controller.UndoRedo.Commit(project =>
        {
            var clip = Locate(project, clipId)?.Clip;
            if (clip == null) return;
            var layers = ClipMaskStack.Edit(clip);
            layers.Add(new ClipMaskLayer { Id = id, Kind = "sam",
                Name = "SAM " + (layers.Count(layer => layer.Kind == "sam") + 1),
                Target = layers.LastOrDefault()?.Target ?? SamMaskTarget.Opacity });
        }, "Crear máscara SAM");
        _controller.SelectMask(id);
        _controller.ReportStatus("Máscara SAM creada · marca el objeto en el monitor");
    }

    private double CurrentShapeSourceSeconds(Clip clip) => clip.SourceIn.Seconds +
        Math.Max(0, SelectedLocalTime - clip.TimelineStart.Seconds) * clip.Speed.Seconds;

    private void RefreshShapeKeyframeStatus(Clip clip, ClipMaskLayer layer)
    {
        var seconds = CurrentShapeSourceSeconds(clip);
        var fps = _controller?.CurrentProject.ActiveSequence?.Fps.Seconds ?? 60;
        var atKey = layer.ShapeKeyframes.Any(key =>
            Math.Abs(key.SourceSeconds - seconds) <= 0.5 / Math.Max(1, fps) + 0.000001);
        MaskShapeKeyframeText.Text = layer.ShapeKeyframes.Count == 0
            ? "Forma fija en todo el medio"
            : layer.ShapeKeyframes.Count + " claves · mueve el cabezal y edita la forma";
        MaskShapeAddKeyButton.IsEnabled = !atKey;
        MaskShapeRemoveKeyButton.IsEnabled = atKey;
    }

    private void AddShapeKeyframe()
    {
        if (_controller == null || _selectedId == null ||
            _controller.SelectedMaskId is not { } maskId) return;
        var clip = Locate(_controller.CurrentProject, _selectedId)?.Clip;
        var layer = clip == null ? null : ClipMaskStack.Read(clip)
            .FirstOrDefault(item => item.Id == maskId && item.Kind == "shape");
        if (clip == null || layer?.Shape == null) return;
        var seconds = CurrentShapeSourceSeconds(clip);
        var fps = _controller.CurrentProject.ActiveSequence?.Fps.Seconds ?? 60;
        var shape = ShapeMaskAnimation.Evaluate(layer, seconds, fps)!;
        var clipId = clip.Id;
        _controller.UndoRedo.Commit(project =>
        {
            var target = Locate(project, clipId)?.Clip;
            var selected = target == null ? null : ClipMaskStack.Edit(target)
                .FirstOrDefault(item => item.Id == maskId);
            if (selected?.Shape == null) return;
            if (selected.ShapeKeyframes.Count == 0 &&
                Math.Abs(seconds - target!.SourceIn.Seconds) > 0.5 / Math.Max(1, fps))
                ShapeMaskAnimation.Upsert(selected, target.SourceIn.Seconds, fps,
                    selected.Shape);
            ShapeMaskAnimation.Upsert(selected, seconds, fps, shape);
        }, "Añadir clave de forma");
    }

    private void RemoveShapeKeyframe()
    {
        if (_controller == null || _selectedId == null ||
            _controller.SelectedMaskId is not { } maskId) return;
        var clip = Locate(_controller.CurrentProject, _selectedId)?.Clip;
        if (clip == null) return;
        var seconds = CurrentShapeSourceSeconds(clip);
        var fps = _controller.CurrentProject.ActiveSequence?.Fps.Seconds ?? 60;
        var clipId = clip.Id;
        _controller.UndoRedo.Commit(project =>
        {
            var target = Locate(project, clipId)?.Clip;
            var selected = target == null ? null : ClipMaskStack.Edit(target)
                .FirstOrDefault(item => item.Id == maskId && item.Kind == "shape");
            if (selected == null) return;
            selected.ShapeKeyframes.RemoveAll(key => Math.Abs(key.SourceSeconds - seconds) <=
                0.5 / Math.Max(1, fps) + 0.000001);
        }, "Quitar clave de forma");
    }
}
