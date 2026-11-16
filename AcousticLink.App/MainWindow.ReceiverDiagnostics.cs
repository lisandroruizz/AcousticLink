using AcousticLink.App.Audio;
using AcousticLink.App.Protocol;
using AcousticLink.App.Visualization;

namespace AcousticLink.App;

public partial class MainWindow
{
    private ReceiverSessionDiagnostics? manualDiagnostics;
    private CapturedAudio? manualDiagnosticsAudio;

    private void StartManualDiagnostics(CapturedAudio audio, bool incremental)
    {
        manualDiagnosticsAudio = audio;
        manualDiagnostics = new(IsBfskReceive ? "BFSK" : "FDM",
            incremental ? "Captura manual con decodificación incremental" : "Captura manual sin decodificación",
            true, IsBfskReceive, audio.SampleRate);
        ShowReceiverDiagnostics(manualDiagnostics);
    }

    private void RefreshManualDiagnostics()
    {
        var diagnostics = manualDiagnostics;
        var audio = manualDiagnosticsAudio;
        if (diagnostics is null || audio is null || !ReferenceEquals(audioSelection.Current?.Audio, audio) ||
            continuousListening is not null) return;
        var metrics = audio.GetMetrics();
        var live = ReferenceEquals(liveReception?.Audio, audio) ? liveReception : null;
        diagnostics.SetSamples(metrics.Count, live?.Receiver.Count ?? 0, audio.SampleRate);
        diagnostics.ObserveAcousticPeak(metrics.Peak);
        if (!capture.IsRecording && !captureBusy && metrics.StopReason != CaptureStopReason.None &&
            (live is null || live.Finished || live.Cancellation.IsCancellationRequested))
        {
            if (capture.Error is { } error) diagnostics.DeviceError(error);
            diagnostics.Stop(metrics.StopReason == CaptureStopReason.Manual ? "Detención solicitada; se conservan solo resultados finalizados."
                : $"Captura finalizada: {metrics.StopReason}.");
        }
        ShowReceiverDiagnostics(diagnostics);
    }

    private void ObserveManualReception(CapturedAudio audio, LiveFdmReception snapshot, bool final)
    {
        var diagnostics = manualDiagnostics;
        if (diagnostics is null || !ReferenceEquals(manualDiagnosticsAudio, audio)) return;
        diagnostics.SetSamples(audio.GetStatus().Count, snapshot.SampleCount, audio.SampleRate);
        long key = snapshot.Result.Synchronization.StartSeconds is double start
            ? (long)Math.Round(start * audio.SampleRate) : -1;
        diagnostics.ObserveProgress(snapshot.ObservedResult, key);
        if (final)
        {
            diagnostics.RecordFinalAnalysis(snapshot.Result);
            diagnostics.Stop("Decodificación incremental finalizada; resultados definitivos registrados.");
        }
        ShowReceiverDiagnostics(diagnostics);
    }

    private ReceiverSessionDiagnostics StartHistoricalDiagnostics(CapturedAudio audio)
    {
        manualDiagnosticsAudio = audio;
        string source = audio.GetMetrics().StopReason == CaptureStopReason.Imported ? "WAV importado" : "Captura finalizada";
        manualDiagnostics = new(IsBfskReceive ? "BFSK" : "FDM", source, false, IsBfskReceive,
            audio.SampleRate, acousticSource: source != "WAV importado");
        manualDiagnostics.SetSamples(audio.GetStatus().Count, 0, audio.SampleRate);
        manualDiagnostics.ObserveAcousticPeak(audio.GetMetrics().Peak);
        ShowReceiverDiagnostics(manualDiagnostics);
        return manualDiagnostics;
    }

    private void FinishHistoricalDiagnostics(ReceiverSessionDiagnostics diagnostics, CapturedAudio audio,
        FdmTextReceiveResult result)
    {
        if (!ReferenceEquals(manualDiagnostics, diagnostics) ||
            !ReferenceEquals(audioSelection.Current?.Audio, audio)) return;
        diagnostics.SetSamples(audio.GetStatus().Count, audio.GetStatus().Count, audio.SampleRate);
        long key = result.Synchronization.StartSeconds is double start ? (long)Math.Round(start * audio.SampleRate) : -1;
        diagnostics.ObserveProgress(result, key);
        diagnostics.RecordFinalAnalysis(result);
        diagnostics.Stop("Análisis posterior finalizado; no representa una captura activa.");
        ShowReceiverDiagnostics(diagnostics);
    }

    private void ShowReceiverDiagnostics(ReceiverSessionDiagnostics? diagnostics)
    {
        if (closing || ReceiverSessionDiagnosticsView is null) return;
        if (diagnostics is null) ReceiverSessionDiagnosticsView.Clear();
        else ReceiverSessionDiagnosticsView.Show(diagnostics.Snapshot());
    }
}
