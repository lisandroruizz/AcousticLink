using AcousticLink.App.Audio;
using AcousticLink.App.Protocol;
using AcousticLink.App.Visualization;

namespace AcousticLink.App;

public partial class MainWindow
{
    private FdmTextReceiveResult? presentedReception;
    private CapturedAudio? presentedReceptionSource;
    private int presentedReceptionSamples;

    private void RefreshReceptionActivity()
    {
        var audio = audioSelection.Current?.Audio;
        if (audio is null) { LiveReceptionInspector.Clear(); ReceiverSignalMonitor.ClearReceptionActivity(); return; }
        bool active = capture.IsRecording && ReferenceEquals(capture.Recording, audio);
        var session = ReferenceEquals(liveReception?.Audio, audio) ? liveReception : null;
        var snapshot = session?.Snapshot;
        bool cancelled = session?.Cancellation.IsCancellationRequested == true;
        bool processing = session is not null && !cancelled && !session.Finished;
        var result = snapshot?.ObservedResult ?? (ReferenceEquals(presentedReceptionSource, audio) ? presentedReception : null);
        var metrics = audio.GetMetrics();
        bool imported = metrics.StopReason == CaptureStopReason.Imported;
        string mode = active ? session is null ? "CAPTURANDO · RECEPCIÓN MANUAL"
            : cancelled ? "CAPTURANDO · DECODIFICACIÓN CANCELADA" : session.Finished ? "CAPTURANDO · DECODIFICACIÓN DETENIDA"
            : "CAPTURANDO · DECODIFICACIÓN EN VIVO"
            : session is { Finished: false } && !cancelled ? "CAPTURA FINALIZADA · COMPLETANDO DECODIFICACIÓN"
            : session is not null ? "CAPTURA FINALIZADA · RESULTADOS INCREMENTALES RETENIDOS"
            : result is not null ? "ANÁLISIS MANUAL COMPLETO · INSPECCIÓN POSTERIOR"
            : imported ? "WAV IMPORTADO · INSPECCIÓN POSTERIOR" : "CAPTURA FINALIZADA · INSPECCIÓN POSTERIOR";
        var activity = new ReceptionActivity(mode, imported ? "WAV" : "Micrófono", audio.SampleRate, metrics.Count,
            snapshot?.SampleCount ?? (ReferenceEquals(presentedReceptionSource, audio) ? presentedReceptionSamples : 0),
            ReceptionChannelActivity.From(result?.ChannelA, processing), ReceptionChannelActivity.From(result?.ChannelB, processing));
        LiveReceptionInspector.Show(activity); ReceiverSignalMonitor.SetReceptionActivity(activity);
    }
}
