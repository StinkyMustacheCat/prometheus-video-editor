using System;
using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Client.Services;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public partial class PropertiesPanelView
{
    private static readonly string[] TransitionKinds =
        [VideoTransitionOperations.Dissolve, VideoTransitionOperations.DipBlack, VideoTransitionOperations.DipWhite];
    private bool _updatingTransition;
    private string[] _transitionChoices = TransitionKinds;

    private void InitializeTransitionControls()
    {
        TransitionKind.ItemsSource = TransitionKinds.Select(VideoTransitionOperations.Name).ToArray();
        TransitionKind.SelectionChanged += (_, _) =>
        {
            if (!_updatingTransition && CurrentTransition() is { } transition) UpdateTransitionLimit(transition);
        };
        TransitionApply.Click += (_, _) => SaveTransitionControls();
    }

    private VideoTransition? CurrentTransition() => _controller?.CurrentProject.ActiveSequence?.VideoTransitions
        .FirstOrDefault(t => t.Id == _controller.SelectedTransitionId);

    private AudioTransition? CurrentAudioTransition() => _controller?.CurrentProject.ActiveSequence?.AudioTransitions
        .FirstOrDefault(t => t.Id == _controller.SelectedTransitionId);

    private void RefreshTransitionControls()
    {
        var transition = CurrentTransition();
        var audio = CurrentAudioTransition();
        var active = transition != null || audio != null;
        TransitionControls.IsVisible = active;
        InspectorTabsHost.IsVisible = InspectorScroll.IsVisible = InspectorKeyframeHost.IsVisible = !active;
        if (!active || _controller?.CurrentProject is not { ActiveSequence: { } sequence } project)
        {
            RefreshTransitionParameters(null, false);
            return;
        }
        _updatingTransition = true;
        try
        {
            SummaryCapsule.IsVisible = true;
            if (audio != null)
            {
                RefreshTransitionParameters(null, false);
                RefreshAudioTransitionControls(project, sequence, audio);
                return;
            }
            if (transition == null) return;
            var templates = MgtTransitionCatalog.All(project).ToDictionary(t => t.Id, StringComparer.Ordinal);
            if (transition.Mgt is { } saved) templates.TryAdd(saved.Id, saved);
            _transitionChoices = TransitionKinds.Concat(templates.Keys).ToArray();
            TransitionKind.ItemsSource = _transitionChoices.Select(id =>
                templates.TryGetValue(id, out var mgt) ? mgt.Name : VideoTransitionOperations.Name(id)).ToArray();
            TransitionDurationLabel.Text = "Duración en fotogramas";
            TransitionDuration.FormatString = "0";
            TransitionDuration.Increment = 1;
            SummaryName.Text = VideoTransitionOperations.Name(transition);
            var window = VideoTransitionOperations.Window(sequence, transition);
            SummaryDetails.Text = window == null ? "" : $"Corte · {window.Cut:0.000} s";
            TransitionKind.SelectedIndex = Array.IndexOf(_transitionChoices, transition.Kind);
            UpdateTransitionLimit(transition);
            TransitionDuration.Value = transition.DurationFrames;
            RefreshTransitionParameters(transition, TransitionKind.IsEnabled);
        }
        finally { _updatingTransition = false; }
    }

    private void RefreshAudioTransitionControls(Project project, Sequence sequence, AudioTransition transition)
    {
        SummaryName.Text = AudioTransitionOperations.Name(transition.Kind);
        SummaryDetails.Text = "Transición de audio";
        TransitionKind.ItemsSource = new[] { SummaryName.Text };
        TransitionKind.SelectedIndex = 0;
        TransitionKind.IsEnabled = false;
        TransitionDurationLabel.Text = "Duración (ms)";
        TransitionDuration.FormatString = "0.###";
        TransitionDuration.Increment = 10;
        var maximum = AudioTransitionOperations.MaximumSeconds(project, sequence, transition.Kind,
            transition.ClipId, transition.RightClipId);
        var span = AudioTransitionOperations.Span(sequence.Clips, transition);
        var locked = sequence.Tracks.Any(t => t.Id == span?.Clip.TrackId && t.Locked);
        TransitionApply.IsEnabled = TransitionDuration.IsEnabled = !locked && maximum > 0;
        TransitionDuration.Minimum = (decimal)(1000d / 48000);
        TransitionDuration.Maximum = Math.Max(TransitionDuration.Minimum, (decimal)(maximum * 1000));
        TransitionDuration.Value = (decimal)(transition.DurationSeconds * 1000);
        TransitionHint.Text = locked ? "Pista bloqueada." : $"Máximo disponible: {maximum * 1000:0.###} ms.";
    }

    private void UpdateTransitionLimit(VideoTransition transition)
    {
        if (_controller?.CurrentProject is not { ActiveSequence: { } sequence } project ||
            TransitionKind.SelectedIndex < 0) return;
        var selectedKind = _transitionChoices[TransitionKind.SelectedIndex];
        var mgt = transition.Kind == selectedKind ? transition.Mgt : MgtTransitionCatalog.Find(project, selectedKind);
        var max = VideoTransitionOperations.MaximumFrames(project, sequence, transition.LeftClipId,
            transition.RightClipId, selectedKind, mgt);
        var min = VideoTransitionOperations.MinimumFrames(selectedKind, sequence.Fps.Seconds, mgt);
        var window = VideoTransitionOperations.Window(sequence, transition);
        var locked = sequence.Tracks.Any(t => t.Id == window?.Left.TrackId && t.Locked);
        TransitionKind.IsEnabled = !locked;
        TransitionApply.IsEnabled = TransitionDuration.IsEnabled = !locked && max >= min;
        TransitionDuration.Minimum = min;
        TransitionDuration.Maximum = Math.Max(min, max);
        TransitionHint.Text = locked ? "Pista bloqueada." : max >= min
            ? $"Máximo disponible: {max} fotogramas ({max / Math.Max(1, sequence.Fps.Seconds):0.00} s)."
            : "Este corte no tiene suficientes fotogramas para ese tipo de transición.";
    }

    private void SaveTransitionControls()
    {
        if (_controller == null || TransitionDuration.Value is not { } value || TransitionKind.SelectedIndex < 0) return;
        try
        {
            if (CurrentAudioTransition() is { } audio)
            {
                _controller.UndoRedo.Commit(project =>
                    AudioTransitionOperations.Apply(project, audio.Kind, audio.ClipId, audio.RightClipId,
                        (double)value / 1000), "Ajustar fundido de audio");
                return;
            }
            if (CurrentTransition() is not { } transition) return;
            var kind = _transitionChoices[TransitionKind.SelectedIndex];
            _controller.UndoRedo.Commit(project =>
                VideoTransitionOperations.Apply(project, transition.LeftClipId, transition.RightClipId, kind,
                    (int)Math.Round(value), kind == transition.Kind ? transition.Mgt : MgtTransitionCatalog.Find(project, kind)),
                "Ajustar transición");
        }
        catch (Exception error) { _controller.ReportStatus(error.Message); }
    }
}
