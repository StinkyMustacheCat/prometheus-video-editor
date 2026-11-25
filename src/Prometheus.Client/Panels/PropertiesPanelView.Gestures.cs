using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Prometheus.Core.Undo;

namespace Prometheus.Client.Panels;

public partial class PropertiesPanelView
{
    private UndoRedoManager? _sliderGestureHistory;
    private bool _flushingGesture;

    private void InitializePropertyGestures()
    {
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (_controller == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var visual = e.Source as Avalonia.Visual;
            if (visual is not Slider && visual?.FindAncestorOfType<Slider>() == null) return;
            FinishPropertyGesture();
            _sliderGestureHistory = _controller.UndoRedo;
            _sliderGestureHistory.BeginGesture();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left && _sliderGestureHistory != null)
                FinishPropertyGesture();
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        DetachedFromVisualTree += (_, _) => FinishPropertyGesture();
    }

    private void FinishPropertyGesture()
    {
        if (_flushingGesture) return;
        _flushingGesture = true;
        try
        {
            CommitSamRefinement(); CommitPending(); CommitMotionEdits(); CommitColorEdits();
            CommitImageEdits(); CommitAudioEdits(); CommitTextEdits();
        }
        finally
        {
            _sliderGestureHistory?.EndGesture();
            _sliderGestureHistory = null;
            _flushingGesture = false;
        }
    }
}
