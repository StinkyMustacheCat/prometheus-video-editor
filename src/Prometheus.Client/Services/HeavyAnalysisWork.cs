using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Prometheus.Client.Views;
using Prometheus.Core.Model;

namespace Prometheus.Client.Services;

// One reservation shared by the monitor, transcript panel and MCP. The owner and
// reservation are managed on the UI thread. Workers report progress independently.
internal sealed class HeavyAnalysisWork(Window owner, PlaybackTransport transport)
{
    internal const string BusyMessage = "Hay un análisis en curso. Espera a que termine o cancélalo en la ventana de progreso.";
    internal Session? Active { get; private set; }

    internal void EnsureIdle(Session? permitted = null)
    {
        if (Active != null && Active != permitted) throw new InvalidOperationException(BusyMessage);
    }

    internal Session Begin(string title, Clip clip, string initialStage)
    {
        EnsureIdle();
        var session = new Session(this, initialStage);
        Active = session;
        try
        {
            transport.Pause();
            session.Show(owner, title, clip);
            return session;
        }
        catch
        {
            session.DisposeSource();
            Active = null;
            throw;
        }
    }

    internal sealed record ProgressState(string Stage, string Message, double? Fraction = null);

    internal sealed class Session(HeavyAnalysisWork work, string initialMessage) : IAsyncDisposable,
        IProgress<double>, IProgress<(int Done, int Total)>
    {
        private readonly CancellationTokenSource _source = new();
        private ProgressState _progress = new("preparing", initialMessage);
        private AnalysisProgressDialog? _dialog;
        private Task? _dialogClosed;
        private Task? _cancelling;

        internal CancellationToken Token => _source.Token;
        internal bool IsCancelling => _source.IsCancellationRequested;
        internal ProgressState Progress => Volatile.Read(ref _progress);

        internal void Show(Window owner, string title, Clip clip)
        {
            _dialog = new AnalysisProgressDialog(title, clip, this);
            _dialogClosed = _dialog.ShowDialog(owner);
        }

        internal void Cancel()
        {
            if (_cancelling != null) return;
            // A worker may be blocked in I/O. Run its process-stop callbacks off
            // the UI thread so the user immediately sees the cancellation state.
            _cancelling = _source.CancelAsync();
            _dialog?.RefreshProgress();
        }

        internal void Report(string stage, string message, double? fraction = null) =>
            Volatile.Write(ref _progress, new(stage, message, fraction));

        void IProgress<double>.Report(double value)
        {
            if (value <= 0.08) Report("loading_model", "Cargando el modelo Whisper…");
            else Report("transcribing", "Transcribiendo el audio…", Math.Max(Progress.Fraction ?? 0, value));
        }

        void IProgress<(int Done, int Total)>.Report((int Done, int Total) value)
        {
            if (value.Total > 0)
                Report("tracking", $"Procesando fotogramas · {value.Done:N0} de {value.Total:N0}",
                    (double)value.Done / value.Total);
        }

        internal void DisposeSource() => _source.Dispose();

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_cancelling != null) await _cancelling;
            }
            finally
            {
                try
                {
                    _dialog?.Finish();
                    if (_dialogClosed != null) await _dialogClosed;
                }
                finally
                {
                    DisposeSource();
                    work.Active = null;
                }
            }
        }
    }
}
