using AcousticLink.App.Audio;
using AcousticLink.App.Protocol;
using System.Windows;

namespace AcousticLink.App;

public partial class MainWindow
{
    private sealed class LiveCaptureReception(CapturedAudio audio, IIncrementalTextReceiver receiver, CancellationTokenSource cancellation)
    {
        public CapturedAudio Audio { get; } = audio;
        public IIncrementalTextReceiver Receiver { get; } = receiver;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public float[] Scratch { get; } = new float[8192];
        public bool Busy, Finished;
        public LiveFdmReception? Snapshot;
        public Task Pending = Task.CompletedTask;
    }
    private LiveCaptureReception? liveReception;
    private Task endingLiveReception = Task.CompletedTask;

    private void StartLiveReception(CapturedAudio audio)
    {
        StopLiveReception(); ResetReceptionObservation();
        IIncrementalTextReceiver receiver = IsBfskReceive
            ? new IncrementalBfskTextReceiver(audio.SampleRate, UseHammingReceiveCheckBox.IsChecked == true,
                RecoverReceiveErasuresCheckBox.IsChecked == true, audio.MaximumDurationSeconds)
            : new IncrementalFdmTextReceiver(audio.SampleRate, UseHammingReceiveCheckBox.IsChecked == true,
                RecoverReceiveErasuresCheckBox.IsChecked == true, audio.MaximumDurationSeconds);
        liveReception = new(audio, receiver,
            CancellationTokenSource.CreateLinkedTokenSource(windowLifetime.Token));
        LiveReceptionStatusText.Text = IsBfskReceive ? "En vivo · buscando cabecera BFSK" : "En vivo · buscando cabeceras A/B";
        ReceptionAView.Clear("Buscando cabecera A…"); ReceptionBView.Clear(IsBfskReceive ? "Canal B no utilizado" : "Buscando cabecera B…");
        CancelReceiveFdmTextButton.IsEnabled = true;
        RefreshReceptionActivity();
    }

    // Invocado por el timer de captura existente (100 ms), nunca por el callback NAudio.
    private void QueueLiveReception()
    {
        var session = liveReception;
        if (closing || session is null || session.Busy || session.Finished || session.Cancellation.IsCancellationRequested
            || !ReferenceEquals(audioSelection.Current?.Audio, session.Audio)) return;
        bool final = !capture.IsRecording && !captureBusy;
        int available = session.Audio.GetStatus().Count;
        if (!final && available == session.Receiver.Count) return;
        session.Busy = true;
        session.Pending = ProcessLiveReceptionAsync(session, available, final);
    }

    private async Task ProcessLiveReceptionAsync(LiveCaptureReception session, int available, bool final)
    {
        CancellationToken token = session.Cancellation.Token;
        try
        {
            var snapshot = await Task.Run(() =>
            {
                LiveFdmReception? last = session.Snapshot;
                while (session.Receiver.Count < available)
                {
                    token.ThrowIfCancellationRequested();
                    int count = Math.Min(session.Scratch.Length, available - session.Receiver.Count);
                    int copied = session.Audio.CopySamples(session.Receiver.Count, session.Scratch.AsSpan(0, count));
                    if (copied == 0) throw new InvalidOperationException("La captura no publicó las muestras esperadas.");
                    last = session.Receiver.Append(session.Scratch.AsSpan(0, copied), token);
                }
                return final ? session.Receiver.Complete(token) : last;
            }, token);
            if (closing || token.IsCancellationRequested || !ReferenceEquals(liveReception, session)
                || !ReferenceEquals(audioSelection.Current?.Audio, session.Audio) || snapshot is null) return;
            bool changed = session.Snapshot?.Result != snapshot.Result;
            session.Snapshot = snapshot; session.Finished = final;
            ObserveManualReception(session.Audio, snapshot, final);
            LiveReceptionStatusText.Text = IsBfskReceive
                ? $"{(final ? "Finalizada" : "Recepción en vivo")} · BFSK: {snapshot.ChannelAStatus}"
                : $"{(final ? "Finalizada" : "Recepción en vivo")} · A: {snapshot.ChannelAStatus} · B: {snapshot.ChannelBStatus}";
            RefreshReceptionActivity();
            if (changed || final) ShowFdmReception(snapshot.ObservedResult, session.Audio, snapshot.SampleCount / (double)session.Audio.SampleRate);
            CancelReceiveFdmTextButton.IsEnabled = !final;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"Recepción incremental: {ex}");
            if (!closing && ReferenceEquals(liveReception, session))
            {
                session.Finished = true; LiveReceptionStatusText.Text = $"Recepción detenida por error: {ex.Message}";
                manualDiagnostics?.Note("Decodificación detenida", ex.Message);
                RefreshReceptionActivity();
                CancelReceiveFdmTextButton.IsEnabled = false;
            }
        }
        finally { session.Busy = false; }
    }

    private void CancelLiveReception()
    {
        if (liveReception is not { Finished: false } session) return;
        session.Cancellation.Cancel(); session.Finished = true;
        manualDiagnostics?.Note("Decodificación cancelada", "El micrófono puede continuar capturando; no se publica una trama parcial como válida.");
        LiveReceptionStatusText.Text = capture.IsRecording ? "Decodificación cancelada · el micrófono sigue capturando." : "Decodificación cancelada.";
        if (session.Snapshot is { } snapshot)
        {
            static FdmTextChannelResult Cancelled(FdmTextChannelResult channel) => channel with
            { IsReceiving = false, Reason = channel.State == FdmTextReceiveState.Valid ? channel.Reason : "Decodificación cancelada. " + channel.Reason };
            var result = snapshot.ObservedResult with { ChannelA = Cancelled(snapshot.ObservedResult.ChannelA), ChannelB = Cancelled(snapshot.ObservedResult.ChannelB) };
            session.Snapshot = snapshot with { Result = result, ObservedResult = result };
            ShowFdmReception(result, session.Audio, snapshot.SampleCount / (double)session.Audio.SampleRate);
        }
        else
        {
            ReceivedFdmText.Text = LiveReceptionStatusText.Text;
            ReceptionAView.Clear(LiveReceptionStatusText.Text); ReceptionBView.Clear(LiveReceptionStatusText.Text);
        }
        CancelReceiveFdmTextButton.IsEnabled = false;
        RefreshReceptionActivity();
    }

    private void StopLiveReception()
    {
        if (liveReception is not { } session) return;
        liveReception = null; session.Cancellation.Cancel();
        endingLiveReception = session.Pending;
        // Una tarea vieja no puede publicar en la sesión nueva. Liberar el CTS cuando su worker ya salió.
        _ = session.Pending.ContinueWith(_ => session.Cancellation.Dispose(), TaskScheduler.Default);
        LiveReceptionStatusText.Text = "Recepción manual";
        CancelReceiveFdmTextButton.IsEnabled = false;
    }
}
