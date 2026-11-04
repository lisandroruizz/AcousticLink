using AcousticLink.App.Visualization;

namespace AcousticLink.App;

public partial class MainWindow
{
    private void OpenReceptionSignal_Click(object sender, System.Windows.RoutedEventArgs e) => ReceiverTools.SelectedIndex = 2;
    private void ResetReceptionObservation()
    {
        ReceiverSignalMonitor.SetReception(null); DashboardSignalMonitor.SetReception(null); GeneratedSignalMonitor.SetReception(null);
    }
    // El monitor observa audio; solo una sesión incremental o Recibir mensajes aportan tramas validadas.
    private void QueueReceptionObservation(CarrierEnergyMap data, object source, bool final = false)
    {
        if (!Dispatcher.CheckAccess()) { if (!Dispatcher.HasShutdownStarted) _ = Dispatcher.BeginInvoke(new Action(() => QueueReceptionObservation(data, source, final))); return; }
        if (closing || data.Signal.Origin == MonitorSignalOrigin.Generated || !ReferenceEquals(audioSelection.Current?.Audio, source)) return;
        RefreshReceptionActivity();
        var session = ReferenceEquals(liveReception?.Audio, source) ? liveReception : null;
        var result = session?.Snapshot?.ObservedResult ?? (ReferenceEquals(presentedReceptionSource, source) ? presentedReception : null);
        double through = session?.Snapshot is { } snapshot ? snapshot.SampleCount / (double)data.Signal.SampleRate : presentedReceptionSamples / (double)data.Signal.SampleRate;
        if (result is not null && data.Signal.Duration >= through)
            foreach (var view in new[] { ReceiverSignalMonitor, DashboardSignalMonitor })
                if (ReferenceEquals(view.Data?.Signal.SourceId, source)) view.SetReception(result, through);
    }
}
